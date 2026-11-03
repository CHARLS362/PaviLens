# Architecture

## Layers

```
PaveLens.Domain          Entities and invariants. No dependencies.
PaveLens.Application     Use cases, ports, ground projection, PCI, quantities, budget, traffic.
PaveLens.Infrastructure  EF Core, image storage, queue + worker, AI adapters.
PaveLens.Api             Minimal API endpoints, error mapping, rate limiting, health.
```

Dependencies point inward. Application defines the ports (`IInspectionRepository`, `IImageStorage`, `IInspectionQueue`, `IDistressSegmenter`, `IDiagnosticReasoner`, `IMaintenanceAssistant`) and Infrastructure implements them. As a result:

- the engineering models and the pipeline can be unit-tested without a database or models;
- swapping SQLite for PostgreSQL, local disk for S3, or the in-memory queue for a broker changes no use case.

## Domain

`Inspection` is the aggregate root. It owns `Distress` and `RepairStep`, and only changes state through methods that enforce the lifecycle:

```
Pending ──BeginProcessing──▶ Processing ──Complete──▶ Completed
   ▲                              │
   └────────────Retry─────────── Fail ──▶ Failed
```

- `Complete` rejects duplicate distress codes and steps whose `TargetCode` matches no distress.
- `UpdateDistressDepth` is only valid on patchable distresses (pothole, alligator cracking, rutting). Cracks are sealed per metre and carry no quantities.
- Step completion and depth updates require a completed inspection.
- Every mutation rotates `Version`, an optimistic concurrency token. A concurrent writer gets `409` instead of silently overwriting.
- Ids are UUIDv7, assigned by the domain.
- Validation at construction:
  - points and boxes must lie in `[0, 1]`;
  - a `GroundCalibration` quad must not be degenerate;
  - width and length must be in range.

## Analysis pipeline

```
POST /inspections
  └─ InspectionService.CreateAsync
       validate → read with size cap → sniff magic bytes (JPEG/PNG/WebP) + dimensions
       → SHA-256 → reuse StoredImage or write blob → Inspection(Pending) → commit → enqueue
                                                                        │
InspectionWorker (BackgroundService) ◀──────────── Channel<Guid> ◀──────┘
  └─ InspectionProcessor.ProcessAsync
       BeginProcessing → commit                Stage = Segmenting
       → IDistressSegmenter (SAM 3)         one polygon per distress, normalized
       → GroundProjection                     Stage = Measuring; scale (nadir) or homography (oblique)
       → skip masks beyond the horizon        oblique only; noted in the summary, fails if none remain
       → PolygonMetrics on the RAW polygon    area, perimeter, oriented L × W, eccentricity
       → commit                               Stage = Reasoning, so polling clients see it
       → IDiagnosticReasoner (Muse Wonder)    code, type, severity per segment + steps
       → validation against the segments      (one self-repair retry with the error)
       → QuantityEstimator                    Stage = Computing; patch quantities for patchable distresses
       → PciCalculator                        ASTM D6433 on the measured quantities
       → RDP simplification                   storage and display only, < 1 px
       → Complete(trace) → commit             Stage cleared; AnalysisTrace stored as TraceJson
     any error → Fail(reason) → commit         (no fallback to fake data)
```

- **Durability.** The queue holds only ids, and the state of record is in the database. On startup the worker re-queues every inspection left in `Pending`/`Processing`, so a restart loses nothing.
- **Honest output.** `Engine` records who produced each result. It is `Meta` only when both segmentation and reasoning are real models. Simulation results are labelled in the API and in the UI.
- **Traceability.** `AnalysisTrace` records every SAM 3 text concept with its mask count, the segmentation and reasoning latencies, how many attempts Muse Wonder needed, and how many masks fell beyond the horizon. The UI shows it as the "Pipeline Meta AI" panel and marks PCI and APU as computed by code.
- **Report language.** `Inspection.Language` (`Es`/`En`) is fixed at creation. `DiagnosisRequest`, the assistant context and `TrafficPlanRequest` carry it, and the prompts ask Muse Wonder to write the free text in that language while keys and enums stay as they are. The simulation scenarios ship an `en` translation of the summary, the distress texts and the steps, and the simulated planner and assistant write in either language. Fixed texts computed by code, such as PCI ratings, budget items and notes, stay in Spanish in the API, and the frontend translates them.

## Measurement

The quality of every downstream number (PCI, quantities, budget) depends on measuring the mask correctly. Three rules apply.

1. **Measure the raw polygon.** Simplification is applied after measuring. A pixel-level tolerance near the horizon of an oblique photo spans centimetres on the ground, so simplifying first biases distant distresses low. The stored outline uses a tolerance of 0.0006 of the image width, under one pixel at 1280 px, and is used only for display.
2. **Project before measuring.** Each vertex goes to metres on the road plane first. Area, perimeter and moments are then computed there, never in pixels.
3. **Use exact polygon integrals.** Green's theorem gives:
   - the signed area and centroid;
   - the second moments μ20, μ02, μ11;
   - the principal axes, giving orientation θ = ½·atan2(2μ11, μ20 − μ02), measured from the cross-road axis;
   - eccentricity √(1 − λ₂/λ₁);
   - the oriented length × width, taken as the extents along the principal axes.

### Ground projection

| Photo | Input | Model |
|---|---|---|
| Nadir | `referenceWidthM` (default 3.5 m) | Uniform scale; height follows the image aspect ratio |
| Oblique | `calibration`: 4 image corners of a known W × L rectangle | Homography by DLT with h33 = 1, solved by Gaussian elimination with partial pivoting |

With h33 = 1 the denominator's sign on the ground is not always positive, because it depends on where the image origin falls (sky in a road photo). The projection therefore takes the ground sign from the calibration corners and rejects points beyond the horizon instead of mirroring them.

