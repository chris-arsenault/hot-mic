# Enhance Plugin - Consonant Transient

## Purpose
Increase speech intelligibility and "enunciation weight" by emphasizing
high-frequency consonant onsets (plosives, fricatives) without broad EQ boosts.

## Algorithm (DSP-facing)
Signal flow (per-sample):

1) High-band isolation:
   - High-pass at 2 kHz.
   - Low-pass at HighCut (3 kHz .. 9 kHz).
2) Onset detection using high-band spectral flux (analysis signal):
   - Input is log spectral-flux in dB for the high band.
   - Track a baseline (120 ms) and compute onsetDeltaDb = flux - baseline.
   - Gate = soft-knee of (onsetDeltaDb - ThresholdDb), knee = 3 dB.
3) Gate hold (25 ms) to align frame-based flux with sample-rate processing.
4) Boost: baseGain = 1 + Amount * Gate. Convert to dB, apply Scale, then
   add the band-limited boost to the dry input.
5) Soft clip the boost to prevent harshness.

This is intentionally onset-driven rather than a general transient shaper.
It targets consonant onsets in the high band, consistent with intelligibility
literature on envelope/onset enhancement.

## Parameters (user-facing)
- Amount (0..1): boost amount applied during detected onsets.
- Threshold (0..6 dB): onset flux threshold above baseline.
- High Cut (3 kHz .. 9 kHz): upper limit of the emphasis band.
- Scale (x1/x2/x5/x10): multiplicative boost for diagnostics/tuning.

## Psychoacoustic Basis
- Consonant intelligibility depends strongly on high-frequency envelope/onset cues.
- Envelope/onset enhancement has been shown to improve consonant identification,
  especially under noise or reduced bandwidth.
- Increasing consonant energy relative to vowels (C/V ratio) is another
  established route to intelligibility gains, which this plugin approximates
  without broadband EQ.

## References
- Hazan & Simpson (1990). "The effect of intensity on consonant identification."
  J Acoust Soc Am. PMID: 2779197.
  https://pubmed.ncbi.nlm.nih.gov/2779197/
- Yoo, Boston, El-Jaroudi, Li, Drullman, Durian (2007). "Speech signal modification
  to increase intelligibility in noisy environments." J Acoust Soc Am. PMID: 17672660.
  https://pubmed.ncbi.nlm.nih.gov/17672660/
- Koning & Wouters (2012). "The potential of onset enhancement for increased speech
  intelligibility in auditory prostheses." J Acoust Soc Am. PMID: 23039450.
  https://pubmed.ncbi.nlm.nih.gov/23039450/
- Koning & Wouters (2016). "Speech onset enhancement improves speech intelligibility
  in adverse listening conditions." J Acoust Soc Am. PMID: 27697583.
  https://pubmed.ncbi.nlm.nih.gov/27697583/

## Code Pointers
- `src/HotMic.Core/Plugins/BuiltIn/ConsonantTransientPlugin.cs`

## Notes / Constraints
- Uses analysis-driven onset flux for consonant targeting.
- Gate hold aligns frame-based detection with sample-rate boost.
- Avoids broadband EQ boosts to preserve timbre.
