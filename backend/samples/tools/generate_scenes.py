"""Generates the synthetic pavement scenes used by the simulation provider.

Each scene is drawn in ground coordinates (metres), so every distress mask is known exactly.
The oblique scene is rendered through the homography defined by the same 4-point calibration
that the API receives; the API projects the masks back to metres with it, closing the loop.

Outputs: samples/*.jpg, samples/manifest.json and the scenario fixtures in
src/PaveLens.Infrastructure/Ai/Simulation/Scenarios/. Deterministic (fixed seeds).

Run from backend/:  python samples/tools/generate_scenes.py  (needs numpy and Pillow)
"""
import hashlib
import json
import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SAMPLES = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCENARIOS = os.path.join(SAMPLES, "..", "src", "PaveLens.Infrastructure", "Ai", "Simulation", "Scenarios")
W_IMG, H_IMG = 1280, 960
ASPECT = H_IMG / W_IMG

CONCEPTS = {
    "Pothole": "pothole",
    "AlligatorCrack": "alligator crack",
    "LongitudinalCrack": "longitudinal crack",
    "TransverseCrack": "transverse crack",
    "Rutting": "rutting",
}


# ---------------------------------------------------------------- geometry

def homography(src, dst):
    """DLT with h33 = 1, same formulation as HomographyProjection.Solve in the API."""
    a, b = [], []
    for (u, v), (x, y) in zip(src, dst):
        a.append([u, v, 1, 0, 0, 0, -u * x, -v * x]); b.append(x)
        a.append([0, 0, 0, u, v, 1, -u * y, -v * y]); b.append(y)
    h = np.linalg.solve(np.array(a, float), np.array(b, float))
    return np.append(h, 1).reshape(3, 3)


def apply(h, pts):
    p = np.c_[np.asarray(pts, float), np.ones(len(pts))] @ h.T
    return p[:, :2] / p[:, 2:3]


MASK_VERTICES = 96


def blob(cx, cy, r, rng, wobble=0.22, squash=0.85, grain=0.025):
    """Irregular closed outline sampled densely enough to be the exact boundary of the painted area.

    Low harmonics give the overall shape; a few weak high harmonics add the ragged edge of broken
    asphalt. MASK_VERTICES points keep the chord error well under a pixel at the scenes' scale.
    """
    low = wobble * np.array([0.55, 0.3, 0.15, 0.08])
    high = grain * np.array([1.0, 0.8, 0.6, 0.5, 0.4])
    phases = rng.uniform(0, 2 * math.pi, len(low) + len(high))
    orders = list(range(1, len(low) + 1)) + [7, 9, 11, 14, 17]
    weights = list(low) + list(high)
    pts = []
    for k in range(MASK_VERTICES):
        a = 2 * math.pi * k / MASK_VERTICES
        rr = r * (1 + sum(w * math.sin(o * a + p) for o, w, p in zip(orders, weights, phases)))
        pts.append((cx + rr * math.cos(a), cy + squash * rr * math.sin(a)))
    return pts


def wiggle(p0, p1, steps, amp, rng):
    """Polyline from p0 to p1 with lateral jitter: the visual path of a crack."""
    (x0, y0), (x1, y1) = p0, p1
    length = math.hypot(x1 - x0, y1 - y0)
    nx, ny = -(y1 - y0) / length, (x1 - x0) / length
    offset, pts = 0.0, []
    for i in range(steps + 1):
        t = i / steps
        if 0 < i < steps:
            offset = 0.6 * offset + amp * rng.normal()
        else:
            offset = 0.0
        pts.append((x0 + t * (x1 - x0) + offset * nx, y0 + t * (y1 - y0) + offset * ny))
    return pts


def crack_half_width(width_m):
    """Half-width of a crack mask: the dark line plus its shadow halo, as SAM would outline it."""
    return 0.5 * width_m + 0.004


def band(polyline, half):
    """Closed outline at ±half around a polyline: the segmentation mask of a crack."""
    left, right = [], []
    for i, (x, y) in enumerate(polyline):
        xa, ya = polyline[max(i - 1, 0)]
        xb, yb = polyline[min(i + 1, len(polyline) - 1)]
        length = math.hypot(xb - xa, yb - ya) or 1
        nx, ny = -(yb - ya) / length, (xb - xa) / length
        left.append((x + half * nx, y + half * ny))
        right.append((x - half * nx, y - half * ny))
    return left + right[::-1]


