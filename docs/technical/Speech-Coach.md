# Speech Coach

## Purpose
Provide real-time speech feedback metrics derived from analysis frames.
The focus is on speaking rate, pauses, prosody, clarity, and intelligibility.

## Inputs (per analysis frame)
- Energy (dB) derived from waveform peak amplitude.
- Pitch (Hz) + pitch confidence.
- Voicing state (Silence / Unvoiced / Voiced).
- Spectral flatness, spectral flux, spectral slope.
- HNR (dB) from harmonic/noise analysis.
- Formant estimates (F1/F2) when available.

## Output Metrics
- **Rate:** syllables/min, articulation rate, pause ratio, mean pause duration, pauses/min, filled-pause ratio.
- **Prosody:** pitch range (semitones), pitch variation (semitones), pitch slope (semitones/sec), monotone score, mean pitch.
- **Clarity:** vowel clarity, consonant clarity, transition sharpness, overall clarity (0-100).
- **Intelligibility:** modulation-based score (0-100).
- **Events:** syllable markers, speaking state (speaking / silent pause / filled pause), stress level, filler count.

## Algorithms (DSP-facing)

### 1) Syllable Detection
- Based on de Jong & Wempe peak-picking.
- Syllable energy uses a 20 ms trailing window of band-limited peak amplitude (300–2000 Hz).
- Smoothed energy envelope (EMA, alpha=0.15).
- Peak when center frame > previous and > current, with comparison span ≈60 ms to reduce hop-size aliasing.
- Prominence threshold: 3 dB above local dip.
- Voiced-only peaks.
- Minimum inter-syllable interval: 50 ms.

### 2) Pause Detection
- **Silent pause:** voicing == Silence for >=150 ms.
- **Filled pause:** voicing == Voiced AND pitch confidence < 0.3 AND spectral flatness > 0.4 for >=100 ms.
- Emits pause events on state exit (type, start, end, duration).

### 3) Speech Rate Metrics
- Sliding window: 10 s default (clamped 1-60 s).
- **Syllable rate:** syllables / total window time.
- **Articulation rate:** syllables / (window - pause time).
- **Pause ratio:** total pause time / window.
- **Pauses per minute:** pause count / window.
- **Filled-pause ratio:** filled pauses / total pauses.

### 4) Prosody / Pitch Contour
- Pitch tracked in semitones relative to 100 Hz reference.
- Window: 5 s default (clamped 1-30 s).
- Metrics:
  - Range = max - min (semitones).
  - Variation = stddev (semitones).
  - Slope = linear regression (semitones/sec).
  - Monotone score = 1 - min(variation / 2 st, 1).

### 5) Stress Detection
- Maintains slow EMA baselines for energy and pitch (alpha=0.01).
- Primary stress: >= +3 dB AND (>= +2 st pitch accent OR very high energy).
- Secondary stress: >= +1.5 dB OR >= +1 st pitch accent.

### 6) Filler Detection
- Candidate when voiced and spectral flux < 0.02.
- Duration window: 80-1200 ms.
- Requires stable pitch (<= 1.5 st stddev) and high voiced ratio (>= 0.8).
- Schwa formant check (if F1/F2 available): F1 400-800 Hz, F2 1000-1600 Hz.
- "Um" heuristic: F2 drop > 200 Hz near the end.
- Note: current pipeline does not supply formants (F1/F2 = 0), so filler type often defaults to Generic.

### 7) Clarity Metrics
- **Vowel clarity:** maps HNR (dB) to 0-100 with `HNR * 5`.
- **Consonant clarity:** maps negative spectral slope (dB/kHz) to 0-100.
- **Transition sharpness:** voicing transition rate; normalized around ~0.1 transitions/frame.
- **Overall clarity:** 50% vowel + 30% consonant + 20% transitions.

### 8) Intelligibility (Modulation-Based)
- Downsample energy envelope to 50 Hz.
- Compute modulation spectrum via DFT on ~5 s window.
- Measure energy in 2-8 Hz modulation band.
- Score combines modulation index and band concentration.

## Update Cadence
Aggregate metrics are recomputed every 10 analysis frames to reduce CPU load.

Implementation refs: (src/HotMic.Core/Dsp/Analysis/Speech/SpeechCoach.cs,
 src/HotMic.Core/Dsp/Analysis/Speech/SyllableDetector.cs,
 src/HotMic.Core/Dsp/Analysis/Speech/PauseDetector.cs,
 src/HotMic.Core/Dsp/Analysis/Speech/SpeechRateCalculator.cs,
 src/HotMic.Core/Dsp/Analysis/Speech/PitchContourAnalyzer.cs,
 src/HotMic.Core/Dsp/Analysis/Speech/StressDetector.cs,
 src/HotMic.Core/Dsp/Analysis/Speech/FillerDetector.cs,
 src/HotMic.Core/Dsp/Analysis/Speech/ClarityAnalyzer.cs,
 src/HotMic.Core/Dsp/Analysis/Speech/IntelligibilityAnalyzer.cs)
