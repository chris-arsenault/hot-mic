# Enhance Plugins Design (Reference)

Purpose: architecture and data-flow design for the 8 plugins described in
`ENHANCE.md`. This doc focuses on correct timing and sidechain alignment, not
UI or implementation details.

Key decision: all 8 are NEW plugins. No behavior changes to existing plugins.

## Constraints

- Audio thread must be allocation-free and lock-free.
- Control signals must be time-aligned to audio, even with large plugin latency.
- Avoid multi-buffer drift: no independent read/write cursors per consumer.
- DSP details belong in dedicated technical docs if/when algorithms change.

## Architecture Overview

### 1) Sample clock

Maintain a monotonic `sampleClock` in the audio callback. Every block advances
the clock by `blockSize`. This clock is the shared timebase for audio, sidechain
signals, and spectral frames.

### 2) Cumulative latency map

For each plugin slot, compute `cumulativeLatencySamples` as the sum of
`LatencySamples` for all earlier slots. Update on chain changes only.

### 3) Sidechain bus (time-domain control)

Use a shared, pre-allocated bus that stores control signals (float) by sample
time. Producers write by sample time at their slot; consumers read from the
nearest upstream active producer for each signal so buffers never drift.

Design rules:
- Per-producer ring buffers keyed by sample time.
- Consumers read using their own `sampleTime` (already latency-corrected).
- No per-consumer read cursors; routing is derived from chain order.

### 4) Frame bus (spectral/formant control)

Spectral and formant signals are frame-based (hop-sized). Use a shared frame
bus with a fixed hop size and frame center offset.

Design rules:
- Frame index derived from `sampleClock` (not from buffer counters).
- Consumers read `frameIndex - delayFrames` where `delayFrames` maps from
  latency samples to hop units.
- Frames are reused from a ring sized for max latency + look-back.

### 5) Producer placement

Sidechain producers live at specific slots in the chain. Consumers always use
the nearest upstream active producer for each required signal.

When no upstream producer exists, insert a Sidechain Tap plugin at the desired
location to generate the shared signals.

## Sidechain Signals (shared definitions)

Minimal set to support all 8 plugins:
- SpeechPresence (0..1, smoothed VAD)
- VoicedProb (0..1, periodicity/harmonicity)
- UnvoicedEnergy (HF energy without periodicity)
- SibilanceEnergy (narrow HF band energy)
- SpectralFlux (frame-based, optional)
- FormantF1/F2 (frame-based, Hz)

Producers should write each signal at their slot's `sampleTime` and maintain
stable definitions for the shared signals.

## Plugin Set (all new)

Each plugin uses the sidechain and/or frame bus so detectors run once and all
consumers stay aligned. IDs are illustrative and should be finalized later.

1) Multiband Upward Expander
- Id: builtin:upward-expander
- Domain: filterbank or STFT (single domain for analysis + gain)
- Sidechain: SpeechPresence
- Notes: per-band expansion with gated detector; smooth attacks/releases.

2) Spectral Contrast Enhancer (lateral inhibition)
- Id: builtin:spectral-contrast
- Domain: STFT + overlap-add
- Sidechain: SpeechPresence
- Notes: apply inter-band inhibition on magnitudes; resynthesize with frame phase.

3) Dynamic EQ (voiced/unvoiced keyed)
- Id: builtin:dynamic-eq
- Domain: time-domain biquads
- Sidechain: VoicedProb, UnvoicedEnergy
- Notes: small, fast dynamic moves; keep average spectrum stable.

4) Room Tone / Ambience Bed
- Id: builtin:room-tone
- Domain: time-domain (looped audio or shaped noise)
- Sidechain: SpeechPresence
- Notes: duck under speech; use slow envelope to avoid modulation artifacts.

5) Keyed Air Exciter (de-ess aware)
- Id: builtin:air-exciter
- Domain: time-domain oversampled harmonic generation
- Sidechain: VoicedProb, SibilanceEnergy
- Notes: excite on voiced regions, clamp on sibilance.

6) Psychoacoustic Bass Enhancer
- Id: builtin:bass-enhancer
- Domain: time-domain (bandpass + harmonic synthesis)
- Sidechain: VoicedProb (optional)
- Notes: subtle harmonics for LF perception; avoid LF boost.

7) Consonant Transient Emphasis
- Id: builtin:consonant-transient
- Domain: time-domain HF transient shaper OR STFT flux
- Sidechain: UnvoicedEnergy or SpectralFlux
- Notes: short window emphasis with hard ceiling.

8) Formant-Aware Enhancement
- Id: builtin:formant-enhance
- Domain: time-domain EQ steered by formants
- Sidechain: FormantF1/F2 (frame-based)
- Notes: light tracking; boost near moving formants, avoid in-between.

## Alignment Rules

- Consumers read sidechain/control at their `sampleTime` derived from the
  chain's cumulative latency.
- Frame-based consumers must read:
  `consumerFrameIndex - delayFrames`
- If a control signal is missing (not yet produced for that time), hold the
  last valid value or fall back to neutral (0..1).

## Implementation Notes (non-binding)

- Add `ISidechainProducer` / `ISidechainConsumer` interfaces for routing and
  missing-sidechain status.
- Extend plugin processing with a `ProcessContext` containing sampleClock,
  sampleTime, and sidechain accessors.
- Use the contextual path for all plugins; no parallel legacy processing.

## Integration Checklist

- Add new plugin classes in `src/HotMic.Core/Plugins/BuiltIn/`.
- Update `PluginFactory` to expose new IDs.
- Add to plugin browser list (UI).
- Add presets only after DSP defaults are confirmed.
