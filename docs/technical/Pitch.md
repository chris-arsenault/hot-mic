# Pitch Detection

## Purpose
Estimate fundamental frequency and confidence for vocal analysis and harmonic processing.

## Algorithms
- YIN: CMND thresholding with parabolic interpolation.
- pYIN: multiple CMND candidates with probabilistic scoring and continuity penalty.
- Autocorrelation: normalized ACF peak with interpolation.
- Cepstral: log-magnitude cepstrum; CPP for confidence.
- SWIPE-style: harmonic summation over log-spaced candidates.
  - When Transform = CQT, SWIPE is forced to YIN (FFT magnitudes are not computed).

## Parameters and Defaults
Common:
- Frame size = FFT size.
- Pitch range for time-domain detectors: 60..1200 Hz (fixed).
- Pitch range for SWIPE: Min Frequency .. Max Frequency.

Algorithm-specific defaults:
| Algorithm | Defaults |
| --- | --- |
| YIN | threshold 0.15 |
| pYIN | threshold 0.15, max candidates 6, jump penalty 0.7, voiced threshold 0.15 |
| Autocorrelation | confidence threshold 0.3 |
| Cepstral | confidence floor 2 dB, Hann window |
| SWIPE | candidates per octave 48, max harmonics 24, confidence threshold 0.2 |

## Output
- Frequency in Hz (0 when unvoiced).
- Confidence 0..1.
- CPP (cepstral peak prominence) reported in dB by the cepstral detector.

## Rate
- Pitch and CPP are evaluated every 2 analysis frames to reduce CPU cost.
- Pitch/CPP work is skipped when Pitch, Pitch Meter, Harmonics, Voicing, and Clarity are all off.
- Pitch/voicing work is gated off when the Analysis Tap Speech Presence (UseExisting or Generate) is <= 0.05.

## Real-time Considerations
- Detectors preallocate buffers and are reconfigured on FFT size changes.

Implementation refs: (src/HotMic.Core/Dsp/Analysis/Pitch/*.cs,
 src/HotMic.Core/Plugins/BuiltIn/VocalSpectrographPlugin.Analysis.cs)
