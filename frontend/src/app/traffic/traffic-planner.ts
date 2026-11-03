import { Component, computed, effect, inject, input, OnDestroy, signal, untracked } from '@angular/core';
import { DecimalPipe, NgClass } from '@angular/common';
import { AnalysisService } from '../services/analysis.service';
import { HttpErrorResponse } from '@angular/common/http';
import { LinkComparison, RoadNetwork, RoadSegment, TrafficPlan, TrafficSimulation, WorkPeriod } from '../models/analysis.model';
import { I18n, TranslatePipe } from '../i18n/i18n';

type MapMode = 'work' | 'base' | 'diff';

/** A chevron placed on the map: position and heading in degrees (0 = east, y grows downwards). */
interface Arrow {
  x: number;
  y: number;
  a: number;
}

/** One travel direction of a segment, drawn as its own offset band. */
interface Lane {
  key: string;
  d: string;
  width: number;
  color: string;
  flow: number;
  /** Seconds per dash cycle: slower when the link is congested. */
  speed: number;
  arrows: Arrow[];
}

/** A detour drawn on the map: beside the street, on the right of its travel direction, with arrows. */
interface RouteLine {
  id: string;
  number: number;
  name: string;
  vehicles: string | null;
  streets: string[];
  extraTimeMin: number;
  color: string;
  d: string;
  arrows: Arrow[];
  start: { x: number; y: number };
  head: Arrow;
}

/** Route colours; chosen to stay apart from the v/c scale (greens, yellow, orange, red). */
const ROUTE_COLORS = ['#7c3aed', '#0e7490', '#c026d3', '#1d4ed8'];

interface Street {
  id: string;
  name: string;
  x1: number; y1: number; x2: number; y2: number;
  mx: number; my: number;
  horizontal: boolean;
  width: number;
  lanes: Lane[];
  workZone: boolean;
  closedShare: number;
  detourRank: number;
  detourDelta: number;
  inspected: boolean;
  selected: boolean;
}

interface DirectionStats {
  label: string;
  baseFlow: number;
  workFlow: number;
  capacity: number;
  vc: number;
  baseMin: number;
  workMin: number;
  closed: boolean;
}

const PERIODS: { id: WorkPeriod; label: string; hours: string }[] = [
  { id: 'Peak', label: 'Hora punta', hours: '07–09 h · 17–19 h' },
  { id: 'OffPeak', label: 'Valle', hours: '09–17 h' },
  { id: 'Night', label: 'Noche', hours: '22–05 h' }
];

/** Street widths in map units (100 units = one 200 m block); exaggerated so flows stay legible. */
const ARTERIAL_W = 18;
const LOCAL_W = 11;

/**
 * Work-zone planner: a stylised map of the demo network with per-direction flows, the closure and its detour,
 * plus the controls and indicators to choose when and how to close a segment.
 */
@Component({
  selector: 'app-traffic-planner',
  standalone: true,
  imports: [DecimalPipe, NgClass, TranslatePipe],
  templateUrl: './traffic-planner.html',
  styleUrl: './traffic-planner.css'
})
export class TrafficPlanner implements OnDestroy {
  private api = inject(AnalysisService);
  private i18n = inject(I18n);

  network = input.required<RoadNetwork>();
  /** Segment of the inspection that motivated the closure, marked on the map. */
  inspectedSegment = input<string | null>(null);
  /** Segment preselected when the planner opens. */
  initialSegment = input<string | null>(null);

  readonly periods = PERIODS;

  segmentId = signal<string>('');
  lanesClosed = signal<number>(1);
  period = signal<WorkPeriod>('Peak');
  durationH = signal<number>(8);
  mode = signal<MapMode>('work');
  simulation = signal<TrafficSimulation | null>(null);
  isSimulating = signal<boolean>(false);
  error = signal<string | null>(null);
  hovered = signal<{ id: string; x: number; y: number } | null>(null);

