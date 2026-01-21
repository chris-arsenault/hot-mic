# Spectral Cleanup (Clarity)

## Purpose
Reduce spectral noise and emphasize harmonic structure for vocal analysis.

## Pipeline Order
1. Noise reduction (optional)
2. HPSS harmonic extraction (optional)
3. Harmonic comb emphasis (optional, Full mode only)
4. Temporal smoothing (optional)

## Modes
- None: bypass, output is the mapped spectrum.
- Noise: noise reduction only.
- Harmonic: noise reduction + HPSS.
- Full: noise reduction + HPSS + harmonic comb.

## Noise Reduction
- Adaptive noise floor using a streaming P^2 quantile estimator on power spectra.
- Bootstrap: 5 frames to seed the estimator.
- Percentile: 10th percentile per bin (approximate).
- Gate threshold: noise * 2.0.
- Over-subtraction alpha: 1.2..2.2 (scaled by amount).
- Spectral floor beta: 0.01..0.02 (scaled by amount).
- Adapt rates: 0.2 (silence), 0.02 (voiced).

## HPSS (Median Filtering)
- Time kernel: 17 frames (2x downsampled for processing).
- Frequency kernel: 17 bins (2x downsampled for processing).
- Mask computed on downsampled spectrum and upsampled to full bins.
- Soft mask with power 2.0.
- Output = input * mix(mask, amount).

## Harmonic Comb
- Requires pitch and voiced state.
- Max harmonics: 24.
- Tolerance: +/-50 cents.
- Boost: 1.35x, attenuation: 0.25x.
- Confidence threshold: 0.35.
- HNR computed as 10*log10(harmonic/noise).

## Temporal Smoothing
- EMA: `output = prev + (input - prev) * (1 - amount)`.
- Bilateral: time radius 2, freq radius 2, spatial sigma 1.5,
  intensity sigma 8 dB.
- Amount 0 = no smoothing, 1 = max smoothing.

## Parameters
| Parameter | Default | Range | Notes |
| --- | --- | --- | --- |
| Clarity mode | None | None, Noise, Harmonic, Full | Stage selection. |
| Noise amount | 1.0 | 0..1 | Scales noise reduction strength. |
| Harmonic amount | 1.0 | 0..1 | Scales HPSS and comb strength. |
| Smoothing amount | 0.3 | 0..1 | 0 = off, 1 = heavy. |
| Smoothing mode | EMA | Off, EMA, Bilateral | Filter choice. |

## Real-time Considerations
- All buffers are preallocated on configuration changes.
- HPSS median filtering is the most CPU-intensive stage.
- Bilateral smoothing reuses precomputed log magnitudes.

Implementation refs: (src/HotMic.Core/Dsp/Spectrogram/SpectrogramNoiseReducer.cs,
 src/HotMic.Core/Dsp/Spectrogram/SpectrogramHpssProcessor.cs,
 src/HotMic.Core/Dsp/Spectrogram/HarmonicCombEnhancer.cs,
 src/HotMic.Core/Dsp/Spectrogram/SpectrogramSmoother.cs)
