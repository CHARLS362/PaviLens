"""Builds demo scenes from real pavement photos (Wikimedia Commons, CC BY-SA 4.0).

For each photo:
  * distress outlines start from hand-placed guide polygons and are refined to the image edges
    with GrabCut (potholes) or kept as smoothed region outlines (alligator areas); cracks are
    bands around a traced polyline. They are NOT SAM 3 output, and the scenario says so.
  * the ground scale is estimated, because the photos carry no measured reference: a pinhole
    camera with assumed focal length and height, pitched to the observed horizon, gives the
    image→ground homography; the API receives it as a 4-point calibration. Nadir close-ups use
    an estimated visible width instead.

Usage (from backend/):
  python samples/tools/real_scenes.py <folder with the downloaded photos> [--preview <folder>]

Writes samples/<file>.jpg (bytes copied unchanged), the scenario fixtures and the manifest
entries, keeping the synthetic entries written by generate_scenes.py.
"""
import argparse
import hashlib
import json
import math
import os
import shutil

import cv2
import numpy as np

SAMPLES = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCENARIOS = os.path.join(SAMPLES, "..", "src", "PaveLens.Infrastructure", "Ai", "Simulation", "Scenarios")

CONCEPTS = {
    "Pothole": "pothole",
    "AlligatorCrack": "alligator crack",
    "LongitudinalCrack": "longitudinal crack",
    "TransverseCrack": "transverse crack",
    "Rutting": "rutting",
}


# ---------------------------------------------------------------- camera model

class Camera:
    """Pinhole camera at height H above a flat road, pitched down so the horizon sits at row y_h.

    yaw_vp_x is the column of the road's vanishing point; the ground frame is aligned with the
    road (x across, y along), so orientations measured by the API stay meaningful.
    """

    def __init__(self, w, h, f, horizon_y, height_m, yaw_vp_x=None):
        self.w, self.h, self.f, self.H = w, h, f, height_m
        self.cx, self.cy = w / 2, h / 2
        self.theta = math.atan((self.cy - horizon_y) / f)
        c, s = math.cos(self.theta), math.sin(self.theta)
        self.right = np.array([1.0, 0, 0])
        self.fwd = np.array([0, c, -s])
        self.down = np.array([0, -s, -c])
        a = ((yaw_vp_x - self.cx) / f) * c if yaw_vp_x is not None else 0.0
        along = np.array([a, 1.0, 0]); along /= np.linalg.norm(along)
        self.along = along
        self.across = np.array([along[1], -along[0], 0])
        self.origin = np.array([0, 0, height_m])

    def project(self, gx, gy):
        p = gx * self.across + gy * self.along
        v = p - self.origin
        zc = v @ self.fwd
        return self.cx + self.f * (v @ self.right) / zc, self.cy + self.f * (v @ self.down) / zc

    def lateral_scale(self, row):
        """Pixels per metre across the road at an image row (on the optical column)."""
        # Ground point straight ahead that projects to this row, then a 1 m lateral step.
        lo, hi = 0.05, 500.0
        for _ in range(80):
            mid = (lo + hi) / 2
            _, y = self.project(0, mid)
            lo, hi = (mid, hi) if y > row else (lo, mid)
        x0, _ = self.project(0, lo)
        x1, _ = self.project(1, lo)
        return abs(x1 - x0)

    def calibration(self, x0, x1, y0, y1):
        corners = [self.project(x0, y0), self.project(x1, y0), self.project(x1, y1), self.project(x0, y1)]
        norm = [[round(px / self.w, 4), round(py / self.h, 4)] for px, py in corners]
        for x, y in norm:
            if not (0 <= x <= 1 and 0 <= y <= 1):
                raise SystemExit(f"Esquina de calibración fuera de la imagen: {norm}")
        return dict(corners=norm, widthM=round(x1 - x0, 3), lengthM=round(y1 - y0, 3))


# ---------------------------------------------------------------- outlines

