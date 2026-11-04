import { AfterViewInit, Component, ElementRef, OnDestroy, computed, inject, signal, viewChild } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { DecimalPipe } from '@angular/common';
import { TranslatePipe, I18n } from '../i18n/i18n';
import { GoogleMapsLoader, MapsConfig, decodePolyline } from './google-maps-loader';
import { GoogleAdapter } from './google-adapter';
import { LeafletAdapter } from './leaflet-adapter';
import { CrewRoute, LatLng, MapAdapter, MapPoint, MapProviderId, PUNO, isFullClosure, pciColor } from './map-adapter';

export type { CrewPlanner, CrewRoute, CrewSitePlan, CrewStop, MapPoint } from './map-adapter';
export { isFullClosure, pciColor } from './map-adapter';

const PROVIDER_KEY = 'pavelens.map';

@Component({
  selector: 'app-maintenance-map', standalone: true, imports: [DecimalPipe, TranslatePipe],
  templateUrl: './maintenance-map.html', styleUrl: './maintenance-map.css',
})
export class MaintenanceMap implements AfterViewInit, OnDestroy {
  private http = inject(HttpClient);
  private loader = inject(GoogleMapsLoader);
  i18n = inject(I18n);
  private canvas = viewChild<ElementRef<HTMLDivElement>>('canvas');

  points = signal<MapPoint[]>([]);
  selectedIds = signal<Set<string>>(new Set());
  priorityFirst = signal(true);
  route = signal<CrewRoute | null>(null);
  busy = signal(false);
  error = signal<string | null>(null);
  mapState = signal<'loading' | 'ready' | 'unavailable'>('loading');
  provider = signal<MapProviderId>('osm');
  config = signal<MapsConfig | null>(null);
  selectedCount = computed(() => this.selectedIds().size);
  sitePlans = computed(() => new Map(this.route()?.planner?.sites.map(s => [s.id, s]) ?? []));
  pciColor = pciColor;
  isFullClosure = isFullClosure;

  private adapter: MapAdapter | null = null;
  private destroyed = false;

  async ngAfterViewInit() {
    this.http.get<MapPoint[]>('/api/v1/maps/inspections').subscribe({
      next: points => {
        this.points.set(points);
        this.selectedIds.set(new Set(points.filter(p => p.status === 'Completed').map(p => p.id)));
        this.redraw(true);
      },
      error: () => this.error.set('No se pudieron cargar las inspecciones.'),
    });
    const config = await this.loader.config();
    this.config.set(config);
    const saved = this.readSaved();
    const wanted: MapProviderId = config.googleAvailable ? (saved ?? config.mapProvider) : 'osm';
    await this.useProvider(wanted);
  }

  ngOnDestroy() { this.destroyed = true; this.adapter?.destroy(); this.adapter = null; }

  /** Switches between OpenStreetMap and Google without losing the selection or the planned route. */
  async useProvider(id: MapProviderId) {
    if (id === 'google' && !this.config()?.googleAvailable) id = 'osm';
    if (this.adapter?.id === id && this.mapState() === 'ready') return;
    this.mapState.set('loading');
    this.adapter?.destroy();
    this.adapter = null;
    const el = this.canvas()?.nativeElement;
    if (!el) return;
    el.replaceChildren();

    let adapter: MapAdapter | null = null;
    try {
      if (id === 'google') {
        const google = await this.loader.google();
        if (google) adapter = new GoogleAdapter(google);
      }
      if (!adapter) {
        const c = this.config();
        adapter = new LeafletAdapter(c?.tileUrl ?? 'https://tile.openstreetmap.org/{z}/{x}/{y}.png', c?.tileAttribution ?? '© OpenStreetMap contributors');
      }
      await adapter.init(el, PUNO);
    } catch {
      this.mapState.set('unavailable');
      return;
    }
    if (this.destroyed) { adapter.destroy(); return; }
    this.adapter = adapter;
    this.provider.set(adapter.id);
    this.save(adapter.id);
    this.mapState.set('ready');
    this.redraw(!this.route());
  }

  toggle(id: string) {
    const next = new Set(this.selectedIds());
    if (next.has(id)) next.delete(id); else next.add(id);
    this.selectedIds.set(next);
  }

  selectAll(all: boolean) { this.selectedIds.set(new Set(all ? this.points().map(p => p.id) : [])); }

  plan() {
    const ids = [...this.selectedIds()];
    if (!ids.length) { this.error.set('Elige al menos una inspección para la ruta.'); return; }
    this.busy.set(true); this.error.set(null);
    const language = this.i18n.lang() === 'en' ? 'En' : 'Es';
    const map = this.provider() === 'google' ? 'Google' : 'Osm';
    this.http.post<CrewRoute>('/api/v1/maps/crew-route', { inspectionIds: ids, priorityFirst: this.priorityFirst(), language, map }).subscribe({
      next: route => { this.route.set(route); this.busy.set(false); this.redraw(false); },
      error: e => { this.busy.set(false); this.error.set(e?.status === 429 ? 'Demasiadas consultas seguidas. Espera un minuto.' : (e?.error?.detail ?? 'No se pudo calcular la ruta.')); },
    });
  }

  focus(p: { latitude: number; longitude: number }) { this.adapter?.focus({ lat: p.latitude, lng: p.longitude }); }

  /** Label for where the street distances came from. */
  sourceLabel(source: CrewRoute['source']): string {
    return source === 'google' ? 'Calles reales · Google Routes'
      : source === 'osm' ? 'Calles reales · OpenStreetMap (OSRM)' : 'Estimación en línea recta';
  }

  /** The app's own "Reducir movimiento" switch or the operating-system setting. */
  private get reduceMotion(): boolean {
    return document.documentElement.dataset['motion'] === 'reduce' || !!window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
  }

  private redraw(fit: boolean) {
    const a = this.adapter;
    if (!a) return;
    const route = this.route();
    const order = new Map(route?.stops.map(s => [s.inspectionId, s.order]) ?? []);
    a.setPoints(this.points(), order, id => this.toggle(id), fit && !route);
    if (!route) return;
    const path: LatLng[] = route.encodedPolyline
      ? decodePolyline(route.encodedPolyline)
      : [{ lat: route.depot.latitude, lng: route.depot.longitude }, ...route.stops.map(s => ({ lat: s.latitude, lng: s.longitude }))];
    a.drawRoute(route, path, id => isFullClosure(this.sitePlans().get(id)?.closure),
      { depot: this.i18n.t('Base de la cuadrilla'), work: this.i18n.t('Obra') }, this.reduceMotion);
  }

  private readSaved(): MapProviderId | null {
    try { const v = localStorage.getItem(PROVIDER_KEY); return v === 'google' || v === 'osm' ? v : null; } catch { return null; }
  }

  private save(id: MapProviderId) { try { localStorage.setItem(PROVIDER_KEY, id); } catch { /* private mode */ } }
}
