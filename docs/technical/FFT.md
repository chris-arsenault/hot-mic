# FFT / STFT

## Purpose
Compute short-time spectra used for pitch, formants, clarity processing, and visualization.

## Parameters
| Parameter | Default | Options/Range | Notes |
| --- | --- | --- | --- |
| FFT size | 2048 | 1024, 2048, 4096, 8192 | Power-of-two sizes. |
| Window | Hann | Hann, Hamming, Blackman-Harris, Gaussian, Kaiser | Gaussian sigma=0.4; Kaiser beta=9. |
| Overlap | 0.75 | 0.5, 0.75, 0.875 | Hop = fftSize * (1 - overlap). |

Derived values:
- Display bins = min(1024, fftSize/2).
- Bin resolution = sampleRate / fftSize.
- Frame rate = sampleRate / hop.

## Algorithm
- Analysis buffer is shifted by hop size each frame.
- Window is applied to the processed buffer before FFT.
- Magnitude per bin: `sqrt(re^2 + im^2) * (2 / sum(window))` (coherent gain compensation).

## Reassignment Support
- When reassignment is enabled, two extra FFTs are computed using:
  - time-weighted window `t * w[n]`
  - derivative window `w'[n]`
- These FFTs supply time/frequency shifts (see Reassignment.md).

## Real-time Considerations
- Window tables and FFT buffers are reallocated only when size changes.
- FFT processing runs on the analysis thread, not the audio callback.

Implementation refs: (src/HotMic.Core/Dsp/Fft/WindowFunctions.cs,
 src/HotMic.Core/Dsp/Fft/FastFft.cs,
 src/HotMic.Core/Analysis/AnalysisSignalProcessor.cs)
