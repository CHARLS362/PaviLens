# AI contracts

PaveLens talks to two hosted model services. The adapters live in `src/PaveLens.Infrastructure/Ai/Meta`. Each goes through `AddStandardResilienceHandler` (retries, per-attempt timeout, circuit breaker).

## 1. Segmentation: Meta SAM 3

`Ai:Meta:SegmentationService` picks one of two transports. Both feed the same pipeline.

### 1a. Roboflow serverless (default)

`RoboflowSam3Segmenter` calls Meta SAM 3 hosted by Roboflow. Only an API key is needed.

```
POST https://serverless.roboflow.com/sam3/concept_segment
Authorization: Bearer <Ai:Meta:Segmentation:ApiKey>
{
  "image":   { "type": "base64", "value": "<image bytes>" },
  "prompts": [ { "type": "text", "text": "pothole" }, { "type": "text", "text": "alligator crack" }, … ],
  "output_prob_thresh": 0.5,          (Ai:Meta:SegmentationScoreThreshold)
  "format": "polygon"
}
```

- One text prompt per concept, at most 16 per request; longer lists are split into several requests.
- The response has one `prompt_results[i]` per prompt, with `echo.text` and `predictions[]`. Each prediction carries `confidence` and `masks`, a list of polygons in image pixels.
- The adapter then cleans up the output:
  - divides by the image size to get normalized coordinates, rejecting points more than 1 % outside the image;
  - splits a prediction into one segment per separate part, so the PCI pothole count stays right;
  - drops rings that lie inside a larger ring of the same prediction (holes), and parts smaller than 16 px²;
  - **de-duplicates across concepts**: SAM 3 answers each prompt independently, so one pothole can also come back as "alligator crack". When two masks of different concepts have IoU > 0.7, the higher-confidence one is kept. Containment alone is not a duplicate: a pothole inside an alligator-cracked area is two distresses.

### 1b. Self-hosted sidecar

For a GPU server running the SAM 3 or SAM 3.1 checkpoints, for example a small Python/FastAPI service.

```
POST {Ai:Meta:Segmentation:BaseUrl}v1/segment
Authorization: Bearer <ApiKey>        (optional)
Content-Type: multipart/form-data

image     <binary>                    JPEG / PNG / WebP as uploaded
concepts  ["pothole", "alligator crack", "longitudinal crack", "transverse crack", "rutting"]
          (JSON array; Analysis:SegmentationConcepts)
model     sam3                        (Ai:Meta:Segmentation:Model)
```

Response `200 application/json`:

```json
{
  "model": "sam3",
  "segments": [
    { "concept": "pothole", "score": 0.97, "polygon": [[0.412, 0.633], [0.415, 0.631], [0.419, 0.630]] }
  ]
}
```

- Coordinates are normalized to `[0, 1]`, with x to the right and y down.
- Each polygon is the outer contour of one connected mask component.
- Polygons with fewer than 3 points are skipped.
- Coordinates up to 0.01 outside the range are clamped; anything further out is rejected as invalid output.
- A non-2xx status becomes `ModelUnavailableException`, and the inspection fails with that reason. The same applies to the Roboflow transport.

#### Contour extraction in the sidecar

PCI, quantities and the budget are only as good as the outline. The API measures exactly what it receives, so the sidecar must not lose shape on the way out:

- **Threshold the mask logits at full image resolution**, not at the decoder's low-resolution grid.
- **Trace the contour without approximation**, for example `cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)`. Do not simplify on the sidecar: the API simplifies after measuring, to under one pixel, and only for display.
- **Emit one segment per connected component** and drop components smaller than a few pixels. Merging separate potholes into one polygon breaks the per-unit pothole count in the PCI.
- **Use sub-pixel vertices when available.** Dividing by the image size, not by size − 1, keeps the outline centred on pixel edges.
- **Ignore holes for now.** They are rare in pavement distresses. If an island of intact pavement inside an alligator area matters, the contract can grow a `holes` array.

## 2. Reasoning: Muse Wonder

Muse Wonder is reached through Meta's OpenAI-compatible API. Any other OpenAI-compatible endpoint works by changing `BaseUrl` and `Model`.

```
POST https://api.meta.ai/v1/chat/completions
Authorization: Bearer <Ai:Meta:Reasoning:ApiKey>
{ "model": "muse-wonder-1.0", "reasoning_effort": "low", "response_format": { "type": "json_object" }, "messages": [...] }
```

- `reasoning_effort`, `temperature` and `response_format` come from `Ai:Meta:ReasoningEffort`, `Temperature` and `JsonMode`.
- Unset values are not sent. Some reasoning models reject `temperature`, so it is unset by default.

- The system prompt is `Ai/Prompts/diagnosis.v1.md`, which is versioned, embedded in the assembly and written in Spanish.
- The user message carries:
  - the photo as a data URL;
  - title, notes, location and image size;
  - for each segment: `index`, `concept`, `score`, `box`, and the measurements already projected to metres (`areaM2`, `lengthM`, `widthM`, `eccentricity`, `orientationDeg`).

  The model therefore judges severity with real dimensions instead of guessing them from pixels.

The model must answer with:

```json
{
  "summary": "…",
  "distresses": [
    { "segmentIndex": 0, "code": "B1", "type": "Pothole|AlligatorCrack|LongitudinalCrack|TransverseCrack|Rutting|Other",
      "severity": "Low|Medium|High", "diagnosis": "…", "recommendedAction": "…" }
  ],
  "repairSteps": [
    { "order": 1, "title": "…", "instructions": "…", "tools": ["…"], "risk": "Low|Medium|High", "targetSegmentIndex": 0 }
  ]
}
```

The output is checked against the segments:

- every `segmentIndex` must exist;
- codes must be unique;
- every step target must point at a segment that has a finding.

On a violation, the error goes back to the model once (self-repair). A second failure fails the inspection with `ModelOutputException`.

Measurements, quantities and PCI never come from the model. They are computed by the Application layer from the mask and the projection, so a model cannot inflate an area or a budget.

## 3. Assistant

The assistant prompt is `Ai/Prompts/assistant.v1.md`. The assistant receives:

- the stored diagnosis: distresses with measurements, quantities, PCI and the region profile;
- the focused distress, if any;
- the recent history (`Analysis:AssistantHistoryTurns`);
- the photo.

It answers in plain Spanish and says when a figure is an estimate (assumed depth, partial PCI, reference prices).

## Simulation provider

With `Ai:Provider = Simulation`, `SimulatedSegmenter`, `SimulatedReasoner` and `SimulatedMaintenanceAssistant` replace the adapters above:

- The fixtures in `Ai/Simulation/Scenarios/*.json` are keyed by image SHA-256 and drive the same pipeline: projection, measurement, validation, PCI, quantities and persistence.
- The fixtures for the demo scenes are generated together with the images by `samples/tools/generate_scenes.py`. Their outlines are the exact ground-truth boundaries of the drawn distresses.
- An image outside the catalogue gets a generic scenario whose summary states that it does not describe the photo.
- Results are stored with `engine = "Simulation"`.
