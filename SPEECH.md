# Speech Prosody Visualization System Specification

## Document Information
- **Version**: 1.0
- **Target Platform**: Windows (.NET 8+ / C#)
- **Application Type**: Standalone Desktop Application
- **Purpose**: Real-time visualization of speech prosody for oration analysis and coaching

---

## 1. Overview

### 1.1 System Purpose
A real-time speech analysis and visualization tool focused on prosodic features relevant to public speaking and oration: pacing, pauses, intonation contour, emphasis patterns, and vocal energy. The system provides both timeline-based visualization for review and dashboard-based metrics for live feedback.

### 1.2 Design Goals
- Emphasize features relevant to speech delivery, not singing or music
- Clean, readable visualizations without spectral noise
- Real-time operation with minimal latency for live feedback
- Post-hoc review capability for recorded speech

### 1.3 Core Visualization Components
1. **Prosody Timeline**: Pitch contour + energy envelope over time
2. **Pace & Pause Display**: Speech segmentation with rate estimation
3. **Live Metrics Dashboard**: Real-time numeric feedback
4. **Spectral Balance Meter**: Simplified frequency band display for clarity assessment

---

## 2. Audio Input

### 2.1 Input Sources
- Microphone (real-time)
- Audio file (.wav, .mp3, .flac)
- System audio loopback

### 2.2 Audio Parameters
| Parameter | Value |
|-----------|-------|
| Sample Rate | 16000 Hz (sufficient for speech, reduces processing) |
| Bit Depth | 16-bit or 32-bit float |
| Channels | Mono (sum stereo if needed) |
| Frame Size | 20-30 ms (320-480 samples @ 16kHz) |
| Frame Hop | 10 ms (160 samples @ 16kHz) |

### 2.3 Preprocessing
| Step | Method | Parameters |
|------|--------|------------|
| DC Removal | Subtract running mean | Window: 1 second |
| High-Pass Filter | Butterworth 2nd order | Cutoff: 70 Hz |
| Pre-Emphasis | First-order difference | Coefficient: 0.97 |

---

## 3. Voice Activity Detection (VAD)

### 3.1 Purpose
Segment audio into speech and non-speech regions. Foundation for pause detection and speaking rate calculation.

### 3.2 Detection Method
Dual-threshold energy-based VAD with hangover.

### 3.3 Features for VAD
| Feature | Calculation | Purpose |
|---------|-------------|---------|
| Frame Energy | RMS of frame samples (dB) | Primary speech indicator |
| Zero-Crossing Rate | Sign changes per frame | Distinguish speech from noise |
| Spectral Centroid | Energy-weighted mean frequency | Voice vs. breath/noise |

### 3.4 VAD Parameters
| Parameter | Value | Description |
|-----------|-------|-------------|
| Energy High Threshold | Adaptive, 90th percentile | Definite speech |
| Energy Low Threshold | Adaptive, 70th percentile | Possible speech |
| Min Speech Duration | 100 ms | Minimum voiced segment |
| Min Pause Duration | 150 ms | Minimum silence to count as pause |
| Hangover Frames | 10 frames (100 ms) | Sustain speech state briefly |
| Noise Floor Adaptation | 10th percentile over 2 sec | Track ambient level |

### 3.5 VAD Output
Per-frame classification:
- **Speech**: Active voiced or unvoiced speech
- **Pause**: Silence between speech segments
- **Silence**: Extended non-speech (>2 seconds)

---

## 4. Pitch Contour Extraction

### 4.1 Purpose
Extract fundamental frequency over time for intonation analysis. Accuracy to ±5 Hz is sufficient; smoothness matters more than precision.

### 4.2 Algorithm
YIN or Autocorrelation-based pitch detection, optimized for speech range.

### 4.3 Parameters
| Parameter | Value | Description |
|-----------|-------|-------------|
| Min F0 | 60 Hz | Lowest expected pitch |
| Max F0 | 400 Hz | Highest expected pitch |
| Frame Size | 50 ms | Longer frame for stability |
| Hop Size | 10 ms | Smooth contour |
| Voicing Threshold | 0.3 | Confidence cutoff |

### 4.4 Post-Processing
| Step | Method | Purpose |
|------|--------|---------|
| Outlier Removal | Median filter (5 frames) | Remove octave jumps |
| Gap Interpolation | Linear interpolation | Bridge short unvoiced gaps (<100ms) |
| Smoothing | Savitzky-Golay filter (order 2, window 11) | Smooth contour for display |
| Pitch Range Normalization | Convert to semitones relative to speaker median | Normalize across speakers |

### 4.5 Pitch Contour Output
- Raw F0 (Hz) per frame
- Smoothed F0 (Hz) per frame
- Pitch in semitones relative to speaker baseline
- Voicing confidence per frame

---

## 5. Energy Envelope Extraction

### 5.1 Purpose
Track vocal intensity for emphasis detection and loudness feedback.

### 5.2 Calculation
| Metric | Formula | Use |
|--------|---------|-----|
| RMS Energy | √(mean(samples²)) | Raw amplitude |
| dB SPL (relative) | 20 × log10(RMS / reference) | Perceptual loudness |
| Loudness (approx.) | A-weighted energy | Perceived volume |

### 5.3 Parameters
| Parameter | Value | Description |
|-----------|-------|-------------|
| Frame Size | 30 ms | Match VAD frames |
| Smoothing | EMA, α = 0.3 | Reduce flicker |
| Reference Level | Running 10th percentile | Normalize to quiet baseline |

### 5.4 Emphasis Detection
Mark frames as "emphasized" when:
- Energy exceeds local mean (500ms window) by > 6 dB
- Coincides with speech segment (not pause)

### 5.5 Energy Output
- Raw energy (dB) per frame
- Smoothed energy (dB) per frame
- Binary emphasis flags
- Running statistics (mean, variance, range)

---

## 6. Speaking Rate Estimation

### 6.1 Purpose
Estimate syllables per second or words per minute for pacing feedback.

### 6.2 Method: Syllable Nuclei Detection
Syllable nuclei correspond to energy peaks in the 300-2000 Hz band (vowel formant region). Count peaks to estimate syllable rate.

### 6.3 Algorithm Steps
1. Bandpass filter audio: 300-2000 Hz (2nd order Butterworth)
2. Compute energy envelope of filtered signal
3. Smooth envelope (EMA, α = 0.1)
4. Detect peaks with minimum prominence and minimum separation
5. Count peaks per unit time

### 6.4 Parameters
| Parameter | Value | Description |
|-----------|-------|-------------|
| Bandpass Low | 300 Hz | Below F1 range |
| Bandpass High | 2000 Hz | Above F2 range |
| Min Peak Prominence | 3 dB | Avoid noise peaks |
| Min Peak Separation | 80 ms | Physical limit ~12 syl/sec |
| Rate Window | 3-5 seconds | Local rate calculation |

### 6.5 Rate Conversion
| Measure | Conversion |
|---------|------------|
| Syllables/second | Direct from peak count / window duration |
| Words/minute | Syllables/second × 60 / 1.5 (avg syllables per English word) |

### 6.6 Speaking Rate Output
- Syllables per second (instantaneous, windowed)
- Words per minute (estimated)
- Running average rate
- Rate variance (indicator of pacing consistency)

---

## 7. Pause Analysis

### 7.1 Purpose
Classify pauses by duration and track pause patterns.

### 7.2 Pause Classification
| Category | Duration | Interpretation |
|----------|----------|----------------|
| Micro-pause | 150-300 ms | Breath, natural juncture |
| Short pause | 300-700 ms | Clause boundary, mild emphasis |
| Medium pause | 700-1500 ms | Sentence boundary, thought transition |
| Long pause | >1500 ms | Dramatic pause, topic shift, hesitation |

### 7.3 Pause Metrics
| Metric | Calculation |
|--------|-------------|
| Pause Count | Number of pauses per category |
| Pause Rate | Pauses per minute of speech |
| Pause Ratio | Total pause time / total duration |
| Mean Pause Duration | Average across all pauses |

### 7.4 Pause Output
- List of pause events with start time, end time, duration, category
- Aggregate statistics per analysis window
- Pause ratio trend over time

---

## 8. Pitch Variation Analysis

### 8.1 Purpose
Quantify intonation variety to detect monotone delivery.

### 8.2 Metrics
| Metric | Calculation | Interpretation |
|--------|-------------|----------------|
| Pitch Range | Max - Min (semitones) over window | Total variation |
| Pitch Standard Deviation | σ of F0 in semitones | Overall variability |
| Pitch Slope Variance | Variance of frame-to-frame Δpitch | Contour dynamism |
| Directional Changes | Count of pitch direction reversals | Melodic complexity |

### 8.3 Parameters
| Parameter | Value |
|-----------|-------|
| Analysis Window | 10-30 seconds |
| Monotone Threshold | σ < 2 semitones |
| Good Variety Threshold | σ > 4 semitones |

### 8.4 Intonation Pattern Detection
Detect terminal pitch patterns in phrases:
| Pattern | Definition |
|---------|------------|
| Falling | Final 200ms pitch drops > 3 semitones |
| Rising | Final 200ms pitch rises > 3 semitones |
| Flat | Final 200ms pitch change < 1 semitone |

---

## 9. Spectral Balance (Clarity Meter)

### 9.1 Purpose
Simplified frequency band display for vocal clarity and projection assessment. Not a full spectrogram—just 4 bands.

### 9.2 Frequency Bands
| Band | Range | Label | Indicates |
|------|-------|-------|-----------|
| Low | 80-300 Hz | Warmth | Chest resonance, room rumble |
| Mid | 300-1000 Hz | Body | Main vocal energy, vowel weight |
| Presence | 1000-4000 Hz | Clarity | Intelligibility, consonant definition |
| High | 4000-8000 Hz | Air | Sibilance, breathiness, crispness |

### 9.3 Calculation
For each band:
1. Apply bandpass filter (or sum FFT bins in range)
2. Compute RMS energy
3. Convert to dB relative to total energy
4. Smooth with EMA (α = 0.3)

### 9.4 Clarity Ratio
`Clarity = Presence Band Energy / (Low Band Energy + Mid Band Energy)`

Higher ratio indicates more projected, intelligible speech.

### 9.5 Output
- Per-band energy levels (dB)
- Clarity ratio
- Band balance visualization (4-bar meter or stacked display)

---

## 10. Visualization Components

### 10.1 Prosody Timeline View
Primary analysis view showing pitch and energy over time.

**Layout**:
```
┌─────────────────────────────────────────────────────────────┐
│ PITCH CONTOUR                                               │
│   12st ┤                                                    │
│    6st ┤    ╭──╮       ╭─────╮                             │
│    0st ┤───╯    ╰─────╯       ╰────╮    ╭──                │
│   -6st ┤                            ╰──╯                    │
│  -12st ┤                                                    │
├─────────────────────────────────────────────────────────────┤
│ ENERGY + SPEECH SEGMENTS                                    │
│        │█▓▓░░██▓▓░░░░░░░█████▓▓░░░░░░███▓░░░               │
│        │────────│ pause │───────│pause│──────               │
│        │ 165wpm │       │180wpm │     │150wpm               │
├─────────────────────────────────────────────────────────────┤
│ TIMELINE                                                    │
│ 0:00        0:10        0:20        0:30        0:40       │
└─────────────────────────────────────────────────────────────┘
```

**Elements**:
| Element | Display |
|---------|---------|
| Pitch Contour | Smoothed line, Y-axis in semitones from speaker median |
| Pitch Confidence | Line thickness or opacity based on voicing confidence |
| Energy Envelope | Filled area or bar height |
| Speech/Pause Segments | Background shading (speech=filled, pause=empty) |
| Emphasis Markers | Highlighted regions where energy > threshold |
| Local Speaking Rate | Numeric labels per phrase segment |
| Pause Duration | Labels on pause regions |

**Interaction**:
- Horizontal scroll for long recordings
- Zoom in/out on time axis
- Click to seek playback position
- Hover for exact values

### 10.2 Pace & Pause Strip
Compact overview of entire speech rhythm.

**Layout**:
```
┌──────────────────────────────────────────────────────────────┐
│ ██░██████░░░████████░░░░░░██████░░████████████░░░░░░░░██████│
│ 0:00              1:00              2:00              3:00   │
└──────────────────────────────────────────────────────────────┘
```

**Encoding**:
- Filled = speech, empty = pause
- Color intensity = energy level
- Height = speaking rate (optional)

### 10.3 Live Metrics Dashboard
Real-time numeric display for practice sessions.

**Layout**:
```
┌──────────────────────────────────────────────────────────┐
│  SPEAKING RATE                                           │
│  ░░░░░░░░░░░▓▓▓▓▓▓▓▓▓▓░░░░░░░░░░░░                      │
│  100       140 ▲ 160       200       240  wpm           │
│            │target│                                      │
│                                                          │
│  Current: 172 wpm    Average: 165 wpm    Target: 150    │
├──────────────────────────────────────────────────────────┤
│  PITCH VARIATION              VOLUME                     │
│  ▓▓▓▓▓▓▓▓░░░░░░              ████████████░░░░           │
│  Monotone │ Varied            Quiet │ Projected         │
│                                                          │
│  Range: 8 st   σ: 3.2 st      Level: -12 dB             │
├──────────────────────────────────────────────────────────┤
│  PAUSE STATS (last 60s)       CLARITY                    │
│  Count: 12                     ▓▓▓▓▓▓▓▓▓░░░░░           │
│  Avg: 0.4s                     Muffled │ Clear          │
│  Ratio: 18%                                              │
├──────────────────────────────────────────────────────────┤
│  SPECTRAL BALANCE                                        │
│  Warmth  ████████░░░░                                    │
│  Body    ██████████████                                  │
│  Clarity ████████████░░                                  │
│  Air     ████░░░░░░░░░░                                  │
└──────────────────────────────────────────────────────────┘
```

**Update Rate**: 10 Hz (100ms) for meters, 1 Hz for statistics

### 10.4 Pitch Contour Mini-View
Rolling window of recent pitch for live feedback.

**Layout**:
```
┌─────────────────────────────────────────┐
│     ╭──╮          ╭───╮                 │
│ ───╯    ╰────────╯     ╰───             │
│                              (10 sec)   │
└─────────────────────────────────────────┘
```

**Parameters**:
| Parameter | Value |
|-----------|-------|
| Window Duration | 10 seconds |
| Scroll Mode | Continuous rightward scroll |
| Y-Axis | ±12 semitones from median |

---

## 11. Configuration & Presets

### 11.1 Target Rate Settings
| Preset | Target WPM | Tolerance |
|--------|------------|-----------|
| Conversational | 150-170 | ±20 |
| Presentation | 130-150 | ±15 |
| Formal Oration | 110-130 | ±10 |
| Narration | 140-160 | ±15 |
| Custom | User-defined | User-defined |

### 11.2 Display Preferences
| Setting | Options |
|---------|---------|
| Pitch Display | Hertz / Semitones / Both |
| Rate Display | WPM / Syllables per second |
| Color Theme | Light / Dark / High Contrast |
| Timeline Scale | 10s / 30s / 60s / Full |

### 11.3 Analysis Sensitivity
| Setting | Options | Effect |
|---------|---------|--------|
| VAD Sensitivity | Low / Medium / High | Pause detection threshold |
| Pitch Smoothing | None / Light / Heavy | Contour detail vs. stability |
| Emphasis Threshold | 3 / 6 / 9 dB | What counts as emphasized |

---

## 12. Data Export

### 12.1 Export Formats
| Format | Contents |
|--------|----------|
| CSV | Time-stamped features (F0, energy, rate, pauses) |
| JSON | Full analysis with metadata and statistics |
| PNG/SVG | Timeline visualization image |

### 12.2 CSV Schema
```
timestamp_ms, is_speech, pitch_hz, pitch_semitones, energy_db, speaking_rate_wpm, is_emphasis, pause_duration_ms
0, 1, 142.5, 0.0, -18.2, 155, 0, 0
10, 1, 145.2, 0.3, -16.1, 158, 0, 0
20, 1, 148.0, 0.6, -12.5, 160, 1, 0
...
500, 0, 0, 0, -45.0, 0, 0, 320
...
```

### 12.3 Summary Statistics Export
| Statistic | Description |
|-----------|-------------|
| Total Duration | Recording length |
| Speech Duration | Time spent speaking |
| Pause Duration | Total pause time |
| Pause Ratio | Pause / Total |
| Mean Speaking Rate | WPM average |
| Rate Variance | Pacing consistency |
| Pitch Mean | Average F0 |
| Pitch Range | Min to max (semitones) |
| Pitch Variance | Intonation variety |
| Emphasis Count | Number of emphasized regions |
| Pause Count by Category | Micro / Short / Medium / Long |

---

## 13. Performance Requirements

### 13.1 Latency Targets
| Component | Target | Maximum |
|-----------|--------|---------|
| Audio to VAD | 30 ms | 50 ms |
| Audio to Pitch | 60 ms | 100 ms |
| Audio to Display | 100 ms | 150 ms |
| Dashboard Update | 100 ms | 200 ms |

### 13.2 Resource Targets
| Metric | Target |
|--------|--------|
| CPU Usage | < 15% |
| Memory | < 150 MB |
| Frame Rate | 30 fps for timeline, 10 Hz for meters |

### 13.3 Processing Architecture
```
Audio Input
     │
     ▼
┌─────────────┐
│ Preprocess  │ DC removal, HPF, pre-emphasis
└──────┬──────┘
       │
       ├──────────────┬──────────────┬──────────────┐
       ▼              ▼              ▼              ▼
┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐
│    VAD      │ │   Pitch     │ │   Energy    │ │  Spectral   │
│  Detection  │ │  Extraction │ │  Envelope   │ │  Balance    │
└──────┬──────┘ └──────┬──────┘ └──────┬──────┘ └──────┬──────┘
       │               │               │               │
       └───────────────┴───────────────┴───────────────┘
                              │
                              ▼
                    ┌─────────────────┐
                    │   Integration   │ Speaking rate, pause analysis,
                    │   & Statistics  │ emphasis detection
                    └────────┬────────┘
                             │
              ┌──────────────┴──────────────┐
              ▼                             ▼
     ┌─────────────────┐          ┌─────────────────┐
     │ Timeline View   │          │ Dashboard View  │
     └─────────────────┘          └─────────────────┘
```

---

## 14. Implementation Dependencies

### 14.1 Recommended Libraries
| Library | Purpose |
|---------|---------|
| NAudio | Audio input/output |
| MathNet.Numerics | DSP, filtering, statistics |
| SkiaSharp | Visualization rendering |

### 14.2 Key Algorithms to Implement
| Algorithm | Purpose | Reference |
|-----------|---------|-----------|
| YIN | Pitch detection | de Cheveigné & Kawahara (2002) |
| Savitzky-Golay | Pitch smoothing | Standard signal processing |
| Butterworth IIR | Bandpass filters | Standard signal processing |
| Peak Detection | Syllable nuclei | Local maxima with prominence |
| Adaptive Thresholding | VAD | Percentile-based |

---

## Appendix A: Speech Rate Reference

| Context | Typical WPM |
|---------|-------------|
| Slow, deliberate speech | 100-120 |
| Formal presentation | 120-150 |
| Conversational | 150-170 |
| Excited/rapid speech | 170-200 |
| Auctioneering | 200-400 |

## Appendix B: Pitch Reference

| Speaker Type | Typical F0 Range |
|--------------|------------------|
| Male | 85-180 Hz |
| Female | 165-255 Hz |
| Child | 250-400 Hz |

| Interval | Semitones | Perceptual |
|----------|-----------|------------|
| Minor 2nd | 1 | Barely noticeable |
| Major 2nd | 2 | Small step |
| Minor 3rd | 3 | Noticeable change |
| Perfect 4th | 5 | Clear interval |
| Octave | 12 | Dramatic shift |