  // Traffic management plan by Muse Wonder (on demand: it is a paid model call).
  plan = signal<TrafficPlan | null>(null);
  isPlanning = signal<boolean>(false);
  planError = signal<string | null>(null);
  showRoutes = signal<boolean>(true);

  private debounce?: ReturnType<typeof setTimeout>;
  private requestId = 0;

  constructor() {
    // Open on the inspection's segment, or the first one.
    effect(() => {
      const net = this.network();
      const initial = this.initialSegment();
      untracked(() => {
        const id = initial && net.segments.some(s => s.id === initial) ? initial : net.segments[0]?.id ?? '';
        if (id !== this.segmentId()) this.select(id);
      });
    });

    // Every change re-solves the equilibrium; it is fast enough to feel live.
    effect(() => {
      const segment = this.segmentId();
      const lanes = this.lanesClosed();
      const period = this.period();
      const duration = this.durationH();
      if (!segment) return;
      clearTimeout(this.debounce);
      this.debounce = setTimeout(() => this.run(segment, lanes, period, duration), 180);
    });
  }

  ngOnDestroy() {
    clearTimeout(this.debounce);
  }

  segment = computed<RoadSegment | null>(() => this.network().segments.find(s => s.id === this.segmentId()) ?? null);

  laneOptions = computed(() => Array.from({ length: this.segment()?.lanesPerDirection ?? 1 }, (_, i) => i + 1));

  /** Map frame: the network spans 0–300; blocks continue to the west and the lake opens to the east. */
  readonly viewBox = '-90 -70 520 440';

  /** Context grid lines (city continues beyond the modelled network). */
  readonly gridX = [-100, 0, 100, 200, 300];
  readonly gridY = [-100, 0, 100, 200, 300, 400];

  private nodes = computed(() => new Map(this.network().nodes.map(n => [n.id, n])));

  private linkIndex = computed(() => {
    const map = new Map<string, LinkComparison>();
    for (const l of this.simulation()?.links ?? []) map.set(`${l.from}>${l.to}`, l);
    return map;
  });

  streets = computed<Street[]>(() => {
    const net = this.network();
    const nodes = this.nodes();
    const sim = this.simulation();
    const links = this.linkIndex();
    const mode = this.mode();
    const detour = this.detourRows();
    const ranks = new Map(detour.map((r, i) => [r.id, { rank: i + 1, delta: r.delta }]));

    return net.segments.map(s => {
      const a = nodes.get(s.from)!;
      const b = nodes.get(s.to)!;
      const width = s.lanesPerDirection > 1 ? ARTERIAL_W : LOCAL_W;
      const lanes: Lane[] = [];
      for (const [from, to] of [[a, b], [b, a]] as const) {
        const link = links.get(`${from.id}>${to.id}`);
        lanes.push(this.lane(`${s.id}:${from.id}`, from, to, width, s, link, mode, !!sim));
      }
      const detourInfo = ranks.get(s.id);
      return {
        id: s.id,
        name: s.name,
        x1: a.x, y1: a.y, x2: b.x, y2: b.y,
        mx: (a.x + b.x) / 2, my: (a.y + b.y) / 2,
        horizontal: a.y === b.y,
        width,
        lanes,
        workZone: s.id === sim?.segmentId,
        closedShare: sim && s.id === sim.segmentId ? sim.lanesClosed / sim.lanesPerDirection : 0,
        detourRank: detourInfo?.rank ?? 0,
        detourDelta: detourInfo?.delta ?? 0,
        inspected: s.id === this.inspectedSegment(),
        selected: s.id === this.segmentId()
      };
    });
  });

  /** Street names, once per corridor, placed on the western and northern edges. */
  corridorLabels = computed(() => {
    const nodes = this.network().nodes;
    const seen = new Set<string>();
    const labels: { name: string; x: number; y: number; vertical: boolean }[] = [];
    for (const s of this.network().segments) {
      if (seen.has(s.name)) continue;
      seen.add(s.name);
      const a = nodes.find(n => n.id === s.from)!;
      const b = nodes.find(n => n.id === s.to)!;
      const vertical = a.x === b.x;
      labels.push(vertical ? { name: s.name, x: a.x, y: -38, vertical } : { name: s.name, x: -48, y: a.y, vertical });
    }
    return labels;
  });

