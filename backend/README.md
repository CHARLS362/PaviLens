# PaveLens backend

.NET 10 API that turns a photo of a road into a pavement condition report:

- Meta SAM 3 segments every distress.
- A ground projection converts each mask to metres.
- Muse Wonder classifies the distresses and plans the maintenance.
- Closed-form engineering models compute PCI (ASTM D6433), patch quantities, a unit-price budget (MTC, PEN) and the traffic impact of a work zone (BPR + Wardrop equilibrium).

## Requirements

- .NET SDK 10.0.100 or later (`global.json` pins the feature band)
- Optional: `dotnet tool install -g dotnet-ef` to create migrations
- Optional: Python 3 with `numpy` and `Pillow` to regenerate the demo scenes (`python samples/tools/generate_scenes.py`)

## Run

```bash
dotnet run --project src/PaveLens.Api --launch-profile http
```

In Development the API:

- listens on `http://localhost:5000`;
- applies migrations to `src/PaveLens.Api/App_Data/pavelens.db` (SQLite);
- seeds the three demo scenes from `samples/` when the database is empty;
- runs with `Ai:Provider = Simulation`, and every result is labelled `engine: "Simulation"`;
- serves the OpenAPI document at `/openapi/v1.json` and the Scalar UI at `/scalar`.

The Angular frontend (`../frontend`, `npm start`) proxies `/api` to this port.

### Demo scenes

| Scene | Projection | Network segment | Distresses |
|---|---|---|---|
| `jr-lima-tramo.jpg` | Oblique; 4-point calibration, 7 × 25 m | H11 | 2 potholes, alligator, longitudinal, transverse, rutting |
| `av-el-sol-bache.jpg` | Nadir, 3.0 m wide | V13 | High-severity pothole, longitudinal crack |
| `jr-arequipa-cocodrilo.jpg` | Nadir, 4.0 m wide | H30 | Alligator cracking, transverse crack, small pothole |

The scenes are procedurally generated. `samples/tools/generate_scenes.py` renders them and writes `manifest.json` plus the simulation scenarios keyed by SHA-256, so the stored masks match the drawn distresses.

## Tests

```bash
dotnet test --solution PaveLens.slnx
```

- **Unit tests** cover:
  - the domain state machine and invariants;
  - homography and scale projections;
  - polygon metrics (area, moments, orientation);
  - PCI deduct and CDV curves and the partial-unit rule;
  - quantities and the ASTM pothole severity matrix;
  - the budget;
  - BPR and Frank-Wolfe convergence;
  - image sniffing and model-output parsing;
  - the SAM 3 adapter: request shape, pixel-to-normalized polygons, parts and holes, cross-concept de-duplication.
- **Integration tests** boot the real API with an isolated SQLite file. They cover:
  - upload → analysis → detail, nadir and calibrated oblique;
  - every demo mask measuring the drawn ground area within 1 % (5 % for cracks);
  - deduplication and ETag/304;
  - field depth → PCI and budget;
  - step completion and conversation;
  - traffic simulation;
  - validation errors, conflicts and the model-outage path.

## Configuration

| Key | Default | Notes |
|---|---|---|
| `ConnectionStrings:PaveLens` | `Data Source=App_Data/pavelens.db` | Relative SQLite paths resolve against the content root |
| `Database:Provider` | `Sqlite` | `Sqlite` or `Postgres`, chosen explicitly, with no silent fallback |
| `Database:MigrateOnStartup` | `false` (`true` in Development) | In production, run migrations as a release step |
| `Storage:LocalRootPath` | `App_Data/images` | Content-addressed blobs `ab/cd/<sha256>.<ext>` |
| `Seed:Enabled` | `false` (`true` in Development) | Loads `samples/manifest.json` through the real use case |
| `Seed:Language` | `Es` | Report language of the seeded demo inspections (`Es` or `En`) |
| `Ai:Provider` | `Simulation` | `Meta` calls the hosted models below |
| `Ai:Meta:SegmentationService` | `Roboflow` | `Roboflow` (SAM 3 serverless API) or `Sidecar` (self-hosted `/v1/segment`), see [docs/ai-contracts.md](docs/ai-contracts.md) |
| `Ai:Meta:Segmentation:BaseUrl`, `Model` | `https://serverless.roboflow.com/`, `sam3` | Set the sidecar URL when `SegmentationService=Sidecar` |
| `Ai:Meta:Segmentation:ApiKey` | — | **Secret.** Roboflow API key; optional for a sidecar |
| `Ai:Meta:SegmentationScoreThreshold` | `0.5` | Minimum SAM 3 instance confidence |
| `Ai:Meta:Reasoning:BaseUrl`, `Model` | `https://api.meta.ai/v1/`, `muse-wonder-1.0` | Any OpenAI-compatible `chat/completions` endpoint works |
| `Ai:Meta:Reasoning:ApiKey` | — | **Secret.** Never put it in `appsettings.json` |
| `Ai:Meta:ReasoningEffort`, `Temperature`, `JsonMode` | `low`, unset, `true` | Unset values are not sent; turn `JsonMode` off if the endpoint rejects `response_format` |
| `Analysis:SegmentationConcepts` | pothole, alligator crack, longitudinal crack, transverse crack, rutting | Text prompts sent to SAM 3 |
| `Pavement:PatchMarginM` | `0.15` | Saw-cut margin around each patch |
| `Pavement:HmaDensityTPerM3`, `WasteFactor` | `2.35`, `0.05` | Hot-mix mass per m³ and waste |
| `Pavement:AssumedDepth{Low,Medium,High}M` | `0.03`, `0.05`, `0.075` | Used until a field depth is entered |
| `Pavement:Prices:*` | see `PriceList` | Unit prices (PEN), overhead 10 %, profit 5 %, IGV 18 % |
| `Pavement:Region:*` | Puno, 3 827 m | Binder recommendation and site notes shown with the budget |
| `Traffic:{Peak,OffPeak,Night}DemandFactor` | `1.0`, `0.65`, `0.2` | Share of peak-hour demand in each work window |
| `Traffic:ValueOfTimePenPerVehH` | `9.6` | Value of time used for the social cost of a closure (PEN per veh·h). Reference value; replace it with the official one for the project |
| `Uploads:MaxImageBytes` | 10 MB | Also bounds the multipart body |
| `Cors:AllowedOrigins` | `[]` | Only needed when the frontend is not behind the proxy |
| `RateLimiting:AiRequestsPerMinute` | 20 | Per client IP; applied to uploads, retries and questions |

