# HotMic - Requirements Document

## Overview

HotMic is a streamlined audio routing application for Windows that captures hardware microphone inputs, processes them through a configurable plugin chain, and outputs to a virtual audio device for use by other applications. It is designed as a focused alternative to VoiceMeeter Banana for users who need simple, reliable mic processing with a modern UI.

## Goals

- Minimal latency audio routing from hardware mics to virtual output
- Clean, modern UI inspired by Ableton's plugin strip design
- Support for both built-in DSP plugins and VST3 plugins
- Two view modes: full edit mode and minimal monitoring mode
- Always-on-top capability for easy access during streaming/calls

## Non-Goals

- Multi-output routing (single virtual output only)
- Recording/playback functionality
- MIDI support
- macOS/Linux support

---

## Functional Requirements

### FR-1: Audio Input

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-1.1 | System shall capture audio from up to 2 hardware microphone inputs simultaneously | Must |
| FR-1.2 | System shall support WASAPI audio capture | Must |
| FR-1.3 | System shall allow selection of input devices from available system devices | Must |
| FR-1.4 | System shall support configurable sample rates (44.1kHz, 48kHz) | Must |
| FR-1.5 | System shall support configurable buffer sizes (128, 256, 512, 1024 samples) | Must |

### FR-2: Audio Output

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-2.1 | System shall output processed audio to VB-Cable virtual audio device | Must |
| FR-2.2 | System shall detect and validate VB-Cable installation on startup | Must |
| FR-2.3 | System shall display clear error if VB-Cable is not installed | Must |
| FR-2.4 | System shall optionally output to a hardware device for monitoring | Should |

### FR-3: Channel Strip

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-3.1 | Each input channel shall have an independent channel strip | Must |
| FR-3.2 | Each channel strip shall have input gain control (-60dB to +12dB) | Must |
| FR-3.3 | Each channel strip shall have output gain control (-60dB to +12dB) | Must |
| FR-3.4 | Each channel strip shall display input level meter (pre-plugin) | Must |
| FR-3.5 | Each channel strip shall display output level meter (post-plugin) | Must |
| FR-3.6 | Each channel strip shall have mute toggle | Must |
| FR-3.7 | Each channel strip shall have solo toggle | Should |

### FR-4: Plugin Chain

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-4.1 | Each channel shall support a serial plugin chain of up to 5 plugins | Must |
| FR-4.2 | Plugins shall process audio in user-defined order | Must |
| FR-4.3 | Plugin order shall be rearrangeable via drag-and-drop | Must |
| FR-4.4 | Each plugin slot shall have bypass toggle | Must |
| FR-4.5 | Each plugin slot shall have remove button | Must |
| FR-4.6 | Empty plugin slots shall show "Add Plugin" interface | Must |

### FR-5: Built-in Plugins

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-5.1 | System shall include Compressor plugin | Must |
| FR-5.1.1 | Compressor: Threshold (-60dB to 0dB) | Must |
| FR-5.1.2 | Compressor: Ratio (1:1 to 20:1) | Must |
| FR-5.1.3 | Compressor: Attack (0.1ms to 100ms) | Must |
| FR-5.1.4 | Compressor: Release (10ms to 1000ms) | Must |
| FR-5.1.5 | Compressor: Makeup gain (0dB to +24dB) | Must |
| FR-5.1.6 | Compressor: Gain reduction meter | Should |
| FR-5.2 | System shall include Noise Gate plugin | Must |
| FR-5.2.1 | Gate: Threshold (-80dB to 0dB) | Must |
| FR-5.2.2 | Gate: Attack (0.1ms to 50ms) | Must |
| FR-5.2.3 | Gate: Hold (0ms to 500ms) | Must |
| FR-5.2.4 | Gate: Release (10ms to 500ms) | Must |
| FR-5.2.5 | Gate: Open/closed indicator | Should |
| FR-5.3 | System shall include FFT Noise Removal plugin | Must |
| FR-5.3.1 | Noise Removal: Learn noise profile button | Must |
| FR-5.3.2 | Noise Removal: Reduction amount (0% to 100%) | Must |
| FR-5.3.3 | Noise Removal: Sensitivity/threshold control | Must |
| FR-5.4 | System shall include 3-Band EQ plugin | Must |
| FR-5.4.1 | EQ: Low band gain and frequency (20Hz-500Hz) | Must |
| FR-5.4.2 | EQ: Mid band gain and frequency (200Hz-5kHz) | Must |
| FR-5.4.3 | EQ: High band gain and frequency (2kHz-20kHz) | Must |
| FR-5.4.4 | EQ: Q/bandwidth control per band | Should |

