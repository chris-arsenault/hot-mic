# Technical Documentation Index

DSP-focused specifications for HotMic. These docs describe algorithms, parameters, defaults, and
real-time constraints for audio analysis and visualization mapping. UI layout and interaction
are intentionally omitted.

## How to Use
- Each doc is written for audio/DSP engineers evaluating the processing chain.
- Section headers are intended for code references: `Doc.md ## Section Name`.
- Code pointers should be brief parenthetical references, not the main content.

## Index
- `docs/technical/Preprocessing.md` - DC removal, rumble HPF, and pre-emphasis.
- `docs/technical/FFT.md` - STFT configuration, windowing, overlap, and magnitude normalization.
- `docs/technical/Frequency-Scales.md` - scale formulas and bin mapping.
- `docs/technical/Magnitude-Scaling.md` - normalization and dynamic range mapping.
- `docs/technical/Cleanup.md` - noise reduction, HPSS, harmonic comb, and smoothing.
- `docs/technical/Reassignment.md` - time/frequency reassignment.
- `docs/technical/Pitch.md` - pitch detectors and confidence metrics.
- `docs/technical/Voicing-Harmonics.md` - voicing classification and harmonic peaks.
- `docs/technical/Spectral-Features.md` - centroid, slope, and flux.
- `docs/technical/Spectrogram-Rendering.md` - magnitude-to-color transfer mapping.
- `docs/technical/Realtime-Pipeline.md` - ring buffer flow and snapshot synchronization.
- `docs/technical/Vocal-Reference.md` - vocal ranges and reference bands.
- `docs/technical/Presets.md` - preset definitions and intent.
- `docs/technical/SignalGenerator.md` - test signal generator plugin.
- `docs/technical/Enhance-Plugins-Design.md` - architecture and timing design for ENHANCE plugins.
- `docs/technical/MultiChannel-Routing.md` - multi-channel routing, copy/merge, and output send design.
