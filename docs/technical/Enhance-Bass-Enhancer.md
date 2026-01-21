# Enhance Plugin - Bass Enhancer

## Purpose
Add perceived low-frequency weight to speech without boosting true sub-bass energy.
This uses psychoacoustic "missing fundamental" cues: harmonics imply a low F0 even
when the fundamental is weak or absent.

## Algorithm (DSP-facing)
Signal flow (per-sample):

1) Band-pass the input around `CenterHz` (Q ~= 0.8).
2) Nonlinear harmonic generation with even-harmonic bias:
   - driveGain = 1 + Drive * 6
   - bias = Drive * 0.18
   - shaped = tanh((low + bias) * driveGain) - tanh(bias * driveGain) - low
   - This is an NLD residual; the bias term introduces even-order harmonics.
3) High-pass the residual at ~1.8 * CenterHz to keep only harmonics.
4) Gate by VoicingScore (analysis) to avoid adding harmonics on unvoiced noise.
5) Mix: output = input * (1 - Mix) + (input + wet) * Mix, where
   wet = harmonic * Amount * Scale * Gate.

No intentional attenuation of fundamentals is performed. Any low-band reductions
in the delta strip are expected from energy redistribution and coarse FFT
resolution in the UI delta strip.

## Parameters (user-facing)
- Amount (0..1): overall harmonic intensity.
- Drive (0..1): nonlinear drive; increases harmonic generation and even-harmonic bias.
- Mix (0..1): dry/wet blend (harmonics are additive).
- Center (70..180 Hz): band-pass center frequency.
- Scale (x1/x2/x5/x10): multiplicative boost for diagnostics/tuning.

## Psychoacoustic Basis
- Missing fundamental: perceived pitch emerges from harmonics even when the F0 is
  absent or weak. This is the core mechanism behind virtual bass.
- Nonlinear device (NLD) harmonic synthesis is a standard method in virtual bass
  systems; even-harmonic bias can enhance perceived bass at lower drive.

## References
- Cedolin & Delgutte (2005). "Pitch of Complex Tones: Rate-Place and Interspike
  Interval Representations in the Auditory Nerve." J Neurophysiol. PMID: 15788522.
  https://pmc.ncbi.nlm.nih.gov/articles/PMC2094528/
- Aarts, Larsen, Schobben (2002). "Improving perceived bass and reconstruction of
  high frequencies for band limited signals." MPCA-2002.
  https://www.researchgate.net/publication/228745871_Improving_perceived_bass_and_reconstruction_of_high_frequencies_for_band_limited_signals
- Mu, Gan, Tan (2012). "A psychoacoustic bass enhancement system with improved
  transient and steady-state performance." ICASSP 2012. DOI: 10.1109/ICASSP.2012.6287837.
  https://www.researchgate.net/publication/236843786_A_psychoacoustic_bass_enhancement_system_with_improved_transient_and_steady-state_performance
- Oo, Gan, Hawksford (2011). "Perceptually-motivated objective grading of nonlinear
  processing in virtual-bass systems." JAES (Fraunhofer repository).
  https://publica.fraunhofer.de/entities/publication/ff561da9-d4b1-4699-a3a3-41b954128913
- MathWorks Audio Toolbox example: "Psychoacoustic Bass Enhancement for Band-Limited
  Signals" (NLD-based harmonic generation and virtual pitch).
  https://www.mathworks.com/help/audio/ug/psychoacoustic-bass-enhancement-for-band-limited-signals.html

## Code Pointers
- `src/HotMic.Core/Plugins/BuiltIn/BassEnhancerPlugin.cs`

## Notes / Constraints
- Designed for speech weight and perceived bass on small speakers.
- Avoids LF boost to prevent rumble and headroom loss.
- Delta strip uses a 256-pt FFT and may show negative low-band deltas even when
  perceived bass increases.
