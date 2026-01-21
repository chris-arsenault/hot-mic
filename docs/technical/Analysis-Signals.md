# Analysis Signals

## Purpose
Define the analysis signals shared across DSP plugins and visual analysis. These signals are
normalized, time-aligned to the audio stream, and designed for real-time use.

## Signal Definitions

### SpeechPresence
- **Range:** 0..1
- **Meaning:** Smoothed speech-likelihood envelope.
- **Computation:** Envelope follower on the preprocessed waveform (DC removal + optional rumble HPF + optional pre-emphasis). Map envelope dB to 0..1 using:
  `presence = clamp((envDb - (-50)) / 30)`.

### VoicingScore
- **Range:** 0..1
- **Meaning:** Continuous voicing strength (periodicity/voicedness).
- **Computation:** Voicing detector output on analysis frames. See `docs/technical/Voicing-Harmonics.md`.

### VoicingState
- **Range:** 0..2 (float)
- **Meaning:** 0 = Silence, 1 = Unvoiced, 2 = Voiced.
- **Computation:** Voicing detector classification on analysis frames.

### FricativeActivity
- **Range:** 0..1
- **Meaning:** High-frequency aperiodic activity (fricative energy proxy).
- **Computation:** High-pass at 2.5 kHz, envelope follower, normalized by the full-band envelope.

### SibilanceEnergy
- **Range:** 0..1
- **Meaning:** Narrow-band sibilance energy around the sibilant band.
- **Computation:** Band-pass at 6.5 kHz (Q=1.2), envelope follower, normalized by the full-band envelope.

### OnsetFluxHigh
- **Range:** >= 0 (dB)
- **Meaning:** High-band onset energy; positive spectral flux above ~2 kHz.
- **Computation:** Log-magnitude flux per bin above 2 kHz, averaged over positive changes only.

### PitchHz
- **Range:** >= 0 (Hz)
- **Meaning:** Estimated fundamental frequency.
- **Computation:** Selected pitch detector (YIN / pYIN / autocorrelation / cepstral / SWIPE). See `docs/technical/Pitch.md`.

### PitchConfidence
- **Range:** 0..1
- **Meaning:** Pitch confidence for the current frame.
- **Computation:** Detector-specific confidence value from the selected pitch algorithm.

### SpectralFlux
- **Range:** >= 0 (linear)
- **Meaning:** Mean-square change in magnitude spectrum between consecutive frames.
- **Computation:** `mean((mag - prevMag)^2)` across bins using the spectral feature extractor.

### HnrDb
- **Range:** dB (clamped to [-120, 120])
- **Meaning:** Harmonic-to-noise ratio estimate.
- **Computation:** Derived from spectral flatness: `HNR = -10 * log10(flatness)`.

## Update Rates
- **Streaming (per-sample over hop):** SpeechPresence, FricativeActivity, SibilanceEnergy.
- **Frame-based (per analysis frame):** PitchHz, PitchConfidence, VoicingScore, VoicingState, SpectralFlux, OnsetFluxHigh, HnrDb.
- Frame-based values are held across the hop when written into the analysis signal bus.

## Gating and Dependencies
- Pitch detection and voicing are gated by speech presence when a gate is enabled.
  - Gate opens when `SpeechPresence > 0.05`.
- Pitch/CPP are evaluated every 2 analysis frames to reduce CPU cost.
- Voicing requires pitch confidence; pitch confidence depends on pitch detection.

## Preprocessing Path
Streaming analysis uses the same preprocessing controls as the analysis pipeline:
- DC removal (one-pole high-pass).
- Optional rumble HPF.
- Optional pre-emphasis.
See `docs/technical/Preprocessing.md` for filter details.

Implementation refs: (src/HotMic.Core/Analysis/AnalysisSignalProcessor.cs,
 src/HotMic.Core/Plugins/AnalysisSignalIds.cs,
 src/HotMic.Core/Dsp/Spectrogram/SpectralFeatureExtractor.cs,
 src/HotMic.Core/Dsp/Analysis/VoicingDetector.cs)
