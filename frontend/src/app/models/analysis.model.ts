// Mirrors the DTOs of the PaveLens API v1 (enums travel as strings).

export type InspectionStatus = 'Pending' | 'Processing' | 'Completed' | 'Failed';
export type DistressType = 'LongitudinalCrack' | 'TransverseCrack' | 'AlligatorCrack' | 'Pothole' | 'Rutting' | 'Other';
export type Severity = 'Low' | 'Medium' | 'High';
export type DepthSource = 'Assumed' | 'Measured';
export type RiskLevel = 'Low' | 'Medium' | 'High';
export type AnalysisEngine = 'Meta' | 'Simulation';

/** Language the models write a report or plan in; the API spells it Es / En. */
export type ReportLanguage = 'Es' | 'En';

/** Where the worker is while an inspection is Processing. */
export type AnalysisStage = 'Segmenting' | 'Measuring' | 'Reasoning' | 'Computing';

export interface ConceptTrace {
  concept: string;
  masks: number;
}

/** What each Meta model was asked and how it went, recorded when the analysis completes. */
export interface AnalysisTrace {
  concepts: ConceptTrace[];
  segmentationMs: number;
  reasoningMs: number;
  reasoningAttempts: number;
  skippedBeyondHorizon: number;
  orchestration?: AgentRun;
}

export interface RegionalConfiguration {
  id: string; name: string; version: string; countryCode: string;
  jurisdictionValidated: boolean; pricesAvailable: boolean; latitude: number; longitude: number;
}
export interface AgentRun {
  runId: string; engine: string; coordinatorModel: string; status: string;
  regionalProfileJson: string; latitude?: number; longitude?: number;
  actions: AgentAction[];
  warnings: string[]; modelCalls: number; inputTokens: number; outputTokens: number; mobilityJson?: string;
  /** The independent reviewer's findings, kept apart from the method's assumptions in warnings. */
  reviewIssues?: string[];
}
export interface AgentAction {
  agent: string; tool: string; model: string; status: string; durationMs: number; evidence: string;
  /** One plain sentence saying what the step found. */
  summary?: string; tokens?: number;
}
export type MessageRole = 'User' | 'Assistant';

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface InspectionSummary {
  id: string;
  title: string;
  location?: string;
  status: InspectionStatus;
  engine?: AnalysisEngine;
  language: ReportLanguage;
  imageUrl: string;
  distressCount: number;
  potholeCount: number;
  pci?: number;
  createdAt: string;
}

export interface ImageInfo {
  id: string;
  url: string;
  width: number;
  height: number;
  contentType: string;
  sizeBytes: number;
}

