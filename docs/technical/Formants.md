# Formant Tracking

## Purpose
Estimate vocal formant frequencies and bandwidths using LPC analysis.

## Algorithm
- Compute LPC coefficients via Levinson-Durbin recursion.
- Build the LPC polynomial and solve for complex roots.
- Convert root angles to frequencies and root magnitudes to bandwidths.
- Filter by frequency range and positive bandwidth.
- Sort by frequency and return up to 5 formants (F1-F5).

## Parameters and Defaults
| Parameter | Default | Range | Notes |
| --- | --- | --- | --- |
| LPC order | sampleRate/1000 + 4 (clamped 8..24) | 8..24 | Default 24 at 48 kHz. |
| Max formants | 5 | fixed | F1..F5. |
| Min/Max frequency | 80 / 8000 Hz | set by analysis bounds | Used to filter formants. |

## Real-time Considerations
- LPC coefficients, root buffers, and scratch arrays are preallocated.
- Formant tracking runs only when voicing is detected.

Implementation refs: (src/HotMic.Core/Dsp/Analysis/Formants/LpcAnalyzer.cs,
 src/HotMic.Core/Dsp/Analysis/Formants/FormantTracker.cs)
