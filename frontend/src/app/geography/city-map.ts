import { Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { TranslatePipe } from '../i18n/i18n';
import { AnalysisService } from '../services/analysis.service';

interface GeoNode { id: number; latitude: number; longitude: number; }
interface GeoEdge { id: string; wayId: number; from: number; to: number; name: string; lengthM: number; speedKmh: number; speedAssumed: boolean; forward: boolean; backward: boolean; }
interface GeoNetwork { id: string; retrievedAtUtc: string; latitude: number; longitude: number; radiusM: number; nodes: GeoNode[]; edges: GeoEdge[]; warnings: string[]; }
interface GeoClosure { closedEdgeId: string; routes: { direction: string; nodeIds: number[]; edgeIds: string[]; distanceM: number; referenceTimeMin: number }[]; warnings: string[]; }

@Component({ selector: 'app-city-map', standalone: true, imports: [FormsModule, DecimalPipe, TranslatePipe], templateUrl: './city-map.html', styleUrl: './city-map.css' })
export class CityMap implements OnDestroy {
  private http = inject(HttpClient);
  private request?: Subscription;
  private closureRequest?: Subscription;
  latitude = signal(-15.8402); longitude = signal(-70.0219); radius = signal(600);
  center = signal({ lat: -15.8402, lon: -70.0219 }); zoom = signal(16);
  network = signal<GeoNetwork | null>(null); closure = signal<GeoClosure | null>(null);
  selected = signal(''); filter = signal(''); busy = signal(false); routing = signal(false); error = signal<string | null>(null);
  readonly places = [ { name: 'Puno', lat: -15.8402, lon: -70.0219 }, { name: 'Lima', lat: -12.0464, lon: -77.0428 }, { name: 'Bogotá', lat: 4.5981, lon: -74.076 }, { name: 'Madrid', lat: 40.4168, lon: -3.7038 } ];
  private nodes = computed(() => new Map(this.network()?.nodes.map(n => [n.id, n]) ?? []));
  edges = computed(() => this.network()?.edges.map(e => ({ ...e, d: this.path([e.from, e.to]) })) ?? []);
  choices = computed(() => {
    const choices = this.network()?.edges.filter(e => e.name.toLocaleLowerCase().includes(this.filter().toLocaleLowerCase())).slice(0, 200) ?? [];
    const selected = this.network()?.edges.find(e => e.id === this.selected());
    return selected && !choices.some(e => e.id === selected.id) ? [selected, ...choices] : choices;
  });
  edge = computed(() => this.network()?.edges.find(e => e.id === this.selected()));
  routes = computed(() => this.closure()?.routes.map(r => ({ ...r, d: this.path(r.nodeIds) })) ?? []);
  tiles = computed(() => {
    const p = this.world(this.center().lat, this.center().lon);
    const n = 2 ** this.zoom();
    const left = p.x - 400, top = p.y - 300;
    const result: { key: string; url: string; x: number; y: number }[] = [];
    for (let x = Math.floor(left / 256); x <= Math.floor((left + 800) / 256); x++) {
      for (let y = Math.floor(top / 256); y <= Math.floor((top + 600) / 256); y++) {
        if (y < 0 || y >= n) continue;
        const wrapped = ((x % n) + n) % n;
        result.push({ key: `${this.zoom()}/${wrapped}/${y}`, url: `https://tile.openstreetmap.org/${this.zoom()}/${wrapped}/${y}.png`, x: x * 256 - left, y: y * 256 - top });
      }
    }
    return result;
  });

  place(place: {lat: number; lon: number}) {
    this.request?.unsubscribe(); this.closureRequest?.unsubscribe(); this.busy.set(false); this.routing.set(false);
    this.network.set(null); this.closure.set(null); this.selected.set(''); this.error.set(null);
    this.latitude.set(place.lat); this.longitude.set(place.lon); this.center.set({ lat: place.lat, lon: place.lon });
  }
  load() {
    if (!Number.isFinite(this.latitude()) || !Number.isFinite(this.longitude()) || Math.abs(this.latitude()) > 85 || Math.abs(this.longitude()) > 180) { this.error.set('Indica coordenadas válidas.'); return; }
    this.request?.unsubscribe(); this.closureRequest?.unsubscribe(); this.routing.set(false);
    this.busy.set(true); this.error.set(null); this.network.set(null); this.closure.set(null); this.selected.set('');
    this.center.set({ lat: this.latitude(), lon: this.longitude() });
    const params = new HttpParams().set('latitude', this.latitude()).set('longitude', this.longitude()).set('radiusM', this.radius());
    this.request = this.http.get<GeoNetwork>('/api/v1/geography/network', {params}).subscribe({
      next: network => { this.network.set(network); this.busy.set(false); },
      error: error => { this.busy.set(false); this.error.set(AnalysisService.describe(error, 'No se pudo cargar la red geográfica.')); }
    });
  }
  select(id: string) { this.closureRequest?.unsubscribe(); this.routing.set(false); this.selected.set(id); this.closure.set(null); this.error.set(null); }
  search(value: string) { this.filter.set(value); this.select(''); }
  closeRoad() {
    const network = this.network(); if (!network || !this.selected()) return;
    this.closureRequest?.unsubscribe(); this.routing.set(true); this.error.set(null);
    this.closureRequest = this.http.post<GeoClosure>(`/api/v1/geography/networks/${network.id}/closure`, {edgeId: this.selected()}).subscribe({
      next: result => { this.closure.set(result); this.routing.set(false); },
      error: error => { this.routing.set(false); this.error.set(AnalysisService.describe(error, 'No se pudo calcular el desvío.')); }
    });
  }
  zoomBy(delta: number) { this.zoom.update(z => Math.max(13, Math.min(18, z + delta))); }
  pan(dx: number, dy: number) {
    const c = this.center(); const p = this.world(c.lat, c.lon); const size = 256 * 2 ** this.zoom();
    const x = p.x + dx, y = Math.max(0, Math.min(size, p.y + dy));
    const next = { lon: ((x / size * 360 - 180 + 540) % 360) - 180, lat: Math.max(-85, Math.min(85, Math.atan(Math.sinh(Math.PI * (1 - 2 * y / size))) * 180 / Math.PI)) };
    this.center.set(next); this.latitude.set(next.lat); this.longitude.set(next.lon);
  }
  onKey(event: KeyboardEvent) {
    if (event.target !== event.currentTarget) return;
    const moves: Record<string, [number, number]> = { ArrowLeft: [-100,0], ArrowRight: [100,0], ArrowUp: [0,-100], ArrowDown: [0,100] };
    if (moves[event.key]) this.pan(...moves[event.key]);
    else if (event.key === '+' || event.key === '=') this.zoomBy(1);
    else if (event.key === '-') this.zoomBy(-1); else return;
    event.preventDefault();
  }
  private world(lat: number, lon: number) {
    const size = 256 * 2 ** this.zoom(); const rad = Math.max(-85, Math.min(85, lat)) * Math.PI / 180;
    return { x: (lon + 180) / 360 * size, y: (1 - Math.log(Math.tan(rad) + 1 / Math.cos(rad)) / Math.PI) / 2 * size };
  }
  private path(ids: number[]) {
    const c = this.world(this.center().lat, this.center().lon);
    return ids.map((id, i) => { const n = this.nodes().get(id); if (!n) return ''; const p = this.world(n.latitude, n.longitude); return `${i ? 'L' : 'M'}${(p.x-c.x+400).toFixed(2)},${(p.y-c.y+300).toFixed(2)}`; }).join(' ');
  }
  ngOnDestroy() { this.request?.unsubscribe(); this.closureRequest?.unsubscribe(); }
}