### FR-6: VST3 Plugin Support

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-6.1 | System shall load and host VST3 plugins via VST.NET | Must |
| FR-6.2 | System shall scan common VST3 directories for available plugins | Must |
| FR-6.3 | System shall cache plugin scan results | Should |
| FR-6.4 | System shall display VST3 plugin native editor UI | Should |
| FR-6.5 | System shall save/restore VST3 plugin state | Should |

### FR-7: Metering

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-7.1 | Meters shall display both Peak and RMS levels simultaneously | Must |
| FR-7.2 | Peak level shall be shown as a thin line/marker | Must |
| FR-7.3 | RMS level shall be shown as a filled bar | Must |
| FR-7.4 | Meters shall have peak hold with decay (1-2 second hold) | Should |
| FR-7.5 | Meters shall update at minimum 30fps | Must |
| FR-7.6 | Meters shall display scale markings (0dB, -6dB, -12dB, -24dB, -48dB) | Should |
| FR-7.7 | Meters shall indicate clipping (red) at 0dB | Must |

### FR-8: User Interface - General

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-8.1 | UI shall have modern, clean aesthetic inspired by Ableton | Must |
| FR-8.2 | UI shall support always-on-top mode toggle | Must |
| FR-8.3 | UI shall have dark theme | Must |
| FR-8.4 | UI elements shall be rendered with SkiaSharp | Must |
| FR-8.5 | UI shall be responsive and not block audio processing | Must |

### FR-9: User Interface - View Modes

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-9.1 | System shall have Full Edit view mode | Must |
| FR-9.2 | Full Edit: Display complete channel strips with all controls | Must |
| FR-9.3 | Full Edit: Allow plugin drag-and-drop reordering | Must |
| FR-9.4 | Full Edit: Show plugin parameter controls | Must |
| FR-9.5 | System shall have Minimal view mode | Must |
| FR-9.6 | Minimal: Display channel names and meters only | Must |
| FR-9.7 | Minimal: Compact window size | Must |
| FR-9.8 | User shall be able to toggle between view modes | Must |

### FR-10: Configuration & Persistence

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-10.1 | System shall persist all settings between sessions | Must |
| FR-10.2 | System shall save plugin chain configuration | Must |
| FR-10.3 | System shall save plugin parameters | Must |
| FR-10.4 | System shall save window position and view mode | Should |
| FR-10.5 | System shall save audio device selections | Must |
| FR-10.6 | Configuration shall be stored in JSON format | Must |
| FR-10.7 | System shall support configuration profiles/presets | Could |

---

## Non-Functional Requirements

### NFR-1: Performance

| ID | Requirement | Target |
|----|-------------|--------|
| NFR-1.1 | Audio latency (input to output) | < 20ms at 256 sample buffer |
| NFR-1.2 | CPU usage (idle, no plugins) | < 2% |
| NFR-1.3 | CPU usage (full chain, built-in plugins) | < 10% |
| NFR-1.4 | UI frame rate | 60fps |
| NFR-1.5 | Meter update rate | 30-60fps |
| NFR-1.6 | Memory usage | < 200MB |
| NFR-1.7 | No audio glitches under normal operation | Zero tolerance |

### NFR-2: Compatibility

| ID | Requirement |
|----|-------------|
| NFR-2.1 | Windows 10 (1903+) and Windows 11 |
| NFR-2.2 | .NET 8.0 runtime |
| NFR-2.3 | x64 architecture only |
| NFR-2.4 | VB-Cable (required external dependency) |

### NFR-3: Reliability

| ID | Requirement |
|----|-------------|
| NFR-3.1 | Application shall recover gracefully from audio device disconnection |
| NFR-3.2 | VST3 plugin crash shall not crash main application |
| NFR-3.3 | Configuration corruption shall fall back to defaults |

---

## Technical Specifications

### Audio Processing

```
Sample Rate:     44100 Hz or 48000 Hz (configurable)
Bit Depth:       32-bit float (internal processing)
Buffer Size:     128 / 256 / 512 / 1024 samples (configurable)
Channels:        Mono per input, processed independently
```

### Technology Stack

| Component | Technology |
|-----------|------------|
| Language | C# / .NET 8 |
| UI Framework | WPF |
| UI Rendering | SkiaSharp (SkiaSharp.Views.WPF) |
| Audio I/O | NAudio (WASAPI) |
| DSP Foundation | NAudio.Dsp, Math.NET Numerics |
| VST3 Hosting | VST.NET (Jacobi.Vst.Core, Jacobi.Vst.Host.Interop) |
| MVVM | CommunityToolkit.Mvvm |
| Configuration | System.Text.Json |