The prices are reference values for the demo. Replace them with the current cost analysis of the contracting entity.

### Secrets

```bash
cd src/PaveLens.Api
dotnet user-secrets set "Ai:Provider" "Meta"
dotnet user-secrets set "Ai:Meta:Segmentation:ApiKey" "<Roboflow key>"
dotnet user-secrets set "Ai:Meta:Reasoning:ApiKey" "<Meta API key>"
```

The endpoints default to Roboflow's SAM 3 and Meta's Muse Wonder API, so the two keys are enough. No GPU or model server is needed.

In containers or CI, use environment variables with `__` as the separator, for example `Ai__Meta__Reasoning__ApiKey`.

With `Provider=Meta`, startup fails if a required value is missing (`ValidateOnStart`). It never falls back to simulation.

### PostgreSQL

```bash
export Database__Provider=Postgres
export ConnectionStrings__PaveLens="Host=localhost;Database=pavelens;Username=pavelens;Password=<secret>"
```

Each provider has its own migrations, in `Persistence/Migrations/Sqlite` and `Persistence/Migrations/Postgres`. To add a migration:

```bash
cd src/PaveLens.Infrastructure
dotnet ef migrations add <Name> -c SqliteAppDbContext -o Persistence/Migrations/Sqlite
dotnet ef migrations add <Name> -c PostgresAppDbContext -o Persistence/Migrations/Postgres
```

## API

All endpoints live under `/api/v1`. Errors are RFC 9457 `application/problem+json`. [`PaveLens.Api.http`](src/PaveLens.Api/PaveLens.Api.http) has runnable examples.

| Method | Route | Result |
|---|---|---|
| `POST` | `/inspections` | `201`; the detail comes back in `Pending` and the worker analyses it. Multipart fields: `image`, `title`, `notes?`, `location?`, `roadSegmentId?`, `referenceWidthM?`, `calibration?` (JSON), `language?` (`es` default, or `en`: the language Muse Wonder writes the report in) |
| `GET` | `/inspections?page&pageSize` | Paged summaries with PCI and pothole count, newest first |
| `GET` | `/inspections/{id}` | Detail: distresses with masks, measurements and quantities, PCI with deducts, and steps |
| `DELETE` | `/inspections/{id}` | `204`; the image is removed if nothing else uses it |
| `POST` | `/inspections/{id}/retry` | `202`, failed inspections only |
| `PUT` | `/inspections/{id}/distresses/{distressId}/depth` | Body `{ "depthM": 0.06 }`. Recomputes quantities, pothole severity and PCI |
| `GET` | `/inspections/{id}/budget` | Unit-price budget (APU) with totals and site notes; `409` until completed |
| `PUT` | `/inspections/{id}/steps/{stepId}/completion` | Body `{ "completed": true }` |
| `GET` / `POST` | `/inspections/{id}/conversation` | History, or a new question `{ question, distressId? }` |
| `GET` | `/network` | Demo road network: nodes, segments, demand |
| `POST` | `/traffic/simulate` | Body `{ segmentId, lanesClosed, period?, durationH? }`, where `period` is `Peak` (default), `OffPeak` or `Night` and `durationH` is in (0, 720], 8 by default. Returns baseline vs work zone in veh·h, per-link flows, detour segments, the social cost (Δveh·h × duration × value of time), the same cost for every window and the recommended window |
| `POST` | `/traffic/plan` | Same body plus `language?` (`Es`/`En`). Muse Wonder turns the simulation into a traffic management plan: routes, signage, phases, public notices and risks. The figures stay those of the simulation |
| `GET` | `/ai/status` | Active provider, plus each model's name, service, host and whether its key is configured. Keys are never returned |
| `GET` | `/images/{id}` | Original bytes, `ETag` = SHA-256, immutable cache |
| `GET` | `/health`, `/health/ready` | Liveness / readiness (database) |

### Calibration (oblique photos)

`calibration` is a JSON string. It gives the four image corners of a ground rectangle of known size, in normalized coordinates, ordered near-left, near-right, far-right, far-left:

```json
{ "corners": [[0.08,0.95],[0.92,0.95],[0.60,0.42],[0.40,0.42]], "widthM": 7, "lengthM": 25 }
```

Without it, the photo is treated as nadir. Its horizontal span is `referenceWidthM` (3.5 m by default), and the vertical span follows from the aspect ratio.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the design decisions and the engineering models.
