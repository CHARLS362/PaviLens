import { Component, ElementRef, OnInit, OnDestroy, signal, computed, inject, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { EMPTY, Subscription, catchError, switchMap, takeWhile, timer } from 'rxjs';
import { AnalysisService } from './services/analysis.service';
import { TrafficPlanner } from './traffic/traffic-planner';
import { CaptureDialog } from './capture/capture-dialog';
import { AgentRunPanel } from './agents/agent-run-panel';
import { CityMap } from './geography/city-map';
import { RegionalConfiguration } from './models/analysis.model';
import { homography } from './capture/homography';
import { I18n, TranslatePipe } from './i18n/i18n';
import { A11yPanel } from './a11y/a11y-panel';
import { DEDUCT_LABELS, TYPE_LABELS, isCrack } from './labels';
import {
  AiStatus,
  AnalysisStage,
  Budget,
  ConversationMessage,
  DeductEntry,
  Distress,
  DistressType,
  InspectionDetail,
  InspectionStatus,
  InspectionSummary,
  RepairStep,
  NewInspection,
  RoadNetwork,
  Severity
} from './models/analysis.model';

type Toast = { kind: 'error' | 'info'; text: string };
type Tab = 'distress' | 'budget' | 'traffic' | 'steps';
type View = 'inspections' | 'planner' | 'geography';

const PIPELINE_STAGES = [
  'Imagen recibida · en cola',
  'SAM 3 · segmentando deterioros',
  'Geometría · proyección a metros',
  'Muse Wonder · clasificación y severidad',
  'PCI · cubicaje · APU'
];

// Index into PIPELINE_STAGES for each stage the worker reports while processing.
const STAGE_INDEX: Record<AnalysisStage, number> = { Segmenting: 1, Measuring: 2, Reasoning: 3, Computing: 4 };

const POLL_MS = 800;

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule, TrafficPlanner, CaptureDialog, TranslatePipe, A11yPanel, AgentRunPanel, CityMap],
  templateUrl: './app.html',
})
export class App implements OnInit, OnDestroy {
  regions = signal<RegionalConfiguration[]>([]);
  private api = inject(AnalysisService);
  readonly i18n = inject(I18n);

  readonly pipelineStages = PIPELINE_STAGES;

  // State signals
  analyses = signal<InspectionSummary[]>([]);
  selectedAnalysis = signal<InspectionDetail | null>(null);
  selectedDistress = signal<Distress | null>(null);
  hoveredDistressId = signal<string | null>(null);
  activeTab = signal<Tab>('distress');
  module = signal<View>('inspections');
  /** Segment the planner opens on. */
  plannerSegment = signal<string | null>(null);
  aiStatus = signal<AiStatus | null>(null);
  isLoading = signal<boolean>(false);
  isLoadingDetail = signal<boolean>(false);
  isUploading = signal<boolean>(false);
  /** Real progress: the worker persists the stage it is in, and polling picks it up. */
  pipelineStage = computed(() => {
    const current = this.selectedAnalysis();
    return current?.status === 'Processing' && current.stage ? STAGE_INDEX[current.stage] : 0;
  });
  showUploadModal = signal<boolean>(false);
  backendOnline = signal<boolean>(false);
  showCracks = signal<boolean>(true);
  showLabels = signal<boolean>(true);
  showCalibration = signal<boolean>(true);
  cursor = signal<{ x: number; y: number } | null>(null);
  /** Viewer zoom: stage transform translate(x, y) scale(s) with origin at its top-left corner. */
  view = signal({ s: 1, x: 0, y: 0 });
  isPanning = signal<boolean>(false);
  private pan?: { startX: number; startY: number; x: number; y: number; moved: boolean };
  /** Set when a drag ends, so the click it produces does not select the mask under the pointer. */
  private dragged = false;
  /** True while the view animates onto a distress picked from a table or the protocol. */
  isFlying = signal<boolean>(false);
  private flyTimer?: ReturnType<typeof setTimeout>;
  private canvasRef = viewChild<ElementRef<HTMLElement>>('canvas');
  /** Pointer inside the viewer, in canvas pixels; drives the mask tooltip. */
  pointer = signal<{ x: number; y: number; w: number } | null>(null);
  maskTip = computed(() => {
    const p = this.pointer();
    const id = this.hoveredDistressId();
    const d = id && p && !this.isPanning() ? this.selectedAnalysis()?.distresses.find(x => x.id === id) : undefined;
    return d && p ? { d, x: p.x, y: p.y, flip: p.x > p.w - 220 } : null;
  });
  private deleteDialog = viewChild<ElementRef<HTMLDialogElement>>('deleteDialog');
  toast = signal<Toast | null>(null);

