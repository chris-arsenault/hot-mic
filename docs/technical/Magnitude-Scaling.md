# Magnitude Scaling

## Purpose
Normalize FFT magnitudes and map them into a fixed dB display range.

## Algorithm
1. Compute magnitude per FFT bin (see FFT.md).
2. Apply per-frame normalization (optional).
3. Map to display bins via max-hold.
4. Apply clarity processing if enabled (see Cleanup.md).
5. Convert to dB and clamp to [floor, ceiling].
6. Normalize to 0..1 for rendering.

## Normalization Modes
- None: raw magnitudes.
- Peak: divide by max magnitude in frame.
- RMS: divide by RMS of magnitudes in frame.
- A-Weighted: multiply magnitudes by per-bin A-weighting curve.

## Dynamic Range Modes
- Custom: use explicit Min dB / Max dB values.
- Voice Optimized: -80..0 dB.
- Full: -120..0 dB.
- Compressed: -60..0 dB.
- Noise Floor: adaptive floor from 10th percentile of display magnitudes.

## Parameters
| Parameter | Default | Range | Notes |
| --- | --- | --- | --- |
| Normalization | None | None, Peak, RMS, A-Weighted | Per-frame normalization. |
| Dynamic range mode | Custom | Custom, Voice, Full, Compressed, Noise Floor | Overrides Min/Max dB except Custom. |
| Min dB | -80 | -120..-20 | Used only when Dynamic range = Custom. |
| Max dB | 0 | -40..0 | Used only when Dynamic range = Custom. |

Noise Floor mode:
- Percentile: 10% (per display bin set).
- Adapt rate: 0.2 (silence) / 0.05 (voiced).
- Floor is clamped to [Min dB, Max dB - 1].

## Real-time Considerations
- A-weighting tables are precomputed per FFT bin.
- Dynamic range tracker reuses scratch buffers.

Implementation refs: (src/HotMic.Core/Dsp/Analysis/AWeighting.cs,
 src/HotMic.Core/Dsp/Spectrogram/SpectrogramDynamicRangeTracker.cs,
 src/HotMic.Core/Plugins/BuiltIn/VocalSpectrographPlugin.Analysis.cs)
