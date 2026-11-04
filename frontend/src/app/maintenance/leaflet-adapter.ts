import type * as Leaflet from 'leaflet';
import { CONE_SVG, CrewRoute, LatLng, MapAdapter, MapPoint, RouteLabels, alongPath, pciColor } from './map-adapter';

/**
 * OpenStreetMap with Leaflet: no key, no account, no Google. Leaflet and its stylesheet are loaded only when this
 * map is opened, so they do not weigh on the first page load. Animations are CSS on the SVG paths (styles.css,
 * "pl-" classes), the same dash flow as the original detour planner, and stop with "Reducir movimiento".
 */
export class LeafletAdapter implements MapAdapter {
  readonly id = 'osm' as const;
  private L!: typeof Leaflet;
  private map?: Leaflet.Map;
  private points?: Leaflet.LayerGroup;
  private route?: Leaflet.LayerGroup;
  private timer?: ReturnType<typeof setInterval>;

  constructor(private tileUrl: string, private attribution: string) {}

  async init(element: HTMLElement, center: LatLng): Promise<void> {
    LeafletAdapter.loadStylesheet();
    this.L = (await import('leaflet')).default ?? (await import('leaflet'));
    const L = this.L;
    this.map = L.map(element, { zoomControl: true, attributionControl: true, scrollWheelZoom: false }).setView([center.lat, center.lng], 15);
    // Wheel zoom only after a click on the map, so the page keeps scrolling normally.
    this.map.once('focus', () => this.map?.scrollWheelZoom.enable());
    L.tileLayer(this.tileUrl, {
      maxZoom: 19, attribution: `<a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener">${this.attribution}</a>`,
      crossOrigin: true, referrerPolicy: 'strict-origin-when-cross-origin',
    }).addTo(this.map);
    this.points = L.layerGroup().addTo(this.map);
    this.route = L.layerGroup().addTo(this.map);
  }

  setPoints(points: MapPoint[], order: Map<string, number>, onClick: (id: string) => void, fit: boolean) {
    if (!this.map || !this.points) return;
    const L = this.L;
    this.points.clearLayers();
    for (const p of points) {
      const n = order.get(p.id);
      const size = n ? 26 : 16;
      const marker = L.marker([p.latitude, p.longitude], {
        title: `${p.title} · PCI ${p.pci != null ? Math.round(p.pci) : '—'}`,
        zIndexOffset: n ? 1000 : 0, keyboard: true,
        icon: L.divIcon({
          className: 'pl-pin-wrap', iconSize: [size, size], iconAnchor: [size / 2, size / 2],
          html: `<span class="pl-pin${n ? ' numbered' : ''}" style="background:${pciColor(p.pci)}">${n ?? ''}</span>`,
        }),
      });
      marker.on('click', () => onClick(p.id));
      marker.addTo(this.points);
    }
    if (fit && points.length) this.map.fitBounds(L.latLngBounds(points.map(p => [p.latitude, p.longitude] as [number, number])), { padding: [50, 50] });
  }

  drawRoute(route: CrewRoute, path: LatLng[], fullClosure: (id: string) => boolean, labels: RouteLabels, reduceMotion: boolean) {
    if (!this.map || !this.route) return;
    const L = this.L;
    this.stop();
    this.route.clearLayers();
    const latlngs = path.map(p => [p.lat, p.lng] as [number, number]);

    // Work zones: pulsing halo (amber one lane, red full closure) and the hatched cone.
    for (const s of route.stops) {
      const full = fullClosure(s.inspectionId);
      L.circle([s.latitude, s.longitude], {
        radius: full ? 34 : 22, color: '#1b1f24', weight: 2, fillColor: full ? '#c62828' : '#f6b300', fillOpacity: 0.3,
        className: 'pl-zone', interactive: false,
      }).addTo(this.route);
      L.marker([s.latitude, s.longitude], {
        interactive: false, zIndexOffset: 900, title: `${labels.work} ${s.order}`,
        icon: L.icon({ iconUrl: CONE_SVG, iconSize: [24, 28], iconAnchor: [-4, 32] }),
      }).addTo(this.route);
    }

    // Route: glow, the line that draws itself, and dashes flowing towards the next site.
    L.polyline(latlngs, { color: '#4f8fd6', weight: 12, opacity: 0.25, interactive: false }).addTo(this.route);
    const line = L.polyline(latlngs, { color: '#1d4f91', weight: 4, opacity: 0.9, interactive: false, className: reduceMotion ? '' : 'pl-route-draw' }).addTo(this.route);
    L.polyline(latlngs, { color: '#ffffff', weight: 2.5, opacity: 1, interactive: false, className: reduceMotion ? 'pl-route-static' : 'pl-route-flow' }).addTo(this.route);
    const el = line.getElement() as SVGPathElement | undefined;
    if (el && !reduceMotion) el.style.setProperty('--len', `${Math.ceil(el.getTotalLength())}`);

    L.marker([route.depot.latitude, route.depot.longitude], {
      title: labels.depot, zIndexOffset: 1100, interactive: false,
      icon: L.divIcon({ className: 'pl-pin-wrap', iconSize: [22, 22], iconAnchor: [11, 11], html: '<span class="pl-depot">⌂</span>' }),
    }).addTo(this.route);

    const crew = L.marker(latlngs[0], {
      interactive: false, zIndexOffset: 1200,
      icon: L.divIcon({ className: 'pl-pin-wrap', iconSize: [20, 20], iconAnchor: [10, 10], html: '<span class="pl-crew">▲</span>' }),
    }).addTo(this.route);
    if (!reduceMotion && path.length > 1) {
      let t = 0;
      this.timer = setInterval(() => {
        t = (t + 0.0025) % 1;
        const at = alongPath(path, t);
        crew.setLatLng([at.point.lat, at.point.lng]);
        const icon = crew.getElement()?.querySelector<HTMLElement>('.pl-crew');
        if (icon) icon.style.transform = `rotate(${at.bearing}deg)`;
      }, 30);
    }
    this.map.fitBounds(L.latLngBounds(latlngs), { padding: [50, 50] });
  }

  focus(point: LatLng) { this.map?.setView([point.lat, point.lng], 17); }

  destroy() { this.stop(); this.map?.remove(); this.map = undefined; }

  private stop() { if (this.timer) clearInterval(this.timer); this.timer = undefined; }

  /** Leaflet's own stylesheet, copied to /vendor/leaflet by angular.json and added once. */
  private static loadStylesheet() {
    if (document.querySelector('link[data-leaflet]')) return;
    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = 'vendor/leaflet/leaflet.css';
    link.dataset['leaflet'] = '';
    document.head.appendChild(link);
  }
}