  // Field depth editor
  depthCm = signal<number | null>(null);
  isSavingDepth = signal<boolean>(false);

  // Budget (computed on the server from the stored quantities)
  budget = signal<Budget | null>(null);
  isLoadingBudget = signal<boolean>(false);

  // Traffic
  network = signal<RoadNetwork | null>(null);

  // Assistant console (persisted per distress on the server)
  userQuestion = signal<string>('');
  messages = signal<ConversationMessage[]>([]);
  pendingQuestion = signal<string | null>(null);
  isAsking = signal<boolean>(false);

  private toastTimer?: ReturnType<typeof setTimeout>;
  private poll?: Subscription;

  isAnalyzing = computed(() => this.isUploading() || this.isRunning(this.selectedAnalysis()?.status));

  isSimulation = computed(() => this.selectedAnalysis()?.engine === 'Simulation');

  sortedSteps = computed(() =>
    [...(this.selectedAnalysis()?.repairSteps ?? [])].sort((a, b) => a.order - b.order)
  );

  completedSteps = computed(() => this.sortedSteps().filter(s => s.completedAt).length);

  counts = computed(() => {
    const list = this.selectedAnalysis()?.distresses ?? [];
    return {
      total: list.length,
      potholes: list.filter(d => d.type === 'Pothole').length,
      high: list.filter(d => d.severity === 'High').length,
      massT: list.reduce((sum, d) => sum + (d.quantities?.massT ?? 0), 0)
    };
  });

  /** Patchable surface (potholes, alligator cracking, rutting) in m². */
  affectedAreaM2 = computed(() =>
    (this.selectedAnalysis()?.distresses ?? [])
      .filter(d => !this.isCrack(d.type))
      .reduce((sum, d) => sum + d.measurement.areaM2, 0)
  );

  /** Image → ground mapping of the selected photo, mirroring the API's projection. */
  private groundMap = computed(() => {
    const a = this.selectedAnalysis();
    if (!a) return null;
    if (a.calibration) {
      const { corners, widthM, lengthM } = a.calibration;
      return homography(corners, [[0, 0], [widthM, 0], [widthM, lengthM], [0, lengthM]]);
    }
    const w = a.referenceWidthM;
    const h = w * (a.image.height / a.image.width);
    return (x: number, y: number): [number, number] => [x * w, y * h];
  });

  /** Cursor position on the road surface, in metres. Null above the horizon of an oblique photo. */
  cursorGround = computed(() => {
    const c = this.cursor();
    const map = this.groundMap();
    if (!c || !map) return null;
    const p = map(c.x, c.y);
    return p && Number.isFinite(p[0]) ? p : null;
  });

  /** 1 m scale bar as a percentage of the image width; nadir photos only (an oblique scale varies). */
  scaleBar = computed(() => {
    const a = this.selectedAnalysis();
    if (!a || a.calibration) return null;
    const metres = a.referenceWidthM > 6 ? 2 : a.referenceWidthM > 2.5 ? 1 : 0.5;
    return { metres, pct: (metres / a.referenceWidthM) * 100 };
  });

  visibleDistresses = computed(() => {
    const list = this.selectedAnalysis()?.distresses ?? [];
    const selectedId = this.selectedDistress()?.id;
    return this.showCracks() ? list : list.filter(d => !this.isCrack(d.type) || d.id === selectedId);
  });

