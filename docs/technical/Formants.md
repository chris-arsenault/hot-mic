# Formant Tracking

## Purpose
Estimate vowel formant frequencies (F1/F2) and bandwidths from LPC analysis.

## Algorithm
- Decimate to a rate at or above 2x the preset formant ceiling (based on F2 max).
- Apply pre-emphasis from 50 Hz and a Gaussian analysis window (25 ms effective).
- Compute LPC coefficients via Burg recursion.
- Solve LPC roots and convert to frequency/bandwidth pairs.
- Candidate filtering (per voiced frame):
  - keep stable poles with bandwidth 20-800 Hz
  - keep poles inside the preset F1/F2 ranges
  - retain top N candidates per formant by pole quality (magnitude + bandwidth)
- Beam search (F1/F2 pairs):
  - pair candidates with F2 > F1 + separation margin
  - continuity cost vs previous frame (preset max deltas)
  - penalize swaps and low separation
  - keep top K hypotheses; prefer runner-up if best violates continuity
- Output:
  - emit best pair
  - apply IIR smoothing to F1/F2 (preset tau)
- Gating:
  - run only when voiced AND vowel-band energy is sufficient (F1 band clamped to 200-1000 Hz)
  - otherwise hold last track internally and emit no update for the frame

## Presets (bounds + continuity)
| Preset | LPC order | F1 range (Hz) | F2 range (Hz) | Max delta F1/F2 (Hz/frame) | Smoothing tau (ms) |
| --- | --- | --- | --- | --- | --- |
| Bass/Baritone | 8 | 180-750 | 700-2200 | 120 / 200 | 25 |
| Tenor (default) | 10 | 200-900 | 800-2500 | 150 / 300 | 20 |
| Alto | 10 | 250-1000 | 1000-3000 | 180 / 400 | 16 |
| Soprano | 12 | 300-1200 | 1200-3500 | 220 / 500 | 12 |

## Parameters and Defaults
| Parameter | Default | Range | Notes |
| --- | --- | --- | --- |
| Formant profile | Tenor | Bass/Baritone/Tenor/Alto/Soprano | Sets F1/F2 bounds, LPC order, continuity, smoothing. |
| LPC order | 10 | 8..24 | Auto-updated when profile changes; user override available in plugin. |
| Window length | 25 ms | fixed | Gaussian window (Praat-style effective length). |
| Pre-emphasis | 50 Hz | fixed | Applied before LPC analysis. |
| Tracked formants | 2 | fixed | F1/F2 only; buffers still allocate up to 5 for compatibility. |
| Beam width | 5 | fixed | K best hypotheses per frame. |
| Candidate cap | 5 | fixed | Top candidates per formant by pole quality. |
| Vowel energy gate | 0.15 | 0..1 | Ratio of F1-band energy to total (band clamped to 200-1000 Hz). |

## Real-time Considerations
- LPC coefficients, root buffers, and candidate/beam arrays are preallocated.
- Formant tracking runs only when voiced and vowel-like energy is present; otherwise it holds prior state.

Implementation refs: (src/HotMic.Core/Dsp/Analysis/Formants/LpcAnalyzer.cs,
 src/HotMic.Core/Dsp/Analysis/Formants/BeamSearchFormantTracker.cs,
 src/HotMic.Core/Plugins/BuiltIn/VocalSpectrographPlugin.Analysis.cs)