def grabcut(img, guide, band=28, iterations=6):
    """Refines a guide polygon to the object edge. Returns the full-resolution outer contour."""
    h, w = img.shape[:2]
    poly = np.array(guide, np.int32)
    inside = np.zeros((h, w), np.uint8)
    cv2.fillPoly(inside, [poly], 1)
    kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * band + 1, 2 * band + 1))
    outer = cv2.dilate(inside, kernel)
    core = cv2.erode(inside, kernel)

    mask = np.full((h, w), cv2.GC_BGD, np.uint8)
    mask[outer == 1] = cv2.GC_PR_BGD
    mask[inside == 1] = cv2.GC_PR_FGD
    mask[core == 1] = cv2.GC_FGD
    bgd, fgd = np.zeros((1, 65), np.float64), np.zeros((1, 65), np.float64)
    cv2.grabCut(img, mask, None, bgd, fgd, iterations, cv2.GC_INIT_WITH_MASK)
    fg = np.isin(mask, (cv2.GC_FGD, cv2.GC_PR_FGD)).astype(np.uint8)
    fg = cv2.morphologyEx(fg, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (9, 9)))
    fg = cv2.morphologyEx(fg, cv2.MORPH_OPEN, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (7, 7)))
    return largest_contour(fg)


def region(img_shape, guide):
    """Smoothed outline of a textured region (alligator cracking) from its guide polygon."""
    m = np.zeros(img_shape[:2], np.uint8)
    cv2.fillPoly(m, [np.array(guide, np.int32)], 1)
    m = cv2.GaussianBlur(m.astype(np.float32), (0, 0), 6) > 0.5
    return largest_contour(m.astype(np.uint8))


def largest_contour(mask):
    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    c = max(contours, key=cv2.contourArea)[:, 0, :].astype(float)
    # Light smoothing along the contour removes the pixel staircase without moving the edge.
    k = 2
    padded = np.vstack([c[-k:], c, c[:k]])
    kernel = np.ones(2 * k + 1) / (2 * k + 1)
    sx = np.convolve(padded[:, 0], kernel, "valid")
    sy = np.convolve(padded[:, 1], kernel, "valid")
    return np.c_[sx, sy]


def band(polyline, half_px):
    pts = np.array(polyline, float)
    left, right = [], []
    for i, (x, y) in enumerate(pts):
        a, b = pts[max(i - 1, 0)], pts[min(i + 1, len(pts) - 1)]
        d = b - a
        n = np.array([-d[1], d[0]]) / (np.linalg.norm(d) or 1)
        left.append((x + half_px * n[0], y + half_px * n[1]))
        right.append((x - half_px * n[0], y - half_px * n[1]))
    return np.array(left + right[::-1])


def resample(outline, max_vertices=400):
    """Keeps every vertex up to max_vertices; beyond that, an even subsample (still sub-pixel spacing)."""
    if len(outline) <= max_vertices:
        return outline
    idx = np.linspace(0, len(outline), max_vertices, endpoint=False).astype(int)
    return outline[idx]


# ---------------------------------------------------------------- scenes

ATTRIB = "Foto: {artist}, {license} ({license_url}), vía Wikimedia Commons: {url}"


