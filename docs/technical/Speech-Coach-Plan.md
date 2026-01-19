# Speech Coach Implementation Plan

**Status: Phase 1-5 Complete (Core + UI + Overlays)**

This document tracks the implementation of real-time speech analysis features within the existing VocalSpectrograph plugin.


## Goal

Add "voice as speech" analysis alongside existing "voice as singing" features. Provide real-time feedback on:
- Speaking rate (syllables/min, articulation rate)
- Pause patterns (frequency, duration, filled vs silent)
- Filler word detection (acoustic-based, no ASR)
- Prosody feedback (monotone detection, pitch variation)
- Clarity metrics (intelligibility indicators)

## Design Principles

1. **Reuse existing infrastructure** - No reimplementing windowing, pitch detection, voicing, etc.
2. **Extend, don't duplicate** - Add to VocalSpectrographPlugin partial classes
3. **Lock-free analysis** - Match existing thread-safety patterns
4. **UI can be messy** - Will decompose after features proven

---

## Existing Infrastructure to Reuse

| Component | Location | Reuse For |
|-----------|----------|-----------|
| Pitch detection (5 algorithms) | `Dsp/Analysis/Pitch/` | Prosody analysis, pitch variation |
| Voicing detector | `Dsp/Analysis/VoicingDetector.cs` | Silence/voiced/unvoiced classification |
| RMS energy | Already computed per frame | Energy envelope for syllable detection |
| Spectral flatness | In VoicingDetector | Voiced/unvoiced discrimination |
| Zero-crossing rate | In VoicingDetector | Consonant detection |
| F0 tracking | Plugin already runs per-frame | Pitch contour analysis |
| Frame timing | Hop-based processing | Temporal measurements |
| Ring buffers | `_spectrogramBuffer` pattern | Store speech metrics history |
| Spectral features | `SpectralFeatureExtractor` | Centroid, slope for clarity |

---

## Phase 1: Speech Rate & Pause Detection

### 1.1 Syllable Nucleus Detector

**Algorithm**: Intensity peak detection with voicing filter (de Jong & Wempe method)

**Implementation**:
```
Input: Per-frame RMS energy (dB), voicing state
Output: Syllable onset events, count

Algorithm:
1. Compute smoothed energy envelope (EMA, α ≈ 0.1)
2. Detect peaks: energy[n] > energy[n-1] AND energy[n] > energy[n-2]
3. Filter by:
   - Minimum prominence (3 dB above surrounding dip)
   - Voicing state == Voiced
   - Minimum inter-onset interval (50ms, prevents double-counting)
4. Emit syllable event with timestamp
```

**New file**: `src/HotMic.Core/Dsp/Analysis/Speech/SyllableDetector.cs`

**Reuses**:
- Frame energy (already computed in analysis loop)
- Voicing state (from VoicingDetector)

### 1.2 Pause Detector

**Algorithm**: Silence duration tracking with filled pause discrimination

**Implementation**:
```
Input: Voicing state, F0 confidence, spectral flatness
Output: Pause events (start, end, type: silent|filled)

States:
- Speaking: voicing == Voiced OR (voicing == Unvoiced AND short duration)
- Silent Pause: voicing == Silence for > 150ms
- Filled Pause: voicing == Voiced AND F0_confidence < 0.3 AND flatness > 0.4 for > 100ms

Transitions:
- Speaking → Silent: silence onset, start timer
- Silent → Speaking: emit SilentPause(duration)
- Speaking → Filled: filled pause onset (low confidence pitch, sustained)
- Filled → Speaking: emit FilledPause(duration)
```

**New file**: `src/HotMic.Core/Dsp/Analysis/Speech/PauseDetector.cs`

**Reuses**:
- Voicing state
- Pitch confidence
- Spectral flatness (from voicing detector internals)

### 1.3 Speech Rate Calculator

**Metrics**:
- **Syllable Rate**: syllables / total_time (includes pauses)
- **Articulation Rate**: syllables / speaking_time (excludes pauses)
- **Pause Ratio**: pause_time / total_time
- **Mean Pause Duration**: total_pause_time / pause_count

**Implementation**:
```
Ring buffer of syllable timestamps (last 30 seconds)
Ring buffer of pause events (last 30 seconds)

Compute over sliding window (configurable: 5s, 10s, 30s):
  syllable_rate = count(syllables in window) / window_duration * 60
  speaking_time = window_duration - sum(pause_durations in window)
  articulation_rate = count(syllables in window) / speaking_time * 60
```