  detourRows = computed(() => {
    const sim = this.simulation();
    if (!sim) return [];
    const names = new Map(this.network().segments.map(s => [s.id, s.name]));
    const bySegment = new Map<string, { base: number; work: number; vc: number }>();
    for (const l of sim.links) {
      const row = bySegment.get(l.segmentId) ?? { base: 0, work: 0, vc: 0 };
      row.base += l.baselineFlowVph;
      row.work += l.workZoneFlowVph;
      row.vc = Math.max(row.vc, l.workZoneCapacityVph ? l.workZoneFlowVph / l.workZoneCapacityVph : 0);
      bySegment.set(l.segmentId, row);
    }
    return sim.detourSegmentIds
      .map(id => {
        const r = bySegment.get(id)!;
        return { id, name: names.get(id) ?? id, base: r.base, work: r.work, delta: r.work - r.base, vc: r.vc };
      })
      .sort((a, b) => b.delta - a.delta);
  });

  maxDetourDelta = computed(() => Math.max(1, ...this.detourRows().map(r => r.delta)));

  windows = computed(() => {
    const sim = this.simulation();
    if (!sim) return [];
    const max = Math.max(1, ...sim.windows.map(w => w.userCostPen));
    return PERIODS.map(p => {
      const w = sim.windows.find(x => x.period === p.id)!;
      return { ...p, ...w, share: w.userCostPen / max, recommended: p.id === sim.recommendedPeriod };
    });
  });

  /** What moving the work to the cheapest window saves, when the chosen one is not it. */
  saving = computed(() => {
    const sim = this.simulation();
    if (!sim || sim.recommendedPeriod === sim.period) return null;
    const best = sim.windows.find(w => w.period === sim.recommendedPeriod)!;
    const pen = sim.userCostPen - best.userCostPen;
    return pen > 0 ? { label: this.periodLabel(best.period).toLowerCase(), pen, pct: (100 * pen) / sim.userCostPen } : null;
  });

  /** Displaced demand that had to find another path, from the closed segment's flow drop. */
  displacedVph = computed(() => {
    const sim = this.simulation();
    if (!sim) return 0;
    return sim.links
      .filter(l => l.segmentId === sim.segmentId)
      .reduce((sum, l) => sum + Math.max(0, l.baselineFlowVph - l.workZoneFlowVph), 0);
  });

  tooltip = computed(() => {
    const h = this.hovered();
    if (!h) return null;
    const s = this.network().segments.find(x => x.id === h.id);
    if (!s) return null;
    const nodes = this.nodes();
    const a = nodes.get(s.from)!;
    const b = nodes.get(s.to)!;
    const dirs: DirectionStats[] = [];
    for (const [from, to] of [[a, b], [b, a]] as const) {
      const l = this.linkIndex().get(`${from.id}>${to.id}`);
      if (!l) continue;
      dirs.push({
        label: this.heading(from.x, from.y, to.x, to.y),
        baseFlow: l.baselineFlowVph,
        workFlow: l.workZoneFlowVph,
        capacity: l.closed ? 0 : l.workZoneCapacityVph,
        vc: l.closed ? 0 : l.workZoneFlowVph / l.workZoneCapacityVph,
        baseMin: l.baselineTimeMin,
        workMin: l.workZoneTimeMin,
        closed: l.closed
      });
    }
    return { ...h, segment: s, dirs };
  });

  select(id: string) {
    this.segmentId.set(id);
    this.lanesClosed.set(1);
  }

  setLanes(n: number) {
    this.lanesClosed.set(n);
  }

  setDuration(value: string) {
    const n = Number(value);
    if (Number.isFinite(n) && n > 0) this.durationH.set(n);
  }

  onStreetEnter(id: string, event: MouseEvent) {
    const host = (event.currentTarget as Element).closest('.planner-map') as HTMLElement;
    const rect = host.getBoundingClientRect();
    this.hovered.set({ id, x: event.clientX - rect.left, y: event.clientY - rect.top });
  }