def shoelace(poly):
    return abs(sum(x0 * y1 - x1 * y0 for (x0, y0), (x1, y1) in zip(poly, poly[1:] + poly[:1]))) / 2


# ---------------------------------------------------------------- texture

class Ground:
    """RGB texture of the road plane. World (X, Y) in metres maps to pixel ((X - x0)·ppm, (Y - y0)·ppm)."""

    def __init__(self, x0, y0, width_m, height_m, ppm, rng):
        self.x0, self.y0, self.ppm, self.rng = x0, y0, ppm, rng
        self.w, self.h = int(round(width_m * ppm)), int(round(height_m * ppm))
        self.gray = self._asphalt()
        self.tint = np.ones((self.h, self.w, 3)) * np.array([1.0, 1.0, 1.035])

    def px(self, pts):
        return [((x - self.x0) * self.ppm, (y - self.y0) * self.ppm) for x, y in pts]

    def _noise(self, scale_m, amp):
        scale = max(scale_m * self.ppm, 1)
        small = self.rng.normal(0, 1, (int(self.h / scale) + 2, int(self.w / scale) + 2)).astype(np.float32)
        return amp * np.asarray(Image.fromarray(small, "F").resize((self.w, self.h), Image.BILINEAR))

    def _asphalt(self):
        base = np.full((self.h, self.w), 88.0)
        for scale_m, amp in [(0.8, 7), (0.2, 6), (0.05, 6), (0.012, 8)]:
            base += self._noise(scale_m, amp)
        speck = self.rng.random((self.h, self.w))
        base[speck > 0.988] += 38
        base[speck < 0.008] -= 28
        return base

    def mask(self, poly, blur_m=0.0):
        img = Image.new("L", (self.w, self.h), 0)
        ImageDraw.Draw(img).polygon(self.px(poly), fill=255)
        if blur_m > 0:
            img = img.filter(ImageFilter.GaussianBlur(blur_m * self.ppm))
        return np.asarray(img, float) / 255

    def lines(self, polylines, width_m):
        img = Image.new("L", (self.w, self.h), 0)
        draw = ImageDraw.Draw(img)
        for line in polylines:
            draw.line(self.px(line), fill=255, width=max(int(round(width_m * self.ppm)), 1), joint="curve")
        return np.asarray(img, float) / 255

    def paint(self, region_poly, rgb, alpha=1.0):
        m = self.mask(region_poly)[..., None] * alpha
        tex = self.gray[..., None] * self.tint
        target = np.array(rgb, float) + (self.gray[..., None] - 88) * 0.5
        self.gray = (tex * (1 - m) + target * m).mean(axis=2)
        self.tint = self.tint * (1 - m) + (np.array(rgb, float) / max(np.mean(rgb), 1)) * m

    # -- distresses

    def pothole(self, poly):
        m = self.mask(poly, blur_m=0.01)
        rim = np.clip(self.mask(poly, blur_m=0.05) * 1.6 - m, 0, 1)
        inner = self.mask(poly, blur_m=0.08)
        gravel = (self.rng.random(self.gray.shape) > 0.9) * 26 + self._noise(0.02, 7)
        floor = self.gray * 0.5 - 22 * inner ** 2 + gravel
        self.gray = self.gray * (1 - m) + floor * m + 16 * rim

    def crack(self, polyline, width_m):
        m = self.lines([polyline], width_m)
        halo = np.asarray(Image.fromarray((m * 255).astype(np.uint8)).filter(
            ImageFilter.GaussianBlur(width_m * self.ppm)), float) / 255
        self.gray = self.gray * (1 - 0.8 * m) + 18 * m - 10 * halo

    def alligator(self, poly, cell_m):
        xs, ys = [p[0] for p in poly], [p[1] for p in poly]
        nx = int((max(xs) - min(xs)) / cell_m) + 3
        ny = int((max(ys) - min(ys)) / cell_m) + 3
        grid = [[(min(xs) + (i - 1) * cell_m + self.rng.uniform(-0.35, 0.35) * cell_m,
                  min(ys) + (j - 1) * cell_m + self.rng.uniform(-0.35, 0.35) * cell_m)
                 for j in range(ny)] for i in range(nx)]
        segs = []
        for i in range(nx):
            for j in range(ny):
                if i + 1 < nx: segs.append(wiggle(grid[i][j], grid[i + 1][j], 4, 0.012, self.rng))
                if j + 1 < ny: segs.append(wiggle(grid[i][j], grid[i][j + 1], 4, 0.012, self.rng))
        area = self.mask(poly, blur_m=0.03)
        net = self.lines(segs, 0.012) * area
        self.gray = self.gray * (1 - 0.75 * net) + 20 * net - 9 * area

    def rutting(self, poly):
        m = self.mask(poly, blur_m=0.12)
        self.gray = self.gray - 16 * m

    def rgb(self):
        return np.clip(self.gray[..., None] * self.tint, 0, 255)

    def sample(self, X, Y):
        """Bilinear lookup at world coordinates; returns RGB and an inside mask."""
        tex = self.rgb()
        fx, fy = (X - self.x0) * self.ppm, (Y - self.y0) * self.ppm
        inside = (fx >= 0) & (fy >= 0) & (fx < self.w - 1) & (fy < self.h - 1)
        fx, fy = np.clip(fx, 0, self.w - 1.001), np.clip(fy, 0, self.h - 1.001)
        ix, iy = fx.astype(int), fy.astype(int)
        dx, dy = (fx - ix)[..., None], (fy - iy)[..., None]
        c = (tex[iy, ix] * (1 - dx) * (1 - dy) + tex[iy, ix + 1] * dx * (1 - dy)
             + tex[iy + 1, ix] * (1 - dx) * dy + tex[iy + 1, ix + 1] * dx * dy)
        return c, inside