**New file**: `src/HotMic.Core/Dsp/Analysis/Speech/SpeechRateCalculator.cs`

### 1.4 Plugin Integration

**New parameters** (add to `VocalSpectrographPlugin.Parameters.cs`):
```csharp
// Speech Coach section
public bool SpeechCoachEnabled { get; set; }          // Master toggle
public int SpeechRateWindowSeconds { get; set; }      // 5, 10, 30
public float SyllableProminenceDb { get; set; }       // 3.0 default
public float MinSyllableIntervalMs { get; set; }      // 50ms default
```

**New overlay data** (add to buffer structs):
```csharp
float SyllableRate;          // syllables/min
float ArticulationRate;      // syllables/min (excluding pauses)
float PauseRatio;            // 0.0-1.0
float MeanPauseDurationMs;
byte PauseState;             // 0=speaking, 1=silent, 2=filled
```

---

## Phase 2: Prosody & Pitch Variation

### 2.1 Pitch Contour Analyzer

**Algorithm**: Track F0 statistics over utterance windows

**Metrics**:
- **Pitch Range**: max(F0) - min(F0) in semitones during voiced segments
- **Pitch Variation**: stddev(F0) in semitones
- **Pitch Slope**: linear regression slope (rising/falling tendency)
- **Monotone Score**: inverse of variation (low variation = monotone)

**Implementation**:
```
Input: F0 values from pitch detector, voicing state
Output: Prosody metrics per analysis window

Track:
- f0_history: ring buffer of (f0_hz, timestamp) for voiced frames
- Compute stats over utterance window (silence-to-silence)
- Convert Hz to semitones: 12 * log2(f0 / reference)
```

**New file**: `src/HotMic.Core/Dsp/Analysis/Speech/PitchContourAnalyzer.cs`

**Reuses**:
- Pitch detection results (already running)
- Voicing state

### 2.2 Stress Pattern Detector

**Algorithm**: Detect syllable-level stress via energy + pitch + duration

**Implementation**:
```
Input: Syllable events, energy per syllable, pitch per syllable, duration
Output: Stress classification per syllable (primary, secondary, unstressed)

Features per syllable:
- Relative energy: dB above/below mean
- Pitch accent: Hz above/below mean
- Duration: ms above/below mean

Classification (threshold-based):
- Primary: energy > +3dB AND (pitch > +2st OR duration > +50ms)
- Secondary: energy > +1.5dB OR pitch > +1st
- Unstressed: otherwise
```

**New file**: `src/HotMic.Core/Dsp/Analysis/Speech/StressDetector.cs`

---

## Phase 3: Filler Word Detection

### 3.1 Acoustic Filler Detector

**Algorithm**: Pattern matching on spectral + temporal features (no ASR)

**Target fillers**: "uh", "um", "er", "ah" (filled pauses with specific characteristics)

**Implementation**:
```
Input: Voicing state, F0, spectral features, duration
Output: Filler event with type and confidence

Characteristics of fillers:
- Voiced, sustained (100ms - 1000ms)
- Low F0 variation (< 1 semitone over duration)
- Low spectral flux (steady spectrum)
  - "uh/ah": F1 ≈ 600-800 Hz, F2 ≈ 1000-1400 Hz (schwa-like)
  - "um": nasal closure following schwa (drop in high-freq energy)

Detection:
1. Identify "sustained voiced" segments (> 100ms voiced with low F0 variation)
2. Check spectral stability (flux < threshold)
4. Classify: filled_pause vs filler_word based on duration/context
```

**New file**: `src/HotMic.Core/Dsp/Analysis/Speech/FillerDetector.cs`

**Reuses**:
- Voicing state
- F0 tracking
- Spectral flux (from SpectralFeatureExtractor)

---

## Phase 4: Clarity Metrics

### 4.1 Articulation Clarity Score

**Algorithm**: Composite metric from spectral and voicing characteristics

**Components**:
- **Spectral contrast**: ratio of high-freq to low-freq energy (clear consonants have more HF)
- **Voicing transitions**: clear speech has crisp V/UV transitions