  periodLabel(p: WorkPeriod): string {
    return this.i18n.t(PERIODS.find(x => x.id === p)?.label ?? p);
  }

  vcClass(vc: number): string {
    return vc > 1 ? 'vc-5' : vc > 0.9 ? 'vc-4' : vc > 0.75 ? 'vc-3' : vc > 0.5 ? 'vc-2' : 'vc-1';
  }

  /** Plain-language name for a volume/capacity ratio, so the colour is never the only cue. */
  loadLabel(vc: number): string {
    return this.i18n.t(vc > 1 ? 'Colapsado' : vc > 0.9 ? 'Al límite' : vc > 0.75 ? 'Denso' : vc > 0.5 ? 'Moderado' : 'Fluido');
  }

  vcLegend(): string {
    return this.i18n.t('Nivel de tráfico = flujo / capacidad (v/c): fluido < 0,5 · moderado < 0,75 · denso < 0,9 · al límite ≤ 1 · colapsado > 1.');
  }

  /** Every segment by street name, for choosing the closure without the map (keyboard, screen readers). */
  segmentOptions = computed(() =>
    [...this.network().segments].sort((a, b) => a.name.localeCompare(b.name, 'es') || a.id.localeCompare(b.id))
  );

  /** One-sentence result, read out by screen readers whenever the simulation changes. */
  announcement = computed(() => {
    const sim = this.simulation();
    const seg = this.segment();
    if (!sim || !seg || this.isSimulating()) return '';
    const best = this.periodLabel(sim.recommendedPeriod).toLowerCase();
    return this.i18n.t(
      'Cierre en {street}, {period}, {hours} horas: costo para los usuarios {pen} soles, {n} calles reciben el desvío. Franja recomendada: {best}.',
      { street: seg.name, period: this.periodLabel(sim.period).toLowerCase(), hours: sim.durationH, pen: Math.round(sim.userCostPen), n: this.detourRows().length, best }
    );
  });

  /** The plan is shown on the map only while it matches the closure being displayed. */
  planIsCurrent = computed(() => {
    const p = this.plan()?.simulation;
    const s = this.simulation();
    return !!p && !!s && p.segmentId === s.segmentId && p.lanesClosed === s.lanesClosed && p.period === s.period && p.durationH === s.durationH;
  });

  /** Detours on the map: the AI plan's routes when current, otherwise the fastest path per direction. */
  routeLines = computed<RouteLine[]>(() => {
    const sim = this.simulation();
    if (!sim || !this.showRoutes()) return [];
    const plan = this.planIsCurrent() ? this.plan() : null;
    const names = new Map(this.network().segments.map(s => [s.id, s.name]));
    const source = plan
      ? plan.routes.map(r => ({ id: r.id, name: r.name, vehicles: r.vehicles, nodes: r.nodes, streets: r.streets, extra: r.extraTimeMin }))
      : sim.detourRoutes.map(r => ({
          id: r.id, name: r.label, vehicles: null, nodes: r.nodes,
          streets: [...new Set(r.segmentIds.map(id => names.get(id) ?? id))], extra: r.extraTimeMin
        }));
    return source.map((r, i) => ({ ...this.routeGeometry(r.nodes), id: r.id, number: i + 1, name: r.name, vehicles: r.vehicles,
      streets: r.streets, extraTimeMin: r.extra, color: ROUTE_COLORS[i % ROUTE_COLORS.length] }));
  });

  /** Signs from the AI plan, numbered and grouped per intersection. */
  signMarkers = computed(() => {
    const plan = this.plan();
    if (!plan || !this.planIsCurrent()) return [];
    const nodes = this.nodes();
    const byNode = new Map<string, number[]>();
    plan.signage.forEach((s, i) => byNode.set(s.nodeId, [...(byNode.get(s.nodeId) ?? []), i + 1]));
    return [...byNode].map(([id, numbers]) => ({ id, x: nodes.get(id)!.x - 14, y: nodes.get(id)!.y - 14, label: numbers.join('·') }));
  });

