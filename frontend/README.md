# PaveLens web

Angular 22 single-page app (standalone component, signals) for reviewing pavement inspections.

## Run

```bash
npm install
npm start
```

Opens on http://localhost:4200. `proxy.conf.json` forwards `/api` to the backend on http://localhost:5000, so start the API first (`dotnet run --project ../backend/src/PaveLens.Api --launch-profile http`, or `..\start.ps1` for both).

## What it shows

- **Inspection list** with the PCI band of each inspection.
- **New inspection**, a three-step dialog:
  1. *Fotografía*: drop or pick a file, or shoot with the device camera (`getUserMedia`, rear camera, needs HTTPS or localhost). On phones, the native camera is also offered.
  2. *Escala*: nadir photos take the visible road width. Oblique photos take four clicked corners of a rectangle of known size; the handles can be dragged, and a 1 m ground grid is projected back onto the photo with the same homography as the API, so a bad calibration shows at a glance.
  3. *Datos y estimación*: title, location (with optional GPS), segment and notes, next to an estimate of the ground resolution in mm/px (near and far edge for oblique photos), the thinnest measurable crack (about 2 px), and the surveyed area, with a warning when it is below the 135 m² ASTM sample unit.
- **Viewer:**
  - each SAM 3 mask drawn over the photo, coloured by severity;
  - zoom up to 600 % and pan, with labels kept at screen size;
  - the cursor position in metres on the road surface: the frontend solves the same homography as the API for oblique photos, and draws a scale bar for nadir ones.
- **Inspector tabs:**
  - *Evaluación*: PCI with its deduct values, plus each distress with its measurements and quantities. Entering a field depth recalculates quantities, severity and PCI.
  - *Presupuesto*: the APU budget in PEN.
  - *Tránsito*: a shortcut to the closure planner for the inspection's segment.
  - *Protocolo*: repair steps that can be checked off, and the maintenance assistant.
- **Planificación de desvíos**, a full-screen closure planner:
  - **Map:** a stylised map of central Puno, with blocks, Plaza de Armas and the lake shore. Each direction of every street is drawn as its own band, coloured by v/c (or by the change in flow) and animated at a speed that follows travel time. It also shows the work zone, numbered detour segments, and a tooltip per direction.
  - **Controls:** lanes closed, work window (peak, off-peak, night) and duration. Every change re-solves the Wardrop equilibrium on the API.
  - **Results:** the social cost of the closure in PEN, its formula, the cost of each window with the recommended one, the detour list, and the model assumptions.
- In the top bar, a chip shows the active models (SAM 3 · Muse Wonder), warns when a key is missing, or shows *Modo demostración* when the backend runs with `Ai:Provider = Simulation`.

## Accessibility

The target is WCAG 2.1 AA, with plain Spanish for non-specialists:

- **Keyboard:**
  - Every action works without a mouse, and a skip link jumps to the main content.
  - Inspector tabs follow the WAI-ARIA tabs pattern: arrows, Home and End move between them.
  - The new-inspection dialog moves focus inside on open, keeps Tab within it, closes with Escape, and returns focus to the button that opened it.
  - Calibration corners move with the arrow keys (Shift for fine steps). *Colocar esquinas guía* places a starting quad for people who cannot click precisely.
  - On the closure map, a street can also be chosen from a list.
- **Screen readers:**
  - Toggle buttons expose `aria-pressed`, and the current module and step expose `aria-current`.
  - The map has a text description, and the side panel holds the same results.
  - Dialog step changes and each new planner result are announced through `aria-live`.
- **Colour:**
  - Text meets 4.5:1. Severity and PCI colours have darker text shades (`--ct`).
  - Traffic load is never shown by colour alone: it is also named (*Fluido* … *Colapsado*).
  - Forced-colors mode is supported.
- **Readability:**
  - Interface text is at least 12 px. Hidden button reasons are written out, for example *Faltan 2 esquina(s) por marcar*.
  - Technical terms (v/c, Wardrop, homography) are moved to the *Cómo se calcula* section.

## Build and test

```bash
npx ng build
```

```bash
npx ng test --watch=false
```

Unit tests run on Vitest. Output goes to `dist/frontend`.
