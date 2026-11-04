import { GoogleNs } from './google-maps-loader';
import { CONE_SVG, CrewRoute, LatLng, MapAdapter, MapPoint, RouteLabels, pciColor } from './map-adapter';

/** Google Maps (optional). Same drawing as the OpenStreetMap adapter, with the Maps JavaScript API. */
export class GoogleAdapter implements MapAdapter {
  readonly id = 'google' as const;
  private map: GoogleNs | null = null;
  private markers: GoogleNs[] = [];
  private layers: GoogleNs[] = [];
  private timer?: ReturnType<typeof setInterval>;

  constructor(private google: GoogleNs) {}

  async init(element: HTMLElement, center: LatLng): Promise<void> {
    this.map = new this.google.maps.Map(element, {
      center, zoom: 15, mapTypeControl: false, streetViewControl: false, fullscreenControl: true,
      clickableIcons: false, gestureHandling: 'cooperative',
    });
  }

  setPoints(points: MapPoint[], order: Map<string, number>, onClick: (id: string) => void, fit: boolean) {
    if (!this.map) return;
    const g = this.google.maps;
    this.clear(this.markers);
    const bounds = new g.LatLngBounds();
    for (const p of points) {
      const n = order.get(p.id);
      const marker = new g.Marker({
        map: this.map, position: { lat: p.latitude, lng: p.longitude },
        title: `${p.title} · PCI ${p.pci != null ? Math.round(p.pci) : '—'}`,
        label: n ? { text: String(n), color: '#fff', fontWeight: '600', fontSize: '12px' } : undefined,
        icon: { path: g.SymbolPath.CIRCLE, scale: n ? 13 : 9, fillColor: pciColor(p.pci), fillOpacity: 1, strokeColor: '#fff', strokeWeight: 2 },
        zIndex: n ? 30 : 1,
      });
      marker.addListener('click', () => onClick(p.id));
      this.markers.push(marker);
      bounds.extend({ lat: p.latitude, lng: p.longitude });
    }
    if (fit && points.length) this.map.fitBounds(bounds, 60);
  }

  drawRoute(route: CrewRoute, path: LatLng[], fullClosure: (id: string) => boolean, labels: RouteLabels, reduceMotion: boolean) {
    if (!this.map) return;
    const g = this.google.maps;
    this.stop();
    this.clear(this.layers);
    const zones: { circle: GoogleNs; base: number }[] = [];

    for (const s of route.stops) {
      const full = fullClosure(s.inspectionId);
      const base = full ? 34 : 22;
      const circle = new g.Circle({
        map: this.map, center: { lat: s.latitude, lng: s.longitude }, radius: base,
        fillColor: full ? '#c62828' : '#f6b300', fillOpacity: 0.28, strokeColor: '#1b1f24', strokeOpacity: 0.9, strokeWeight: 2, clickable: false, zIndex: 5,
      });
      zones.push({ circle, base });
      this.layers.push(circle, new g.Marker({
        map: this.map, position: { lat: s.latitude, lng: s.longitude }, clickable: false, zIndex: 25, title: `${labels.work} ${s.order}`,
        icon: { url: CONE_SVG, scaledSize: new g.Size(26, 30), anchor: new g.Point(-6, 34) },
      }));
    }

    const glow = new g.Polyline({ map: this.map, path, strokeColor: '#4f8fd6', strokeOpacity: 0.25, strokeWeight: 12, clickable: false, zIndex: 8 });
    const line = new g.Polyline({ map: this.map, path: reduceMotion ? path : path.slice(0, 1), strokeColor: '#1d4f91', strokeOpacity: 0.9, strokeWeight: 4, clickable: false, zIndex: 9 });
    const flow = new g.Polyline({
      map: this.map, path, strokeOpacity: 0, clickable: false, zIndex: 10,
      icons: [{ icon: { path: 'M 0,-1 0,1', strokeOpacity: 1, strokeColor: '#ffffff', scale: 2.5 }, offset: '0', repeat: '18px' }],
    });
    const crew = new g.Polyline({
      map: this.map, path, strokeOpacity: 0, clickable: false, zIndex: 40,
      icons: [{ icon: { path: g.SymbolPath.FORWARD_CLOSED_ARROW, scale: 5, fillColor: '#1b1f24', fillOpacity: 1, strokeColor: '#fff', strokeWeight: 1.5 }, offset: '0%' }],
    });
    this.layers.push(glow, line, flow, crew, new g.Marker({
      map: this.map, position: { lat: route.depot.latitude, lng: route.depot.longitude }, title: labels.depot, zIndex: 35,
      icon: { path: g.SymbolPath.BACKWARD_CLOSED_ARROW, scale: 6, fillColor: '#1b1f24', fillOpacity: 1, strokeColor: '#fff', strokeWeight: 2 },
    }));

    const bounds = new g.LatLngBounds();
    path.forEach(p => bounds.extend(p));
    this.map.fitBounds(bounds, 60);
    if (reduceMotion) return;

    let shown = 1, tick = 0;
    const step = Math.max(1, Math.ceil(path.length / 40));
    this.timer = setInterval(() => {
      tick++;
      if (shown < path.length) { shown = Math.min(path.length, shown + step); line.setPath(path.slice(0, shown)); }
      const dash = flow.get('icons'); dash[0].offset = `${(tick * 1.5) % 18}px`; flow.set('icons', dash);
      const arrow = crew.get('icons'); arrow[0].offset = `${(tick * 0.25) % 100}%`; crew.set('icons', arrow);
      const pulse = 0.5 + 0.5 * Math.sin(tick / 6);
      for (const z of zones) { z.circle.setRadius(z.base * (0.85 + 0.3 * pulse)); z.circle.setOptions({ fillOpacity: 0.18 + 0.2 * pulse }); }
    }, 30);
  }

  focus(point: LatLng) { this.map?.panTo(point); this.map?.setZoom(17); }

  destroy() { this.stop(); this.clear(this.markers); this.clear(this.layers); this.map = null; }

  private stop() { if (this.timer) clearInterval(this.timer); this.timer = undefined; }
  private clear(list: GoogleNs[]) { list.forEach(o => o.setMap(null)); list.length = 0; }
}