  /** Road segment of the selected inspection, with its name. */
  inspectionSegment = computed(() => {
    const id = this.selectedAnalysis()?.roadSegmentId;
    return id ? this.network()?.segments.find(s => s.id === id) ?? null : null;
  });

  ngOnInit() {
    this.api.regions().subscribe({ next: profiles => this.regions.set(profiles), error: () => this.regions.set([]) });
    this.loadAnalyses();
    this.api.network().subscribe({ next: n => this.network.set(n), error: () => this.network.set(null) });
    this.api.aiStatus().subscribe({ next: st => this.aiStatus.set(st), error: () => this.aiStatus.set(null) });
  }

  ngOnDestroy() {
    clearTimeout(this.toastTimer);
    this.poll?.unsubscribe();
  }

  loadAnalyses() {
    this.isLoading.set(true);
    this.api.list().subscribe({
      next: page => {
        this.analyses.set(page.items);
        this.backendOnline.set(true);
        if (page.items.length > 0 && !this.selectedAnalysis()) {
          this.selectAnalysis(page.items[0]);
        }
        this.isLoading.set(false);
      },
      error: err => {
        console.warn('API not reachable:', err);
        this.backendOnline.set(false);
        this.isLoading.set(false);
      }
    });
  }

  selectAnalysis(summary: InspectionSummary) {
    if (this.selectedAnalysis()?.id === summary.id) return;
    this.isLoadingDetail.set(true);
    this.budget.set(null);
    this.watch(summary.id);
  }

  /** Loads an inspection and keeps polling while the worker is still analysing it. */
  private watch(id: string) {
    this.poll?.unsubscribe();
    let first = true;
    let failures = 0;
    this.poll = timer(0, POLL_MS)
      .pipe(
        switchMap(() =>
          this.api.get(id).pipe(
            // Swallow transient errors so polling survives a network hiccup, also on the first load
            // (a just-created inspection must keep updating); a 404 means it is gone, so stop at once.
            catchError(err => {
              failures++;
              if ((err instanceof HttpErrorResponse && err.status === 404) || failures >= 5) throw err;
              return EMPTY;
            })
          )
        ),
        takeWhile(detail => this.isRunning(detail.status), true)
      )
      .subscribe({
        next: detail => {
          failures = 0;
          const wasRunning = this.isRunning(this.selectedAnalysis()?.status) && this.selectedAnalysis()?.id === id;
          this.applyAnalysis(detail, first || (wasRunning && !this.isRunning(detail.status)));
          this.syncSummary(detail);
          if (wasRunning && detail.status === 'Failed') {
            this.showToast('error', detail.failureReason ?? 'El análisis falló.');
          }
          first = false;
          this.isLoadingDetail.set(false);
        },
        error: err => {
          this.isLoadingDetail.set(false);
          this.showToast('error', AnalysisService.describe(err, 'No se pudo cargar la inspección.'));
        }
      });
  }

  private applyAnalysis(detail: InspectionDetail, reset: boolean) {
    this.selectedAnalysis.set(detail);
    if (!reset) return;

    this.resetView();
    this.selectedDistress.set(null);
    const worst = this.mostSevere(detail.distresses);
    if (worst) this.selectDistress(worst, false);
    else this.messages.set([]);
    if (this.activeTab() === 'budget') this.loadBudget();
  }

  private syncSummary(d: InspectionDetail) {
    this.analyses.update(list =>
      list.map(s =>
        s.id === d.id
          ? {
              ...s,
              status: d.status,
              engine: d.engine,
              distressCount: d.distresses.length,
              potholeCount: d.distresses.filter(x => x.type === 'Pothole').length,
              pci: d.pci?.pci
            }
          : s
      )
    );
  }

  isRunning(status?: InspectionStatus): boolean {
    return status === 'Pending' || status === 'Processing';
  }

  isCrack(type: DistressType): boolean {
    return isCrack(type);
  }