export interface Box {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface Calibration {
  corners: [number, number][];
  widthM: number;
  lengthM: number;
}

export interface Measurement {
  areaM2: number;
  perimeterM: number;
  lengthM: number;
  widthM: number;
  eccentricity: number;
  /** 0° across the road, ±90° along it. */
  orientationDeg: number;
}

export interface Quantities {
  depthM: number;
  depthSource: DepthSource;
  cutAreaM2: number;
  volumeM3: number;
  massT: number;
}

export interface Distress {
  id: string;
  code: string;
  type: DistressType;
  rddCode?: string;
  severity: Severity;
  confidence: number;
  box: Box;
  /** Closed polygon in normalized image coordinates, [x, y] pairs. */
  mask: [number, number][];
  /** Interior rings of intact pavement, already excluded from the measured area. */
  holes?: [number, number][][];
  maskVertexCountRaw: number;
  sourceConcept?: string;
  measurement: Measurement;
  /** Null for cracks: they are sealed, not patched. */
  quantities?: Quantities;
  diagnosis?: string;
  recommendedAction?: string;
}

export interface DeductEntry {
  distress: 'AlligatorCracking' | 'LongitudinalTransverseCracking' | 'Pothole' | 'Rutting';
  severity: Severity;
  quantity: number;
  unit: 'SquareMetres' | 'Metres' | 'Count';
  densityPct: number;
  deductValue: number;
}

export interface CdvIteration {
  q: number;
  totalDeductValue: number;
  correctedDeductValue: number;
}

export interface PciResult {
  pci: number;
  rating: string;
  treatment: string;
  surveyedAreaM2: number;
  sampleUnitAreaM2: number;
  isPartial: boolean;
  maxAllowedDeducts: number;
  deducts: DeductEntry[];
  iterations: CdvIteration[];
}

export interface RepairStep {
  id: string;
  order: number;
  title: string;
  instructions: string;
  tools: string[];
  risk: RiskLevel;
  targetCode?: string;
  completedAt?: string;
}

export interface InspectionDetail {
  id: string;
  title: string;
  notes?: string;
  location?: string;
  roadSegmentId?: string;
  status: InspectionStatus;
  stage?: AnalysisStage;
  trace?: AnalysisTrace;
  language: ReportLanguage;
  engine?: AnalysisEngine;
  segmentationModel?: string;
  reasoningModel?: string;
  summary?: string;
  failureReason?: string;
  referenceWidthM: number;
  calibration?: Calibration;
  image: ImageInfo;
  attempts: number;
  createdAt: string;
  startedAt?: string;
  completedAt?: string;
  processingTimeMs?: number;
  surveyedAreaM2?: number;
  pci?: PciResult;
  distresses: Distress[];
  repairSteps: RepairStep[];
}

export interface BudgetLine {
  item: string;
  description: string;
  unit: string;
  quantity: number;
  unitPrice: number;
  amount: number;
}

export interface Budget {
  inspectionId: string;
  currency: string;
  lines: BudgetLine[];
  directCost: number;
  overheadPct: number;
  overhead: number;
  profitPct: number;
  profit: number;
  subtotal: number;
  igvPct: number;
  igv: number;
  total: number;
  assumedDepthCount: number;
  region: { name: string; altitudeM: number; recommendedBinder: string; notes: string[] };
  notes: string[];
}

export interface ConversationMessage {
  id: string;
  distressId?: string;
  role: MessageRole;
  content: string;
  engine?: AnalysisEngine;
  createdAt: string;
}

export interface Answer {
  question: ConversationMessage;
  answer: ConversationMessage;
}

export interface NewInspection {
  regionId?: string;
  latitude?: number;
  longitude?: number;
  title: string;
  notes?: string;
  location?: string;
  roadSegmentId?: string;
  referenceWidthM?: number;
  calibration?: Calibration;
  image: Blob;
  fileName: string;
  language?: ReportLanguage;
}

// ---------- Traffic ----------

export interface RoadNode {
  id: string;
  name: string;
  x: number;
  y: number;
  lat: number;
  lon: number;
}

export interface RoadSegment {
  id: string;
  name: string;
  from: string;
  to: string;
  lengthKm: number;
  freeSpeedKmh: number;
  lanesPerDirection: number;
  capacityPerLaneVph: number;
}

export interface RoadNetwork {
  name: string;
  description: string;
  nodes: RoadNode[];
  segments: RoadSegment[];
  demand: { from: string; to: string; vph: number }[];
}

export interface LinkComparison {
  segmentId: string;
  from: string;
  to: string;
  capacityVph: number;
  baselineFlowVph: number;
  baselineTimeMin: number;
  workZoneCapacityVph: number;
  workZoneFlowVph: number;
  workZoneTimeMin: number;
  closed: boolean;
}

export interface ScenarioSummary {
  totalTravelTimeVehH: number;
  iterations: number;
  relativeGap: number;
}

export type WorkPeriod = 'Peak' | 'OffPeak' | 'Night';

/** The same closure evaluated in one time window. */
export interface WorkWindow {
  period: WorkPeriod;
  demandFactor: number;
  deltaVehH: number;
  deltaPct: number;
  userCostPen: number;
  overCapacityLinks: number;
}

export interface TrafficSimulation {
  segmentId: string;
  segmentName: string;
  lanesClosed: number;
  lanesPerDirection: number;
  period: WorkPeriod;
  demandFactor: number;
  durationH: number;
  valueOfTimePenPerVehH: number;
  baseline: ScenarioSummary;
  workZone: ScenarioSummary;
  deltaVehH: number;
  deltaPct: number;
  userCostPen: number;
  overCapacityLinks: number;
  links: LinkComparison[];
  detourSegmentIds: string[];
  windows: WorkWindow[];
  recommendedPeriod: WorkPeriod;
  notes: string[];
  detourRoutes: DetourRoute[];
}

/** Fastest path around the work zone, computed by the traffic model (Dijkstra on work-zone times). */
export interface DetourRoute {
  id: string;
  label: string;
  fromNode: string;
  toNode: string;
  nodes: string[];
  segmentIds: string[];
  timeMin: number;
  extraTimeMin: number;
}

export interface PlannedRoute {
  id: string;
  name: string;
  vehicles: string;
  nodes: string[];
  segmentIds: string[];
  streets: string[];
  timeMin: number;
  extraTimeMin: number;
  rationale: string;
}

export interface PlannedSign {
  nodeId: string;
  nodeName: string;
  kind: string;
  message: string;
}

export interface PlannedPhase {
  order: number;
  title: string;
  when: string;
  description: string;
}

/** Work-zone traffic management plan reasoned by Muse Wonder (or the rule-based simulation). */
export interface TrafficPlan {
  engine: 'Meta' | 'Simulation';
  model: string;
  summary: string;
  routes: PlannedRoute[];
  signage: PlannedSign[];
  phases: PlannedPhase[];
  communication: string[];
  risks: string[];
  simulation: TrafficSimulation;
}

export interface ModelStatus {
  name: string;
  model: string;
  service: string;
  host?: string;
  keyConfigured: boolean;
}

/** Active AI provider. Keys never leave the server; only whether they are configured. */
export interface AiStatus {
  provider: 'Simulation' | 'Meta';
  segmentation?: ModelStatus;
  reasoning?: ModelStatus;
}

/** RFC 9457 problem body returned by the API on errors. */
export interface Problem {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}
