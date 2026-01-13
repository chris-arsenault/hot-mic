# Vocal Spectrograph DSP Specification

## Document Information
- **Version**: 1.0
- **Target Platform**: Windows (.NET 8+ / C#)
- **Application Type**: Standalone Desktop Application (NOT VST)

---

## 1. Overview

### 1.1 Purpose
A real-time spectrograph application optimized exclusively for analyzing, visualizing, and highlighting human vocal frequencies. The application emphasizes clarity in the vocal spectrum while providing specialized tools for voice analysis, formant tracking, and harmonic visualization.

### 1.2 Design Philosophy
- **Vocal-First**: All frequency ranges, resolutions, and visual mappings are optimized for the human voice (approximately 80 Hz – 8 kHz primary range)
- **Clarity Over Breadth**: Rather than displaying the full audible spectrum, the display emphasizes vocal-relevant frequencies with enhanced resolution
- **Real-Time Performance**: Achieve ≤20ms latency from audio input to visual update
- **Musically Informed**: Options tailored to singing, speech, and vocal production analysis

---

## 2. Vocal Frequency Reference

### 2.1 Fundamental Frequency Ranges
| Voice Type | Approximate F0 Range |
|------------|---------------------|
| Bass | 80 – 350 Hz |
| Baritone | 95 – 400 Hz |
| Tenor | 120 – 500 Hz |
| Alto | 160 – 700 Hz |
| Mezzo-Soprano | 180 – 800 Hz |
| Soprano | 250 – 1100 Hz |

### 2.2 Formant Frequencies (Average Adult)
| Formant | Description | Typical Range |
|---------|-------------|---------------|
| F1 | First formant (jaw openness) | 250 – 1000 Hz |
| F2 | Second formant (tongue position) | 700 – 2500 Hz |
| F3 | Third formant (voice quality) | 1800 – 3500 Hz |
| F4 | Fourth formant | 3000 – 4500 Hz |
| F5 | Fifth formant | 4000 – 6000 Hz |

### 2.3 Vocal Frequency Zones
| Zone | Range | Significance |
|------|-------|--------------|
| Sub-Bass Rumble | < 80 Hz | Plosives, breath noise, filtering candidate |
| Fundamental Zone | 80 – 500 Hz | Pitch fundamentals, body |
| Lower Harmonics | 500 – 2000 Hz | Warmth, vowel definition |
| Presence Zone | 2000 – 4000 Hz | Intelligibility, clarity, sibilance |
| Brilliance Zone | 4000 – 8000 Hz | Air, brightness, consonant detail |
| Extended Air | 8000 – 12000 Hz | Breathiness, high harmonics |

---

## 3. Audio Input Architecture

### 3.1 Supported Input Sources
```
┌─────────────────────────────────────────────────────┐
│                  INPUT SOURCES                       │
├─────────────────────────────────────────────────────┤
│  • System Audio Device (via WASAPI/NAudio)          │
│  • Microphone Input (real-time)                     │
│  • Audio File Playback (.wav, .mp3, .flac, .ogg)   │
│  • Audio Stream (loopback capture)                  │
└─────────────────────────────────────────────────────┘
```

### 3.2 Audio Engine Requirements
| Parameter | Specification |
|-----------|--------------|
| Sample Rates | 44100 Hz, 48000 Hz (primary), 96000 Hz |
| Bit Depth | 16-bit, 24-bit, 32-bit float |
| Channels | Mono (summed), Stereo (selectable L/R/Sum) |
| Buffer Size | 256 – 2048 samples (user configurable) |
| Recommended Library | NAudio 2.x or CSCore |

### 3.3 Input Processing Chain
```
Audio Source
    │
    ▼
┌──────────────────┐
│  DC Offset       │  Remove DC bias
│  Removal         │
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  High-Pass       │  Optional: Remove sub-vocal rumble
│  Filter (≤60Hz)  │  (Butterworth 2nd order)
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  Pre-Emphasis    │  Optional: +6dB/octave above 1kHz
│  Filter          │  (Improves high-frequency formant visibility)
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  Frame Buffer    │  Collect samples for FFT
│  (Ring Buffer)   │
└────────┬─────────┘
         │
         ▼
    DSP Pipeline
```

---

## 4. DSP Processing Pipeline

### 4.1 Core FFT Configuration

#### 4.1.1 FFT Size Options (Vocal-Optimized)
| FFT Size | Freq Resolution @ 48kHz | Time Resolution | Use Case |
|----------|------------------------|-----------------|----------|
| 1024 | 46.9 Hz | 21.3 ms | Fast transients, consonants |
| 2048 | 23.4 Hz | 42.7 ms | **Default - balanced** |
| 4096 | 11.7 Hz | 85.3 ms | Pitch accuracy, sustained notes |
| 8192 | 5.9 Hz | 170.7 ms | Fine harmonic analysis |

**Recommendation**: Default to 2048 with option for adaptive sizing based on detected vocal activity.

#### 4.1.2 Window Functions
| Window | Main Lobe Width | Side Lobe Level | Vocal Application |
|--------|-----------------|-----------------|-------------------|
| Hann | 1.50 bins | -31.5 dB | **Default** - good all-around |
| Hamming | 1.36 bins | -42.7 dB | Better frequency isolation |
| Blackman-Harris | 1.90 bins | -92 dB | Clean harmonic separation |
| Gaussian (σ=0.4) | 1.55 bins | -55 dB | Smooth formant tracking |
| Kaiser (β=9) | Variable | -90 dB | Configurable precision |

**Recommendation**: Hann as default, Blackman-Harris for detailed harmonic analysis mode.

#### 4.1.3 Overlap Configuration
| Overlap % | Hop Size @ 2048 FFT | Temporal Smoothness | CPU Load |
|-----------|---------------------|---------------------|----------|
| 50% | 1024 samples | Good | Low |
| 75% | 512 samples | **Better (default)** | Medium |
| 87.5% | 256 samples | Excellent | High |

### 4.2 Frequency Scale Options

#### 4.2.1 Linear Scale
- Standard linear Hz mapping
- Best for: Technical analysis, exact frequency measurement
- Display Range: 50 Hz – 8000 Hz (vocal-focused crop)

#### 4.2.2 Logarithmic Scale
- Perceptually uniform spacing
- Best for: Musical pitch relationships, octave visualization
- Base: 2 (octave-based) or 10

#### 4.2.3 Mel Scale (Recommended Default)
```
mel = 2595 * log10(1 + f/700)
```
- Matches human pitch perception
- Best for: Voice analysis, formant visibility
- Enhanced resolution in vocal fundamental range

#### 4.2.4 ERB (Equivalent Rectangular Bandwidth) Scale
```
ERB(f) = 24.7 * (4.37 * f/1000 + 1)
```
- Models auditory filter bandwidth
- Best for: Critical band analysis, masking visualization

#### 4.2.5 Bark Scale
- Psychoacoustic critical band scale
- 24 critical bands
- Best for: Perceptual loudness analysis

### 4.3 Magnitude Scaling

#### 4.3.1 Decibel Conversion
```csharp
float magnitudeDb = 20 * Math.Log10(magnitude + 1e-10f);
```

#### 4.3.2 Dynamic Range Options
| Mode | Range | Use Case |
|------|-------|----------|
| Full Dynamic | -120 dB to 0 dB | Technical analysis |
| Voice Optimized | -80 dB to 0 dB | **Default** |
| Compressed | -60 dB to 0 dB | Quiet recordings |
| Noise Floor Aware | Adaptive | Auto-adjusting |

#### 4.3.3 Normalization Modes
- **Peak**: Normalize to maximum magnitude in frame
- **RMS**: Normalize to frame RMS level
- **A-Weighted**: Apply A-weighting curve for perceptual loudness
- **None**: Raw magnitude values

---

## 5. Vocal-Specific Analysis Algorithms

### 5.1 Pitch Detection

#### 5.1.1 Algorithm Options
| Algorithm | Accuracy | Latency | Best For |
|-----------|----------|---------|----------|
| YIN | High | Medium | **Default** - robust for voice |
| pYIN (Probabilistic) | Very High | Medium-High | Uncertain pitch regions |
| Autocorrelation | Medium | Low | Fast tracking |
| Cepstral (CEPS) | Medium | Low | Harmonic voices |
| SWIPE' | Very High | High | Precision analysis |

#### 5.1.2 YIN Implementation Parameters
```csharp
public class YinConfig
{
    public float Threshold = 0.15f;        // Aperiodicity threshold
    public int MinFrequency = 60;          // Hz - lowest detectable
    public int MaxFrequency = 1200;        // Hz - highest detectable
    public int FrameSize = 2048;           // Samples
    public int HopSize = 512;              // Samples
}
```

#### 5.1.3 Pitch Display Options
- Overlay fundamental frequency line on spectrogram
- Separate pitch track lane
- Piano roll reference grid
- Cents deviation from nearest note

### 5.2 Formant Tracking

#### 5.2.1 LPC (Linear Predictive Coding) Method
```csharp
public class LpcFormantConfig
{
    public int LpcOrder = 12;              // Coefficients (rule: SampleRate/1000 + 4)
    public int MaxFormants = 5;            // Number to track
    public float PreEmphasis = 0.97f;      // High-frequency boost
    public int FrameSize = 1024;           // Analysis window
}
```

#### 5.2.2 Formant Detection Pipeline
```
Audio Frame
    │
    ▼
┌──────────────────┐
│  Pre-Emphasis    │
│  y[n] = x[n] -   │
│  α*x[n-1]        │
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  LPC Analysis    │
│  (Levinson-      │
│  Durbin)         │
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  Root Finding    │
│  (Polynomial)    │
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  Formant         │
│  Selection &     │
│  Tracking        │
└────────┬─────────┘
         │
         ▼
    F1, F2, F3...
```

#### 5.2.3 Formant Display Options
- Colored overlay dots/lines on spectrogram
- Formant trajectory traces
- Vowel space plot (F1 vs F2)
- Formant bandwidth indicators

### 5.3 Voiced/Unvoiced Detection

#### 5.3.1 Detection Criteria
| Feature | Voiced | Unvoiced |
|---------|--------|----------|
| Zero-Crossing Rate | Low (< 50/frame) | High (> 100/frame) |
| Energy | Higher | Lower |
| Autocorrelation Peak | Strong | Weak |
| Spectral Flatness | Low (harmonic) | High (noisy) |

#### 5.3.2 Implementation
```csharp
public class VoicingDetector
{
    public float ZcrThreshold = 0.1f;           // Normalized
    public float EnergyThreshold = -40f;        // dB
    public float AutocorrThreshold = 0.3f;      // Peak ratio
    public float SpectralFlatnessThreshold = 0.5f;
    
    public VoicingState Detect(float[] frame);  // Returns Voiced, Unvoiced, Silence
}
```

#### 5.3.3 Display Integration
- Color-code spectrogram regions (e.g., blue=voiced, gray=unvoiced, black=silence)
- Separate indicator lane
- Opacity modulation

### 5.4 Harmonic Analysis

#### 5.4.1 Harmonic Peak Detection
```csharp
public class HarmonicAnalyzer
{
    public int MaxHarmonics = 20;
    public float PeakThreshold = -40f;         // dB below max
    public float HarmonicTolerance = 0.03f;    // ±3% of expected frequency
    
    public HarmonicSeries Analyze(float[] spectrum, float fundamentalFreq);
}
```

#### 5.4.2 Harmonic-to-Noise Ratio (HNR)
- Measure of voice quality/breathiness
- Display as numeric value or meter
- Typical healthy voice: 20+ dB

#### 5.4.3 Cepstral Peak Prominence (CPP)
- Voice quality metric
- Correlates with perceived breathiness
- Display alongside spectrogram

### 5.5 Spectral Features for Voice Quality

#### 5.5.1 Spectral Centroid
```csharp
float centroid = Σ(f[i] * mag[i]) / Σ(mag[i])
```
- Indicates "brightness" of voice
- Track over time for timbral changes

#### 5.5.2 Spectral Slope
- Measure of spectral tilt
- Relates to vocal effort/tension
- Calculated via linear regression on dB spectrum

#### 5.5.3 Spectral Flux
- Frame-to-frame spectral change
- Highlights consonants, transitions
- Useful for segmentation

---

## 6. Visualization Specification

### 6.1 Main Spectrogram Display

#### 6.1.1 Rendering Architecture
```
┌─────────────────────────────────────────────────────────────┐
│                    SPECTROGRAM VIEW                          │
│  ┌────────┬─────────────────────────────────────┬────────┐  │
│  │        │                                     │        │  │
│  │  Freq  │     Main Spectrogram Canvas         │ Scale  │  │
│  │  Axis  │     (GPU-accelerated bitmap)        │  Bar   │  │
│  │  (Hz/  │                                     │ (dB)   │  │
│  │  Note) │                                     │        │  │
│  │        │                                     │        │  │
│  ├────────┼─────────────────────────────────────┼────────┤  │
│  │        │         Time Axis                   │        │  │
│  └────────┴─────────────────────────────────────┴────────┘  │
└─────────────────────────────────────────────────────────────┘
```

#### 6.1.2 Display Parameters
| Parameter | Default | Range | Description |
|-----------|---------|-------|-------------|
| Width | 1200 px | 400-4000 | Horizontal pixels |
| Height | 600 px | 200-2000 | Vertical pixels |
| Time Window | 5 sec | 1-60 sec | Visible duration |
| Scroll Mode | Continuous | Fixed/Scroll | Update behavior |
| Frame Rate | 60 fps | 30-144 | Render rate |

#### 6.1.3 Color Maps (Vocal-Optimized)
```csharp
public enum SpectrogramColorMap
{
    Vocal,          // Custom: Black → Purple → Blue → Cyan → Yellow → White
                    // Enhanced contrast in formant regions
    
    VocalWarm,      // Custom: Black → Brown → Orange → Yellow → White
                    // Warm aesthetic, good for presentations
    
    Grayscale,      // Black → White
                    // Classic, print-friendly
    
    Inferno,        // Perceptually uniform (Matplotlib)
    Viridis,        // Perceptually uniform, colorblind-safe
    Magma,          // Perceptually uniform, dark background
    
    Custom          // User-defined gradient
}
```

#### 6.1.4 Vocal Color Map Definition
```csharp
public static Color[] VocalColorMap = new Color[]
{
    // Index 0-255 mapped from min to max dB
    Color.FromArgb(0, 0, 0),          // -80 dB: Black (silence)
    Color.FromArgb(30, 0, 50),        // -70 dB: Deep purple
    Color.FromArgb(60, 0, 120),       // -60 dB: Purple
    Color.FromArgb(0, 50, 180),       // -50 dB: Blue
    Color.FromArgb(0, 150, 200),      // -40 dB: Cyan
    Color.FromArgb(50, 200, 150),     // -30 dB: Teal
    Color.FromArgb(200, 230, 50),     // -20 dB: Yellow-green
    Color.FromArgb(255, 200, 0),      // -10 dB: Orange
    Color.FromArgb(255, 255, 200),    // 0 dB: Bright yellow-white
};
```

### 6.2 Frequency Axis Options

#### 6.2.1 Scale Labels
| Mode | Labels |
|------|--------|
| Hz | 100, 200, 500, 1k, 2k, 4k, 8k |
| Note | C2, C3, C4, C5, C6 |
| Both | C3 (130Hz), C4 (262Hz), ... |

#### 6.2.2 Vocal Range Markers
- Highlight fundamental range for selected voice type
- Optional formant zone indicators
- Reference grid at vocal landmark frequencies

### 6.3 Overlay Layers

#### 6.3.1 Layer System
```csharp
public class OverlayManager
{
    public bool ShowPitchTrack { get; set; } = true;
    public bool ShowFormants { get; set; } = true;
    public bool ShowHarmonics { get; set; } = false;
    public bool ShowVoicingIndicator { get; set; } = true;
    public bool ShowFrequencyGuides { get; set; } = false;
    
    public float OverlayOpacity { get; set; } = 0.8f;
    public int PitchLineThickness { get; set; } = 2;
    public int FormantDotSize { get; set; } = 6;
}
```

#### 6.3.2 Pitch Track Overlay
- Color: Configurable (default: bright green #00FF88)
- Style: Solid line, dotted during uncertain regions
- Confidence shading: Opacity based on detection confidence

#### 6.3.3 Formant Overlay
- F1: Red dots/line
- F2: Orange dots/line
- F3: Yellow dots/line
- F4: Green dots/line (optional)
- F5: Cyan dots/line (optional)
- Connected lines showing trajectory

### 6.4 Auxiliary Displays

#### 6.4.1 Waveform View
```
┌─────────────────────────────────────────────┐
│  ╭──╮    ╭───╮  ╭─╮    ╭──────╮            │
│──╯  ╰────╯   ╰──╯ ╰────╯      ╰────────────│
│                                             │
│  [Time-aligned with spectrogram]            │
└─────────────────────────────────────────────┘
```
- Height: 80-150 px
- Position: Above or below spectrogram
- Features: Zero-crossing markers, envelope overlay

#### 6.4.2 Spectrum Slice View
```
┌──────────────────────────────────────┐
│     │    ╱╲                          │
│  dB │   ╱  ╲    ╱╲                   │
│     │  ╱    ╲  ╱  ╲   ╱╲            │
│     │ ╱      ╲╱    ╲─╱  ╲───────    │
│     └────────────────────────────    │
│           Frequency (Hz)             │
└──────────────────────────────────────┘
```
- Real-time spectrum at current playhead
- Peak markers with frequency/note labels
- Formant peaks highlighted

#### 6.4.3 Pitch Meter
```
┌───────────────────────┐
│     ♩ A4  440.0 Hz    │
│   ══════════════════  │
│   -50¢    0    +50¢   │
│         ▲             │
└───────────────────────┘
```
- Current detected pitch
- Nearest note name
- Cents deviation indicator

#### 6.4.4 Vowel Space Plot
```
┌─────────────────────────┐
│  F1     i       u       │
│  (Hz)     ╲   ╱         │
│            ╲ ╱          │
│  ───────────●───────    │
│            ╱ ╲          │
│           æ   ɑ         │
│         F2 (Hz)         │
└─────────────────────────┘
```
- Real-time F1 vs F2 plot
- Reference vowel positions
- Trajectory trail

---

## 7. User Configuration

### 7.1 Presets System

#### 7.1.1 Built-in Presets
```csharp
public enum VocalPreset
{
    // Voice Type Presets
    SpeechMale,         // Optimized for male speech analysis
    SpeechFemale,       // Optimized for female speech analysis
    SingingClassical,   // Wide dynamic range, fine pitch resolution
    SingingContemporary,// Emphasis on presence/brightness zones
    VoiceoverAnalysis,  // Focus on clarity and intelligibility
    
    // Analysis Mode Presets
    PitchTracking,      // High time resolution, pitch overlay prominent
    FormantAnalysis,    // LPC enabled, formant overlays active
    HarmonicDetail,     // Large FFT, harmonic overlay
    TransientCapture,   // Small FFT, fast update rate
    
    // Display Presets
    Presentation,       // High contrast, large text
    Technical,          // All data visible, compact
    Minimal             // Spectrogram only, clean
}
```

#### 7.1.2 Preset Data Structure
```csharp
public class SpectrogramPreset
{
    public string Name { get; set; }
    public int FftSize { get; set; }
    public WindowFunction Window { get; set; }
    public float OverlapPercent { get; set; }
    public FrequencyScale FreqScale { get; set; }
    public float MinFrequency { get; set; }
    public float MaxFrequency { get; set; }
    public float MinDb { get; set; }
    public float MaxDb { get; set; }
    public SpectrogramColorMap ColorMap { get; set; }
    public OverlaySettings Overlays { get; set; }
    public PitchDetectorType PitchAlgorithm { get; set; }
    public bool FormantTrackingEnabled { get; set; }
    public int LpcOrder { get; set; }
}
```

### 7.2 Real-Time Adjustable Parameters

#### 7.2.1 Quick Controls (Always Visible)
| Control | Type | Range |
|---------|------|-------|
| FFT Size | Dropdown | 1024, 2048, 4096, 8192 |
| Time Zoom | Slider | 1-60 seconds |
| Frequency Range | Dual Slider | 20-20000 Hz |
| Dynamic Range | Dual Slider | -120 to 0 dB |
| Brightness | Slider | 0.5x - 2.0x |
| Contrast | Slider | 0.5x - 2.0x |

#### 7.2.2 Keyboard Shortcuts
| Key | Action |
|-----|--------|
| Space | Play/Pause |
| +/- | Zoom time in/out |
| ↑/↓ | Zoom frequency in/out |
| P | Toggle pitch overlay |
| F | Toggle formant overlay |
| H | Toggle harmonic overlay |
| G | Toggle grid |
| 1-5 | Load preset 1-5 |
| S | Snapshot current view |

---

## 8. Data Export & Integration

### 8.1 Export Formats

#### 8.1.1 Image Export
| Format | Use Case |
|--------|----------|
| PNG | Lossless, with transparency support |
| JPEG | Compressed, for sharing |
| SVG | Vector, scalable for publications |
| TIFF | High-quality archival |

#### 8.1.2 Data Export
| Format | Contents |
|--------|----------|
| CSV | Time-stamped pitch, formants, features |
| JSON | Full analysis data with metadata |
| SDIF | Standard interchange for spectral data |
| Praat TextGrid | Segmentation, labels (compatibility) |

#### 8.1.3 Export Data Structure
```csharp
public class AnalysisExport
{
    public AudioMetadata Source { get; set; }
    public AnalysisSettings Settings { get; set; }
    public List<AnalysisFrame> Frames { get; set; }
}

public class AnalysisFrame
{
    public double TimeSeconds { get; set; }
    public float? PitchHz { get; set; }
    public float PitchConfidence { get; set; }
    public float[] FormantFrequencies { get; set; }
    public float[] FormantBandwidths { get; set; }
    public VoicingState Voicing { get; set; }
    public float Hnr { get; set; }
    public float SpectralCentroid { get; set; }
    public float[] HarmonicAmplitudes { get; set; }
}
```

### 8.2 Clipboard Integration
- Copy current spectrum slice as image
- Copy pitch/formant data as tab-separated text
- Paste audio from clipboard for analysis

### 8.3 File Associations
- Register as handler for common audio formats
- Support drag-and-drop of audio files
- Recent files list

---

## 9. Performance Requirements

### 9.1 Computational Targets
| Metric | Target | Maximum |
|--------|--------|---------|
| Audio-to-Display Latency | ≤20 ms | 50 ms |
| Frame Rate | 60 fps | - |
| CPU Usage (idle) | <5% | 10% |
| CPU Usage (analyzing) | <25% | 50% |
| Memory Usage | <200 MB | 500 MB |
| Startup Time | <2 sec | 5 sec |

### 9.2 Optimization Strategies

#### 9.2.1 FFT Optimization
```csharp
// Use optimized FFT library
// Options: MathNet.Numerics, Intel MKL wrapper, or custom SIMD implementation

public interface IFftProvider
{
    void Forward(Span<float> real, Span<float> imaginary);
    void ForwardReal(Span<float> input, Span<Complex> output);
}

// Prefer real-only FFT for audio (2x speedup)
// Pre-compute window function arrays
// Use power-of-2 sizes for radix-2 efficiency
```

#### 9.2.2 Rendering Optimization
- Use GPU acceleration (SkiaSharp with OpenGL backend, or Direct2D)
- Render to bitmap buffer, update only changed regions
- Use texture streaming for scrolling spectrogram
- Implement level-of-detail for zoomed-out views

#### 9.2.3 Threading Model
```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│   Audio     │────▶│    DSP      │────▶│   Render    │
│   Thread    │     │   Thread    │     │   Thread    │
│             │     │             │     │             │
│  • Capture  │     │  • FFT      │     │  • Draw     │
│  • Buffer   │     │  • Analysis │     │  • Overlay  │
│  • Resample │     │  • Features │     │  • UI Sync  │
└─────────────┘     └─────────────┘     └─────────────┘
        │                   │                   │
        └───────────────────┴───────────────────┘
                    Lock-free queues
```

### 9.3 Memory Management
- Ring buffer for audio (size: 2 × display time window × sample rate)
- Fixed-size spectrogram texture buffer
- Object pooling for analysis frames
- Aggressive GC hints during idle

---

## 10. Implementation Architecture

### 10.1 Project Structure
```
VocalSpectrograph/
├── src/
│   ├── VocalSpectrograph.Core/           # Core DSP library
│   │   ├── Audio/
│   │   │   ├── AudioCapture.cs
│   │   │   ├── AudioFileReader.cs
│   │   │   └── RingBuffer.cs
│   │   ├── Dsp/
│   │   │   ├── FftProcessor.cs
│   │   │   ├── WindowFunctions.cs
│   │   │   └── Filters.cs
│   │   ├── Analysis/
│   │   │   ├── PitchDetector.cs
│   │   │   ├── YinPitchDetector.cs
│   │   │   ├── FormantTracker.cs
│   │   │   ├── LpcAnalyzer.cs
│   │   │   ├── HarmonicAnalyzer.cs
│   │   │   └── VoicingDetector.cs
│   │   ├── Features/
│   │   │   ├── SpectralFeatures.cs
│   │   │   └── VoiceQualityMetrics.cs
│   │   └── Export/
│   │       ├── CsvExporter.cs
│   │       ├── JsonExporter.cs
│   │       └── ImageExporter.cs
│   │
│   ├── VocalSpectrograph.Visualization/  # Rendering library
│   │   ├── SpectrogramRenderer.cs
│   │   ├── ColorMaps.cs
│   │   ├── OverlayRenderer.cs
│   │   ├── AxisRenderer.cs
│   │   └── Themes/
│   │
│   └── VocalSpectrograph.App/            # WPF/WinUI Application
│       ├── Views/
│       │   ├── MainWindow.xaml
│       │   ├── SpectrogramView.xaml
│       │   └── SettingsView.xaml
│       ├── ViewModels/
│       │   ├── MainViewModel.cs
│       │   └── SpectrogramViewModel.cs
│       ├── Services/
│       │   ├── AudioService.cs
│       │   └── AnalysisService.cs
│       └── Resources/
│
├── tests/
│   ├── VocalSpectrograph.Core.Tests/
│   └── VocalSpectrograph.Visualization.Tests/
│
└── docs/
    ├── API.md
    └── UserGuide.md
```

### 10.2 Key Interfaces
```csharp
// Core analysis pipeline interface
public interface IAnalysisPipeline
{
    event EventHandler<AnalysisFrameEventArgs> FrameReady;
    
    void Configure(AnalysisSettings settings);
    void ProcessBuffer(Span<float> audioBuffer);
    void Reset();
}

// Pitch detector interface
public interface IPitchDetector
{
    PitchResult Detect(ReadOnlySpan<float> frame, int sampleRate);
}

public record PitchResult(
    float? FrequencyHz,
    float Confidence,
    bool IsVoiced
);

// Formant tracker interface
public interface IFormantTracker
{
    FormantResult Track(ReadOnlySpan<float> frame, int sampleRate, float? pitchHz);
}

public record FormantResult(
    float[] Frequencies,
    float[] Bandwidths,
    float[] Amplitudes
);

// Spectrogram renderer interface
public interface ISpectrogramRenderer
{
    void Render(
        SKCanvas canvas, 
        SpectrogramData data, 
        RenderSettings settings,
        IReadOnlyList<IOverlay> overlays
    );
}
```

### 10.3 Dependencies
| Library | Purpose | Version |
|---------|---------|---------|
| NAudio | Audio I/O | 2.2+ |
| MathNet.Numerics | FFT, Linear Algebra | 5.0+ |
| SkiaSharp | GPU-accelerated rendering | 2.88+ |
| CommunityToolkit.Mvvm | MVVM framework | 8.2+ |
| System.Reactive | Event processing | 6.0+ |

---

## 11. Testing Requirements

### 11.1 Unit Tests
- FFT accuracy against known signals
- Pitch detection accuracy (±1 Hz for synthetic tones)
- Formant extraction validation against Praat
- Window function correctness
- Filter frequency response

### 11.2 Integration Tests
- End-to-end latency measurement
- Memory leak detection over extended operation
- Audio device switching
- File format compatibility

### 11.3 Reference Test Signals
| Signal | Purpose |
|--------|---------|
| Sine sweeps (50-8000 Hz) | Frequency response |
| Synthetic vowels | Formant accuracy |
| Pitch-annotated speech corpora | Pitch accuracy |
| White/pink noise | Noise handling |
| Recorded voice samples | Real-world validation |

---

## 12. Future Considerations

### 12.1 Potential Extensions
- Machine learning voice classification
- Speaker diarization
- Emotion detection overlay
- Real-time pitch correction feedback
- MIDI output from pitch track
- Network streaming support
- Plugin architecture for custom analyzers

### 12.2 Platform Expansion
- macOS port (via Avalonia or MAUI)
- Linux support
- Mobile companion viewer

---

## Appendix A: Algorithm References

### Pitch Detection
- de Cheveigné, A., & Kawahara, H. (2002). YIN, a fundamental frequency estimator for speech and music. *JASA*, 111(4), 1917-1930.
- Mauch, M., & Dixon, S. (2014). pYIN: A fundamental frequency estimator using probabilistic threshold distributions. *ICASSP*.

### Formant Analysis
- Markel, J. D., & Gray, A. H. (1976). *Linear Prediction of Speech*. Springer.
- Snell, R. C., & Milinazzo, F. (1993). Formant location from LPC analysis data. *IEEE Trans. Speech Audio Process.*

### Voice Quality
- Hillenbrand, J., et al. (1994). Acoustic characteristics of American English vowels. *JASA*, 97(5), 3099-3111.

---

## Appendix B: Vocal Frequency Quick Reference

### Note-to-Frequency Table (A4 = 440 Hz)
| Note | Frequency | Voice Application |
|------|-----------|-------------------|
| E2 | 82.4 Hz | Bass low |
| A2 | 110 Hz | Baritone low |
| E3 | 164.8 Hz | Tenor low |
| A3 | 220 Hz | Alto low |
| C4 | 261.6 Hz | Middle C |
| A4 | 440 Hz | Tuning reference |
| C5 | 523.3 Hz | Soprano mid |
| C6 | 1046.5 Hz | Soprano high / Whistle register |

### Critical Frequencies for Vocal Processing
| Frequency | Significance |
|-----------|--------------|
| 80 Hz | Low male voice fundamental |
| 150 Hz | Average male speech F0 |
| 250 Hz | Average female speech F0 |
| 500 Hz | Vowel warmth region |
| 1-2 kHz | Vowel intelligibility |
| 2-4 kHz | Presence, clarity |
| 3 kHz | Singer's formant region |
| 5-8 kHz | Sibilance, air |

---

*End of Specification Document*