def khybalivka(img):
    h, w = img.shape[:2]
    # Road vanishing point from both road edges: (902, 114). Coolpix ≈ 26 mm equivalent → f ≈ 931 px.
    cam = Camera(w, h, f=931, horizon_y=114, height_m=1.4, yaw_vp_x=902)
    b1 = grabcut(img, [(333, 652), (350, 610), (395, 560), (440, 535), (520, 520), (600, 510), (680, 512),
                       (760, 530), (800, 555), (822, 600), (815, 650), (790, 690), (745, 725), (680, 748),
                       (600, 760), (520, 755), (460, 745), (410, 715), (370, 685)])
    fl1 = band([(832, 548), (862, 566), (890, 592), (910, 625), (925, 662), (938, 700), (946, 725)], 3)
    fl2 = band([(300, 615), (306, 586), (322, 556), (352, 530), (392, 512), (432, 500), (470, 492)], 3)
    return dict(
        file="real-khybalivka-bache.jpg", source="khybalivka.jpg",
        title="Foto real · bache aislado en vía rural",
        location="Khybalivka, Ucrania",
        commons="https://commons.wikimedia.org/wiki/File:Khybalivka_2015_DSCN2070_17_%D0%B4%D0%BE%D1%80%D0%BE%D0%B3%D0%B0_%D0%B7_%D1%8F%D0%BC%D0%BE%D1%8E.jpg",
        artist="Nickispeaki",
        camera=cam, calibration=cam.calibration(-1.6, 1.6, 2.2, 6.0),
        scale_note="Escala estimada: cámara a 1.4 m, focal ≈ 26 mm equivalente, horizonte en el punto de fuga de la vía.",
        summary="Bache aislado de bordes verticales con base granular expuesta y un bloque de suelo desprendido. "
                "Dos fisuras en media luna alrededor del borde indican que el bache sigue creciendo por bombeo de finos.",
        items=[
            ("B1", "Pothole", "High", b1, 0.93,
             "Bache profundo de bordes verticales; base granular expuesta con material suelto y un bloque desprendido.",
             "Bacheo profundo: corte rectangular con margen, retiro del material suelto hasta la base sana y reposición en capas."),
            ("FL1", "LongitudinalCrack", "Low", fl1, 0.81,
             "Fisura en media luna que nace del borde derecho del bache.",
             "Incluir en el corte del bacheo o sellar con emulsión."),
            ("FL2", "LongitudinalCrack", "Low", fl2, 0.79,
             "Fisura concéntrica al borde izquierdo del bache: zona de expansión.",
             "Ampliar el corte del bacheo hasta cubrirla."),
        ],
        steps=[
            ("Señalizar el bache", "Coloca conos 15 m antes del bache en ambos sentidos.", ["Conos", "Señal de obra"], "Medium", None),
            ("Bacheo profundo de B1", "Corta un rectángulo que incluya FL1 y FL2, retira el material suelto y el bloque, compacta la base y repón la carpeta.",
             ["Cortadora de disco", "Martillo neumático", "Plancha vibratoria"], "High", "B1"),
        ],
    )


def finland(img):
    h, w = img.shape[:2]
    # Steep downward view; scale checked against the maple leaves (≈ 11 cm) near rows 450 and 650.
    cam = Camera(w, h, f=931, horizon_y=-560, height_m=1.45)
    b1 = grabcut(img, [(588, 615), (612, 578), (650, 562), (720, 556), (790, 560), (838, 585), (870, 625),
                       (880, 668), (858, 712), (815, 745), (760, 758), (700, 752), (648, 735), (605, 700),
                       (588, 660)], band=22)
    pc1 = region(img.shape, [(105, 450), (150, 400), (230, 335), (330, 312), (440, 318), (470, 360),
                             (410, 420), (360, 480), (335, 520), (350, 600), (330, 680), (300, 760),
                             (230, 800), (120, 790), (40, 720), (20, 620), (50, 520)])
    pc2 = region(img.shape, [(730, 330), (880, 300), (1040, 290), (1180, 300), (1280, 330), (1280, 600),
                             (1210, 560), (1120, 520), (1020, 490), (930, 470), (820, 430), (760, 390)])
    return dict(
        file="real-finlandia-cocodrilo.jpg", source="finland-potholes.jpg",
        title="Foto real · bache y piel de cocodrilo avanzada",
        location="Finlandia",
        commons="https://commons.wikimedia.org/wiki/File:Potholes_on_asphalt_road_20171023.jpg",
        artist="Santeri Viinamäki",
        camera=cam, calibration=cam.calibration(-1.5, 1.5, 1.4, 3.6),
        scale_note="Escala estimada: cámara a 1.45 m con inclinación de unos 48°, contrastada con hojas de arce de ≈ 11 cm.",
        summary="Carpeta desintegrada por fatiga: un bache con la base expuesta, una zona de desprendimiento con "
                "agregado suelto y piel de cocodrilo extendida en el resto del tramo. El patrón indica falla "
                "estructural de la base; el bacheo aislado no basta.",
        items=[
            ("B1", "Pothole", "High", b1, 0.91,
             "Bache con la base granular expuesta y agua empozada reciente; bordes rotos por la piel de cocodrilo.",
             "Bacheo profundo dentro de la reconstrucción de PC1."),
            ("PC1", "AlligatorCrack", "High", pc1, 0.86,
             "Piel de cocodrilo de severidad alta con piezas desprendidas y agregado suelto sobre la base.",
             "Reconstrucción localizada de carpeta y base; revisar drenaje."),
            ("PC2", "AlligatorCrack", "Medium", pc2, 0.83,
             "Malla de fisuras interconectadas sin desprendimiento, celdas de 10–30 cm.",
             "Sello de fisuras como medida temporal; programar recapeo."),
        ],
        steps=[
            ("Cerrar el área de trabajo", "Delimita toda la zona de PC1 con conos y cinta.", ["Conos", "Cinta de seguridad"], "Medium", None),
            ("Reconstruir PC1 y B1", "Retira la carpeta desintegrada y el agregado suelto, compacta la base y repón la carpeta en capas.",
             ["Minicargador", "Rodillo compactador"], "High", "PC1"),
            ("Tratar PC2", "Sella la malla de fisuras y programa recapeo antes del invierno.", ["Sellador asfáltico", "Compresora"], "Low", "PC2"),
        ],
    )