  private mostSevere(list: Distress[]): Distress | null {
    const rank = (s: Severity) => (s === 'High' ? 0 : s === 'Medium' ? 1 : 2);
    return [...list].sort((a, b) => rank(a.severity) - rank(b.severity) || b.measurement.areaM2 - a.measurement.areaM2)[0] ?? null;
  }

  setTab(tab: Tab) {
    this.activeTab.set(tab);
    if (tab === 'budget' && !this.budget()) this.loadBudget();
  }

  /** WAI-ARIA tabs pattern: arrows, Home and End move between tabs and focus follows. */
  onTabKey(event: KeyboardEvent) {
    const order: Tab[] = ['distress', 'budget', 'traffic', 'steps'];
    const i = order.indexOf(this.activeTab());
    const next =
      event.key === 'ArrowRight' ? order[(i + 1) % order.length]
      : event.key === 'ArrowLeft' ? order[(i + order.length - 1) % order.length]
      : event.key === 'Home' ? order[0]
      : event.key === 'End' ? order[order.length - 1]
      : null;
    if (!next) return;
    event.preventDefault();
    this.setTab(next);
    queueMicrotask(() => document.getElementById(`tab-${next}`)?.focus());
  }

  skipToMain(event: Event) {
    event.preventDefault();
    document.getElementById('main')?.focus();
  }

  selectDistress(d: Distress, switchTab = true) {
    const changed = this.selectedDistress()?.id !== d.id;
    this.selectedDistress.set(d);
    this.depthCm.set(d.quantities ? Math.round(d.quantities.depthM * 1000) / 10 : null);
    if (switchTab) this.activeTab.set('distress');
    if (changed) this.loadConversation(d);
  }

  private loadConversation(d: Distress) {
    const current = this.selectedAnalysis();
    this.messages.set([]);
    this.pendingQuestion.set(null);
    if (!current) return;
    this.api.conversation(current.id, d.id).subscribe({
      next: list => {
        if (this.selectedDistress()?.id === d.id) this.messages.set(list);
      },
      error: () => this.messages.set([])
    });
  }

  focusStepTarget(step: RepairStep) {
    const d = this.distressForStep(step);
    if (d) this.showOnPhoto(d, false);
  }

  /** Selects a distress and zooms the photo onto it, so a pick from a table or the protocol is visible. */
  showOnPhoto(d: Distress, switchTab = true) {
    this.selectDistress(d, switchTab);
    const canvas = this.canvasRef()?.nativeElement;
    const stage = canvas?.querySelector<HTMLElement>('.stage');
    if (!canvas || !stage || !stage.offsetWidth) return;
    const w = stage.offsetWidth;
    const h = stage.offsetHeight;
    const { x, y, width, height } = d.box;
    // The box fills about half the view: close enough to read the outline, wide enough to keep context.
    const fit = 0.5 * Math.min(canvas.clientWidth / Math.max(width * w, 1), canvas.clientHeight / Math.max(height * h, 1));
    const s = Math.min(Math.max(fit, 1.5), 6);
    const cx = (x + width / 2) * w;
    const cy = (y + height / 2) * h;
    this.isFlying.set(true);
    clearTimeout(this.flyTimer);
    this.flyTimer = setTimeout(() => this.isFlying.set(false), 400);
    this.view.set({ s, x: canvas.clientWidth / 2 - stage.offsetLeft - s * cx, y: canvas.clientHeight / 2 - stage.offsetTop - s * cy });
    canvas.scrollIntoView({ block: 'nearest' });
  }

  distressForStep(step: RepairStep): Distress | undefined {
    if (!step.targetCode) return undefined;
    return this.selectedAnalysis()?.distresses.find(d => d.code === step.targetCode);
  }

  /** Outline plus holes as one SVG path; the even-odd fill leaves intact pavement inside the mask uncovered. */
  maskPath(d: Distress): string {
    const ring = (points: [number, number][]) => 'M' + points.map(([x, y]) => `${x * 100},${y * 100}`).join(' L') + ' Z';
    if (d.mask?.length >= 3) {
      return [d.mask, ...(d.holes ?? []).filter(h => h.length >= 3)].map(ring).join(' ');
    }
    const { x, y, width, height } = d.box;
    return ring([[x, y], [x + width, y], [x + width, y + height], [x, y + height]]);
  }

