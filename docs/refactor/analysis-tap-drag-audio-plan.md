# Analysis Tap Drag/Audio Fix Plan

Goal: Fix analysis tap drag/drop behavior and resolve silence/clipping regressions introduced during the analysis signal bus refactor.

## Phase 1: Drag/Drop Targeting
- Audit main window drag/drop logic for plugin slots and routing slots.
- Add explicit drop index resolution that honors before/after position (and matches orange insertion line).
- Apply the same insertion logic to plugin container windows so intra-container drag/drop is consistent.
- Keep input plugins pinned and preserve container/routing constraints.

## Phase 2: Analysis Tap Generation + Meter Updates
- Ensure analysis tap generation works when mode is Generate (meters update regardless of downstream usage).
- Maintain “compute only when needed” by relying on tap requests and generate-mode intent.
- Keep use/gen/off behavior consistent with analysis signal production + blocking.

## Phase 3: Signal Safety + Audio Integrity
- Sanitize analysis signal outputs to ensure finite, bounded values before they reach consumers.
- Clamp/normalize only at signal emission points (not in every consumer) to avoid per-sample overhead.
- Verify that VAD/producer outputs cannot write NaN/Inf into the analysis bus.

## Phase 4: Cleanup + Validation Hooks
- Verify UI meters + clipping indicators map to the correct slots after drag/drop.
- Ensure analysis tap requested signals are applied per channel and cleared on dispose.
- Leave notes for Windows-side verification (audio output + drag/drop behavior).

## Progress Log
- Implemented drop index resolution for main window and plugin container windows.
- Updated analysis tap processing to compute Generate signals for meters while only publishing requested signals.
- Added analysis signal sanitization in AnalysisSignalProcessor and VAD producers (RNNoise, Silero Voice Gate).
- Added mouse capture for plugin slot dragging in main window to stabilize drop targeting.
