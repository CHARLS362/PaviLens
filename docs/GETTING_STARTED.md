# Getting started

From a fresh clone to a running PaveLens in about five minutes. Demo mode needs no keys and costs nothing. The real Meta models need one or two API keys in a `.env` file.

*En español: [INSTALACION.es.md](INSTALACION.es.md)* · Once it runs, see the **[User guide](USER_GUIDE.md)** for how to use every screen.

## Fast path: only your Meta API key

If you already have Git, the .NET 10 SDK and Node.js 22.22+ (see [Prerequisites](#1-prerequisites)), these are the only steps:

**1. Clone the repository**

```bash
git clone https://github.com/CHARLS362/PaviLens.git
```

```bash
cd PaviLens
```

**2. Create `.env` and paste your Meta API key.** Copy the template:

Windows (PowerShell):

```powershell
Copy-Item .env.example .env
```

macOS / Linux:

```bash
cp .env.example .env
```

Open `.env` in any text editor, find the line `META_API_KEY=` and paste your key after the `=` sign, with no spaces or quotes:

```dotenv
META_API_KEY=paste-your-meta-api-key-here
```

Save the file. Leave every other line as it is. Don't have a key yet? Create one at https://dev.meta.ai. If you skip this step, PaveLens runs in free **demo mode**.

**3. Start PaveLens**

Windows (PowerShell):

```powershell
powershell -ExecutionPolicy Bypass -File .\start.ps1
```

macOS / Linux:

```bash
bash start.sh
```

The first run installs the packages (1–3 minutes). On Windows, two new windows open: one for the API and one for the web app. Keep them open while you use PaveLens. Closing them stops it.

**4. Open http://localhost:4200.** The top bar should show the Meta models, for example *SAM 3.1 · Muse Wonder 1.0*, instead of *Demo mode*. Click **New inspection**, upload a real photo of a road, set the scale and click **Analyse**. The [User guide](USER_GUIDE.md) walks through each step.

> With real Meta models, upload a **real road photo**. The three built-in sample scenes are drawings: SAM usually finds no damage in them, and the analysis ends as *Failed* with that reason. They are meant for demo mode.

The rest of this page explains each step in detail, the other configuration options and troubleshooting.

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
4. **New inspection** → *Try a sample scene* → **Analyse**: the full pipeline runs again and the agent panel shows every tool call. (Sample scenes are for demo mode; with real Meta models, upload a real road photo.)
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
META_API_KEY=<your Meta API key>
```

Any Meta API key works on its own. With the defaults (`Auto`) the API switches to the real models, segments with SAM 3.1 on Meta using the same key, and on startup lists the models the key can use (`GET /v1/models`) to pick the best ones: the newest Muse Wonder, else the newest Muse Spark, and the newest SAM. `-contributor` models, which share data for training, are never chosen. The startup log and `GET /api/v1/ai/status` show the models picked.

| Variable | Used for | Where to get it |
|---|---|---|
| `PAVELENS_AI_PROVIDER` | `Auto` (default: `Meta` when `META_API_KEY` is set, else demo), `Simulation` or `Meta` | — |
| `META_API_KEY` | Muse Wonder: diagnosis, assistant, traffic plan, agent coordinator. Also SAM 3.1 when the segmentation service is `Meta`. | https://dev.meta.ai |
| `PAVELENS_SEGMENTATION_SERVICE` | `Auto` (default: `Roboflow` when `ROBOFLOW_API_KEY` is set, else `Meta`), `Roboflow` (SAM 3), `Meta` (SAM 3.1 direct) or `Sidecar` | — |
| `ROBOFLOW_API_KEY` | SAM 3 on Roboflow's serverless API | https://app.roboflow.com → Settings → API Keys |
| `META_SAM31_API_KEY` | Optional: a separate key for SAM 3.1 | https://dev.meta.ai |
| `META_REASONING_MODEL` | Optional: a fixed model name; empty or `auto` picks the best available | — |

Two working setups:

- **Meta only** (one key, SAM 3.1 direct, the default): just `META_API_KEY`.
- **Meta + Roboflow** (two keys): `META_API_KEY` and `ROBOFLOW_API_KEY`.

SAM 3.1 direct needs the mask decoder once. `start.ps1` / `start.sh` install it automatically; by hand:

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

In Meta mode the API **refuses to start** if a required key is missing, and says which one. It never falls back to demo results silently.

### Precedence and other ways to set keys

Highest wins:

1. command-line arguments;
2. real environment variables, e.g. `Ai__Meta__Reasoning__ApiKey` (for containers and CI);
3. `.env`;
4. `dotnet user-secrets`;
5. `appsettings.json` (no secrets here, ever).

Any configuration key can go in `.env` with `__` instead of `:`, for example `Ai__Meta__Reasoning__TimeoutSeconds=180` or `Orchestration__MaxToolCalls=12`. The full list is in [backend/README.md](../backend/README.md) and [backend/docs/ORCHESTRATION.md](../backend/docs/ORCHESTRATION.md).

## 6. Run the tests

Run the tests from `backend/` because `global.json` lives there:

```bash
cd backend
```
```bash
dotnet test --solution PaveLens.slnx
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
| An inspection fails with *… found no distress in the photo* | SAM saw no damage. Expected with the drawn sample scenes in Meta mode; with your own photo, get closer, use even light and keep the damage in the lower half of the frame. See the [User guide](USER_GUIDE.md#9-taking-good-photos). |
| The top bar still says *Demo mode* after adding the key | The API reads `.env` only on startup: close the API window and start it again. Check that the file is named exactly `.env` (not `.env.txt`) and sits next to `README.md`. |
| Start over with clean demo data | Stop the API and delete `backend/src/PaveLens.Api/App_Data/`; it is recreated on the next start. |
