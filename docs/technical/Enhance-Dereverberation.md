# Enhance Plugin - Dereverberation

## Purpose
Remove late room reverberation from a single-channel speech signal using frame-online
recursive WPE (Weighted Prediction Error). This is a blind dereverberation method:
no prior knowledge of the room impulse response is needed.

## Algorithm (DSP-facing)
The plugin operates in the STFT domain with overlap-add reconstruction.

Per STFT frame, per frequency bin `f`:

1. Extract prediction window `x_tilde` from the circular frame buffer:
   past frames at indices `[t-D, t-D-1, ..., t-D-K+1]` where D = prediction delay,
   K = filter taps.
2. Compute prediction: `y_hat = sum_k conj(g_k) * conj(x_tilde_k)`
   where `g` is the adaptive filter vector.
3. Compute dereverberated output: `z = y - y_hat` (used for filter adaptation).
4. Update power estimate: `sigma2 = alpha * sigma2 + (1 - alpha) * |z|^2`
5. Compute Kalman gain:
   - `numerator = P @ conj(x_tilde)` (matrix-vector, K ops per row)
   - `denominator = alpha * sigma2 + x_tilde^T @ numerator` (complex scalar)
   - `K_gain = numerator / denominator`
6. Update inverse covariance (Sherman-Morrison rank-1):
   `P = (P - K_gain @ numerator^H) / alpha`
7. Update filter taps: `g = g + K_gain * conj(z)`
8. Blended output: `output = y - reduction * y_hat`

The filter adapts on the full dereverberated signal `z` regardless of the reduction
parameter, ensuring proper convergence at all blend levels.

## Parameters (user-facing)
- **Reduction** (0..1, default 0.5): dry/wet blend. 0 = bypass, 1 = full dereverberation.

## Internal parameters (quality-profile controlled)
- **FFT size**: STFT window length (1024 or 2048).
- **Hop size**: STFT hop (256 or 512). Overlap ratio = 1 - hop/fft.
- **Taps (K)**: Prediction filter order. More taps = longer reverb tails captured.
- **Delay (D)**: Prediction delay in frames. Preserves direct sound and early
  reflections by preventing the filter from predicting them.
- **Alpha**: Exponential smoothing factor for power and covariance updates.

## Quality profiles

| Profile | FFT | Hop | Taps | Delay | Alpha | Latency |
|---------|-----|-----|------|-------|-------|---------|
| LatencyPriority | 1024 | 256 | 15 | 3 | 0.99 | 768 samples (16 ms) |
| QualityPriority | 2048 | 512 | 30 | 3 | 0.99 | 1536 samples (32 ms) |

Reverb context: at hop=256 (5.3 ms/frame), 15 taps covers ~80 ms of late reverb.
At hop=512 (10.7 ms/frame), 30 taps covers ~320 ms.

## Signal chain placement
Place **after** noise gate (prevents deverb processing silence) and **before** noise
reduction / speech denoiser (deverb removes spectral smearing that confuses denoisers).

## Warmup
The filter needs `delay + taps` frames of history before prediction begins.
During warmup, the plugin passes audio through unmodified while filling the frame buffer.

## Computational complexity
Per frame: O(bins * taps^2) for the covariance update (dominant cost).
At quality priority (257 bins, 30 taps, ~94 frames/sec): ~2.2 GFLOPS, approximately
2-4% of a single modern CPU core.

## Code reference
- `src/HotMic.Core/Plugins/BuiltIn/DereverbPlugin.cs`
- Registered as `builtin:deverb` in `src/HotMic.Core/Plugins/PluginFactory.cs`

## References
- Yoshioka & Nakatani, "Generalization of multi-channel linear prediction methods
  for blind MIMO impulse response shortening" (IEEE Trans. Audio, Speech, Language
  Processing, 2012)
- nara_wpe: https://github.com/fgnt/nara_wpe (reference Python implementation)
