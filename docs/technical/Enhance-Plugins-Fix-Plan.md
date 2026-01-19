# Enhance Plugins Correctness Plan

Goal: make the 8 enhance plugins match ENHANCE.md and remove shim-style gates.
Analysis signals exist only to support these plugins.

## Scope
- Upward Expander
- Spectral Contrast
- Dynamic EQ
- Room Tone
- Air Exciter
- Bass Enhancer
- Consonant Transient
- Formant Enhancer

## Steps
- [x] Review ENHANCE.md intent for each plugin and map required analysis signals.
- [x] Remove shim gates (SpectralFlux, HNR, Pitch, Fricative-only gating).
- [x] Update DSP implementations to match ENHANCE.md behavior.
- [x] Update plugin UI/meters to reflect new detectors and gains.
- [x] Align docs: Enhance-Plugins-Design.md signal list + per-plugin mapping.
- [x] Sanity check for missing analysis signals and status messages.

## Signal Mapping (target)
- Upward Expander: SpeechPresence + VoicingState
- Spectral Contrast: SpeechPresence
- Dynamic EQ: VoicingScore + FricativeActivity (edge + air bands)
- Room Tone: SpeechPresence
- Air Exciter: VoicingScore + SibilanceEnergy
- Bass Enhancer: VoicingScore
- Consonant Transient: OnsetFluxHigh
- Formant Enhancer: FormantF1/F2/F3 + FormantConfidence (+ SpeechPresence gate)
