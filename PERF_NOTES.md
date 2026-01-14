# Performance Notes (Spectrograph + DSP)

## Hot Paths Observed
- Noise floor estimation: `SpectrogramNoiseReducer.UpdateEstimate` sorts a history window per bin per frame. This is O(bins * history log history) and becomes expensive at high bin counts (4096+).
- HPSS: `SpectrogramHpssProcessor.Apply` performs median filtering per bin across time and frequency (nested loops with insertion sort). This is O(bins * kernel^2) per frame.
- Bilateral smoothing: `SpectrogramSmoother.ApplyBilateral` runs nested loops with per-neighbor `LinearToDb` and `Exp`, which is heavy at large bin counts.
- Renderer: `VocalSpectrographRenderer.ShiftSpectrogramLeft` shifts the full pixel buffer each tick and then copies the full buffer into the bitmap. This is O(width * height) per update.

## Likely Impact
- 4096-bin mode amplifies per-bin DSP costs and the full-frame pixel shift, which matches observed stutter.
- Any "full rebuild" paths in the renderer cause a complete recompute and pixel upload, which is visible as a frame-time spike.

## Potential Optimizations (No Behavior Change)
- Noise reducer: replace per-bin full sort with a rolling percentile estimator (P^2) or maintain a small histogram per bin.
- HPSS: decimate the time axis for the median, or compute HPSS only on a downsampled spectrum and upsample the mask.
- Bilateral: precompute log magnitude once per bin, and reuse it for the neighbor weight calculation; consider limiting time radius at large bin counts.
- Renderer: use a ring-buffered bitmap (write columns only) to avoid per-frame left-shift and full buffer copy.

## Profiling Suggestions
- Capture CPU profile around the analysis thread at 4096 bins.
- Add timers around the DSP stages (noise, HPSS, smoothing, reassign) to quantify relative cost.
- Measure total render time per frame, and isolate bitmap shifts vs. column updates.
