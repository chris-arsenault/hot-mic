# Spectrograph Presets

## Purpose
Describe built-in vocal spectrograph presets and their parameter intent.

## Base Parameters (applied to all presets)
| Parameter | Default |
| --- | --- |
| FFT Size | 2048 |
| Window | Hann |
| Overlap | 0.75 |
| Scale | Mel |
| Min Freq | 80 Hz |
| Max Freq | 8000 Hz |
| Min dB | -80 dB |
| Max dB | 0 dB |
| Time Window | 5 s |
| Color Map | Blue |
| Pitch Overlay | On |
| Harmonics | On |
| Voicing | On |
| Pre-Emphasis | On |
| HPF Enabled | On |
| HPF Cutoff | 60 Hz |
| Reassign | Off |
| Reassign Threshold | -60 dB |
| Reassign Spread | 1.0 |
| Clarity Mode | Full |
| Clarity Noise | 1.0 |
| Clarity Harmonic | 1.0 |
| Clarity Smoothing | 0.3 |
| Pitch Algorithm | YIN |
| Axis Mode | Hz |
| Voice Range | Tenor |
| Range Overlay | On |
| Guides | On |
| Waveform View | On |
| Spectrum View | On |
| Pitch Meter | On |
| Smoothing Mode | EMA |
| Brightness | 1.0 |
| Gamma | 0.8 |
| Contrast | 1.2 |
| Color Levels | 32 |
| Normalization | None |
| Dynamic Range | Custom |

## Preset Overrides
- SpeechMale: Voice Range Baritone; Min Freq 60 Hz; Time Window 6 s; Clarity Noise 0.8; Clarity Harmonic 0.8.
- SpeechFemale: Voice Range Alto; Min Freq 100 Hz; Max Freq 9000 Hz; Time Window 6 s; Clarity Noise 0.8; Clarity Harmonic 0.8.
- SingingClassical: FFT 4096; Window Blackman-Harris; Overlap 0.875; Scale Log; Min Freq 60 Hz; Max Freq 10000 Hz; Time Window 8 s; Reassign Frequency; Clarity Smoothing 0.35; Smoothing Mode Bilateral; Brightness 1.1; Gamma 0.75; Contrast 1.25.
- SingingContemporary: Max Freq 12000 Hz; Time Window 6 s; Color Map VocalWarm; Voice Range Mezzo-Soprano; Clarity Noise 0.9; Clarity Harmonic 0.9; Brightness 1.15; Gamma 0.78; Contrast 1.3.
- VoiceoverAnalysis: FFT 4096; Window Blackman-Harris; Overlap 0.875; Min Freq 70 Hz; Max Freq 9000 Hz; Min dB -65; Time Window 6 s; Reassign Frequency; Clarity Smoothing 0.35; Smoothing Mode Bilateral; Gamma 0.75; Contrast 1.25.
- PitchTracking: FFT 1024; Overlap 0.875; Scale Log; Time Window 4 s; Harmonics Off; Clarity Mode Noise; Clarity Noise 0.6; Clarity Harmonic 0; Clarity Smoothing 0.2; Smoothing Mode EMA.
- HarmonicDetail: FFT 4096; Window Blackman-Harris; Overlap 0.875; Scale Log; Min dB -75; Reassign Frequency; Clarity Noise 0.8; Clarity Harmonic 1.0; Clarity Smoothing 0.35; Smoothing Mode Bilateral; Time Window 6 s.
- TransientCapture: FFT 1024; Overlap 0.5; Scale Linear; Time Window 3 s; Min dB -60; Pitch Overlay Off; Harmonics Off; Voicing Off; Pitch Meter Off; Clarity Mode Noise; Clarity Noise 0.4; Clarity Harmonic 0; Clarity Smoothing 0; Smoothing Mode Off.
- Presentation: Time Window 10 s; Axis Mode Both; Brightness 1.2; Gamma 0.75; Contrast 1.35; Color Levels 24; Clarity Smoothing 0.35; Smoothing Mode Bilateral.
- Technical: Min dB -90; Axis Mode Both; Clarity Mode None; Clarity Noise 0; Clarity Harmonic 0; Clarity Smoothing 0; Smoothing Mode Off; Brightness 1; Gamma 1; Contrast 1; Color Levels 64.
- Minimal: Time Window 8 s; Pitch Overlay Off; Harmonics Off; Voicing Off; Range Overlay Off; Guides Off; Waveform View Off; Spectrum View Off; Pitch Meter Off.
- Maximum Clarity: Min dB -65; Clarity Mode Full; Clarity Noise 1; Clarity Harmonic 1; Clarity Smoothing 0.35; Smoothing Mode Bilateral; Gamma 0.75.
- Balanced: no overrides (base parameters only).
- Low Latency: FFT 1024; Overlap 0.5; Min dB -60; Clarity Mode Noise; Clarity Noise 0.5; Clarity Harmonic 0; Clarity Smoothing 0.2; Smoothing Mode EMA; Gamma 0.85.
- Analysis Mode: Min dB -80; Clarity Mode None; Clarity Noise 0; Clarity Harmonic 0; Clarity Smoothing 0; Smoothing Mode Off; Gamma 1; Contrast 1; Brightness 1.