  /** "No entry" discs at both ends of a full closure. */
  closureEnds = computed(() => {
    const sim = this.simulation();
    if (!sim || sim.lanesClosed < sim.lanesPerDirection) return [];
    const seg = this.network().segments.find(s => s.id === sim.segmentId);
    if (!seg) return [];
    const a = this.nodes().get(seg.from)!;
    const b = this.nodes().get(seg.to)!;
    const len = Math.hypot(b.x - a.x, b.y - a.y);
    const ux = (b.x - a.x) / len;
    const uy = (b.y - a.y) / len;
    return [{ x: a.x + ux * 20, y: a.y + uy * 20 }, { x: b.x - ux * 20, y: b.y - uy * 20 }];
  });

  generatePlan() {
    const sim = this.simulation();
    if (!sim || this.isPlanning()) return;
    this.isPlanning.set(true);
    this.planError.set(null);
    this.api.plan(sim.segmentId, sim.lanesClosed, sim.period, sim.durationH, this.i18n.lang() === 'en' ? 'En' : 'Es').subscribe({
      next: plan => {
        this.plan.set(plan);
        this.isPlanning.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.isPlanning.set(false);
        this.planError.set(
          err.status === 429 ? this.i18n.t('Demasiadas consultas a la IA en un minuto. Espera un momento y vuelve a intentarlo.')
          : err.status === 502 || err.status === 503 ? this.i18n.t('El servicio de IA de Meta no respondió o devolvió un plan inválido. Revisa la clave API en el servidor e inténtalo de nuevo.')
          : err.error?.detail ?? this.i18n.t('No se pudo generar el plan.')
        );
      }
    });
  }

  routeColor(index: number): string {
    return ROUTE_COLORS[index % ROUTE_COLORS.length];
  }

  mapLabel = computed(() => {
    const seg = this.segment();
    return seg
      ? this.i18n.t('Mapa del centro de Puno con la obra en {street} y {n} calles de desvío numeradas. El mismo resultado está en el panel lateral.',
          { street: seg.name, n: this.detourRows().length })
      : this.i18n.t('Mapa de la red vial del centro de Puno');
  });

  private run(segment: string, lanes: number, period: WorkPeriod, duration: number) {
    const id = ++this.requestId;
    this.isSimulating.set(true);
    this.api.simulate(segment, lanes, period, duration).subscribe({
      next: result => {
        if (id !== this.requestId) return;
        this.simulation.set(result);
        this.error.set(null);
        this.isSimulating.set(false);
      },
      error: err => {
        if (id !== this.requestId) return;
        this.error.set(AnalysisService.describe(err, 'No se pudo resolver el equilibrio.'));
        this.isSimulating.set(false);
      }
    });
  }