def bear_tavern(img):
    b1 = grabcut(img, [(70, 462), (150, 448), (300, 440), (470, 432), (650, 438), (820, 446), (1000, 450),
                       (1100, 452), (1160, 466), (1185, 500), (1150, 522), (1000, 524), (820, 522),
                       (640, 520), (460, 518), (280, 515), (130, 510), (72, 500)], band=18)
    pc1 = region(img.shape, [(40, 540), (300, 532), (600, 540), (900, 545), (1200, 560), (1280, 570),
                             (1280, 960), (0, 960), (0, 560)])
    return dict(
        file="real-bear-tavern-junta.jpg", source="bear-tavern.jpg",
        title="Foto real · bache en junta y piel de cocodrilo",
        location="Bear Tavern Road, Ewing (Nueva Jersey, EE. UU.)",
        commons="https://commons.wikimedia.org/wiki/File:2026-09-13_13_07_57_Pothole_revealing_the_original_cement_surface_of_Mercer_County_Route_579_(Bear_Tavern_Road)_in_Ewing_Township,_Mercer_County,_New_Jersey.jpg",
        artist="Famartin",
        camera=None, referenceWidthM=1.3,
        scale_note="Escala estimada: toma casi cenital de ≈ 1.3 m de ancho, deducida del tamaño del agregado (2–3 cm).",
        summary="Bache alargado sobre una junta del pavimento de concreto subyacente: la sobrecapa asfáltica se "
                "desprendió y deja ver el concreto original. Debajo, piel de cocodrilo por reflexión de la junta.",
        items=[
            ("B1", "Pothole", "Medium", b1, 0.9,
             "Desprendimiento lineal de la sobrecapa sobre una junta; se ve el concreto original con agregado redondeado.",
             "Bacheo superficial con corte recto a lo largo de la junta y mezcla en caliente."),
            ("PC1", "AlligatorCrack", "Medium", pc1, 0.84,
             "Piel de cocodrilo por reflexión de la junta y fatiga de la sobrecapa.",
             "Fresado y reposición de la sobrecapa con geotextil antirreflejo de fisuras."),
        ],
        steps=[
            ("Bacheo de B1", "Corta en línea recta a ambos lados de la junta, limpia, aplica riego de liga y repón.",
             ["Cortadora de disco", "Compresora", "Plancha vibratoria"], "Medium", "B1"),
            ("Tratar PC1", "Fresa la sobrecapa fisurada y repón con geotextil para frenar la reflexión de fisuras.",
             ["Fresadora", "Geotextil", "Rodillo"], "Medium", "PC1"),
        ],
    )


# ---------------------------------------------------------------- output