  /** Calibration rectangle drawn over oblique photos. */
  calibrationPoints(a: InspectionDetail): string {
    return a.calibration?.corners.map(([x, y]) => `${x * 100},${y * 100}`).join(' ') ?? '';
  }

  severityClass(s: Severity): string {
    return s === 'High' ? 'sev-high' : s === 'Medium' ? 'sev-medium' : 'sev-low';
  }

  severityLabel(s: Severity): string {
    return this.i18n.t(s === 'High' ? 'Alta' : s === 'Medium' ? 'Media' : 'Baja');
  }

  typeLabel(t: DistressType): string {
    return this.i18n.t(TYPE_LABELS[t] ?? t);
  }

  deductLabel(d: DeductEntry): string {
    return this.i18n.t(DEDUCT_LABELS[d.distress] ?? d.distress);
  }

  unitLabel(d: DeductEntry): string {
    return d.unit === 'SquareMetres' ? 'm²' : d.unit === 'Metres' ? 'm' : this.i18n.t('und');
  }

  /** Same bands as the API's PciCalculator.Classify. */
  pciClass(pci?: number | null): string {
    if (pci == null) return 'pci-none';
    return pci >= 85 ? 'pci-a' : pci >= 70 ? 'pci-b' : pci >= 55 ? 'pci-c' : pci >= 40 ? 'pci-d' : 'pci-e';
  }

  orientationLabel(deg: number): string {
    const a = Math.abs(deg);
    return this.i18n.t(a >= 60 ? 'longitudinal' : a <= 30 ? 'transversal' : 'diagonal');
  }

  statusLabel(status: InspectionStatus): string {
    switch (status) {
      case 'Pending':
        return this.i18n.t('En cola');
      case 'Processing':
        return this.i18n.t('Analizando');
      case 'Failed':
        return this.i18n.t('Falló');
      default:
        return '';
    }
  }

  /** Who produced a result: the real Meta models, or the labelled simulation fixtures. */
  source(kind: 'reasoning' | 'segmentation'): string {
    if (this.isSimulation()) return this.i18n.t('Simulación');
    return kind === 'segmentation' ? 'SAM 3' : 'Muse Wonder';
  }

  riskLabel(risk: string): string {
    return this.i18n.t(risk === 'High' ? 'Alto' : risk === 'Medium' ? 'Medio' : 'Bajo');
  }

  onViewerWheel(event: WheelEvent) {
    event.preventDefault();
    const stage = (event.currentTarget as HTMLElement).querySelector('.stage') as HTMLElement | null;
    if (!stage) return;
    this.zoomAt(event.deltaY < 0 ? 1.25 : 1 / 1.25, stage, event.clientX, event.clientY);
  }

  zoomBy(factor: number, canvas: HTMLElement) {
    const stage = canvas.querySelector('.stage') as HTMLElement | null;
    const rect = canvas.getBoundingClientRect();
    if (stage) this.zoomAt(factor, stage, rect.left + rect.width / 2, rect.top + rect.height / 2);
  }

  resetView() {
    this.view.set({ s: 1, x: 0, y: 0 });
  }

  /** Keyboard for the focused viewer: + and − zoom, 0 resets, arrows pan the zoomed photo. */
  onViewerKey(event: KeyboardEvent, canvas: HTMLElement) {
    if (event.target !== canvas || event.ctrlKey || event.metaKey || event.altKey) return;
    const step = event.shiftKey ? 120 : 40;
    const pan: Record<string, [number, number]> = {
      ArrowLeft: [step, 0], ArrowRight: [-step, 0], ArrowUp: [0, step], ArrowDown: [0, -step]
    };
    if (event.key === '+' || event.key === '=') this.zoomBy(1.5, canvas);
    else if (event.key === '-' || event.key === '_') this.zoomBy(1 / 1.5, canvas);
    else if (event.key === '0') this.resetView();
    else if (pan[event.key] && this.view().s > 1) {
      const [dx, dy] = pan[event.key];
      this.view.update(v => ({ ...v, x: v.x + dx, y: v.y + dy }));
    } else return;
    event.preventDefault();
  }