**Implementation**:
```
Input: Spectrum, voicing state, HNR
Output: Clarity score (0-100)

Metrics:
- consonant_clarity: spectral_slope during unvoiced (steeper = crisper)
- transition_sharpness: rate of voicing state changes
- overall_clarity: weighted combination
```

**New file**: `src/HotMic.Core/Dsp/Analysis/Speech/ClarityAnalyzer.cs`

**Reuses**:
- HNR (from HarmonicCombEnhancer)
- Spectral slope (from SpectralFeatureExtractor)
- Voicing transitions

### 4.2 Intelligibility Indicators

**Simplified STI-inspired metric** (not full STI, too complex for real-time):

**Implementation**:
```
Input: Modulation spectrum of energy envelope
Output: Modulation integrity score

Algorithm:
1. Compute energy envelope (RMS per frame)
2. Compute modulation spectrum via FFT of envelope (1-16 Hz range)
3. Measure modulation depth at speech-relevant frequencies (2-8 Hz)
4. Higher modulation depth = better intelligibility

This approximates STI's core insight: speech intelligibility depends on
preserving amplitude modulations in the 2-8 Hz range (syllable rate).
```

---

## Phase 5: UI & Display

### 5.1 New Overlay: Speech Metrics Panel

**Display elements**:
```
┌─────────────────────────────────────┐
│ SPEECH COACH                        │
├─────────────────────────────────────┤
│ Rate:  145 syl/min  [====----]      │  ← Bar: 100-200 range, green zone 130-170
│ Artic: 162 syl/min  [=====---]      │
│ Pause: 18%          [==------]      │  ← Lower is more fluent
│ Pitch: ±4.2 st      [===-----]      │  ← Variation, monotone warning < 2st
│ Fillers: 2/min      [=-------]      │
│ Clarity: 78         [======--]      │
└─────────────────────────────────────┘
```

### 5.2 Overlay Markers on Spectrogram

- **Syllable ticks**: Small vertical marks at detected syllable onsets
- **Pause regions**: Shaded bands (gray for silent, orange for filled)
- **Filler highlights**: Red markers on timeline for detected fillers
- **Stress markers**: Accent marks above stressed syllables

### 5.3 New Parameters for UI

```csharp
// Speech Coach Display
public bool ShowSpeechMetricsPanel { get; set; }
public bool ShowSyllableMarkers { get; set; }
public bool ShowPauseOverlay { get; set; }
public bool ShowFillerMarkers { get; set; }
public bool ShowStressMarkers { get; set; }
```

---

## Implementation Order

| Phase | Feature | Est. Complexity | Dependencies |
|-------|---------|-----------------|--------------|
| 1a | SyllableDetector | Low | Frame energy, voicing |
| 1b | PauseDetector | Low | Voicing, pitch confidence |
| 1c | SpeechRateCalculator | Low | SyllableDetector, PauseDetector |
| 1d | Plugin integration | Medium | All Phase 1 |
| 2a | PitchContourAnalyzer | Low | Pitch detector results |
| 2b | StressDetector | Medium | SyllableDetector, PitchContour |
| 4a | ClarityAnalyzer | Low | HNR, spectral features |
| 4b | IntelligibilityScore | Medium | Energy envelope FFT |
| 5 | UI overlays | Medium | All analysis complete |

---

## File Structure

```
src/HotMic.Core/Dsp/Analysis/Speech/
├── SyllableDetector.cs         # Phase 1a
├── PauseDetector.cs            # Phase 1b
├── SpeechRateCalculator.cs     # Phase 1c
├── PitchContourAnalyzer.cs     # Phase 2a
├── StressDetector.cs           # Phase 2b
├── FillerDetector.cs           # Phase 3
├── ClarityAnalyzer.cs          # Phase 4a
└── IntelligibilityAnalyzer.cs  # Phase 4b
```

---

## Testing Strategy

Per project policy (AGENTS.md):
- **Math verification**: Test syllable detection against pre-computed reference from labeled audio
- **Pre-computed values**: Use Python/Praat to generate expected syllable counts, pause durations
- **No behavior tests**: Don't test that "rate should increase when speaking faster"
- **Deterministic inputs**: Use fixed audio samples with known characteristics

---

## Risk Areas

1. **Syllable detection accuracy**: May need tuning per voice type
2. **Filler vs word confusion**: "uh" in "umbrella" - need context
3. **Real-time latency**: Metrics need smoothing window, introduces display delay
4. **UI clutter**: Many overlays - need good defaults and grouping

