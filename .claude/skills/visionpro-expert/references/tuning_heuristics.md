# VisionPro Tuning Heuristics

This document contains expert knowledge and rules of thumb for solving common VisionPro problems.
When a user asks for advice on how to fix an issue, consult this guide for potential solutions.

---

## Symptom: Low Match Score (CogPMAlignTool)

- **Hypothesis**: The acceptance threshold is too strict, or the training pattern is not robust enough.
- **Primary Property to Adjust**: `RunParams.AcceptThreshold`
- **Action**: Lower the `AcceptThreshold` in small increments (e.g., from 0.8 to 0.75).
- **Secondary Properties**:
    - `RunParams.ZoneAngle`: If the target can rotate, ensure the angle range (`.Low` and `.High`) is sufficient.
    - `RunParams.ZoneScale`: If the target can change in size, ensure the scale range is sufficient.

## Symptom: Blob Not Found or Incorrect Blob Count (CogBlobTool)

- **Hypothesis**: The threshold is not correctly separating the blob from the background, due to lighting changes or part variation.
- **Primary Property**: `RunParams.SegmentationParams.HardFixedThreshold`
- **Actions**:
    - If the image is **darker** than usual, **lower** the threshold.
    - If the image is **brighter** than usual, **increase** the threshold.
    - If the user mentions "black blobs" or "white blobs", check `RunParams.SegmentationParams.Polarity`.

- **Secondary Properties**:
    - `RunParams.ConnectivityMinPixels`: If small, noisy blobs are being detected, **increase** this value to filter them out.
    - `RunParams.ConnectivityMaxPixels`: If large blobs are being incorrectly ignored, ensure this value is high enough.

## Symptomen: Incorrect Measurement (CogFindLineTool, etc.)

- **Hypothesis**: The edge detection is not correctly identifying the feature.
- **Primary Property**: `RunParams.Caliper.ContrastThreshold`
- **Action**: Adjust the contrast threshold. If edges are faint, try lowering it. If there is a lot of noise, try increasing it.
- **Secondary Property**: `RunParams.Caliper.SearchDirection`
- **Action**: Ensure the search direction is perpendicular to the expected edge.

---
*[TODO: Add more expert rules over time based on common problems.]*