### Verification

`samples/tools/generate_scenes.py` renders the demo scenes from ground-truth outlines in metres:
- 96-vertex potholes and alligator areas;
- crack bands of half-width ½·w + 4 mm around the drawn crack path.

It rasterises every outline to check that it is a valid simple polygon. It writes the true areas to `samples/manifest.json`. The integration test `EveryMaskMeasuresTheDrawnGroundAreaWithinOnePercent` uploads each scene through the real API and checks the result:

- Areal distresses (potholes, alligator cracking, rutting) must come within **1 %** of the true area, in both nadir and oblique photos.
- Cracks get **5 %**. A 2 cm crack 20 m from the camera is 1–2 px wide in the oblique photo, so its area is resolution-bound. ASTM D6433 rates cracks by length, which is unaffected.

## Engineering models

| Model | Implementation | Notes |
|---|---|---|
| PCI, ASTM D6433 | `PciCalculator` | Deduct curves piecewise-linear in log₁₀(density): potholes counted, L&T cracks in m, alligator and rutting in m². Allowed deducts m = 1 + 9/98·(100 − HDV), capped at 10, fractional part on the next value. CDV iterates by reducing the smallest value > 2 down to 2, and the maximum CDV is kept |
| Partial unit | `PciCalculator` | If the surveyed area is < 135 m², densities use a 225 m² standard unit and the result is flagged `IsPartial` (an upper bound) |
| Pothole severity | `QuantityEstimator.PotholeSeverity` | ASTM matrix: depth ≤ 25 / ≤ 50 / > 50 mm against diameter < 200 / < 450 / ≥ 450 mm |
| Patch quantities | `QuantityEstimator` | A_cut = (L + 2m)(W + 2m), m = 0.15 m; V = A·d; M = V·2.35·(1 + 5 %). The depth is assumed by severity until a field value is entered |
| Budget (APU) | `CostEstimator` | MTC EG-2013 line items 01–06 in PEN. Computed on read from `PavementOptions.Prices`, then overhead, profit and IGV |
| Traffic | `TrafficAssignment` | BPR t = t₀·(1 + 0.15·(v/c)⁴). User equilibrium (Wardrop) by Frank-Wolfe: Dijkstra all-or-nothing plus a bisection line search, stopping at relative gap < 1e-4 or 200 iterations |
| Work zone | `TrafficService` | A lane closure scales capacity; a full closure removes the link. Detour = links whose flow rises by more than max(25 veh/h, 5 %) |

PCI and the budget are computed on read from the stored measurements, so changing the prices or a field depth never leaves stale totals in the database.

## Storage

| Data | Where | Why |
|---|---|---|
| Inspections, distresses, steps, conversation | Relational DB (SQLite dev / PostgreSQL prod) | Transactions, constraints, queries |
| Mask polygons | `MaskJson` column (simplified, < 1 px) | Always read whole, never queried by vertex |
| Measurement | Complex property columns on `Distress` | 1:1, always present |
| Quantities | Owned columns on `Distress` | Nullable as a group (null for cracks) |
| Calibration | `CalibrationJson` column | Small value object, read whole |
| Image bytes | Blob store, key `ab/cd/<sha256>.<ext>` | Keeps the DB small; dedup by content |

- Enums are stored as text, and all timestamps are UTC.
- Blob writes are atomic (temp file + move). Keys are validated against a strict pattern and checked against the root.
- Deleting an inspection removes the image row only when nothing else references it, then deletes the blob on a best-effort basis after the commit.

## Frontend

Angular 22 standalone component with signals:
- The inspection list, photo viewer and inspector share one selected-inspection signal.
- The viewer draws the stored masks as SVG over the photo.
- Zoom and pan keep the labels at screen size.
- The cursor position is shown in metres on the road surface. For oblique photos the frontend solves the same homography as the API.
- A scale bar is drawn for nadir photos.
- "Show on photo" zooms the viewer onto the distress; on touch screens two fingers pinch and one finger pans once zoomed in.
- `PrintReport` is the A4 inspection file. It is hidden on screen; `@media print` hides the rest of the app, and the browser saves it as PDF, so no PDF library ships to the client.
- `public/samples` holds byte-identical copies of the demo scenes, so a sample upload hits the same simulation scenario as the seed.
- ES/EN. Spanish is the source language and each string is its own key. The `I18n` service and the `t` pipe look a string up in the `EN` dictionary, then in `EN_PATTERNS` for server texts with numbers, and otherwise fall back to the Spanish text. The choice is stored in `localStorage` and also sets the report language of new inspections and traffic plans.
- Colours encode meaning only: severity, the five PCI bands of `PciCalculator.Classify`, and v/c on the traffic map.

## API conventions

- Versioned prefix `/api/v1`; JSON enums as strings; nulls omitted.
- Exceptions map to problem details:

  | Exception | Status |
  |---|---|
  | `ValidationException` | 400 |
  | `NotFoundException` | 404 |
  | `DomainException`, `ConcurrencyConflictException` | 409 |
  | `ModelOutputException` | 502 |
  | `ModelUnavailableException` | 503 |
  | anything else | 500, logged, with no internals leaked |

- Fixed-window rate limiting per IP on the endpoints that call models.
- Options are validated at startup (`ValidateOnStart`), so a bad configuration fails fast.

## Build quality

- Central Package Management (`Directory.Packages.props`).
- `TreatWarningsAsErrors`, analyzers at `latest-recommended`, code style enforced during build.
- Logging through `[LoggerMessage]` source generators.
- Tests: xUnit v3 on Microsoft.Testing.Platform. Unit tests cover the models; `WebApplicationFactory` integration tests run on an isolated SQLite file.
