# Spectrogram Rendering (DSP Mapping)

## Purpose
Map normalized magnitudes into color indices for display.

## Inputs
- Per-frame, per-bin magnitudes normalized to 0..1 (after dynamic range mapping).

## Transfer Function
Applied to each normalized value `v`:
1. Brightness: `v = clamp(v * brightness)`
2. Gamma: `v = v^gamma`
3. Contrast: `v = (v - 0.5) * contrast + 0.5`
4. Clamp to [0, 1]
5. Quantize to `ColorLevels` steps
6. Convert to a 0..255 LUT index

## Color Maps
- 256-entry LUTs interpolated from fixed stops.
- Maps:
  - Vocal: 000000, 1E0032, 3C0078, 0032B4, 0096C8, 32C896, C8E632, FFC800, FFFFC8
  - VocalWarm: warm gradient (black to amber)
  - Grayscale: black to white
  - Inferno, Viridis, Magma: perceptual gradients
  - Blue: 000000, 040B1A, 0A1E3A, 0F3C72, 1F6BB6, 2FA4FF, C8F0FF

## Parameters and Defaults
| Parameter | Default | Range | Notes |
| --- | --- | --- | --- |
| Color map | Blue | Vocal, VocalWarm, Grayscale, Inferno, Viridis, Magma, Blue | LUT choice. |
| Brightness | 1.0 | 0.5..2.0 | Multiplies normalized magnitude. |
| Gamma | 0.8 | 0.6..1.2 | Power curve. |
| Contrast | 1.2 | 0.8..1.5 | Contrast stretch around 0.5. |
| Color levels | 32 | 16..64 | Quantized steps. |

## Overlay Projection
- Pitch, formants, harmonics, and voicing are projected using the same
  frequency scale and frame index used for spectrogram bins.

## Real-time Considerations
- LUTs are recomputed only when parameters change.
- Spectrogram bitmap updates incrementally per new frame.

Implementation refs: (src/HotMic.App/UI/PluginComponents/AnalyzerRenderer.cs,
 src/HotMic.App/UI/PluginComponents/SpectrogramColorMaps.cs)