# ---------------------------------------------------------------- scenes

def distress(code, dtype, severity, ground_poly, score, diagnosis, action):
    return dict(code=code, type=dtype, severity=severity, ground=ground_poly, score=score,
                diagnosis=diagnosis, recommendedAction=action)


def scene_oblique():
    rng = np.random.default_rng(31)
    road_w, unit_l = 7.0, 25.0
    g = Ground(-6, -4, 19, 90, 100, rng)

    # Curbs, sidewalks and lane markings.
    g.paint([(-0.25, -4), (0, -4), (0, 86), (-0.25, 86)], (170, 168, 160))
    g.paint([(7, -4), (7.25, -4), (7.25, 86), (7, 86)], (170, 168, 160))
    g.paint([(-6, -4), (-0.25, -4), (-0.25, 86), (-6, 86)], (138, 128, 112))
    g.paint([(7.25, -4), (13, -4), (13, 86), (7.25, 86)], (138, 128, 112))
    for y in np.arange(-4, 86, 6):
        g.paint([(3.44, y), (3.56, y), (3.56, y + 3), (3.44, y + 3)], (196, 160, 52), 0.85)
    g.paint([(0.15, -4), (0.27, -4), (0.27, 86), (0.15, 86)], (205, 205, 200), 0.8)
    g.paint([(6.73, -4), (6.85, -4), (6.85, 86), (6.73, 86)], (205, 205, 200), 0.8)

    fl1 = wiggle((4.55, 1.2), (4.8, 10.8), 90, 0.012, rng)
    ft1 = wiggle((0.45, 20.4), (6.35, 20.95), 70, 0.012, rng)
    items = [
        distress("B1", "Pothole", "High", blob(2.0, 6.0, 0.38, rng), 0.96,
                 "Bache de bordes verticales y fondo con agregado suelto; afecta la huella de rodada derecha del carril.",
                 "Bacheo profundo: corte rectangular, retiro del material, riego de liga y mezcla en caliente compactada por capas."),
        distress("B2", "Pothole", "Medium", blob(5.2, 12.5, 0.25, rng), 0.93,
                 "Bache en formación con pérdida de carpeta y bordes desgranados.",
                 "Bacheo superficial con corte rectangular y mezcla en caliente."),
        distress("PC1", "AlligatorCrack", "Medium",
                 blob(2.0, 16.55, 1.0, rng, wobble=0.3, squash=1.65, grain=0.015), 0.91,
                 "Fisuras interconectadas en malla (piel de cocodrilo) por fatiga; celdas de 10–20 cm sin desprendimiento.",
                 "Bacheo profundo del área fisurada; revisar drenaje y capacidad de la base."),
        distress("FL1", "LongitudinalCrack", "Low", band(fl1, crack_half_width(0.012)), 0.88,
                 "Fisura longitudinal fina junto al eje, ancho estimado menor de 1 cm.",
                 "Limpieza con aire comprimido y sellado con emulsión asfáltica."),
        distress("FT1", "TransverseCrack", "Medium", band(ft1, crack_half_width(0.018)), 0.9,
                 "Fisura transversal de lado a lado, típica de contracción térmica por heladas del altiplano.",
                 "Ruteo y sellado con asfalto modificado para soportar los ciclos térmicos."),
        distress("AH1", "Rutting", "Low", [(5.35, 15.5), (5.95, 15.5), (5.95, 24.0), (5.35, 24.0)], 0.84,
                 "Depresión longitudinal leve en la huella de rodada exterior.",
                 "Fresado y reposición de carpeta en la huella si progresa; monitorear en la próxima inspección."),
    ]
    g.rutting(items[5]["ground"])
    g.alligator(items[2]["ground"], 0.16)
    g.crack(fl1, 0.012)
    g.crack(ft1, 0.018)
    g.pothole(items[0]["ground"])
    g.pothole(items[1]["ground"])

    corners = [(0.08, 0.95), (0.92, 0.95), (0.60, 0.42), (0.40, 0.42)]
    world = [(0, 0), (road_w, 0), (road_w, unit_l), (0, unit_l)]
    h_iw = homography(corners, world)        # image → ground
    h_wi = np.linalg.inv(h_iw)               # ground → image

    ss = 2
    v, u = np.mgrid[0:H_IMG * ss, 0:W_IMG * ss]
    un, vn = (u + 0.5) / (W_IMG * ss), (v + 0.5) / (H_IMG * ss)
    # With h33 = 1 the denominator's sign on the ground is that of the calibration corners, not always +.
    sign = np.sign(h_iw[2, 0] * corners[0][0] + h_iw[2, 1] * corners[0][1] + h_iw[2, 2])
    den = (h_iw[2, 0] * un + h_iw[2, 1] * vn + h_iw[2, 2]) * sign
    safe = np.where(den > 1e-6, den, 1) * sign
    X = (h_iw[0, 0] * un + h_iw[0, 1] * vn + h_iw[0, 2]) / safe
    Y = (h_iw[1, 0] * un + h_iw[1, 1] * vn + h_iw[1, 2]) / safe
    color, inside = g.sample(X, Y)
    ground_ok = (den > 1e-6) & inside

    # Sky of the altiplano with haze toward the horizon.
    t = np.clip(vn / 0.27, 0, 1)[..., None]
    sky = np.array([62, 112, 186]) * (1 - t) + np.array([186, 204, 222]) * t
    haze = np.clip(1 - np.exp(-np.clip(Y, 0, None) / 70), 0, 1)[..., None]
    color = color * (1 - 0.65 * haze) + np.array([176, 190, 205]) * 0.65 * haze
    img = np.where(ground_ok[..., None], color, sky)
    out = Image.fromarray(img.astype(np.uint8)).resize((W_IMG, H_IMG), Image.LANCZOS)

    to_image = lambda poly: apply(h_wi, poly)
    return dict(
        file="jr-lima-tramo.jpg",
        title="Jr. Lima, cuadra 3 · tramo de 25 m",
        notes="Toma oblicua desde el borde de la calzada. Calibración con el ancho de la vía (7 m) y 25 m de longitud medidos con wincha.",
        location="Jr. Lima, cuadra 3 · Puno",
        roadSegmentId="H11",
        referenceWidthM=None,
        calibration=dict(corners=[list(c) for c in corners], widthM=road_w, lengthM=unit_l),
        summary="[Simulación] Escena sintética de demostración. Tramo de 25 m con dos baches, un área de piel de cocodrilo, "
                "fisuras longitudinal y transversal y ahuellamiento leve. El conjunto apunta a fatiga de la carpeta "
                "acelerada por ciclos de helada: conviene bachear antes de la temporada de lluvias.",
        steps=[
            ("Señalizar y cerrar un carril", "Instala el desvío según la simulación de tráfico del tramo H11 antes de iniciar los cortes.",
             ["Conos", "Señales de desvío", "Paletas PARE/SIGA"], "Medium", None),
            ("Bacheo profundo de B1", "Corta un rectángulo 15 cm más allá del borde visible, retira el material hasta la base sana, aplica riego de liga y repón con mezcla en caliente en capas de 5 cm.",
             ["Cortadora de disco diamantado", "Martillo neumático", "Plancha vibratoria"], "High", "B1"),
            ("Bacheo de B2", "Mismo procedimiento que B1 con una sola capa de mezcla.", ["Cortadora de disco diamantado", "Plancha vibratoria"], "Medium", "B2"),
            ("Reparar la piel de cocodrilo PC1", "Delimita el área fisurada con margen, retira la carpeta y verifica la base antes de reponer.",
             ["Fresadora o martillo neumático", "Rodillo compactador"], "Medium", "PC1"),
            ("Sellar FT1 y FL1", "Abre las fisuras con ruteadora, limpia con aire comprimido y sella con asfalto modificado.",
             ["Ruteadora", "Compresora", "Sellador asfáltico"], "Low", "FT1"),
        ],
        items=items,
        to_image=to_image,
        image=out,
    )