  /** Spoken name of a distress for its label button and the selection announcement. */
  distressLabel(d: Distress): string {
    const size = isCrack(d.type)
      ? `${d.measurement.lengthM.toFixed(2)} m`
      : `${d.measurement.areaM2.toFixed(2)} m²`;
    return this.i18n.t('{code}: {type}, severidad {sev}, {size}', {
      code: d.code, type: this.typeLabel(d.type), sev: this.severityLabel(d.severity).toLowerCase(), size
    });
  }

  /** Read by screen readers when the selected distress or the zoom changes. */
  viewerAnnouncement = computed(() => {
    const d = this.selectedDistress();
    const zoom = Math.round(this.view().s * 100);
    const selected = d ? this.i18n.t('Seleccionado {name}.', { name: this.distressLabel(d) }) : '';
    return `${selected} ${this.i18n.t('Zoom {z} %', { z: zoom })}`.trim();
  });

  /** Zooms keeping the image point under (clientX, clientY) fixed on screen. */
  private zoomAt(factor: number, stage: HTMLElement, clientX: number, clientY: number) {
    const v = this.view();
    const s = Math.min(Math.max(v.s * factor, 1), 10);
    if (s === 1) return this.resetView();
    const rect = stage.getBoundingClientRect();
    const px = (clientX - rect.left) / v.s;
    const py = (clientY - rect.top) / v.s;
    this.view.set({ s, x: v.x + (v.s - s) * px, y: v.y + (v.s - s) * py });
  }

  onViewerDown(event: MouseEvent) {
    this.dragged = false;
    if (event.button !== 0 || this.view().s === 1) return;
    const v = this.view();
    this.pan = { startX: event.clientX, startY: event.clientY, x: v.x, y: v.y, moved: false };
  }

  onViewerMove(event: MouseEvent) {
    const canvas = event.currentTarget as HTMLElement;
    const rect = canvas.getBoundingClientRect();
    this.pointer.set({ x: event.clientX - rect.left, y: event.clientY - rect.top, w: rect.width });
    if (!this.pan) return;
    const dx = event.clientX - this.pan.startX;
    const dy = event.clientY - this.pan.startY;
    if (!this.pan.moved && Math.hypot(dx, dy) < 3) return;
    this.pan.moved = true;
    this.isPanning.set(true);
    this.view.update(v => ({ ...v, x: this.pan!.x + dx, y: this.pan!.y + dy }));
  }

  onViewerUp() {
    this.dragged = !!this.pan?.moved;
    this.pan = undefined;
    this.isPanning.set(false);
  }

  // Touch: two fingers pinch around their midpoint, one finger pans once zoomed. Unzoomed, one finger
  // scrolls the page (touch-action in CSS), so the photo never traps the scroll on a phone.
  private touch?: { dist: number; midX: number; midY: number; view: { s: number; x: number; y: number }; moved: boolean };

  onViewerTouchStart(event: TouchEvent) {
    const t = event.touches;
    if (t.length === 2) {
      this.touch = { ...this.touchFrame(t), view: this.view(), moved: false };
    } else if (t.length === 1 && this.view().s > 1) {
      this.touch = { dist: 0, midX: t[0].clientX, midY: t[0].clientY, view: this.view(), moved: false };
    } else {
      this.touch = undefined;
    }
  }

