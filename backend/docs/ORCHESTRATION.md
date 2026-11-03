# Agent orchestration, regional profiles and city maps

Spanish technical report: [single-key Meta orchestration, activation and operational gaps](../../docs/ORQUESTACION_AGENTICA_META.es.md).

Implemented in the inspection worker. Existing inspections and the fixed demo network remain readable. No schema migration is required: execution evidence and the regional snapshot extend the existing `TraceJson` document.

## Run locally

The default `Ai:Provider=Simulation` uses a deterministic coordinator and demo specialists. It runs the same checked tools and explicitly labels the execution as simulation. New inspections show **Agent orchestration** in the Evaluation tab. Previously completed inspections have no agent history until a new analysis is created.

```powershell
dotnet run --project backend/src/PaveLens.Api --launch-profile http
npm --prefix frontend start
```

Choose **City maps** for geographic streets, or **Detour planning** for the existing synthetic traffic experiment. The geographic view loads a bounded OpenStreetMap area only after **Load streets in this area** is pressed. It supports coordinates, city presets, keyboard navigation, a searchable street selector and reference detours around a closed segment.

## Enable real Meta calls

The coordinator, diagnostic specialist, mobility specialist and independent reviewer use Meta Model API. The current model comes from `Ai:Meta:Reasoning:Model` (`muse-wonder-1.0` by default). Tools enforce prerequisites and compute engineering figures in .NET. The reviewer uses JSON Schema; the coordinator uses function calls.

For segmentation directly through Meta SAM 3.1, install Node.js 20.17+ or 22.9+ and the published parser:

```powershell
npm ci --prefix backend/tools/sam31 --ignore-scripts
cd backend/src/PaveLens.Api
dotnet user-secrets set "Ai:Provider" "Meta"
dotnet user-secrets set "Ai:Meta:Reasoning:ApiKey" "<META_MODEL_API_KEY>"
dotnet user-secrets set "Ai:Meta:SegmentationService" "Meta"
```

SAM 3.1 uses the reasoning API key unless `Ai:Meta:Sam31:ApiKey` is supplied. `Ai:Meta:Sam31` defaults to `https://api.meta.ai/v1/`, model `sam-3.1`. One concept is sent per Responses request, with mask confidence requested. Geometry is decoded with `@meta-sam/parser` 0.1.0 through a local Node process; that process receives response geometry only, not keys or images. Pixel-center contours are converted to image-edge coordinates before measurement.

The existing `Roboflow` and `Sidecar` segmentation services are still supported. There is no automatic fallback to simulation if Meta fails.

For deployment, ship `backend/tools/sam31` with its dependencies and set `Ai:Meta:Sam31ParserScript` to the absolute `decode.mjs` path. The development default resolves `../../tools/sam31/decode.mjs` against the API content root. `Ai:Meta:NodeExecutable` defaults to `node` and can be an absolute executable path.

The parser is distributed by Meta under its SAM license. Retain its accompanying license when distributing its package. Package provenance: https://github.com/meta-models/meta-sam . API documentation: https://dev.meta.ai/docs/sam/overview .

## Tool execution

The coordinator can call:

1. `load_region`: immutable profile, source versions and coverage.
2. `load_city_network`: real streets when explicit inspection coordinates were provided. Missing coordinates or unavailable Overpass data remain visible.
3. `segment_image`: perception specialist. Optional concepts must belong to `Analysis:SegmentationConcepts`.
4. `measure_segments`: ground projection and polygon metrics.
5. `diagnose_distresses`: diagnostic specialist; exactly one finding per measured mask is required.
6. `compute_report`: quantities, PCI and a budget when a local price book exists.
7. `assess_mobility`: existing Puno scenario and specialist when a matching demo segment is assigned; otherwise records the additional road/demand information required.
8. `verify_report`: deterministic checks plus an independent specialist review.
9. `finish`: accepted only after verification.

Repeated segmentation invalidates measurements, diagnosis, calculations and verification. A model's prose cannot mark an inspection completed. Every executed tool records its role, model or deterministic provenance, status, duration and evidence. The trace exposes actions and outputs, not private model reasoning.

Settings under `Orchestration`:

| Setting | Default |
| --- | --- |
| `MaxToolCalls` | 16 |
| `MaxAttemptsPerTool` | 2 |
| `TimeoutSeconds` | 300 |
| `MaxOutputTokens` | 2048 per coordinator turn |
| `MaxSegments` | 200 |

These are execution limits, not an exact currency budget. Reported tokens cover the coordinator only; specialist and segmentation usage are additional. A timeout or exhausted execution fails visibly. A completed preliminary report can have `NeedsReview` status due to assumptions or missing evidence.