def scene_nadir(file, title, notes, location, segment, width_m, seed, build, summary, steps):
    rng = np.random.default_rng(seed)
    height_m = width_m * ASPECT
    g = Ground(0, 0, width_m, height_m, W_IMG / width_m, rng)
    items = build(g, rng, width_m, height_m)
    img = Image.fromarray(g.rgb().astype(np.uint8)).resize((W_IMG, H_IMG), Image.LANCZOS)
    to_image = lambda poly: np.array([(x / width_m, y / height_m) for x, y in poly])
    return dict(file=file, title=title, notes=notes, location=location, roadSegmentId=segment,
                referenceWidthM=width_m, calibration=None, summary=summary, steps=steps,
                items=items, to_image=to_image, image=img)


def build_pothole(g, rng, w, h):
    g.paint([(0.12, 0), (0.24, 0), (0.24, h), (0.12, h)], (210, 208, 200), 0.85)
    fl1 = wiggle((2.35, 0.05), (2.55, 2.2), 80, 0.008, rng)
    items = [
        distress("B1", "Pothole", "High", blob(1.45, 1.1, 0.46, rng, 0.18), 0.97,
                 "Bache profundo con bordes verticales y agua empozada reciente; la base granular queda expuesta en el centro.",
                 "Bacheo profundo inmediato: el agua acelera la erosión de la base."),
        distress("FL1", "LongitudinalCrack", "Low", band(fl1, crack_half_width(0.008)), 0.86,
                 "Fisura longitudinal fina paralela al bache.",
                 "Sellar con emulsión asfáltica en la misma intervención."),
    ]
    g.crack(fl1, 0.008)
    g.pothole(items[0]["ground"])
    return items