  onViewerTouchMove(event: TouchEvent) {
    const start = this.touch;
    const t = event.touches;
    if (!start || (t.length === 2) !== start.dist > 0) return;
    event.preventDefault();
    start.moved = true;
    this.pointer.set(null);
    const v = start.view;
    if (t.length === 1) {
      this.view.set({ ...v, x: v.x + t[0].clientX - start.midX, y: v.y + t[0].clientY - start.midY });
      return;
    }
    const now = this.touchFrame(t);
    const s = Math.min(Math.max((v.s * now.dist) / start.dist, 1), 10);
    if (s === 1) return this.resetView();
    // Keep the photo point that was under the starting midpoint under the fingers as they move.
    const stage = (event.currentTarget as HTMLElement).querySelector<HTMLElement>('.stage');
    if (!stage) return;
    const left = stage.offsetLeft;
    const top = stage.offsetTop;
    const canvas = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const px = (start.midX - canvas.left - left - v.x) / v.s;
    const py = (start.midY - canvas.top - top - v.y) / v.s;
    this.view.set({ s, x: now.midX - canvas.left - left - s * px, y: now.midY - canvas.top - top - s * py });
  }

  onViewerTouchEnd(event: TouchEvent) {
    // Lifting one finger of a pinch hands over to a one-finger pan from where the view is now.
    if (event.touches.length === 1 && this.view().s > 1) {
      const t = event.touches[0];
      this.touch = { dist: 0, midX: t.clientX, midY: t.clientY, view: this.view(), moved: !!this.touch?.moved };
    } else if (!event.touches.length) {
      this.touch = undefined;
    }
  }

  private touchFrame(t: TouchList) {
    const [a, b] = [t[0], t[1]];
    return { dist: Math.max(Math.hypot(a.clientX - b.clientX, a.clientY - b.clientY), 1), midX: (a.clientX + b.clientX) / 2, midY: (a.clientY + b.clientY) / 2 };
  }

  /** Selection from the photo: ignored when the pointer was dragging the view. */
  pickOnPhoto(d: Distress) {
    if (!this.dragged) this.selectDistress(d);
  }

  onStageMove(event: MouseEvent) {
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    this.cursor.set({
      x: (event.clientX - rect.left) / rect.width,
      y: (event.clientY - rect.top) / rect.height
    });
  }

  // ---------- depth, budget, traffic

  saveDepth() {
    const current = this.selectedAnalysis();
    const d = this.selectedDistress();
    const cm = this.depthCm();
    if (!current || !d || cm == null || this.isSavingDepth()) return;

    this.isSavingDepth.set(true);
    this.api.setDepth(current.id, d.id, cm / 100).subscribe({
      next: () => {
        this.isSavingDepth.set(false);
        this.budget.set(null);
        // PCI and severity may change with the depth: reload the whole inspection.
        this.api.get(current.id).subscribe(detail => {
          this.selectedAnalysis.set(detail);
          this.syncSummary(detail);
          const fresh = detail.distresses.find(x => x.id === d.id);
          if (fresh) this.selectedDistress.set(fresh);
          if (this.activeTab() === 'budget') this.loadBudget();
        });
        this.showToast('info', this.i18n.t('Profundidad de {code} guardada: cubicaje y PCI recalculados.', { code: d.code }));
      },
      error: err => {
        this.isSavingDepth.set(false);
        this.showToast('error', AnalysisService.describe(err, 'No se pudo guardar la profundidad.'));
      }
    });
  }

  loadBudget() {
    const current = this.selectedAnalysis();
    if (!current || current.status !== 'Completed') return;
    this.isLoadingBudget.set(true);
    this.api.budget(current.id).subscribe({
      next: b => {
        if (this.selectedAnalysis()?.id === b.inspectionId) this.budget.set(b);
        this.isLoadingBudget.set(false);
      },
      error: err => {
        this.isLoadingBudget.set(false);
        this.showToast('error', AnalysisService.describe(err, 'No se pudo calcular el presupuesto.'));
      }
    });
  }

  /** Opens the work-zone planner, on the inspection's segment when it has one. */
  openPlanner(segmentId?: string | null) {
    this.plannerSegment.set(segmentId ?? this.selectedAnalysis()?.roadSegmentId ?? null);
    this.module.set('planner');
  }

  // ---------- upload

  openUpload() {
    this.showUploadModal.set(true);
  }