  private lane(
    key: string,
    from: { x: number; y: number },
    to: { x: number; y: number },
    width: number,
    s: RoadSegment,
    link: LinkComparison | undefined,
    mode: MapMode,
    simulated: boolean
  ): Lane {
    const dx = to.x - from.x;
    const dy = to.y - from.y;
    const len = Math.hypot(dx, dy);
    const ux = dx / len;
    const uy = dy / len;
    // Right-hand traffic: each direction runs on the right of its travel direction (y grows downwards).
    const off = width / 4;
    const nx = -uy * off;
    const ny = ux * off;
    const trim = ARTERIAL_W / 2 + 2;
    const x1 = from.x + ux * trim + nx;
    const y1 = from.y + uy * trim + ny;
    const x2 = to.x - ux * trim + nx;
    const y2 = to.y - uy * trim + ny;
    const d = `M${x1.toFixed(1)},${y1.toFixed(1)}L${x2.toFixed(1)},${y2.toFixed(1)}`;

    const capacity = s.lanesPerDirection * s.capacityPerLaneVph;
    const maxWidth = width / 2 - 1.5;
    // Travel-direction chevrons at one and two thirds of the band.
    const angle = (Math.atan2(uy, ux) * 180) / Math.PI;
    const arrows: Arrow[] = [0.33, 0.67].map(t => ({ x: x1 + (x2 - x1) * t, y: y1 + (y2 - y1) * t, a: angle }));
    if (!simulated || !link) {
      return { key, d, width: 1.2, color: 'var(--map-idle)', flow: 0, speed: 0, arrows: [] };
    }

    const base = link.baselineFlowVph;
    const work = link.closed ? 0 : link.workZoneFlowVph;
    const free = (s.lengthKm / s.freeSpeedKmh) * 60;
    if (mode === 'diff') {
      const delta = work - base;
      const w = Math.min(maxWidth, 1 + Math.abs(delta) / 60);
      const color = Math.abs(delta) < 15 ? 'var(--map-idle)' : delta > 0 ? 'var(--flow-up)' : 'var(--flow-down)';
      return { key, d, width: w, color, flow: delta, speed: 0, arrows: Math.abs(delta) < 15 ? [] : arrows };
    }

    const flow = mode === 'base' ? base : work;
    const cap = mode === 'base' ? capacity : link.closed ? 0 : link.workZoneCapacityVph;
    const vc = cap > 0 ? flow / cap : 0;
    const minutes = mode === 'base' ? link.baselineTimeMin : link.workZoneTimeMin;
    return {
      key,
      d,
      width: flow <= 0 ? 0 : Math.min(maxWidth, 1.6 + flow / 110),
      color: `var(--${this.vcClass(vc)})`,
      flow,
      speed: flow <= 0 ? 0 : Math.min(6, 0.7 * Math.max(1, minutes / free)),
      arrows: flow <= 0 ? [] : arrows
    };
  }

  /**
   * Polyline through the route's intersections, shifted to the right of the travel direction and just outside
   * the asphalt, so the two directions of a detour never overlap. Arrows sit at the middle of every block.
   */
  private routeGeometry(nodeIds: string[]): Pick<RouteLine, 'd' | 'arrows' | 'start' | 'head'> {
    const nodes = this.nodes();
    const segs = this.network().segments;
    const points: { x: number; y: number }[] = [];
    const arrows: Arrow[] = [];
    let head: Arrow = { x: 0, y: 0, a: 0 };
    for (let i = 0; i < nodeIds.length - 1; i++) {
      const a = nodes.get(nodeIds[i])!;
      const b = nodes.get(nodeIds[i + 1])!;
      const seg = segs.find(s => (s.from === a.id && s.to === b.id) || (s.from === b.id && s.to === a.id));
      const off = (seg && seg.lanesPerDirection > 1 ? ARTERIAL_W : LOCAL_W) / 2 + 5;
      const len = Math.hypot(b.x - a.x, b.y - a.y);
      const ux = (b.x - a.x) / len;
      const uy = (b.y - a.y) / len;
      const nx = -uy * off;
      const ny = ux * off;
      const angle = (Math.atan2(uy, ux) * 180) / Math.PI;
      const first = i === 0 ? 14 : 0;
      const last = i === nodeIds.length - 2 ? 18 : 0;
      points.push({ x: a.x + nx + ux * first, y: a.y + ny + uy * first }, { x: b.x + nx - ux * last, y: b.y + ny - uy * last });
      arrows.push({ x: (a.x + b.x) / 2 + nx, y: (a.y + b.y) / 2 + ny, a: angle });
      if (i === nodeIds.length - 2) head = { x: b.x + nx - ux * last, y: b.y + ny - uy * last, a: angle };
    }
    const d = points.map((p, i) => `${i ? 'L' : 'M'}${p.x.toFixed(1)},${p.y.toFixed(1)}`).join('');
    return { d, arrows, start: points[0] ?? { x: 0, y: 0 }, head };
  }

  private heading(x1: number, y1: number, x2: number, y2: number): string {
    if (x1 === x2) return y2 > y1 ? 'Hacia el sur' : 'Hacia el norte';
    return x2 > x1 ? 'Hacia el este' : 'Hacia el oeste';
  }
}
