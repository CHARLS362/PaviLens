/** Shared types and drawing rules for the maintenance map, whatever the map provider. */

export interface MapPoint {
  id: string; title: string; location?: string; status: string; pci?: number | null;
  distressCount: number; potholeCount: number; latitude: number; longitude: number; coordinateSource: 'gps' | 'segment';
}
export interface CrewStop {
  order: number; inspectionId: string; title: string; pci?: number | null; priority: string;
  latitude: number; longitude: number; legDistanceM: number; legDurationMin: number;
}
export interface CrewSitePlan { id: string; closure: string; window: string; rationale: string; }
export interface CrewPlanner {
  engine: string; model: string; orderBy: 'meta' | 'simulation' | 'code'; summary: string; sites: CrewSitePlan[];
  risks: string[]; communication: string[]; codeOrderDistanceM: number; orderChanged: boolean;
}
export interface CrewRoute {
  source: 'osm' | 'google' | 'estimate'; depot: { latitude: number; longitude: number }; stops: CrewStop[];
  totalDistanceM: number; totalDurationMin: number; encodedPolyline?: string | null; warnings: string[]; planner?: CrewPlanner | null;
}

export type LatLng = { lat: number; lng: number };
export type MapProviderId = 'osm' | 'google';

/** What the component needs from a map, so OpenStreetMap (Leaflet) and Google Maps are interchangeable. */
export interface MapAdapter {
  readonly id: MapProviderId;
  init(element: HTMLElement, center: LatLng): Promise<void>;
  setPoints(points: MapPoint[], order: Map<string, number>, onClick: (id: string) => void, fit: boolean): void;
  drawRoute(route: CrewRoute, path: LatLng[], fullClosure: (inspectionId: string) => boolean, labels: RouteLabels, reduceMotion: boolean): void;
  focus(point: LatLng): void;
  destroy(): void;
}

export interface RouteLabels { depot: string; work: string; }

export const PUNO: LatLng = { lat: -15.8402, lng: -70.0219 };

/** PCI colour, same bands as the rest of the app (ASTM D6433 ratings). */
export function pciColor(pci?: number | null): string {
  if (pci == null) return '#8a929c';
  return pci >= 85 ? '#2e7d32' : pci >= 70 ? '#7cb342' : pci >= 55 ? '#f2b705' : pci >= 40 ? '#ef6c00' : '#c62828';
}

/** A closure that takes the whole road (planner wording in Spanish or English). */
export function isFullClosure(closure?: string | null): boolean {
  return !!closure && /total|full/i.test(closure);
}

/** Traffic cone with the black-and-amber hatch of the original work-zone marker. */
export const CONE_SVG = 'data:image/svg+xml;charset=UTF-8,' + encodeURIComponent(
  `<svg xmlns="http://www.w3.org/2000/svg" width="30" height="34" viewBox="-15 -17 30 34">
    <defs><pattern id="h" width="5" height="5" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
      <rect width="5" height="5" fill="#f6b300"/><rect width="2.5" height="5" fill="#1b1f24"/></pattern></defs>
    <path d="M0,-15 L12,9 L-12,9 Z" fill="url(#h)" stroke="#1b1f24" stroke-width="1.5" stroke-linejoin="round"/>
    <rect x="-14" y="9" width="28" height="5" rx="1.5" fill="#1b1f24"/></svg>`);

/** Position and heading at fraction <paramref name="t"/> (0–1) of a path, measured along its length. */
export function alongPath(path: LatLng[], t: number): { point: LatLng; bearing: number } {
  if (path.length === 1) return { point: path[0], bearing: 0 };
  const seg = path.slice(1).map((p, i) => Math.hypot(p.lat - path[i].lat, (p.lng - path[i].lng) * Math.cos(p.lat * Math.PI / 180)));
  const total = seg.reduce((a, b) => a + b, 0) || 1;
  let left = Math.min(Math.max(t, 0), 1) * total;
  for (let i = 0; i < seg.length; i++) {
    if (left <= seg[i] || i === seg.length - 1) {
      const f = seg[i] ? Math.min(left / seg[i], 1) : 0;
      const a = path[i], b = path[i + 1];
      const bearing = Math.atan2((b.lng - a.lng) * Math.cos(a.lat * Math.PI / 180), b.lat - a.lat) * 180 / Math.PI;
      return { point: { lat: a.lat + (b.lat - a.lat) * f, lng: a.lng + (b.lng - a.lng) * f }, bearing };
    }
    left -= seg[i];
  }
  return { point: path[path.length - 1], bearing: 0 };
}
