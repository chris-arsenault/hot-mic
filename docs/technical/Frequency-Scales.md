# Frequency Scales

## Purpose
Map FFT bins into perceptual frequency scales and fixed display bins.

## Scale Formulas
- Linear: `scale = f`.
- Log10: `scale = log10(f)`.
- Mel: `scale = 2595 * log10(1 + f/700)`.
- ERB: `scale = 24.7 * (4.37 * f/1000 + 1)`.
- Bark (Traunmuller): `13*atan(0.00076*f) + 3.5*atan((f/7500)^2)`.

## Bin Mapping
- Analysis bin count is derived from the active transform:
  - FFT: `fftSize/2`
  - ZoomFFT: `OutputBins`
  - CQT: `BinCount`
- Display bin count = `min(1024, analysisBins)`.
- The min/max display bounds are mapped into scale space.
- Each display bin spans a uniform interval in scale space and maps back to a
  range of analysis bins using the analysis bin centers/edges.
- Mapping uses max-hold across the analysis-bin range per display bin.
- Display-bin center frequencies are computed from the scale midpoint and reused
  for axis/overlays.
- Spectral features use analysis-bin center frequencies (not display bins).
- Reassignment uses the same display scale mapping for frequency -> display bin.

## Parameters
| Parameter | Default | Range | Notes |
| --- | --- | --- | --- |
| Scale | Mel | Linear, Log, Mel, ERB, Bark | Perceptual mapping choice. |
| Min frequency | 80 Hz | 20-2000 Hz | Clamped to [1, Nyquist-1]. |
| Max frequency | 8000 Hz | 2000-12000 Hz | Clamped to [min+1, Nyquist]. |
| Display bins | min(1024, fft/2) | fixed | Derived from FFT size. |

## Real-time Considerations
- Scale mappings and bin ranges are updated only on configuration changes.
- No per-frame allocations.

Implementation refs: (src/HotMic.Core/Dsp/Mapping/FrequencyScaleUtils.cs,
 src/HotMic.Core/Dsp/Mapping/SpectrogramDisplayMapper.cs,
 src/HotMic.Core/Dsp/Spectrogram/SpectrogramAnalysisDescriptor.cs,
 src/HotMic.App/UI/PluginComponents/DisplayPipeline.cs,
 src/HotMic.Core/Analysis/AnalysisOrchestrator.cs)