### Dependencies

**NuGet Packages:**
```
NAudio >= 2.2.0
SkiaSharp >= 2.88.0
SkiaSharp.Views.WPF >= 2.88.0
MathNet.Numerics >= 5.0.0
Jacobi.Vst >= 2.0.0-alpha
CommunityToolkit.Mvvm >= 8.2.0
```

**External:**
```
VB-Cable Virtual Audio Device (https://vb-audio.com/Cable/)
```

---

## User Interface Specifications

### Color Palette (Dark Theme)

```
Background Primary:    #1a1a1a
Background Secondary:  #242424
Background Tertiary:   #2d2d2d
Surface:              #333333
Border:               #444444
Text Primary:         #ffffff
Text Secondary:       #888888
Accent:               #ff6b00 (orange)
Meter Green:          #00ff00
Meter Yellow:         #ffff00
Meter Red:            #ff0000
```

### Layout - Full Edit Mode

```
┌─────────────────────────────────────────────────────────────────┐
│  [HotMic]                    [Minimal] [⚙️] [📌]  [—][□][×] │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌─────────────────────────┐  ┌─────────────────────────┐      │
│  │      CHANNEL 1          │  │      CHANNEL 2          │      │
│  │  ┌─────┐    ┌───────┐   │  │  ┌─────┐    ┌───────┐   │      │
│  │  │INPUT│    │ METER │   │  │  │INPUT│    │ METER │   │      │
│  │  │GAIN │    │  ██   │   │  │  │GAIN │    │  ██   │   │      │
│  │  └─────┘    │  ██   │   │  │  └─────┘    │  ██   │   │      │
│  │             │  ██   │   │  │             │  ██   │   │      │
│  │  ┌───────────────────┐  │  │  ┌───────────────────┐  │      │
│  │  │ [1] Noise Gate    │  │  │  │ [1] Compressor    │  │      │
│  │  │ [2] Compressor    │  │  │  │ [2] EQ            │  │      │
│  │  │ [3] EQ            │  │  │  │ [3] + Add Plugin  │  │      │
│  │  │ [4] + Add Plugin  │  │  │  │                   │  │      │
│  │  │                   │  │  │  │                   │  │      │
│  │  └───────────────────┘  │  │  └───────────────────┘  │      │
│  │             ┌───────┐   │  │             ┌───────┐   │      │
│  │  ┌─────┐    │ METER │   │  │  ┌─────┐    │ METER │   │      │
│  │  │ OUT │    │  ██   │   │  │  │ OUT │    │  ██   │   │      │
│  │  │GAIN │    │  ██   │   │  │  │GAIN │    │  ██   │   │      │
│  │  └─────┘    └───────┘   │  │  └─────┘    └───────┘   │      │
│  │  [M] [S]                │  │  [M] [S]                │      │
│  └─────────────────────────┘  └─────────────────────────┘      │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Layout - Minimal Mode

```
┌────────────────────────────────┐
│  HotMic        [Full] [📌] │
├────────────────────────────────┤
│  CH1  ████████████░░  -12dB   │
│  CH2  ██████████░░░░  -18dB   │
└────────────────────────────────┘
```

---

## Glossary

| Term | Definition |
|------|------------|
| Channel Strip | Complete signal path for one input: gain → plugins → output |
| Plugin Chain | Ordered sequence of audio effect plugins |
| VB-Cable | Third-party virtual audio cable software |
| WASAPI | Windows Audio Session API |
| RMS | Root Mean Square - average signal level |
| Peak | Maximum instantaneous signal level |
| FFT | Fast Fourier Transform - used for frequency analysis |

---

## Appendix A: Built-in Plugin Algorithms

### Compressor
- Feed-forward design
- Log-domain gain calculation
- Soft-knee option
- Lookahead: none (zero-latency)

### Noise Gate
- Hysteresis between open/close thresholds
- Exponential envelope follower
- Range control (how much to attenuate when closed)

### FFT Noise Removal
- STFT with 50% overlap
- Noise profile: average magnitude spectrum during "learn" period
- Spectral subtraction with flooring
- Reconstruction via overlap-add

### 3-Band EQ
- State-variable or biquad filters
- Low: Low-shelf
- Mid: Peaking/parametric
- High: High-shelf
