# PaveLens AI 🛣️

> **Meta Global AI Developer Hackathon · Indie Developer Track**
> From one road photo to a measured, costed and traffic-aware pavement repair plan. Meta SAM 3 outlines every distress, a ground projection turns it into real metres, and PaveLens computes the Pavement Condition Index (ASTM D6433), patch quantities, a unit-price budget and the traffic impact of closing the street. Meta Muse Wonder classifies, explains and plans the works.

*Leer en español: [README.es.md](README.es.md)*

New: [agent orchestration, native Meta SAM 3.1, regional profiles and geographic maps](backend/docs/ORCHESTRATION.md). See the [implementation and frontend review report (Spanish)](docs/REPORTE_IMPLEMENTACION.es.md) for verified scope and remaining work.

![An angled photo of a street: every distress outlined and measured in metres, with the PCI, the Meta AI pipeline trace and the quantities](docs/screenshots/inspection.png)

## The problem

Pavement surveys are still done on foot, with a tape measure and a notebook. Severity ratings vary between inspectors, patch quantities are estimated by eye, and the budgets cannot be audited against the damage. When a crew closes a lane, nobody models the detour, so congestion moves to streets with no spare capacity. Municipalities such as Puno, Peru (3,827 m above sea level, nightly frost, extreme UV) face this with very small maintenance budgets.

## What it does

