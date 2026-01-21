# Analysis Signal Routing

## Purpose
Describe how analysis signals are produced in the plugin chain, time-aligned, and
forwarded to the analysis/visualization pipeline without blocking the audio thread.

## Key Concepts
- **AnalysisSignalBus:** Per-producer ring buffers keyed by sample time.
- **Producers/Consumers:** Plugins can publish or consume signals without global recompute.
- **AnalysisTapPlugin:** Optional in-chain tap that can generate or block signals.
- **AnalysisCaptureLink:** Bridges the audio thread to the analysis thread.
- **AnalysisOrchestrator:** Consumes captured audio and signals for visualizers.

## In-Chain Signal Flow
1) **PluginChain** allocates an `AnalysisSignalBus` if any producer exists.
2) Each slot receives a `PluginProcessContext` with:
   - `sampleClock` (block time) and `sampleTime` (latency-corrected time).
   - A producer map pointing to the nearest upstream producer per signal.
3) Producers write signals via `AnalysisSignalWriter.WriteBlock()`.
4) Consumers read signals via `AnalysisSignalSource.ReadSample(sampleTime)`.
5) Blockers can invalidate signals for downstream slots.

The producer map is updated per slot, so consumers always read the closest
upstream source for each signal.

## Analysis Tap Behavior
The Analysis Tap plugin can operate per signal in one of three modes:
- **Use Existing:** read upstream signals when present.
- **Generate:** compute signals locally and publish them.
- **Disabled:** block the signal from downstream consumers.

When active, the tap forwards audio + signal context to the analysis pipeline.

## Analysis Capture Link
`AnalysisCaptureLink` collects audio + signal context from the audio thread and
passes it to the analysis thread.

Rules:
- Only **channel 0** is forwarded to the analysis orchestrator.
- If a plugin capture and output capture occur in the same block, the plugin
  capture wins.
- The capture link snapshots producer indices and the bus reference so the
  analysis thread can read aligned signals.

## Analysis Orchestrator
- Runs on a dedicated analysis thread.
- Pulls audio from a lock-free ring buffer.
- Uses the captured analysis bus first; computes missing signals only when
  required by active visualizers.

Signal definitions and DSP details live in `docs/technical/Analysis-Signals.md`.

Implementation refs: (src/HotMic.Core/Plugins/AnalysisSignalBus.cs,
 src/HotMic.Core/Plugins/AnalysisSignalAccess.cs,
 src/HotMic.Core/Plugins/PluginProcessContext.cs,
 src/HotMic.Core/Plugins/BuiltIn/AnalysisTapPlugin.cs,
 src/HotMic.Core/Analysis/AnalysisCaptureLink.cs,
 src/HotMic.Core/Analysis/AnalysisOrchestrator.cs)
