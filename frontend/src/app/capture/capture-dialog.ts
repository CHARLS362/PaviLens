import { afterNextRender, Component, computed, ElementRef, inject, input, OnDestroy, output, signal, viewChild } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { NewInspection, RoadSegment } from '../models/analysis.model';
import { groundRect, homography, isConvexQuad, Point } from './homography';
import { I18n, TranslatePipe } from '../i18n/i18n';

type Step = 'photo' | 'scale' | 'details';
type Source = 'file' | 'camera';
type ScaleMode = 'nadir' | 'oblique';

/** ASTM D6433 sample unit for asphalt: 225 ± 90 m². Below 135 m² the API flags the PCI as partial. */
const MIN_SAMPLE_UNIT_M2 = 135;
/** Thinnest crack the outline can follow reliably: about two pixels across. */
const MIN_CRACK_PX = 2;
/** Same limits as the API (Uploads:MaxImageBytes and its magic-byte sniffing). */
const ACCEPTED_TYPES = ['image/jpeg', 'image/png', 'image/webp'];
const MAX_IMAGE_BYTES = 10 * 1024 * 1024;

const CORNER_NAMES = ['cercana izquierda', 'cercana derecha', 'lejana derecha', 'lejana izquierda'];

/** Starting trapezoid for users who cannot click the corners precisely; they adjust it afterwards. */
const GUIDE_CORNERS: Point[] = [[0.2, 0.9], [0.8, 0.9], [0.62, 0.45], [0.38, 0.45]];

const STEP_TITLES: Record<Step, string> = { photo: 'Fotografía', scale: 'Escala', details: 'Datos y estimación' };

const FOCUSABLE = 'button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [href], [tabindex]:not([tabindex="-1"])';

const WIDTH_PRESETS = [
  { label: 'Carril 3,5 m', value: 3.5 },
  { label: 'Calzada 2 carriles · 7,0 m', value: 7 },
  { label: 'Jirón 6,0 m', value: 6 }
];

const RECT_PRESETS = [
  { label: 'Carril 3,5 × 10 m', w: 3.5, l: 10 },
  { label: 'Calzada 7 × 25 m', w: 7, l: 25 },
  { label: 'Losa 3,6 × 4,5 m', w: 3.6, l: 4.5 }
];

/**
 * Demo scenes served from public/samples, byte-identical to backend/samples so their SHA-256 matches the
 * simulation scenarios, with the scale they were shot at. They let anyone try the full flow without a photo.
 */
interface Sample {
  file: string;
  label: string;
  hint: string;
  title: string;
  location: string;
  segmentId: string;
  notes: string;
  widthM?: number;
  calibration?: { corners: Point[]; widthM: number; lengthM: number };
}

const SAMPLES: Sample[] = [
  {
    file: 'jr-lima-tramo.jpg',
    label: 'Jr. Lima · tramo de 25 m',
    hint: 'En ángulo · 6 deterioros',
    title: 'Jr. Lima, cuadra 3 · tramo de 25 m',
    location: 'Jr. Lima, cuadra 3 · Puno',
    segmentId: 'H11',
    notes: 'Toma oblicua desde el borde de la calzada. Calibración con el ancho de la vía (7 m) y 25 m de longitud medidos con wincha.',
    calibration: { corners: [[0.08, 0.95], [0.92, 0.95], [0.6, 0.42], [0.4, 0.42]], widthM: 7, lengthM: 25 }
  },
  {
    file: 'av-el-sol-bache.jpg',
    label: 'Av. El Sol · bache',
    hint: 'Desde arriba · 3 m de ancho',
    title: 'Av. El Sol · bache en carril derecho',
    location: 'Av. El Sol, frente al mercado · Puno',
    segmentId: 'V13',
    notes: 'Foto cenital a 1.6 m de altura; el encuadre cubre 3 m de ancho.',
    widthM: 3
  },
  {
    file: 'jr-arequipa-cocodrilo.jpg',
    label: 'Jr. Arequipa · piel de cocodrilo',
    hint: 'Desde arriba · 4 m de ancho',
    title: 'Jr. Arequipa · piel de cocodrilo',
    location: 'Jr. Arequipa, cuadra 5 · Puno',
    segmentId: 'H30',
    notes: 'Foto cenital; el encuadre cubre 4 m de ancho.',
    widthM: 4
  }
];

