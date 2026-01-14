# Vocal Reference

## Purpose
Provide vocal frequency ranges and reference bands used for analysis overlays and
interpretation.

## Fundamental Ranges (F0)
| Voice Type | Range (Hz) |
| --- | --- |
| Bass | 80-350 |
| Baritone | 95-400 |
| Tenor | 120-500 |
| Alto | 160-700 |
| Mezzo-Soprano | 180-800 |
| Soprano | 250-1100 |

## Formant Ranges (Typical Adult)
| Formant | Range (Hz) | Description |
| --- | --- | --- |
| F1 | 250-1000 | Jaw openness |
| F2 | 700-2500 | Tongue position |
| F3 | 1800-3500 | Voice quality |
| F4 | 3000-4500 | Upper resonances |
| F5 | 4000-6000 | Upper resonances |

## Vocal Zones
| Zone | Range (Hz) | Significance |
| --- | --- | --- |
| Sub-bass | <80 | Plosives, rumble |
| Fundamental | 80-500 | Pitch body |
| Lower harmonics | 500-2000 | Warmth, vowel definition |
| Presence | 2000-4000 | Intelligibility, clarity |
| Brilliance | 4000-8000 | Air, consonant detail |
| Extended air | 8000-12000 | Breathiness |

## Note Mapping
- Reference: A4 = 440 Hz.
- MIDI note: `m = round(69 + 12 * log2(f / 440))`.

## Usage
- Voice ranges drive range overlays and guide bands.
- Zone guides provide consistent reference lines across scales.

Implementation refs: (src/HotMic.Core/Dsp/Voice/VocalRangeInfo.cs)