def write(scene, src_dir, preview_dir):
    src = os.path.join(src_dir, scene["source"])
    dst = os.path.join(SAMPLES, scene["file"])
    shutil.copyfile(src, dst)
    img = cv2.imread(dst)
    h, w = img.shape[:2]
    sha = hashlib.sha256(open(dst, "rb").read()).hexdigest()

    attribution = ATTRIB.format(artist=scene["artist"], license="CC BY-SA 4.0",
                                license_url="https://creativecommons.org/licenses/by-sa/4.0/", url=scene["commons"])
    summary = (f"[Simulación] {scene['summary']} Contornos trazados con GrabCut sobre guías manuales (no son salida de "
               f"SAM 3). {scene['scale_note']} Las áreas son aproximadas.")
    fixture = dict(sha256=sha, summary=summary, distresses=[], steps=[])
    print(f"\n{scene['file']}  sha256={sha[:12]}…")

    overlay = img.copy()
    colours = {"High": (40, 40, 200), "Medium": (0, 110, 230), "Low": (0, 170, 200)}
    for code, dtype, severity, outline, score, diagnosis, action in scene["items"]:
        outline = resample(outline)
        norm = [[round(float((x + 0.5) / w), 5), round(float((y + 0.5) / h), 5)] for x, y in outline]
        norm = [[min(max(x, 0.0), 1.0), min(max(y, 0.0), 1.0)] for x, y in norm]
        fixture["distresses"].append(dict(concept=CONCEPTS[dtype], score=score, polygon=norm, code=code, type=dtype,
                                          severity=severity, diagnosis=diagnosis, recommendedAction=action))
        pts = outline.astype(np.int32)
        cv2.polylines(overlay, [pts], True, colours[severity], 2, cv2.LINE_AA)
        x, y = pts.min(axis=0)
        cv2.putText(overlay, code, (int(x), int(y) - 6), cv2.FONT_HERSHEY_SIMPLEX, 0.7, colours[severity], 2, cv2.LINE_AA)
        print(f"  {code:4} {dtype:18} {len(norm):4} vértices  {cv2.contourArea(outline.astype(np.float32)):9.0f} px²")

    cam, calibration = scene.get("camera"), scene.get("calibration")
    if calibration:
        quad = np.array([[x * w, y * h] for x, y in calibration["corners"]], np.int32)
        cv2.polylines(overlay, [quad], True, (255, 255, 255), 1, cv2.LINE_AA)
        for row in (450, 650, 850):
            print(f"  escala lateral en la fila {row}: {cam.lateral_scale(row):6.0f} px/m")
    for title, instructions, tools, risk, target in scene["steps"]:
        fixture["steps"].append(dict(title=title, instructions=instructions, tools=tools, risk=risk, target=target))

    if preview_dir:
        os.makedirs(preview_dir, exist_ok=True)
        cv2.imwrite(os.path.join(preview_dir, scene["file"].replace(".jpg", ".preview.jpg")), overlay)

    with open(os.path.join(SCENARIOS, scene["file"].replace(".jpg", ".json")), "w", encoding="utf-8") as f:
        json.dump(fixture, f, ensure_ascii=False, indent=2)
        f.write("\n")

    entry = dict(file=scene["file"], title=scene["title"], notes=attribution, location=scene["location"])
    if calibration:
        entry["calibration"] = calibration
    else:
        entry["referenceWidthM"] = scene["referenceWidthM"]
    entry["attribution"] = dict(author=scene["artist"], license="CC BY-SA 4.0", source=scene["commons"])
    return entry


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("photos", help="Folder with the downloaded Commons photos")
    parser.add_argument("--preview", help="Folder for overlay previews")
    args = parser.parse_args()

    builders = [(khybalivka, "khybalivka.jpg"), (finland, "finland-potholes.jpg"), (bear_tavern, "bear-tavern.jpg")]
    entries = []
    for build, name in builders:
        img = cv2.imread(os.path.join(args.photos, name))
        if img is None:
            raise SystemExit(f"No se pudo leer {name}")
        entries.append(write(build(img), args.photos, args.preview))

    path = os.path.join(SAMPLES, "manifest.json")
    manifest = [e for e in json.load(open(path, encoding="utf-8")) if "attribution" not in e]
    with open(path, "w", encoding="utf-8") as f:
        json.dump(manifest + entries, f, ensure_ascii=False, indent=2)
        f.write("\n")


if __name__ == "__main__":
    main()
