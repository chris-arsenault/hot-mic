# Metering

## Purpose
Describe the level meters used for channel and master monitoring.

## Peak/RMS Meter (per channel, per plugin slot)
- **Peak:** Fast attack (~1 ms) / medium release (~100 ms) envelope follower.
- **RMS:** Slower attack (~50 ms) / release (~150 ms) to smooth display.
- **Clip Hold:** 0.5 s latch if any sample exceeds 1.0 or is non-finite.

### Smoothing Model
For a block of N samples:
- `coeff = 1 - exp(-1 / (timeConstant * sampleRate))`
- Effective per-block coefficient: `1 - (1 - coeff)^N`.
- Applied to peak and RMS envelopes independently.

## LUFS Meter (master output)
- Implements ITU-R BS.1770 K-weighting:
  - High-pass: 60 Hz, Q=0.5.
  - High-shelf: 4 kHz, +4 dB, Q=0.707.
- **Momentary window:** 400 ms.
- **Short-term window:** 3 s.
- **Offset:** -0.691 dB (BS.1770 reference).
- **Floor:** -70 LUFS.

Implementation refs: (src/HotMic.Core/Metering/MeterProcessor.cs,
 src/HotMic.Core/Metering/LufsMeterProcessor.cs)
