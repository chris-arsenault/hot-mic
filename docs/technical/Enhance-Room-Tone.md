# Enhance Plugin - Room Tone

## Purpose
Reintroduce a subtle ambience bed so cleaned speech does not sound overly dry.
The bed is ducked under speech using analysis-driven gating.

## Algorithm (DSP-facing)
Signal flow (per-sample):

1) Generate white noise using a lightweight LCG.
2) Shape the noise with a band-pass using cascaded filters:
   - High-pass: 80 Hz.
   - Low-pass: `ToneHz` (3 kHz .. 12 kHz).
3) Compute target level with speech ducking:
   - `target = levelLinear * Scale * (1 - DuckStrength * SpeechPresence)`
4) Smooth the target with a 1-pole filter (80 ms time constant).
5) Mix into the output:
   - `output = input + shapedNoise * currentLevel`

## Parameters (user-facing)
- **Level (-60..-20 dB):** Base room-tone level.
- **Duck (0..1):** Amount of attenuation during speech.
- **Tone (3 kHz .. 12 kHz):** Low-pass cutoff for the noise bed.
- **Scale (x1/x2/x5/x10):** Post-level multiplier for tuning/diagnostics.

## Notes / Constraints
- Ducking is driven by `SpeechPresence` and smoothed to avoid modulation artifacts.
- The room bed is intentionally narrow-band to avoid low-frequency buildup.

## Code Pointer
- `src/HotMic.Core/Plugins/BuiltIn/RoomTonePlugin.cs`
