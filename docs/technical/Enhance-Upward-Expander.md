# Enhance Plugin - Upward Expander

## Purpose
Restore micro-dynamics within speech bands that were flattened by denoise/de-ess/
compression, while avoiding noise pumping. This is a speech-gated, multiband
upward expander (dynamic decompression), not a general broadband expander.

## Algorithm (DSP-facing)
Signal flow (per-sample):

1) Split into 3 bands:
   - Low: low-pass at LowSplitHz.
   - High: high-pass at HighSplitHz.
   - Mid: input - low - high.
2) Track per-band envelopes (attack/release).
3) Gain computer (upward expansion):
   - levelDb = 20*log10(env)
   - if levelDb > ThresholdDb:
       gainDb = min(MaxBoostDb, (levelDb - ThresholdDb) * (Ratio - 1))
     else gainDb = 0 dB
   - Ratio = 1 + (AmountPct/100) * 0.3 (cap at 1.3)
4) Speech gating:
   - gateBase = SpeechPresence * GateStrength
   - Voiced bands: lowGate = gateBase * voicedWeight
   - Unvoiced bands: highGate = gateBase * unvoicedWeight
   - MidGate = gateBase * (0.6*voicedWeight + 0.4*unvoicedWeight)
5) Apply gate and Scale (post-gate):
   - target = 1 + (gainLinear - 1) * gate
   - target = 1 + (target - 1) * Scale
6) Smooth target gain with band-specific attack/release
   (low slower, high faster), then recombine bands.

This matches speech literature on envelope expansion: small ratios, speech-gated,
multiband operation to recover temporal contrast without lifting noise.

## Parameters (user-facing)
- Amount (0..100%): controls expansion ratio (1.0 -> 1.3).
- Threshold (-60..-10 dB): expansion starts above this envelope level.
- Low Split (80..400 Hz): low band cutoff.
- High Split (1.5..8 kHz): high band cutoff.
- Attack (2..50 ms): expansion attack time.
- Release (30..300 ms): expansion release time.
- Gate Strength (0..1): scales speech-gated behavior.
- Scale (x1/x2/x5/x10): post-gate boost multiplier for tuning/diagnostics.

## Psychoacoustic Basis
- Envelope expansion can improve consonant recognition when applied
  selectively to high-SNR regions (i.e., speech-gated expansion). Broad
  expansion of noisy speech often shows no benefit or even degradation.
- Using voiced/unvoiced cues to weight bands supports consonant/vowel
  contrast without broad EQ boosts.

## References
- Effects of expanding envelope fluctuations on consonant perception in
  noise (multiband expansion; SNR-based expansion helps, broad expansion does not).
  https://pubmed.ncbi.nlm.nih.gov/29756553/
- Envelope enhancement improves speech perception in auditory neuropathy.
  https://pubmed.ncbi.nlm.nih.gov/18091107/
- Multichannel compression/expansion effects on nonsense syllables in noise
  (expansion can degrade intelligibility if applied broadly).
  https://pubmed.ncbi.nlm.nih.gov/6491047/
- Envelope expansion in quiet vs noise (small decrements in quiet, gains in noise).
  https://pubmed.ncbi.nlm.nih.gov/10511632/

## Code Pointer
- `src/HotMic.Core/Plugins/BuiltIn/UpwardExpanderPlugin.cs`

## Notes / Constraints
- Max boost is capped at 6 dB to avoid pumping/clipping.
- Scale is post-gate and intended for auditioning; it should be dialed back
  for normal operation.
- The mid band is a residual (input - low - high), not a complementary
  crossover; this keeps CPU low but can introduce mild coloration.
