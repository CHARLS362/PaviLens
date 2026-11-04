# PaveLens AI 🛣️

> **Meta Global AI Developer Hackathon · Indie Developer Track**
> From one road photo to a measured, costed and traffic-aware pavement repair plan. Meta SAM 3 outlines every distress, a ground projection turns it into real metres, and PaveLens computes the Pavement Condition Index (ASTM D6433), patch quantities, a unit-price budget and the traffic impact of closing the street. Meta Muse Wonder classifies, explains and plans the works.

*Leer en español: [README.es.md](README.es.md)*

New: [agent orchestration, native Meta SAM 3.1, regional profiles and geographic maps](backend/docs/ORCHESTRATION.md).

## Demo video

[![PaveLens AI demo: one photo becomes a measured, priced and planned road repair. Click to watch (1:52)](docs/media/demo-thumbnail.jpg)](docs/media/PaveLens_AI_demo.mp4)

**[▶ Watch the demo (1:52, MP4)](docs/media/PaveLens_AI_demo.mp4)** · [YouTube](https://youtu.be/bFq20ClnQ1o) · [English subtitles (.srt)](docs/media/PaveLens_AI_demo.en.srt)

The video shows a real run with the Meta models: the 89-second analysis is sped up and labelled as such, and the detour planner runs on the synthetic demo network of central Puno.

| Time | Section |
|---|---|
| 0:00 | Intro: SAM 3.1 outlines and measures every distress |
| 0:09 | The problem in Puno, Peru |
| 0:27 | One photo, one scale, one click |
| 0:32 | The real analysis run |
| 0:36 | 01 See: Meta SAM 3.1 segmentation |
| 0:44 | 02 Reason: the Muse Wonder agent and its independent reviewer |
| 1:01 | 03 Compute: PCI, budget and live recalculation from a field depth |
| 1:14 | 04 Plan: detour planning with a Wardrop traffic equilibrium |
| 1:28 | The result |
| 1:39 | Open source on GitHub |

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
| Diagnosis, assistant, traffic plan, agent coordinator | Meta Muse Wonder, or the best Muse model the key can use | `https://api.meta.ai/v1`, OpenAI-compatible | `META_API_KEY` |

Keys go in a git-ignored **`.env` file at the repository root**, created from [`.env.example`](.env.example):

```dotenv
META_API_KEY=<Meta API key>
```

That one key is enough: the mode switches to Meta, segmentation uses SAM 3.1 on Meta, and on startup the API asks Meta which models the key can use and picks the best (newest Muse Wonder, else Muse Spark; newest SAM; never `-contributor` models). Add `ROBOFLOW_API_KEY` to segment with SAM 3 on Roboflow instead, or set `META_REASONING_MODEL` to fix a model. Without keys, the mode is `Simulation`. Results come from fixed scenarios keyed by the image hash and are labelled `engine: "Simulation"`, and the UI shows a *Demo mode* badge. Environment variables (`Ai__Meta__Reasoning__ApiKey`...) and `dotnet user-secrets` also work; keys never go in `appsettings.json`. Every option is explained in **[docs/GETTING_STARTED.md](docs/GETTING_STARTED.md#5-use-the-real-meta-models-env)**.

Every inspection shows the **Meta AI pipeline** panel, which traces the work stage by stage:
- the concepts sent to SAM 3 and the masks returned;
- the projection used;
- the Muse Wonder model and how many attempts its JSON needed;
- which numbers were computed by code.

## Quick start

Requirements: **Git**, the **.NET 10 SDK** and **Node.js 22.22+ or 24**. No database server, Docker or GPU. The only thing you add is your Meta API key.

**1. Clone**

```bash
git clone https://github.com/CHARLS362/PaviLens.git
```

```bash
cd PaviLens
```

**2. Add your Meta API key.** Copy the template to `.env`:

```powershell
Copy-Item .env.example .env
```

(macOS / Linux: `cp .env.example .env`). Open `.env`, paste your key after `META_API_KEY=` and save:

```dotenv
META_API_KEY=paste-your-meta-api-key-here
```

Get a key at https://dev.meta.ai. Skip this step to run the free **demo mode** instead.

> 🔒 **Keep your key private.** `.env` is git-ignored, so `git add .` never picks it up. Never paste the key into `appsettings.json`, the code, an issue or a screenshot. If a key is ever exposed, revoke it at https://dev.meta.ai and create a new one.

**3. Start**

Windows (PowerShell):

```powershell
powershell -ExecutionPolicy Bypass -File .\start.ps1
```

macOS / Linux:

```bash
bash start.sh
```

The script checks the prerequisites, installs the packages on the first run (1–3 minutes) and starts the API and the web app.

**4. Open http://localhost:4200**, click **New inspection**, upload a real road photo, set the scale and click **Analyse**.

**5. Check that the real models are on.** The top bar shows the model names (for example *SAM 3.1 · Muse Wonder 1.0*) instead of *Demo mode*, and http://localhost:5000/api/v1/ai/status reports `Meta`. Still in demo mode? The API reads `.env` only on startup: close the API window and run the start script again.

- API: http://localhost:5000, with interactive docs at `/scalar`.
- With real Meta models, use a real road photo: the three built-in sample scenes are drawings meant for demo mode.

📘 Manuals:
- **[docs/GETTING_STARTED.md](docs/GETTING_STARTED.md)**: installation step by step, manual start, every configuration option and troubleshooting.
- **[docs/USER_GUIDE.md](docs/USER_GUIDE.md)**: how to use every screen, from taking the photo to the budget and the detour plan.

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

- [docs/GETTING_STARTED.md](docs/GETTING_STARTED.md): installation, configuration and troubleshooting.
- [docs/USER_GUIDE.md](docs/USER_GUIDE.md): user manual for every screen.
- [backend/README.md](backend/README.md): running, configuration and API reference.
- [backend/docs/ARCHITECTURE.md](backend/docs/ARCHITECTURE.md): layers, pipeline, engineering models and storage.
- [backend/docs/ai-contracts.md](backend/docs/ai-contracts.md): contracts with SAM 3 and Muse Wonder.

## Tests

160 backend tests (unit and integration) and 14 frontend tests. Run the tests from `backend/` because `global.json` lives there:

```bash
cd backend
```
```bash
dotnet test --solution PaveLens.slnx
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
