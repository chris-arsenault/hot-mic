# Enhance Plugin - Dynamic EQ

## Purpose
Reintroduce time-varying warmth and presence based on voicing and fricative activity,
without applying a static EQ curve.

## Algorithm (DSP-facing)
Signal flow (per-sample with per-block gain updates):

1) Analysis signals (per block):
   - VoicingScore drives the low shelf (warmth).
   - FricativeActivity drives the edge and air boosts (presence + air).
   - Fricative weighting is reduced by voicing: `fricative * (1 - voicing)`.
2) Target gains:
   - `lowTargetDb = VoicingScore * LowBoostDb * Scale`
   - `edgeTargetDb = FricativeActivity * HighBoostDb * Scale`
   - `airTargetDb = FricativeActivity * HighBoostDb * Scale * 0.6`
3) One-pole smoothing (time constant = `SmoothingMs`) applied each block.
4) Filter chain (serial):
   - Low shelf: 220 Hz.
   - Presence peak: 3.4 kHz, Q=1.1.
   - Air shelf: 9 kHz, Q=0.707.

## Parameters (user-facing)
- **Low Boost (-6..+6 dB):** Maximum voiced-band low-shelf lift.
- **High Boost (-6..+6 dB):** Maximum unvoiced high-band lift.
- **Smoothing (20..200 ms):** Gain-smoothing time constant.
- **Scale (x1/x2/x5/x10):** Post-boost multiplier for tuning/diagnostics.

## Notes / Constraints
- Gains are updated once per block and then held for the block.
- Air boost uses 60% of the high-boost amount to keep the top end subtle.

## Code Pointer
- `src/HotMic.Core/Plugins/BuiltIn/DynamicEqPlugin.cs`
