# Enhance Plugin - Air Exciter

## Purpose
Add controlled high-frequency "air" by generating harmonics in the upper band, keyed by
voicing and suppressed during sibilance.

## Algorithm (DSP-facing)
Signal flow (per-sample):

1) High-pass isolate the air band.
   - HPF cutoff: `CutoffHz` (default 4.5 kHz), Q = 0.707.
2) Voicing gate with sibilance suppression:
   - `gate = clamp(VoicingScore * (1 - SibilanceEnergy), 0..1)`.
3) Drive modulation:
   - Slow random LFO at 0.35 Hz, depth 0.08, slewed over 500 ms.
   - LFO depth is reduced by sibilance (`1 - SibilanceEnergy`).
4) Nonlinear shaping:
   - `drive = clamp(Drive * Scale * (1 + lfo*depth), 0..10)`
   - `shaped = tanh(high * (1.5 + drive * 4))`
5) Mix back:
   - `output = input + shaped * (Mix * gate)`

When analysis signals are unavailable, the plugin falls back to ungated excitation
with the same high-pass and shaping.

## Parameters (user-facing)
- **Drive (0..1):** Harmonic generation intensity.
- **Mix (0..1):** Wet mix of generated air.
- **Cutoff (3 kHz .. 10 kHz):** High-pass cutoff for the exciter band.
- **Scale (x1/x2/x5/x10):** Post-drive multiplier for tuning/diagnostics.

## Notes / Constraints
- Sibilance suppression prevents the exciter from exaggerating "s" and "sh".
- LFO modulation is subtle and slow to avoid static sheen.

## Code Pointer
- `src/HotMic.Core/Plugins/BuiltIn/AirExciterPlugin.cs`
