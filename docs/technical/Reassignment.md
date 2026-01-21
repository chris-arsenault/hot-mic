# Time/Frequency Reassignment

## Purpose
Sharpen time-frequency localization by relocating energy to instantaneous
frequency and time estimates.

## Modes
- Off
- Frequency (synchrosqueezing-style)
- Time
- Time + Frequency

## Algorithm
- Compute base FFT plus two auxiliary FFTs using:
  - time-weighted window `t * w[n]`
  - derivative window `w'[n]`
- For each bin above the magnitude threshold:
  - Time shift from the time-weighted FFT.
  - Frequency shift from the derivative FFT.
  - Clamp shifts to max limits.
  - Deposit energy into the reassigned time/bin using bilinear weights.

## Parameters
| Parameter | Default | Range | Notes |
| --- | --- | --- | --- |
| Mode | Off | Off, Frequency, Time, Time+Frequency | Reassignment type. |
| Threshold | -60 dB | -120..-20 dB | Applied after display gain. |
| Spread | 1.0 | 0..1 | Scales max shifts. |

Limits (at Spread = 1):
- Max time shift: 0.5 frames.
- Max frequency shift: 0.5 bins.

Latency:
- Time reassignment adds up to 1 frame of latency.

## Real-time Considerations
- Adds two extra FFTs per frame when enabled.
- Uses precomputed time-weighted and derivative windows.

Implementation refs: (src/HotMic.Core/Analysis/AnalysisSignalProcessor.cs,
 src/HotMic.Core/Analysis/AnalysisOrchestrator.cs)
