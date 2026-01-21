# Enhance Plugin - Spectral Contrast

## Purpose
Increase spectral contrast (peaks vs valleys) to improve perceived clarity and
articulation, especially for speech in noise. This is a speech-gated spectral
sharpening stage designed to be subtle and non-EQ-like.

## Algorithm (DSP-facing)
Signal flow (per-frame STFT):

1) STFT analysis: 512-pt FFT, hop 128, Hann window.
2) Compute magnitude spectrum and log-magnitudes (dB).
3) Auditory-contrast kernel (DoG on ERB scale):
   - Convert bin frequencies to ERB-rate.
   - Build a zero-mean difference-of-Gaussians (DoG) kernel whose bandwidth
     varies with frequency (auditory filter bandwidth scaling).
   - Convolve log-magnitudes with the DoG kernel per bin (kernel L1-normalized)
     to obtain contrast in dB.
4) Gain compute:
   - gainDb = clamp(Strength * Gate * Scale * contrastDb, +/- 6 dB)
   - gainDb is smoothed per-bin (attack/release) to reduce musical-noise artifacts.
   - gain = dbToLinear(gainDb)
5) Apply gain to the complex spectrum; preserve original phase.
6) Overlap-add resynthesis using per-sample weight normalization:
   - Accumulate windowed output and a parallel weight buffer (window^2).
   - Divide by weight when outputting samples to ensure perfect reconstruction.

Speech gating:
- gate = SpeechPresence * GateStrength
- Effect is fully disabled when speech is not detected.

## Parameters (user-facing)
- Strength (0..100%): spectral contrast amount (DoG gain).
- Mix (0..100%): dry/wet blend.
- Gate Strength (0..1): scales SpeechPresence gating.
- Scale (x1/x2/x5/x10): post-gate multiplier for diagnostics/tuning.

## Psychoacoustic Basis
- Spectral contrast enhancement (SCE) and lateral inhibition can improve
  intelligibility by sharpening spectral prominences (formants, place cues),
  particularly in noise.
- Auditory-inspired implementations operate on an excitation pattern and use
  a DoG kernel whose bandwidth varies like auditory filters (ERB scale).
- Reported gains are modest and depend on SNR and listener population; overly
  aggressive settings can reduce intelligibility.

## References
- Spectral contrast enhancement via auditory excitation and DoG filtering with
  ERB-varying bandwidth; modest intelligibility gains in noise.
  https://pubmed.ncbi.nlm.nih.gov/2356717/
- Real-time contrast enhancement with lateral inhibition and dynamic gain,
  improving consonant/vowel recognition especially in noise.
  https://pubmed.ncbi.nlm.nih.gov/21949736/
- ERB-scale spectral contrast enhancement effects in noise; large enhancement
  can reduce intelligibility while moderate settings are safer.
  https://pubmed.ncbi.nlm.nih.gov/8263829/

## Code Pointer
- `src/HotMic.Core/Plugins/BuiltIn/SpectralContrastPlugin.cs`

## Notes / Constraints
- This is not a static EQ; it is an adaptive, speech-gated spectral sharpener.
- Overlap-add normalization is required to avoid broadband attenuation.
- Excessive Strength can suppress valleys too aggressively; keep defaults mild.
