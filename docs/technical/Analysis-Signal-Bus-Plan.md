# Analysis Signal Bus Refactor Plan

Goals
- Replace sidechain signaling with a unified analysis signal bus used by DSP + visuals.
- Compute analysis signals once (canonical implementations) and reuse everywhere.
- Allow analysis tap to be placed anywhere in the chain with use/gen/off per signal.
- Fallback analysis only when visuals are open AND the requested signal is missing.
- No legacy sidechain interfaces, no shims.

Proposed Analysis Signals (initial)
- SpeechPresence (AI VAD or fallback energy VAD)
- VoicingScore (continuous 0..1)
- VoicingState (0=Silence, 1=Unvoiced, 2=Voiced as float)
- FricativeActivity (HF aperiodic activity, 0..1)
- SibilanceEnergy (narrow HF band, 0..1)
- OnsetFluxHigh (HF spectral flux, mean positive dB change per frame)

Note: the visualization pipeline may still compute additional analysis signals
outside this list; those are out of scope for the enhance plugins.

Architecture
- AnalysisSignalBus: time-aligned ring buffers per producer, SidechainBus replacement.
- AnalysisSignalWriter/Source: read/write access by sampleTime.
- AnalysisSignalProducer/Consumer/Blocker: new plugin interfaces.
- AnalysisTapPlugin: generates analysis signals with per-signal mode (Use/Gen/Off), forwards audio to orchestrator.
- AnalysisSignalProcessor: shared DSP implementation used by audio-thread tap and background fallback.
- AnalysisCaptureLink: connects analysis tap audio + bus to AnalysisOrchestrator.
- AnalysisOrchestrator: reads bus first, computes missing signals only when required by active visuals.

Work Steps
1) Define new analysis signal IDs, masks, bus, access structs, and interfaces; remove sidechain types.
2) Refactor PluginProcessContext + PluginChain to use analysis bus and requested-signal masks.
3) Implement AnalysisSignalProcessor (shared DSP) with preallocated buffers; move pitch/voicing/flux logic here.
4) Replace SidechainTap with AnalysisTapPlugin and updated UI; add per-signal modes and indicators.
5) Update producers/consumers (VAD plugins, DeEsser, enhance plugins) to new signals + behavior mapping.
6) Integrate AnalysisCaptureLink + AnalysisOrchestrator fallback reading/writing of analysis signals; add VAD track to result store.
7) Remove legacy AnalysisTap capture in OutputPipeline; wire orchestrator to analysis tap source.
8) Update analyzer window + renderers if needed for new tracks; keep visuals consistent.
9) Clean up docs/technical references as needed.

Progress Tracker
- [x] Step 1: New analysis signal types + bus
- [x] Step 2: Plugin chain/context updates
- [x] Step 3: Shared analysis processor
- [x] Step 4: Analysis tap plugin + UI
- [x] Step 5: Producer/consumer updates (enhance plugins now consuming new signals)
- [x] Step 6: Orchestrator integration + result store
- [x] Step 7: Remove output analysis tap
- [x] Step 8: UI/visual updates
- [x] Step 9: Docs refresh
