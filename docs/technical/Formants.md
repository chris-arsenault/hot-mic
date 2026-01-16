# Formant Tracking

## Purpose
Estimate vocal formant frequencies and bandwidths using LPC analysis.

## Algorithm
- Decimate to a rate at or above 2× the formant ceiling.
- Apply pre-emphasis from 50 Hz and a Gaussian analysis window (25 ms effective).
- Compute LPC coefficients via Burg recursion.
- Build the LPC polynomial and solve for complex roots.
- Convert root angles to frequencies and root magnitudes to bandwidths.
- Filter by ceiling/min range and bandwidth, then select trajectories with beam search.

## Parameters and Defaults
| Parameter | Default | Range | Notes |
| --- | --- | --- | --- |
| Formant profile | Male | Male/Female/Child | Male=5000 Hz ceiling, Female=5500 Hz, Child=8000 Hz. |
| LPC order | 10 | 8..24 | Auto-updated when profile changes (Burg: 2×formants; +2 for Child). |
| Window length | 25 ms | fixed | Gaussian window (Praat-style effective length). |
| Pre-emphasis | 50 Hz | fixed | Applied before LPC analysis. |
| Max formants | 5 | fixed | F1..F5. |
| Min/Max frequency | 50 Hz / ceiling | set by profile | Used to filter formants. |

## Real-time Considerations
- LPC coefficients, root buffers, and scratch arrays are preallocated.
- Formant tracking runs only when voicing is detected.

Implementation refs: (src/HotMic.Core/Dsp/Analysis/Formants/LpcAnalyzer.cs,
 src/HotMic.Core/Dsp/Analysis/Formants/BeamSearchFormantTracker.cs,
 src/HotMic.Core/Plugins/BuiltIn/VocalSpectrographPlugin.Analysis.cs)
