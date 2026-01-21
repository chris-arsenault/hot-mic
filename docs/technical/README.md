# Technical Documentation Index

DSP-focused specifications for HotMic. These docs describe algorithms, parameters, defaults, and
real-time constraints for audio analysis and visualization mapping. UI layout and interaction
are intentionally omitted.

## How to Use
- Each doc is written for audio/DSP engineers evaluating the processing chain.
- Section headers are intended for code references: `Doc.md ## Section Name`.
- Code pointers should be brief parenthetical references, not the main content.

Architecture and system design docs live under `docs/architecture/`. Feature/reference docs
live under `docs/reference/`.

## Index
- `docs/technical/Preprocessing.md` - DC removal, rumble HPF, and pre-emphasis.
- `docs/technical/FFT.md` - STFT configuration, windowing, overlap, and magnitude normalization.
- `docs/technical/Frequency-Scales.md` - scale formulas and bin mapping.
- `docs/technical/Magnitude-Scaling.md` - normalization and dynamic range mapping.
- `docs/technical/Cleanup.md` - noise reduction, HPSS, harmonic comb, and smoothing.
- `docs/technical/Reassignment.md` - time/frequency reassignment.
- `docs/technical/Analysis-Signals.md` - analysis signal definitions and computation.
- `docs/technical/Pitch.md` - pitch detectors and confidence metrics.
- `docs/technical/Voicing-Harmonics.md` - voicing classification and harmonic peaks.
- `docs/technical/Spectral-Features.md` - centroid, slope, and flux.
- `docs/technical/Spectrogram-Rendering.md` - magnitude-to-color transfer mapping.
- `docs/technical/Metering.md` - peak/RMS and LUFS metering behavior.
- `docs/technical/Vocal-Reference.md` - vocal ranges and reference bands.
- `docs/technical/Speech-Coach.md` - speech metrics and intelligibility analysis.
- `docs/technical/SignalGenerator.md` - test signal generator plugin.
- `docs/technical/Enhance-Bass-Enhancer.md` - psychoacoustic bass enhancer details.
- `docs/technical/Enhance-Consonant-Transient.md` - consonant transient emphasis details.
- `docs/technical/Enhance-Air-Exciter.md` - keyed exciter (voiced + de-ess aware).
- `docs/technical/Enhance-Dynamic-EQ.md` - voiced/unvoiced dynamic EQ shaping.
- `docs/technical/Enhance-Room-Tone.md` - room tone bed with speech ducking.
- `docs/technical/Enhance-Spectral-Contrast.md` - spectral contrast enhancement details.
- `docs/technical/Enhance-Upward-Expander.md` - multiband upward expander details.
- `docs/technical/Vitalizer-Mk2T.md` - Vitalizer Mk2-T (Tube) approximation (mono, no stereo expander).
