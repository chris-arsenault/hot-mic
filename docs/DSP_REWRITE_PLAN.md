# DSP Rewrite Plan (Voice-Focused)

## Constraints
- End-to-end latency target: 30 ms; hard cap: 60 ms (includes buffer + lookahead + algorithmic delay).
- Plugins must remain reorderable; no fixed vocal chain.
- Manual noise learning is the primary workflow; UI must allow toggling learning.
- Mono now, but design must stay stereo-ready (linkable detectors/state per channel).
- No hardware modeling; prefer modern, transparent algorithms tuned for voice.

## Scope
- Audio pipeline tuning for vocal use: smoother gains, sidechain filtering, thread-safe meters, latency reporting.
- Full rewrite of built-in plugins: Gain, Noise Gate/Expander, Compressor, 3-Band EQ, FFT Noise Removal.
- UI updates where needed for new behavior (learning toggle, latency display).
- Comments in complex DSP sections to document intent and tuning.

## Implementation Phases

### Phase 1: DSP Infrastructure
1. Add parameter smoothing helpers (linear ramp / one-pole) with no allocations.
2. Add lightweight HPF for sidechain detection (rumble control).
3. Replace NAudio biquad allocations with in-place biquad coefficients/state.
4. Add per-plugin latency reporting (samples) and thread-safe meter publishing.
5. Add sample tap or ring buffer for UI analysis (avoid audio-thread FFT for display).

### Phase 2: Plugin Rewrites
- Gain
  - Smooth gain changes; keep phase invert; thread-safe meter updates.
  - Latency: 0 samples.

- Noise Gate / Expander (voice tuned)
  - RMS detector + sidechain HPF.
  - Hysteresis + hold retained; add soft transition and fixed range for natural gating.
  - Thread-safe gate status and meter updates.
  - Latency: 0 samples.

- Compressor (voice tuned)
  - Feedforward RMS detector with sidechain HPF.
  - Soft knee and program-dependent release; ratio/attack/release remain.
  - Internal peak/RMS blend tuned for vocals.
  - Latency: 0 samples (no lookahead by default).

- 3-Band EQ
  - In-place biquad coefficients + optional smoothing to avoid zipper noise.
  - Keep existing parameter set; ensure voiced defaults remain sensible.
  - Move spectrum analysis off audio thread (UI-side FFT or decimated analysis).
  - Latency: 0 samples.

- FFT Noise Removal
  - Manual learning remains primary; UI toggle to start/stop.
  - Replace FFT implementation with in-place, precomputed radix-2 (no allocations).
  - Use decision-directed Wiener/MMSE-style gain with smoothing and spectral floor.
  - Keep overlap-add, tune FFT size/hop for <= 60 ms budget.
  - Latency: report frame-based delay (e.g., FFT size - hop).

### Phase 3: UI & Diagnostics
1. Update plugin windows to reflect new behaviors (learning toggle, latency display).
2. Ensure plugin parameter windows route commands via parameter queue (no direct audio-thread mutations).
3. Add latency summary to slot/parameters view for quick visibility.

### Phase 4: Verification
- Build and sanity-check on typical buffer sizes (256/512 at 48 kHz).
- Verify no allocations in audio thread hotspots.
- Confirm latency reporting aligns with actual processing delay.

## Notes on Latency Budget
- Prefer FFT size 1024 with 50–75% overlap to keep algorithmic delay within budget.
- Avoid hidden lookahead in dynamics unless explicitly surfaced via latency reporting.

