# Preprocessing

## Purpose
Prepare the analysis signal for FFT and feature extraction while removing low-frequency bias.

## Signal Chain
1. DC removal (one-pole high-pass @ 10 Hz)
2. Optional rumble HPF (biquad high-pass)
3. Optional pre-emphasis (first-order differentiator)

## Parameters
| Parameter | Default | Range | Notes |
| --- | --- | --- | --- |
| DC cutoff | 10 Hz | fixed | Always on. |
| HPF enabled | On | bool | Controls rumble HPF stage. |
| HPF cutoff | 60 Hz | 20-120 Hz | 2nd-order high-pass, Q=0.707. |
| Pre-emphasis enabled | On | bool | Applied before FFT/LPC. |
| Pre-emphasis alpha | 0.97 | fixed | `y[n] = x[n] - alpha * x[n-1]`. |

## Algorithm Details
- DC removal uses a one-pole high-pass: `a = exp(-2*pi*fc/sr)` and
  `y[n] = a * (y[n-1] + x[n] - x[n-1])`.
- Rumble HPF uses a biquad high-pass (Butterworth-ish at Q=0.707).
- Pre-emphasis applies a single-tap differentiator with alpha 0.97.

## Implementation Details
- Two analysis buffers are maintained:
  - Raw buffer: post DC/HPF (used for waveform metrics and voicing).
  - Processed buffer: post pre-emphasis (used for FFT and LPC).

## Real-time Considerations
- Filter coefficients update only on parameter changes.
- Per-sample processing uses struct state and avoids allocations.

Implementation refs: (src/HotMic.Core/Plugins/BuiltIn/VocalSpectrographPlugin.Analysis.cs,
 src/HotMic.Core/Dsp/Filters/OnePoleHighPass.cs,
 src/HotMic.Core/Dsp/Filters/BiquadFilter.cs,
 src/HotMic.Core/Dsp/Filters/PreEmphasisFilter.cs)