---

## Open Questions

1. Should metrics be computed on separate thread from spectrogram analysis?
2. Target rates: Are 130-170 syl/min good defaults for all contexts?
3. Filler detection: Require ASR confirmation or pure acoustic sufficient?
4. Export: Should metrics be exportable to file for post-session review?

---

## References

- de Jong & Wempe (2009): Praat syllable nuclei detection
- Goto (1999): Real-time filled pause detection
- Speech Rate Meter: https://github.com/zhitko/speech-rate-meter
- PodcastFillers benchmark: https://arxiv.org/abs/2203.15135

---

## Implementation Status

### Completed (Phase 1-4)

**New Files Created:**
- `src/HotMic.Core/Dsp/Analysis/Speech/SyllableDetector.cs` - Energy peak detection with voicing filter
- `src/HotMic.Core/Dsp/Analysis/Speech/PauseDetector.cs` - Silent/filled pause state machine
- `src/HotMic.Core/Dsp/Analysis/Speech/SpeechRateCalculator.cs` - Windowed rate metrics
- `src/HotMic.Core/Dsp/Analysis/Speech/PitchContourAnalyzer.cs` - F0 statistics and monotone detection
- `src/HotMic.Core/Dsp/Analysis/Speech/StressDetector.cs` - Syllable stress classification
- `src/HotMic.Core/Dsp/Analysis/Speech/FillerDetector.cs` - Acoustic filler word detection
- `src/HotMic.Core/Dsp/Analysis/Speech/ClarityAnalyzer.cs` - HNR/spectral clarity metrics
- `src/HotMic.Core/Dsp/Analysis/Speech/IntelligibilityAnalyzer.cs` - Modulation-based intelligibility
- `src/HotMic.Core/Dsp/Analysis/Speech/SpeechCoach.cs` - Coordinator class

**Plugin Integration:**
- Added 6 new parameters: SpeechCoachEnabled, SpeechRateWindow, ShowSpeechMetrics, ShowSyllableMarkers, ShowPauseOverlay, ShowFillerMarkers
- Added 8 tracking buffers for per-frame metrics
- Integrated into WriteOverlayData analysis loop
- Added CopySpeechMetrics and GetCurrentSpeechMetrics buffer export methods

**Reused Infrastructure:**
- VoicingState enum from VoicingDetector
- Pitch detection (F0, confidence)
- Spectral features (flux, slope)
- HNR from HarmonicCombEnhancer
- Existing ring buffer copy patterns

### Completed (Phase 5 - UI)

**Window Changes:**
- Extended window width from 1440px to 1640px
- Added speech coach right sidebar panel (200px wide)
- Added buffer allocation for speech metrics tracking arrays
- Added click handlers for speech toggle buttons

**Renderer Changes:**
- Added `DrawSpeechPanel()` method rendering:
  - Toggle buttons: Coach, Stats, Syl, Pause, Filler
  - Metrics display with progress bars:
    - Rate (syllables/min)
    - Articulation rate
    - Pause ratio
    - Pitch variation (inverse of monotone)
    - Clarity score
    - Intelligibility score
- Added hit test handling for 5 speech toggles
- Extended `VocalSpectrographState` record with 13 new fields

**UI Entry Point:**
- "Speech" toggle in VIEW panel → Displays section enables the Speech Coach panel on the right side

**Parameters:**
- `SpeechCoachEnabled` - Master toggle (shows/hides right panel) - controlled by "Speech" in VIEW panel
- `ShowSpeechMetrics` - Show metrics bars in panel (Stats button)
- `ShowSyllableMarkers` - Toggle yellow tick marks at syllable onsets (Syl button)
- `ShowPauseOverlay` - Toggle shaded regions for silent (gray) and filled (orange) pauses (Pause button)
- `ShowFillerMarkers` - Toggle orange dot markers at center of detected filled pauses (Filler button)

**Spectrogram Overlays:**
- **Syllable markers**: Yellow vertical tick marks above voicing lane at detected syllable onsets
- **Pause regions**: Semi-transparent shading over spectrogram (gray for silent pauses, orange for filled pauses)
- **Filler markers**: Orange dots with outlines at the top of the spectrogram marking detected fillers