1. **Segmentation (Meta SAM 3).** The photo goes to SAM 3 with the text concepts `pothole`, `alligator crack`, `longitudinal crack`, `transverse crack` and `rutting`. It returns one polygon per distress, not a bounding box. When two concepts return the same mask, the more confident one is kept.
2. **Measurement in metres.** The photo is uploaded or taken with the device camera.
   - For a top-down photo, the visible road width is enough.
   - For an angled photo, the inspector marks 4 corners of a known rectangle and PaveLens solves a homography. A projected 1 m grid confirms the calibration.
   - Before the analysis, the app estimates the ground resolution (mm/px), the finest measurable crack and the surveyed area.
   - Each projected polygon gets its area (Green's theorem), perimeter, oriented length × width and eccentricity. Tests check every demo mask against the true drawn area within 1 %.
3. **Diagnosis (Meta Muse Wonder).** The model sees the photo together with the measurements already computed. It assigns a code, type, severity, finding and intervention, writes the repair protocol, and answers the crew's questions. It **never** computes areas, quantities or costs: code does, so a hallucination cannot inflate a budget. Its JSON is validated against the segments, with one self-repair retry.
4. **PCI (ASTM D6433).** The index is built from deduct values by density, the maximum number of deducts *m* and an iterative CDV. When the photo covers less than one 135 m² sample unit, the PCI is flagged as partial (an upper bound).
5. **Quantities and budget.** Each patch gets a rectangular saw cut with a margin, a volume and the tonnes of hot mix. The budget uses Peruvian MTC work items priced in soles (PEN), plus overhead, profit and IGV. Site conditions for Puno are included: polymer-modified binder, frost and UV. A depth measured in the field replaces the assumed one and recomputes everything.
6. **Detour planning.** Lane closures are simulated on a demo network of central Puno, drawn as a map with flow per direction.
   - Traffic is assigned with a Wardrop user equilibrium (Frank-Wolfe + BPR), by time window (peak, off-peak, night) and works duration.
   - The result gives the vehicle-hours lost, the streets that absorb the detour and the social cost in soles, and recommends the cheapest window.
   - Muse Wonder turns the result into a traffic management plan: routes by vehicle type, signage, work phases, public notices and risks.
7. **Try it without a photo.** The new-inspection dialog offers the three demo scenes with their scale already set, so anyone can run the whole flow in a few clicks. On a phone, the viewer supports pinch to zoom and one-finger pan.
8. **Spanish and English.** The interface switches between ES and EN at any time. Every inspection and traffic plan records the language its report is written in: Muse Wonder receives it in the prompt, and the demo scenarios ship in both languages.
9. **Accessibility.** An *Accessibility* panel in the top bar sets the text size (100, 115 or 130 %), high contrast, reduced motion and a color-blind-safe palette that also marks severity with dash patterns. Preferences persist in the browser, and the system settings (`prefers-contrast`, `prefers-reduced-motion`, forced colors) apply on their own. The photo viewer works from the keyboard (+, −, arrows, 0) and announces the selected distress and zoom to screen readers. Tabs, dialogs, live regions and every icon button have proper ARIA, and all font sizes are in rem so browser zoom and text settings are respected.

## Screenshots

| | |
|---|---|
| ![Zoomed onto the alligator-cracking outline next to the unit-price budget](docs/screenshots/budget.png) | ![Lane closure on Jr. Lima: detour routes, streets that absorb the traffic, signs and the time-window costs](docs/screenshots/detour-planner.png) |
| **Show on photo** zooms onto a distress; the budget is computed by code from the measured areas. | **Detour planning**: Wardrop equilibrium on the demo network of central Puno, with the plan written by Muse Wonder. |

*The screenshots run in demo mode (`Ai:Provider = Simulation`), which the app labels on every screen.*

## Meta models

Both models are consumed through hosted APIs, so no GPU or model server is needed.

| Step | Model | Service | Key |
|---|---|---|---|
| Segmentation | Meta SAM 3 (Roboflow serverless) or SAM 3.1 (Meta Model API) | `PAVELENS_SEGMENTATION_SERVICE` = `Roboflow` / `Meta` | `ROBOFLOW_API_KEY`, or `META_API_KEY` for SAM 3.1 |
| Diagnosis, assistant, traffic plan, agent coordinator | Meta Muse Wonder (`muse-wonder-1.0`) | `https://api.meta.ai/v1`, OpenAI-compatible | `META_API_KEY` |

Keys go in a git-ignored **`.env` file at the repository root**, created from [`.env.example`](.env.example):

```dotenv
PAVELENS_AI_PROVIDER=Meta
META_API_KEY=<Meta API key>
PAVELENS_SEGMENTATION_SERVICE=Roboflow
ROBOFLOW_API_KEY=<Roboflow key>
```

Without keys, `PAVELENS_AI_PROVIDER` is `Simulation`. Results come from fixed scenarios keyed by the image hash and are labelled `engine: "Simulation"`, and the UI shows a *Demo mode* badge. Environment variables (`Ai__Meta__Reasoning__ApiKey`...) and `dotnet user-secrets` also work; keys never go in `appsettings.json`. Every option is explained in **[docs/GETTING_STARTED.md](docs/GETTING_STARTED.md#5-use-the-real-meta-models-env)**.

Every inspection shows the **Meta AI pipeline** panel, which traces the work stage by stage:
- the concepts sent to SAM 3 and the masks returned;
- the projection used;
- the Muse Wonder model and how many attempts its JSON needed;
- which numbers were computed by code.

## Quick start

Requirements: **.NET 10 SDK** and **Node.js 22.22+ or 24**. No keys, database server or GPU needed for the demo.

```bash
git clone https://github.com/CHARLS362/PaviLens.git
```

```bash
cd PaviLens
```

Windows:

```powershell
.\start.ps1
```

macOS / Linux:

```bash
bash start.sh
```

The script checks the prerequisites, creates `.env` from `.env.example` (demo mode), installs the frontend packages on the first run and starts both parts.

- Web: http://localhost:4200 (proxies `/api` to the backend)
- API: http://localhost:5000, with interactive docs at `/scalar`

Step-by-step guide, manual start, real Meta keys and troubleshooting: **[docs/GETTING_STARTED.md](docs/GETTING_STARTED.md)**.

In Development, the backend uses SQLite, applies the migrations and seeds three demo inspections: Jr. Lima (calibrated angled photo), Av. El Sol (pothole) and Jr. Arequipa (alligator cracking).

## Layout

```
meta/
├── backend/                      .NET 10, Clean Architecture
│   ├── PaveLens.slnx
│   ├── src/
│   │   ├── PaveLens.Domain/          Inspection, distresses, validated geometry
│   │   ├── PaveLens.Application/     Use cases, projection, PCI, quantities, budget, traffic
│   │   ├── PaveLens.Infrastructure/  EF Core (SQLite/PostgreSQL), blobs, queue + worker, AI adapters
│   │   └── PaveLens.Api/             Minimal API /api/v1, ProblemDetails, rate limiting
│   ├── tests/                    Unit and integration tests (xUnit v3)
│   └── samples/                  Demo scenes and their generator
├── frontend/                     Angular 22 (standalone, signals), ES/EN
├── .env.example                  Configuration template: copy to .env for real Meta keys
└── start.ps1 / start.sh          Check prerequisites, create .env, start the API and the web app
```

## Documentation

- [backend/README.md](backend/README.md): running, configuration and API reference.
- [backend/docs/ARCHITECTURE.md](backend/docs/ARCHITECTURE.md): layers, pipeline, engineering models and storage.
- [backend/docs/ai-contracts.md](backend/docs/ai-contracts.md): contracts with SAM 3 and Muse Wonder.
- In Spanish: [tematica.md](tematica.md) (product and user flow), [PLANIFICACION.md](PLANIFICACION.md) (plane projection, PCI, quantities, MTC unit prices, BPR + Wardrop) and [DESAFIO.md](DESAFIO.md) (hackathon rules and submission).

## Tests

133 backend tests (unit and integration) and 10 frontend tests:

```bash
dotnet test --solution backend/PaveLens.slnx
```

```bash
npm --prefix frontend test -- --watch=false
```

## Roadmap

These items are **not implemented**. They are designed to grow without touching the computation core.

- Video inspection with SAM 3.1 (mask tracking across frames), aggregated by sample unit.
- A georeferenced map of inspections from the photo's GPS.
- A real road network from OpenStreetMap instead of the demo network.
- Validation of the segmentation against Crack500 and RDD2022.