`Ai:Meta:MaxSpecialistOutputTokens` defaults to 8192 per specialist request (configurable from 512 to 32768). Coordinator turns use their own `MaxOutputTokens` setting.

Changing measured depth recalculates quantities and PCI using the saved regional configuration, and marks the old review as stale. Original action evidence is retained and explicitly identified as describing the original calculation.

## Regional configuration

`GET /api/v1/regions` lists the available configurations. New multipart inspection fields are `regionId`, `latitude` and `longitude`. Coordinates must be supplied together. Providing coordinates permits a server query of that OpenStreetMap area.

Built-in profiles:

- `pe-puno`: the existing reference configuration and illustrative PEN price book. Not certified local compliance.
- `global-reference`: the same implemented condition method with neutral regional notes and no price book. Its budget endpoint rejects estimates instead of silently reusing Peruvian prices.

Additional profiles are configured through `Regions:Profiles` in deployment settings. See `region-profile.example.json`; merge its `Regions` section into configuration and fill project-specific data. Prices are unavailable by default. For an actual budget, provide all relevant unit prices, currency, taxes, quantity assumptions and source versions, and set `PricesAvailable=true`. `JurisdictionValidated=true` requires versioned HTTPS references; it is a declaration made by the configuration maintainer, not an automatic legal certification by the model.

The selected profile is serialized before queuing and retained with the inspection. Later catalog edits do not replace that snapshot. The diagnostic and maintenance prompts receive this context. The only implemented condition method currently accepted is `ASTM D6433 reference model`; other methods require their own calculator and validation.

## Geographic network and detours

Endpoints:

- `GET /api/v1/geography/network?latitude=-15.8402&longitude=-70.0219&radiusM=600`
- `POST /api/v1/geography/networks/{id}/closure` with `{ "edgeId": "<imported segment id>" }`

Overpass imports a 200–1500 m area, capped at 8 MB, 5000 nodes and 7000 edges. The provider caches up to 16 network snapshots in memory; identical queries reuse a snapshot for 20 minutes. A restart or eviction requires loading the area again. IDs and retrieval times identify the data used in a map session; these snapshots are not durable GIS archives.

Routing uses a directed graph, explicit access exclusions, one-way streets, roundabouts and simple via-node turn restrictions. Reversible streets are excluded. Complex/conditional restrictions are reported for local review. This is reference routing for cars, not a validated truck, bus, emergency or operational work-zone plan. Missing speeds are explicitly assumed and flagged. Expanding the area may be necessary when a closure has no alternative within the imported boundary.

The geographic view does not invent traffic counts, capacities or origin-destination demand. Its times exclude congestion. The original Wardrop/BPR planner remains a separate, clearly labelled synthetic Puno experiment. Imported geographic demand calibration and geographic traffic equilibrium are not implemented.

Basemap tiles come directly from `https://tile.openstreetmap.org` through ordinary browser image requests, with browser caching and visible attribution. No tile prefetch or offline download is implemented. For public/high-volume deployment, select a suitable tile provider and review its service limits. OSM attribution and data-license information: https://www.openstreetmap.org/copyright . Tile policy: https://operations.osmfoundation.org/policies/tiles/ .

## Validation and current limits

Tests cover coordinator tool dispatch and usage, premature completion, diagnosis coverage, regional budget refusal, saved execution evidence, directed routing, turn restrictions, SAM request shape, and the real parser using a documented wire fixture. Existing geometry, pavement and API tests are retained.

```powershell
cd backend
dotnet test --solution PaveLens.slnx
cd ../frontend
npx ng test --watch=false
cd ..
node --test backend/tools/sam31/decode.test.mjs
```

Tests use simulated providers or mock HTTP responses. They do not establish field accuracy or prove that a live Meta account can access the configured models. A live key and real calibrated photos are required for that verification.

Verification on 2026-11-02 (America/Lima): 98 unit tests, 35 API integration tests, 10 Angular tests and 3 Node parser tests passed. Backend compilation succeeded without warnings. The production Angular build passed with an initial-bundle size warning (about 589 kB versus the 500 kB warning budget). A live Overpass request timed out in this environment; the adapter now returns a descriptive availability error instead of a generic internal error. Live geographic import is not claimed as verified.

Cleanup on 2026-11-03 removed unused routing setup and legacy styles. The subsequent production Angular build is 510.21 kB (warning threshold: 500 kB); all 10 Angular tests and the application/test TypeScript checks passed again. Backend compilation remained warning-free.

Mask holes (intact pavement inside an outline) are kept by both SAM adapters: their area is subtracted from the measurement, they are stored with the distress and drawn on the photo. No-detection results remain visible analysis failures requiring inspection/retry, rather than being declared healthy pavement. Autonomous extraction/approval of country regulations, automatic global compliance, video aggregation, calibrated global traffic and certified engineering decisions are outside this implementation.
