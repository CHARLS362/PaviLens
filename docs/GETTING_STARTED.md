# Getting started

From a fresh clone to a running PaveLens in about five minutes. Demo mode needs no keys and costs nothing. The real Meta models need one or two API keys in a `.env` file.

*En español: [INSTALACION.es.md](INSTALACION.es.md)*

## 1. Prerequisites

| Tool | Version | Check | Download |
|---|---|---|---|
| Git | any | `git --version` | https://git-scm.com |
| .NET SDK | 10.0.100 or later | `dotnet --list-sdks` | https://dotnet.microsoft.com/download/dotnet/10.0 |
| Node.js | 22.22+ or 24 LTS (npm included) | `node --version` | https://nodejs.org |

No database server, Docker or GPU is needed: the API uses SQLite in a local file, and the Meta models run behind hosted APIs.

## 2. Clone

```bash
git clone https://github.com/CHARLS362/PaviLens.git
```

```bash
cd PaviLens
```

## 3. Run

### Option A: one command

Windows (PowerShell):

```powershell
.\start.ps1
```

If PowerShell refuses to run scripts:

```powershell
powershell -ExecutionPolicy Bypass -File .\start.ps1
```

macOS / Linux:

```bash
bash start.sh
```

The script:
1. checks .NET 10 and Node;
2. creates `.env` from `.env.example` on the first run (demo mode);
3. installs the frontend packages on the first run (`npm ci`, about a minute);
4. starts the API and the web app.

### Option B: by hand, in two terminals

Terminal 1, the API:

```bash
dotnet run --project backend/src/PaveLens.Api --launch-profile http
```

Terminal 2, the web app:

```bash
cd frontend
```

```bash
npm ci
```

```bash
npm start
```

### Open it

| What | URL |
|---|---|
| Web app | http://localhost:4200 |
| API reference (Scalar) | http://localhost:5000/scalar |
| Health | http://localhost:5000/health |
| Active AI provider | http://localhost:5000/api/v1/ai/status |

On the first start the API creates the SQLite database (`backend/src/PaveLens.Api/App_Data/pavelens.db`) and loads three demo inspections: Jr. Lima (calibrated angled photo), Av. El Sol (pothole) and Jr. Arequipa (alligator cracking).

## 4. Try it (demo mode)

1. Pick an inspection on the left: the photo shows each distress outlined and measured in metres, with the PCI and the quantities.
2. Click a code in the **distress log** (right panel): the photo zooms onto it.
3. Open the **Budget** tab: unit-price budget computed by code from the measured areas.
4. **New inspection** → *Try a sample scene* → **Analyze**: the full pipeline runs again and the agent panel shows every tool call.
5. **Detour planning** (top bar): close a lane on the Puno demo network and compare time windows.
6. **ES / EN** switches the language; **Accessibility** sets text size, contrast, motion and a color-blind-safe palette.

In demo mode results come from fixed scenarios and every screen says *Demo mode*.

## 5. Use the real Meta models (`.env`)

All keys go in **one file: `.env` at the repository root** (next to `README.md`). It is git-ignored, so keys never reach GitHub. If the start script did not create it:

```powershell
Copy-Item .env.example .env
```

```bash
cp .env.example .env
```

Open `.env` and fill in:

```dotenv
PAVELENS_AI_PROVIDER=Meta
META_API_KEY=<your Meta API key>
PAVELENS_SEGMENTATION_SERVICE=Roboflow
ROBOFLOW_API_KEY=<your Roboflow key>
```

| Variable | Used for | Where to get it |
|---|---|---|
| `PAVELENS_AI_PROVIDER` | `Simulation` (demo) or `Meta` (real models) | — |
| `META_API_KEY` | Muse Wonder: diagnosis, assistant, traffic plan, agent coordinator. Also SAM 3.1 when the segmentation service is `Meta`. | https://dev.meta.ai |
| `PAVELENS_SEGMENTATION_SERVICE` | `Roboflow` (SAM 3, default), `Meta` (SAM 3.1 direct) or `Sidecar` | — |
| `ROBOFLOW_API_KEY` | SAM 3 on Roboflow's serverless API | https://app.roboflow.com → Settings → API Keys |
| `META_SAM31_API_KEY` | Optional: a separate key for SAM 3.1 | https://dev.meta.ai |
| `META_REASONING_MODEL` | Optional: model name, default `muse-wonder-1.0` | — |

Two working setups:

- **Meta + Roboflow** (two keys): `PAVELENS_SEGMENTATION_SERVICE=Roboflow`, `META_API_KEY`, `ROBOFLOW_API_KEY`.
- **Meta only** (one key, SAM 3.1 direct): `PAVELENS_SEGMENTATION_SERVICE=Meta` and `META_API_KEY`. It also needs the mask decoder once. `start.ps1` / `start.sh` install it automatically; by hand:

  ```bash
  cd backend/tools/sam31
  ```

  ```bash
  npm ci
  ```

Restart the API after editing `.env`. Then check:

- the API log prints `Configuration: ...\.env | AI provider: Meta`;
- http://localhost:5000/api/v1/ai/status reports `Meta` with both keys configured;
- the *Demo mode* badge in the top bar is replaced by the model names.

With `PAVELENS_AI_PROVIDER=Meta` the API **refuses to start** if a required key is missing, and says which one. It never falls back to demo results silently.

### Precedence and other ways to set keys

Highest wins:

1. command-line arguments;
2. real environment variables, e.g. `Ai__Meta__Reasoning__ApiKey` (for containers and CI);
3. `.env`;
4. `dotnet user-secrets`;
5. `appsettings.json` (no secrets here, ever).

Any configuration key can go in `.env` with `__` instead of `:`, for example `Ai__Meta__Reasoning__TimeoutSeconds=180` or `Orchestration__MaxToolCalls=12`. The full list is in [backend/README.md](../backend/README.md) and [backend/docs/ORCHESTRATION.md](../backend/docs/ORCHESTRATION.md).

## 6. Run the tests

```bash
dotnet test --solution backend/PaveLens.slnx
```

```bash
cd frontend
```

```bash
npm test -- --watch=false
```

The tests always run in demo mode and ignore `.env`, so they never spend credits.

## 7. Troubleshooting

| Symptom | Fix |
|---|---|
| `start.ps1 cannot be loaded because running scripts is disabled` | `powershell -ExecutionPolicy Bypass -File .\start.ps1` |
| `A compatible .NET SDK was not found` | Install the .NET 10 SDK (`global.json` asks for 10.0.100 or later). |
| `The Angular CLI requires a minimum Node.js version` | Install Node 22.22+ or 24 LTS. |
| Port 5000 or 4200 already in use | Close the other process, or run the API with `--urls http://localhost:5055` and update `frontend/proxy.conf.json`. |
| The web app says *Disconnected* | The API is not running or is still starting; check its window for errors. |
| API exits with `Ai:Meta:...:ApiKey es obligatorio` | A key is missing in `.env` for the chosen provider/service. |
| Real models return 401/403 | The key is wrong or the account has no access to the model; try it in the provider's console. |
| SAM 3.1: `node` or `decode.mjs` not found | Run `npm ci` in `backend/tools/sam31` and make sure `node` is on the PATH. |
| Start over with clean demo data | Stop the API and delete `backend/src/PaveLens.Api/App_Data/`; it is recreated on the next start. |