def build_alligator(g, rng, w, h):
    ft1 = wiggle((0.15, 2.72), (3.85, 2.62), 90, 0.01, rng)
    items = [
        distress("PC1", "AlligatorCrack", "High",
                 blob(1.88, 1.45, 1.0, rng, wobble=0.24, squash=0.88, grain=0.015), 0.94,
                 "Piel de cocodrilo severa: celdas pequeñas con bordes desprendidos y bombeo de finos en las juntas.",
                 "Reconstrucción localizada de carpeta y base; corregir el drenaje lateral."),
        distress("FT1", "TransverseCrack", "Medium", band(ft1, crack_half_width(0.012)), 0.89,
                 "Fisura transversal por contracción térmica, ancho de 6 a 10 mm.",
                 "Ruteo y sellado con asfalto modificado."),
        distress("B1", "Pothole", "Low", blob(3.4, 1.05, 0.17, rng), 0.9,
                 "Bache incipiente por desprendimiento de una celda de la malla de fisuras.",
                 "Bacheo superficial junto con la reparación de PC1."),
    ]
    g.alligator(items[0]["ground"], 0.1)
    g.crack(ft1, 0.012)
    g.pothole(items[2]["ground"])
    return items


def check_mask(d, ppm=400):
    """Rasterises the ground outline and compares with the shoelace area: a self-intersecting
    or degenerate outline (e.g. a crack band folding on a sharp turn) shows up as a mismatch."""
    poly = d["ground"]
    xs, ys = [p[0] for p in poly], [p[1] for p in poly]
    x0, y0 = min(xs) - 0.05, min(ys) - 0.05
    w, h = int((max(xs) - x0 + 0.05) * ppm), int((max(ys) - y0 + 0.05) * ppm)
    img = Image.new("L", (w, h), 0)
    ImageDraw.Draw(img).polygon([((x - x0) * ppm, (y - y0) * ppm) for x, y in poly], fill=1)
    raster = np.asarray(img, float).sum() / ppm ** 2
    exact = shoelace(poly)
    perimeter = sum(math.dist(a, b) for a, b in zip(poly, poly[1:] + poly[:1]))
    # PIL fills every pixel the edge touches: up to one pixel along the perimeter.
    tolerance = perimeter / ppm + 0.002 * exact
    if abs(raster - exact) > tolerance:
        raise SystemExit(f"{d['code']}: máscara inválida, área {exact:.4f} m² vs raster {raster:.4f} m²")
    return exact


