# Voicing and Harmonics

## Purpose
Classify voiced/unvoiced/silence and extract harmonic structure metrics.

## Voicing Detection
Inputs: time-domain frame, magnitude spectrum, pitch confidence.

Metrics:
- RMS energy (dB)
- Zero-crossing rate (ZCR)
- Spectral flatness (geometric/arithmetic mean)
- Pitch confidence

Thresholds (defaults):
- Energy < -40 dB => Silence
- ZCR threshold = 0.1
- Spectral flatness threshold = 0.5
- Pitch confidence threshold = 0.3

Logic: Voiced when confidence >= threshold and ZCR/flatness are below thresholds.

## Harmonic Peaks
- Uses detected pitch and FFT magnitudes.
- Expected harmonic frequencies = n * f0.
- Tolerance = +/-3% (0.03).
- Returns up to 24 harmonics.

## HNR and CPP
- HNR computed from harmonic comb mask energy ratio (dB).
- CPP reported by the cepstral detector (dB).

## Real-time Considerations
- Voicing and harmonic peak detection run per analysis frame with preallocated buffers.
- When Pitch, Pitch Meter, Harmonics, Voicing, and Clarity are all off, voicing/harmonic work is skipped.

Implementation refs: (src/HotMic.Core/Dsp/Analysis/VoicingDetector.cs,
 src/HotMic.Core/Dsp/Analysis/HarmonicPeakDetector.cs,
 src/HotMic.Core/Dsp/Spectrogram/HarmonicCombEnhancer.cs)
