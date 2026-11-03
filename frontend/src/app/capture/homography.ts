export type Point = [number, number];
export type Mapping = (x: number, y: number) => Point | null;

/**
 * Plane-to-plane homography from four correspondences (DLT with h33 = 1), the same formulation as the API's
 * HomographyProjection. Returns null for points on the far side of the horizon.
 */
export function homography(src: Point[], dst: Point[]): Mapping {
  const a: number[][] = [];
  for (let i = 0; i < 4; i++) {
    const [u, v] = src[i];
    const [x, y] = dst[i];
    a.push([u, v, 1, 0, 0, 0, -u * x, -v * x, x]);
    a.push([0, 0, 0, u, v, 1, -u * y, -v * y, y]);
  }
  for (let c = 0; c < 8; c++) {
    let pivot = c;
    for (let r = c + 1; r < 8; r++) if (Math.abs(a[r][c]) > Math.abs(a[pivot][c])) pivot = r;
    [a[c], a[pivot]] = [a[pivot], a[c]];
    for (let r = 0; r < 8; r++) {
      if (r === c) continue;
      const f = a[r][c] / a[c][c];
      for (let k = c; k < 9; k++) a[r][k] -= f * a[c][k];
    }
  }
  const h = a.map((row, i) => row[8] / row[i]);
  const den = (u: number, v: number) => h[6] * u + h[7] * v + 1;
  const groundSign = Math.sign(den(src[0][0], src[0][1]));
  return (u, v) => {
    const w = den(u, v);
    if (w * groundSign <= 1e-9) return null;
    return [(h[0] * u + h[1] * v + h[2]) / w, (h[3] * u + h[4] * v + h[5]) / w];
  };
}

/** Ground rectangle (metres) for a calibration, in the API's corner order. */
export function groundRect(widthM: number, lengthM: number): Point[] {
  return [[0, 0], [widthM, 0], [widthM, lengthM], [0, lengthM]];
}

/** A quad is usable when it is simple and convex, with the same turn at every corner. */
export function isConvexQuad(p: Point[]): boolean {
  if (p.length !== 4) return false;
  let sign = 0;
  for (let i = 0; i < 4; i++) {
    const [ax, ay] = p[i];
    const [bx, by] = p[(i + 1) % 4];
    const [cx, cy] = p[(i + 2) % 4];
    const cross = (bx - ax) * (cy - by) - (by - ay) * (cx - bx);
    if (Math.abs(cross) < 1e-6) return false;
    const s = Math.sign(cross);
    if (sign && s !== sign) return false;
    sign = s;
  }
  return true;
}