def write_scene(scene):
    path = os.path.join(SAMPLES, scene["file"])
    scene["image"].save(path, quality=90)
    sha = hashlib.sha256(open(path, "rb").read()).hexdigest()

    fixture = dict(sha256=sha, summary=scene["summary"], distresses=[], steps=[])
    truth = {}
    print(f"\n{scene['file']}  sha256={sha[:12]}…")
    for d in scene["items"]:
        img_poly = np.clip(scene["to_image"](d["ground"]), 0, 1)
        fixture["distresses"].append(dict(
            concept=CONCEPTS[d["type"]], score=d["score"],
            polygon=[[round(float(x), 4), round(float(y), 4)] for x, y in img_poly],
            code=d["code"], type=d["type"], severity=d["severity"],
            diagnosis=d["diagnosis"], recommendedAction=d["recommendedAction"]))
        truth[d["code"]] = round(check_mask(d), 5)
        print(f"  {d['code']:4} {d['type']:18} área real {truth[d['code']]:7.4f} m²  {len(img_poly)} vértices")
    for title, instructions, tools, risk, target in scene["steps"]:
        fixture["steps"].append(dict(title=title, instructions=instructions, tools=tools, risk=risk, target=target))

    name = os.path.splitext(scene["file"])[0] + ".json"
    with open(os.path.join(SCENARIOS, name), "w", encoding="utf-8") as f:
        json.dump(fixture, f, ensure_ascii=False, indent=2)
        f.write("\n")

    entry = dict(file=scene["file"], title=scene["title"], notes=scene["notes"], location=scene["location"],
                 roadSegmentId=scene["roadSegmentId"])
    if scene["referenceWidthM"] is not None:
        entry["referenceWidthM"] = scene["referenceWidthM"]
    if scene["calibration"] is not None:
        entry["calibration"] = scene["calibration"]
    # Ground-truth areas of the drawn distresses: the integration tests check the API against them.
    entry["groundTruthAreaM2"] = truth
    return entry


def main():
    scenes = [
        scene_oblique(),
        scene_nadir(
            "av-el-sol-bache.jpg", "Av. El Sol · bache en carril derecho",
            "Foto cenital a 1.6 m de altura; el encuadre cubre 3 m de ancho.", "Av. El Sol, frente al mercado · Puno",
            "V13", 3.0, 7, build_pothole,
            "[Simulación] Escena sintética de demostración. Bache profundo de severidad alta con agua empozada y una "
            "fisura longitudinal fina al costado. La foto cubre menos que una unidad de muestreo: el PCI es una cota superior.",
            [("Delimitar y señalizar", "Cierra el carril derecho con conos 20 m antes del bache.", ["Conos", "Señal de obra"], "Medium", None),
             ("Bacheo profundo de B1", "Retira el agua y el material suelto, corta en rectángulo con margen de 15 cm y repón en capas compactadas.",
              ["Cortadora de disco diamantado", "Martillo neumático", "Plancha vibratoria"], "High", "B1"),
             ("Sellar FL1", "Limpia y sella la fisura con emulsión asfáltica.", ["Compresora", "Sellador asfáltico"], "Low", "FL1")]),
        scene_nadir(
            "jr-arequipa-cocodrilo.jpg", "Jr. Arequipa · piel de cocodrilo",
            "Foto cenital; el encuadre cubre 4 m de ancho.", "Jr. Arequipa, cuadra 5 · Puno",
            "H30", 4.0, 19, build_alligator,
            "[Simulación] Escena sintética de demostración. Piel de cocodrilo severa con bombeo de finos, una fisura "
            "transversal térmica y un bache incipiente. Indica falla estructural de la base: no basta con sellar.",
            [("Señalizar la zona de trabajo", "Cierra el carril afectado según la simulación de tráfico del tramo H30.", ["Conos", "Señales de desvío"], "Medium", None),
             ("Reconstrucción localizada de PC1", "Retira carpeta y base contaminada, compacta base nueva y repón la carpeta.",
              ["Retroexcavadora pequeña", "Rodillo compactador"], "High", "PC1"),
             ("Bacheo de B1", "Inclúyelo en el corte de PC1.", ["Cortadora de disco diamantado"], "Medium", "B1"),
             ("Sellar FT1", "Rutea y sella con asfalto modificado.", ["Ruteadora", "Sellador asfáltico"], "Low", "FT1")]),
    ]
    manifest = [write_scene(s) for s in scenes]
    with open(os.path.join(SAMPLES, "manifest.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)
        f.write("\n")


if __name__ == "__main__":
    main()
