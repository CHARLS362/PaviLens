# PaveLens AI · User guide

This guide explains how to use every screen of PaveLens once it is running. To install and start it, see **[GETTING_STARTED.md](GETTING_STARTED.md)** (short version: put your Meta API key in `.env` and run `start.ps1` or `start.sh`).

Contents:

1. [What PaveLens does](#1-what-pavelens-does)
2. [The main screen](#2-the-main-screen)
3. [Run a new inspection](#3-run-a-new-inspection)
4. [Read the results](#4-read-the-results)
5. [Refine the results in the field](#5-refine-the-results-in-the-field)
6. [Plan the traffic detour](#6-plan-the-traffic-detour)
7. [City maps](#7-city-maps)
8. [Language and accessibility](#8-language-and-accessibility)
9. [Taking good photos](#9-taking-good-photos)
10. [When an analysis fails](#10-when-an-analysis-fails)
11. [Demo mode and Meta mode](#11-demo-mode-and-meta-mode)

---

## 1. What PaveLens does

You give PaveLens one photo of a road. It returns a repair plan:

| Step | Who does it | Result |
|---|---|---|
| Find the damage | **Meta SAM 3.1** (or SAM 3) | One outline (mask) per pothole, crack or cracked area |
| Measure it | PaveLens code | Area in m², length, width, perimeter, from your scale |
| Classify it | **Meta Muse Wonder** | Distress type, severity, finding, recommended treatment |
| Rate the road | PaveLens code (ASTM D6433) | Pavement Condition Index (PCI) from 0 to 100 |
| Cost the repair | PaveLens code | Patch quantities, tonnes of hot mix, unit-price budget |
| Plan the closure | PaveLens code + Muse Wonder | Detour routes, delay, social cost per time window |
| Check the report | Muse Wonder (independent reviewer) | A list of findings a human should review |

The AI never computes a number. Areas, PCI, quantities and costs always come from code, so a model cannot inflate a budget.

---

## 2. The main screen

Open **http://localhost:4200**.

**Top bar**

- **Inspections**, **Detour planning**, **City maps**: the three modules.
- **Model chip** (for example *SAM 3.1 · Muse Wonder 1.0*): the models in use. If it says **Demo mode**, the app runs without real AI (see [section 11](#11-demo-mode-and-meta-mode)).
- **Accessibility**: text size, contrast, motion and colours.
- **ES / EN**: interface language.
- **Connected**: the web app can reach the API. If it says *Disconnected*, the API is not running.
- **New inspection**: starts a new analysis.

**Left column: Saved inspections.** Each card shows the title, the number of distresses and potholes, the date and the PCI badge. Cards marked *Simulation* were produced in demo mode. Click a card to open it.

**Centre: the photo viewer.** The photo with every distress outlined and coloured by severity.

**Right column: the inspector**, with four tabs: **Assessment**, **Budget**, **Traffic** and **Protocol**.

---

## 3. Run a new inspection

Click **New inspection**. The dialog has three steps.

### Step 1 · Photo

Choose one source:

- **Upload photo**: drag a JPEG, PNG or WebP (up to 10 MB) onto the box, or click to choose a file.
- **Use camera**: uses the device camera in the browser (works on `localhost` or HTTPS). Frame the whole road and avoid hard shadows, then press the shutter.
- **Take a photo with your phone**: on a phone, opens the native camera.
- **Try a sample scene**: three drawn scenes with the scale already set. They are meant for **demo mode**. With the real Meta models, SAM usually finds no damage in them because they are drawings, so the analysis fails on purpose (the dialog warns you). Use a real road photo instead.

Click **Continue**.

### Step 2 · Scale

PaveLens needs to know how big the photo is in real metres. Choose how the photo was taken:

**From above** (camera pointing straight down, for example from a drone, a pole or a high window):

1. Enter the **Visible road width (m)**: the real distance on the ground between the left and right edges of the photo.
2. Or click a preset: *Lane 3.5 m*, *2-lane road 7.0 m*, *Street 6.0 m*.
3. A scale bar is drawn on the photo so you can check it.

**At an angle** (photo taken standing on the street or from a vehicle):

1. Find a rectangle on the road whose real size you know, for example lane lines, a crosswalk stripe or a manhole frame.
2. Tap its **4 corners** on the photo in the order the dialog asks. You can drag a corner to adjust it, or move it with the arrow keys (Shift for fine steps). **Place guide corners** drops a starting rectangle if clicking precisely is hard.
3. Enter the **Rectangle width (m)** and **Rectangle length (m)**, or pick one of the **Common rectangles**.
4. A 1 m grid is projected on the road. If the grid lines look square and follow the road, the calibration is good. If they look skewed, move the corners.

Click **Continue**.

### Step 3 · Details and estimate

Fill in:

| Field | Required | Notes |
|---|---|---|
| **Regional profile** | yes | Climate notes and unit prices. *Puno · Peru* has prices in soles (PEN). The international reference has no prices, so no budget is issued. |
| **Latitude / Longitude** | no | Enter both or neither. With coordinates, the AI can load the real street network around the site. |
| **Title** | yes | For example *Av. El Sol · pothole in right lane*. |
| **Location** | no | Free text. **Use my location** fills it from the device GPS. |
| **Road network segment** | no | The street of the demo traffic network to close for the repair. Needed for the detour plan. |
| **Field notes** | no | Observed depth, drainage, heavy traffic, anything the AI should know. |

On the right, the **Pre-analysis estimate** shows, before spending any AI call:

- the **ground resolution** in mm per pixel (near and far edge for angled photos);
- the **thinnest measurable crack** (about 2 pixels);
- the **surveyed area** in m², with a warning when it is smaller than one ASTM sample unit, which makes the PCI partial.

Click **Analyse**. The dialog closes and the new inspection appears at the top of the list as *Analysing…*.

### While it runs

A panel shows each stage live:

1. **Regional context** (code): loads the profile.
2. **Road network** (code): loads the street network if coordinates were given.
3. **See** (Meta SAM): outlines the damage.
4. **Measure** (code): converts outlines into metres.
5. **Diagnose** (Meta Muse Wonder): classifies every distress.
6. **Calculate** (code): PCI, quantities and budget.
7. **Traffic** (code + Muse Wonder): detour plan.
8. **Review** (Muse Wonder): independent check.

With the real models an analysis takes about 1 to 2 minutes. You can keep using the app meanwhile; the page updates by itself.

---

## 4. Read the results

### The header

Title, segment, calibration, models used and total time, followed by five numbers:

- **PCI** with its rating (*Excellent*, *Good*, *Fair*, *Poor* or *Failed*). *(partial)* means the photo covers less than one sample unit, so the real PCI can only be equal or lower.
- **Distresses** and **Potholes**: counts.
- **Affected area** in m² (potholes and area distresses).
- **Asphalt mix** in tonnes needed for the patches.

### The photo viewer

- Every distress has a coloured outline and a code: **B** pothole, **PC** alligator cracking, **FL** longitudinal crack, **FT** transverse crack, **AH** rutting.
- Colours: red high, orange medium, yellow low severity.
- **Severity**, **Cracks**, **Labels** toggles hide or show layers.
- Zoom with the mouse wheel, pinch, the **+ / −** buttons, or the **+** and **−** keys. Drag to pan, or use the arrow keys when the viewer has focus. **0** or a double-click resets the view. Zoom goes up to 600 %.
- The cursor position is shown in **metres on the road** (bottom right), and a **1 m** scale bar is drawn.
- Click an outline to select that distress.

### Assessment tab

- **Segment diagnosis** (Muse Wonder): a plain-language summary of the road.
- **How the AI worked**: a timeline of every step, with who did it (*code* or *Meta · model*), how long it took and its result. **Show technical evidence** opens the raw data of each step, including the tokens used. **Review the findings** jumps to the reviewer's list.
- **Reviewer findings**: issues the independent reviewer wants a human to check, for example a severity that does not fit the geometry. **Method assumptions** lists what the report assumes (reference profile, assumed depth, partial sample…).
- **Pavement Condition Index**: the PCI gauge and the deduct-value table (distress, severity, quantity, density, deduct value), with *m*, the maximum CDV and the iterations, as in ASTM D6433.
- **Selected distress card**: type, severity, confidence, area, length × width, perimeter, orientation, eccentricity, number of outline vertices, and the patch take-off (assumed depth, cut area, volume, tonnes of hot mix). Below it, the **Finding** and **Recommended intervention** written by Muse Wonder.
- **Technical question about …**: ask Muse Wonder anything about the selected distress (for example *"Can I use cold mix here?"*) and click **Ask**.
- **Distress log**: every distress in a table. Click a row, or **Show … on the photo**, to zoom onto it.

### Budget tab

The **Unit-price analysis**: saw cut, removal, tack coat, hot mix, compaction and crack sealing, each with quantity, unit price and subtotal, plus overhead, profit and tax (IGV in Peru), and the **Total budget**. Every quantity comes from the measured areas.

### Traffic tab

A summary of the closure needed for the repair (lanes per direction, capacity, length) and **Plan the closure**, which opens the detour planner on that segment.

### Protocol tab

The **Maintenance protocol** written by Muse Wonder: ordered repair steps, each linked to a distress (**Show … on the photo →**). Tick each step as the crew completes it; the tab title shows the progress (for example *Protocol 3/8*).

---

## 5. Refine the results in the field

The AI assumes a patch depth from the severity. When the crew measures the real depth:

1. Select the distress (on the photo or in the Distress log).
2. In **Depth measured in the field**, type the depth in centimetres.
3. Click **Apply**.

Quantities, severity, PCI and budget are recomputed at once, and the card no longer says *assumed*.

To re-run a whole analysis, open a failed inspection and click **Retry analysis**. To remove one, click **Delete** in the header and confirm.

---

## 6. Plan the traffic detour

Open **Detour planning** in the top bar (or **Plan the closure** from the Traffic tab).

> The street network is a **synthetic demo network of central Puno**. It is not based on real traffic counts.

1. **Pick the street to close**: click a street on the map, or choose it from **Work-zone segment** in the panel.
2. **Lanes closed per direction**: how many lanes the works take.
3. **Work schedule**: *Peak hour*, *Off-peak (09–17 h)* or *Night (22–05 h)*.
4. **Duration of the works**: in hours.

Every change re-solves a Wardrop user equilibrium (Frank-Wolfe with BPR travel times). The map shows:

- the work zone and the numbered detour routes;
- every direction of every street as its own band, coloured by how full it is (the load is also named: *Free-flowing* … *Gridlocked*) and animated at a speed that follows travel time;
- **With works**, **Without works** and **Difference** views.

The panel shows the vehicle-hours lost, the **social cost in soles**, the cost of each time window with the cheapest one recommended, the streets that absorb the traffic, and the model assumptions. When an inspection is linked, Muse Wonder writes a traffic management plan: routes per vehicle type, signage, work phases, public notices and risks.

---

## 7. City maps

**City maps** shows real streets from OpenStreetMap, for exploring a closure anywhere:

1. In **Study area**, pick a city or enter **Latitude**, **Longitude** and the **Area radius**.
2. Click **Load streets in this area**.
3. Use **Find a street** and choose the **Work-zone segment**.
4. Click **Close segment and find detours**. Red is the closed segment, blue the calculated detour.

These are reference routes: there are no traffic counts or live congestion here.

---

## 8. Language and accessibility

- **ES / EN** switches the interface at any time. Each inspection keeps the language its report was written in.
- **Accessibility** sets:
  - **Text size**: 100, 115 or 130 %;
  - **High contrast**;
  - **Reduce motion** (stops the map animation);
  - **Color-blind friendly colors**, which also marks severity with dash patterns.
- Preferences are saved in the browser. System settings (high contrast, reduced motion, forced colours) apply on their own.
- Everything works from the keyboard. Tabs move with the arrow keys, dialogs close with Escape, and the viewer and the map announce changes to screen readers.

---

## 9. Taking good photos

SAM finds what it can see. For reliable results:

- **Real road surface**, close enough that a crack is at least 2 pixels wide. The *Pre-analysis estimate* tells you the thinnest crack you can measure.
- **Even light**: overcast days are best. Avoid hard shadows, wet surfaces and glare.
- **Top-down is the most accurate.** For angled photos, include a rectangle of known size on the road (lane markings work well) and keep it in the lower half of the photo.
- **Avoid the horizon**: damage far from the camera is less accurate, and anything beyond the calibration horizon is excluded from the measurement.
- One street segment per photo. Many small photos are better than one wide shot.

---

## 10. When an analysis fails

PaveLens never invents a result. If a step cannot be completed, the inspection ends as **Failed** with the reason on the photo, and **Retry analysis** runs it again.

| Message | Meaning | What to do |
|---|---|---|
| *sam-3.1 found no distress in the photo…* | SAM did not see any damage. Typical with the drawn sample scenes, or with blurry, dark or very distant photos. | Use a real, close-up and well-lit photo of the pavement. |
| *Invalid calibration…* | The 4 corners or the width give an impossible projection. | Create the inspection again and check the corners and the rectangle size. |
| *AI service unavailable…* | Meta's API did not answer, or timed out. | Check your internet connection and your key, then **Retry analysis**. |
| *Invalid model response…* | A model answer broke the expected format twice. | **Retry analysis**. If it repeats, try another photo. |
| *Disconnected* in the top bar | The web app cannot reach the API. | Make sure the API window is running without errors. |

---

## 11. Demo mode and Meta mode

| | Demo mode | Meta mode |
|---|---|---|
| How to get it | No key in `.env` | `META_API_KEY` in `.env` |
| Top bar shows | **Demo mode** | The model names, e.g. *SAM 3.1 · Muse Wonder 1.0* |
| Results | Fixed scenarios for the three sample scenes | Real SAM masks and Muse Wonder diagnosis for any photo |
| Cost | Free | Uses your Meta API credits (about 15 calls per inspection) |
| Sample scenes | Work | Usually fail: they are drawings, not real roads |

The mode is decided when the API starts. If a Meta model fails, the inspection ends as **Failed**; PaveLens never falls back to demo results silently.

To switch, edit `.env` at the repository root and restart the API (close its window and run the start script again):

```dotenv
# Real Meta models
META_API_KEY=<your key>

# Force demo mode even with a key
PAVELENS_AI_PROVIDER=Simulation
```