/**
 *New-inspection dialog: photo from file or camera, ground scale (nadir width or a 4-point calibration drawn on
 * the photo) and inspection details. Before uploading it estimates what the photo can measure: ground
 * resolution, the thinnest detectable crack and the surveyed area against the ASTM sample unit.
 */
@Component({
  selector: 'app-capture-dialog',
  standalone: true,
  imports: [DecimalPipe, FormsModule, TranslatePipe],
  templateUrl: './capture-dialog.html',
  styleUrl: './capture-dialog.css',
  host: { '(document:keydown.escape)': 'close()' }
})
export class CaptureDialog implements OnDestroy {
  segments = input<RoadSegment[]>([]);
  busy = input<boolean>(false);
  submitted = output<NewInspection>();
  closed = output<void>();

  readonly widthPresets = WIDTH_PRESETS;
  readonly rectPresets = RECT_PRESETS;
  readonly cornerNames = CORNER_NAMES;
  readonly samples = SAMPLES;
  loadingSample = signal(false);
  private i18n = inject(I18n);

  private video = viewChild<ElementRef<HTMLVideoElement>>('video');
  private dialog = viewChild.required<ElementRef<HTMLElement>>('dialog');
  /** Element that opened the dialog; focus goes back to it on close. */
  private readonly opener = document.activeElement as HTMLElement | null;

  constructor() {
    afterNextRender(() => this.dialog().nativeElement.focus());
  }

  step = signal<Step>('photo');
  stepNumber = computed(() => ['photo', 'scale', 'details'].indexOf(this.step()) + 1);
  stepTitle = computed(() => STEP_TITLES[this.step()]);
  source = signal<Source>('file');
  isDragging = signal(false);

  file = signal<File | null>(null);
  fileError = signal<string | null>(null);
  previewUrl = signal<string | null>(null);
  imageSize = signal<{ w: number; h: number } | null>(null);

  // Camera
  stream = signal<MediaStream | null>(null);
  cameraError = signal<string | null>(null);
  devices = signal<MediaDeviceInfo[]>([]);
  private deviceIndex = 0;

  // Scale
  /** No default: a wrong guess here silently scales every measurement, so the inspector has to choose. */
  scaleMode = signal<ScaleMode | null>(null);
  widthM = signal<number>(3.5);
  corners = signal<Point[]>([]);
  rectW = signal<number>(3.5);
  rectL = signal<number>(10);
  private dragIndex: number | null = null;

  // Details
  title = signal('');
  location = signal('');
  segmentId = signal('');
  notes = signal('');
  locating = signal(false);

  ngOnDestroy() {
    this.stopCamera();
    this.revoke();
    if (this.opener?.isConnected) this.opener.focus();
  }

  /** Keeps Tab and Shift+Tab inside the modal. */
  trapFocus(event: Event) {
    const e = event as KeyboardEvent;
    const items = [...this.dialog().nativeElement.querySelectorAll<HTMLElement>(FOCUSABLE)].filter(el => el.offsetParent !== null);
    if (!items.length) return;
    const first = items[0];
    const last = items[items.length - 1];
    const active = document.activeElement;
    if (e.shiftKey && (active === first || active === this.dialog().nativeElement)) {
      e.preventDefault();
      last.focus();
    } else if (!e.shiftKey && active === last) {
      e.preventDefault();
      first.focus();
    }
  }

  // ---------------- photo

  onFileSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    const f = input.files?.[0];
    if (f) void this.useFile(f);
    input.value = '';
  }

  onDrop(event: DragEvent) {
    event.preventDefault();
    this.isDragging.set(false);
    const f = event.dataTransfer?.files?.[0];
    if (f) void this.useFile(f);
  }

  async useSource(source: Source) {
    this.source.set(source);
    if (source === 'camera') await this.startCamera();
    else this.stopCamera();
  }

  async startCamera(deviceId?: string) {
    this.stopCamera();
    this.cameraError.set(null);
    if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia) {
      this.cameraError.set('La cámara solo está disponible en HTTPS o en localhost. En el celular puedes usar «Tomar foto».');
      return;
    }
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: false,
        video: deviceId
          ? { deviceId: { exact: deviceId }, width: { ideal: 1920 }, height: { ideal: 1080 } }
          : { facingMode: { ideal: 'environment' }, width: { ideal: 1920 }, height: { ideal: 1080 } }
      });
      this.stream.set(stream);
      const all = await navigator.mediaDevices.enumerateDevices();
      this.devices.set(all.filter(d => d.kind === 'videoinput'));
      queueMicrotask(() => {
        const el = this.video()?.nativeElement;
        if (el) {
          el.srcObject = stream;
          void el.play();
        }
      });
    } catch (err) {
      const name = (err as DOMException)?.name;
      this.cameraError.set(
        name === 'NotAllowedError'
          ? 'Permiso de cámara denegado. Habilítalo en el navegador o sube una foto.'
          : name === 'NotFoundError' || name === 'OverconstrainedError'
            ? 'No se encontró una cámara disponible.'
            : 'No se pudo abrir la cámara.'
      );
    }
  }

  switchCamera() {
    const list = this.devices();
    if (list.length < 2) return;
    this.deviceIndex = (this.deviceIndex + 1) % list.length;
    void this.startCamera(list[this.deviceIndex].deviceId);
  }

  capture() {
    const el = this.video()?.nativeElement;
    if (!el || !el.videoWidth) return;
    const canvas = document.createElement('canvas');
    canvas.width = el.videoWidth;
    canvas.height = el.videoHeight;
    canvas.getContext('2d')!.drawImage(el, 0, 0);
    canvas.toBlob(
      blob => {
        if (!blob) return;
        const stamp = new Date().toISOString().slice(0, 19).replace(/[-:T]/g, '');
        void this.useFile(new File([blob], `captura-${stamp}.jpg`, { type: 'image/jpeg' }));
        this.stopCamera();
      },
      'image/jpeg',
      0.92
    );
  }

  /** Loads a demo scene with the scale and details it was shot with, and goes straight to the scale step. */
  async useSample(s: Sample) {
    this.loadingSample.set(true);
    try {
      const res = await fetch(`samples/${s.file}`);
      if (!res.ok) throw new Error(String(res.status));
      const blob = await res.blob();
      if (!(await this.useFile(new File([blob], s.file, { type: 'image/jpeg' })))) return;
      if (s.calibration) {
        this.scaleMode.set('oblique');
        this.corners.set(s.calibration.corners);
        this.rectW.set(s.calibration.widthM);
        this.rectL.set(s.calibration.lengthM);
      } else {
        this.scaleMode.set('nadir');
        this.widthM.set(s.widthM ?? 3.5);
      }
      this.title.set(this.i18n.t(s.title));
      this.location.set(this.i18n.t(s.location));
      this.segmentId.set(s.segmentId);
      this.notes.set(this.i18n.t(s.notes));
      this.step.set('scale');
    } catch {
      this.fileError.set('No se pudo cargar la foto de muestra.');
    } finally {
      this.loadingSample.set(false);
    }
  }

  retake() {
    this.revoke();
    this.file.set(null);
    this.imageSize.set(null);
    this.corners.set([]);
    if (this.source() === 'camera') void this.startCamera();
  }

  /** Rejects what the API would reject (type, size), and files the browser cannot decode, before any upload. */
  private useFile(f: File): Promise<boolean> {
    this.revoke();
    this.file.set(null);
    this.imageSize.set(null);
    this.corners.set([]);
    if (!ACCEPTED_TYPES.includes(f.type)) {
      this.fileError.set('Formato no admitido: usa una foto JPEG, PNG o WebP.');
      return Promise.resolve(false);
    }
    if (f.size > MAX_IMAGE_BYTES) {
      this.fileError.set('La foto pesa más de 10 MB. Redúcela o usa otra.');
      return Promise.resolve(false);
    }
    this.fileError.set(null);
    const url = URL.createObjectURL(f);
    this.file.set(f);
    this.previewUrl.set(url);
    if (!this.title()) this.title.set(f.name.replace(/\.[^.]+$/, '').replace(/[-_]+/g, ' '));
    return new Promise(resolve => {
      const img = new Image();
      img.onload = () => {
        this.imageSize.set({ w: img.naturalWidth, h: img.naturalHeight });
        resolve(true);
      };
      img.onerror = () => {
        this.revoke();
        this.file.set(null);
        this.fileError.set('No se pudo leer la imagen; el archivo puede estar dañado.');
        resolve(false);
      };
      img.src = url;
    });
  }

  private stopCamera() {
    this.stream()?.getTracks().forEach(t => t.stop());
    this.stream.set(null);
  }

  private revoke() {
    const url = this.previewUrl();
    if (url) URL.revokeObjectURL(url);
    this.previewUrl.set(null);
  }

  // ---------------- scale

  applyRectPreset(p: { w: number; l: number }) {
    this.rectW.set(p.w);
    this.rectL.set(p.l);
  }

  /** Click on the photo: place the next corner, in the order the API expects. */
  onCalibrationClick(event: MouseEvent) {
    if (this.dragIndex !== null || this.corners().length >= 4) return;
    this.corners.update(c => [...c, this.relative(event)]);
  }

  startDrag(index: number, event: PointerEvent) {
    event.stopPropagation();
    event.preventDefault();
    this.dragIndex = index;
    (event.target as Element).setPointerCapture(event.pointerId);
  }

  onDrag(event: PointerEvent) {
    if (this.dragIndex === null) return;
    const i = this.dragIndex;
    const p = this.relative(event);
    this.corners.update(c => c.map((q, k) => (k === i ? p : q)));
  }

  endDrag() {
    // Defer so the click that ends a drag does not add a corner.
    setTimeout(() => (this.dragIndex = null));
  }

  resetCorners() {
    this.corners.set([]);
  }

  placeGuideCorners() {
    this.corners.set(GUIDE_CORNERS.map(([x, y]) => [x, y] as Point));
  }

  /** Arrow keys move a corner by 0.5 % of the photo (0.1 % with Shift), as an alternative to dragging. */
  nudge(index: number, event: KeyboardEvent) {
    const step = event.shiftKey ? 0.001 : 0.005;
    const delta: Record<string, Point> = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] };
    const d = delta[event.key];
    if (!d) return;
    event.preventDefault();
    const clamp = (v: number) => Math.min(1, Math.max(0, v));
    this.corners.update(c => c.map((q, k) => (k === index ? ([clamp(q[0] + d[0]), clamp(q[1] + d[1])] as Point) : q)));
  }

  private relative(event: MouseEvent): Point {
    const rect = (event.currentTarget as HTMLElement).closest('.calib-stage')!.getBoundingClientRect();
    const x = Math.min(1, Math.max(0, (event.clientX - rect.left) / rect.width));
    const y = Math.min(1, Math.max(0, (event.clientY - rect.top) / rect.height));
    return [x, y];
  }

  calibrationValid = computed(() => {
    const c = this.corners();
    return c.length === 4 && isConvexQuad(c) && this.rectW() > 0 && this.rectL() > 0 && !!this.toGround();
  });

  private toGround = computed(() => {
    const c = this.corners();
    if (c.length !== 4 || !isConvexQuad(c)) return null;
    return homography(c, groundRect(this.rectW(), this.rectL()));
  });

  /** One-metre grid of the calibrated rectangle drawn back onto the photo, to check the calibration by eye. */
  gridLines = computed(() => {
    const c = this.corners();
    if (c.length !== 4 || !isConvexQuad(c)) return [];
    const W = this.rectW();
    const L = this.rectL();
    if (!(W > 0 && L > 0)) return [];
    const toImage = homography(groundRect(W, L), c);
    const step = (len: number) => (len > 30 ? 5 : len > 12 ? 2 : len > 5 ? 1 : 0.5);
    const lines: string[] = [];
    const seg = (a: Point, b: Point) => {
      const p = toImage(a[0], a[1]);
      const q = toImage(b[0], b[1]);
      if (p && q) lines.push(`M${p[0] * 100},${p[1] * 100}L${q[0] * 100},${q[1] * 100}`);
    };
    for (let y = step(L); y < L - 1e-6; y += step(L)) seg([0, y], [W, y]);
    for (let x = step(W); x < W - 1e-6; x += step(W)) seg([x, 0], [x, L]);
    return lines;
  });

  quadPoints = computed(() => this.corners().map(([x, y]) => `${x * 100},${y * 100}`).join(' '));

  // ---------------- estimates

  /** What the photo can measure, before spending a model call on it. */
  estimate = computed(() => {
    const size = this.imageSize();
    if (!size) return null;
    if (this.scaleMode() === 'nadir') {
      const w = this.widthM();
      if (!(w > 0)) return null;
      const mmPerPx = (w / size.w) * 1000;
      const area = w * w * (size.h / size.w);
      return { near: mmPerPx, far: mmPerPx, area, minCrackMm: mmPerPx * MIN_CRACK_PX, partial: area < MIN_SAMPLE_UNIT_M2 };
    }
    const map = this.toGround();
    const c = this.corners();
    if (!map || !this.calibrationValid()) return null;
    // Ground size of one pixel at the middle of the near and far edges of the rectangle.
    const gsd = (u: number, v: number) => {
      const a = map(u, v);
      const b = map(u + 1 / size.w, v);
      const d = map(u, v + 1 / size.h);
      if (!a || !b || !d) return NaN;
      return Math.max(Math.hypot(b[0] - a[0], b[1] - a[1]), Math.hypot(d[0] - a[0], d[1] - a[1])) * 1000;
    };
    const near = gsd((c[0][0] + c[1][0]) / 2, (c[0][1] + c[1][1]) / 2);
    const far = gsd((c[2][0] + c[3][0]) / 2, (c[2][1] + c[3][1]) / 2);
    const area = this.rectW() * this.rectL();
    return { near, far, area, minCrackMm: far * MIN_CRACK_PX, partial: area < MIN_SAMPLE_UNIT_M2 };
  });

  // ---------------- details

  useMyLocation() {
    if (!navigator.geolocation) return;
    this.locating.set(true);
    navigator.geolocation.getCurrentPosition(
      pos => {
        const { latitude, longitude, accuracy } = pos.coords;
        const where = `${latitude.toFixed(5)}, ${longitude.toFixed(5)} (±${Math.round(accuracy)} m)`;
        this.location.set(this.location() ? `${this.location()} · ${where}` : where);
        this.locating.set(false);
      },
      () => this.locating.set(false),
      { enableHighAccuracy: true, timeout: 10000 }
    );
  }

  // ---------------- navigation

  canContinue = computed(() => {
    switch (this.step()) {
      case 'photo':
        return !!this.file() && !!this.imageSize();
      case 'scale':
        if (!this.scaleMode()) return false;
        return this.scaleMode() === 'nadir' ? this.widthM() >= 0.5 && this.widthM() <= 100 : this.calibrationValid();
      default:
        return !!this.title().trim() && !this.busy();
    }
  });

  /** Why the main button is disabled, in plain words, so nobody is left guessing. */
  blockedReason = computed(() => {
    if (this.canContinue()) return null;
    switch (this.step()) {
      case 'photo':
        return this.fileError() ?? 'Elige o toma una foto para continuar.';
      case 'scale':
        if (!this.scaleMode()) return 'Elige cómo se tomó la foto.';
        if (this.scaleMode() === 'nadir') return 'Indica un ancho entre 0,5 y 100 m.';
        if (this.corners().length === 3) return 'Falta 1 esquina por marcar.';
        if (this.corners().length < 4) return `Faltan ${4 - this.corners().length} esquinas por marcar.`;
        return 'Revisa las esquinas y las medidas del rectángulo.';
      default:
        return this.busy() ? null : 'Escribe un título para la inspección.';
    }
  });

  next() {
    if (!this.canContinue()) return;
    if (this.step() === 'photo') this.step.set('scale');
    else if (this.step() === 'scale') this.step.set('details');
    else this.submit();
  }

  back() {
    if (this.step() === 'details') this.step.set('scale');
    else if (this.step() === 'scale') this.step.set('photo');
  }

  close() {
    this.stopCamera();
    this.closed.emit();
  }

  private submit() {
    const f = this.file();
    if (!f) return;
    const oblique = this.scaleMode() === 'oblique';
    const round = (v: number) => Math.round(v * 10000) / 10000;
    this.submitted.emit({
      title: this.title().trim(),
      notes: this.notes().trim() || undefined,
      location: this.location().trim() || undefined,
      roadSegmentId: this.segmentId() || undefined,
      referenceWidthM: oblique ? undefined : this.widthM(),
      calibration: oblique
        ? { corners: this.corners().map(([x, y]) => [round(x), round(y)] as [number, number]), widthM: this.rectW(), lengthM: this.rectL() }
        : undefined,
      image: f,
      fileName: f.name
    });
    this.stopCamera();
  }
}