  startNewAnalysis(request: NewInspection) {
    this.isUploading.set(true);
    this.showUploadModal.set(false);

    this.module.set('inspections');
    this.api
      .create({ ...request, language: this.i18n.lang() === 'en' ? 'En' : 'Es' })
      .subscribe({
        next: created => {
          this.isUploading.set(false);
          this.analyses.update(list => [
            {
              id: created.id,
              title: created.title,
              location: created.location,
              status: created.status,
              engine: created.engine,
              language: created.language,
              imageUrl: created.image.url,
              distressCount: 0,
              potholeCount: 0,
              createdAt: created.createdAt
            },
            ...list
          ]);
          this.budget.set(null);
          this.selectedAnalysis.set(created);
          this.watch(created.id);
        },
        error: err => {
          this.isUploading.set(false);
          this.showToast('error', AnalysisService.describe(err, 'No se pudo subir la imagen.'));
        }
      });
  }

  retryAnalysis() {
    const current = this.selectedAnalysis();
    if (!current) return;
    this.api.retry(current.id).subscribe({
      next: detail => {
        this.selectedAnalysis.set(detail);
        this.syncSummary(detail);
        this.watch(detail.id);
      },
      error: err => this.showToast('error', AnalysisService.describe(err, 'No se pudo reintentar.'))
    });
  }

  askDelete() {
    this.deleteDialog()?.nativeElement.showModal();
  }

  closeDelete() {
    this.deleteDialog()?.nativeElement.close();
  }

  deleteAnalysis() {
    const current = this.selectedAnalysis();
    this.closeDelete();
    if (!current) return;
    this.api.delete(current.id).subscribe({
      next: () => {
        this.poll?.unsubscribe();
        this.selectedAnalysis.set(null);
        this.selectedDistress.set(null);
        this.budget.set(null);
        this.analyses.update(list => list.filter(a => a.id !== current.id));
        const next = this.analyses()[0];
        if (next) this.selectAnalysis(next);
      },
      error: err => this.showToast('error', AnalysisService.describe(err, 'No se pudo eliminar.'))
    });
  }

  askAssistant() {
    const q = this.userQuestion().trim();
    const d = this.selectedDistress();
    const current = this.selectedAnalysis();
    if (!q || !d || !current || this.isAsking()) return;

    this.isAsking.set(true);
    this.pendingQuestion.set(q);
    this.userQuestion.set('');
    this.api.ask(current.id, q, d.id).subscribe({
      next: res => {
        if (this.selectedDistress()?.id === d.id) {
          this.messages.update(list => [...list, res.question, res.answer]);
        }
        this.pendingQuestion.set(null);
        this.isAsking.set(false);
      },
      error: err => {
        this.pendingQuestion.set(null);
        this.userQuestion.set(q);
        this.isAsking.set(false);
        this.showToast('error', AnalysisService.describe(err, 'No se pudo contactar al asistente.'));
      }
    });
  }

  /** The assistant answers in light markdown; the console renders plain text. */
  plain(text: string): string {
    return text.replace(/\*\*(.+?)\*\*/g, '$1');
  }

  toggleStepComplete(step: RepairStep) {
    const current = this.selectedAnalysis();
    if (!current) return;
    const completed = !step.completedAt;
    this.replaceStep(current.id, { ...step, completedAt: completed ? new Date().toISOString() : undefined });

    this.api.setStepCompletion(current.id, step.id, completed).subscribe({
      next: saved => this.replaceStep(current.id, saved),
      error: err => {
        this.replaceStep(current.id, step);
        this.showToast('error', AnalysisService.describe(err, 'No se pudo guardar el paso.'));
      }
    });
  }

  private replaceStep(inspectionId: string, step: RepairStep) {
    this.selectedAnalysis.update(a =>
      a && a.id === inspectionId ? { ...a, repairSteps: a.repairSteps.map(s => (s.id === step.id ? step : s)) } : a
    );
  }

  showToast(kind: Toast['kind'], text: string) {
    this.toast.set({ kind, text });
    clearTimeout(this.toastTimer);
    this.toastTimer = setTimeout(() => this.toast.set(null), 5000);
  }
}

