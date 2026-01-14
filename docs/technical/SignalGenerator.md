# Signal Generator

Test signal generator plugin for evaluating vocal processing chains. Provides 3 independent generator slots with internal summing, plus a master output section.

## Signal Flow

```
┌─────────────────────────────────────────────────────────────┐
│                   SignalGeneratorPlugin                      │
│                                                              │
│  [Record] ← Input buffer captured before overwrite           │
│                                                              │
│  Slot 1: [Type] → [Gain] → [Mute/Solo] ──┐                  │
│  Slot 2: [Type] → [Gain] → [Mute/Solo] ──┼─► Σ (sum)        │
│  Slot 3: [Type] → [Gain] → [Mute/Solo] ──┘       │          │
│                                                   ▼          │
│                              Headroom Compensation           │
│                                        │                     │
│                                  Master Gain                 │
│                                        ▼                     │
│                                  Output Buffer               │
└─────────────────────────────────────────────────────────────┘
```

## Generator Types

### Oscillators (Slots 0-3)

| Type | Description | Additional Controls |
|------|-------------|---------------------|
| Sine | Pure tone, fundamental testing | Frequency, Sweep |
| Square | Rich harmonics, pulse width control | Frequency, Sweep, Pulse Width |
| Saw | Full harmonic series | Frequency, Sweep |
| Triangle | Odd harmonics only | Frequency, Sweep |

**Anti-aliasing:** Square and saw waveforms use PolyBLEP (polynomial bandlimited step) to reduce aliasing at high frequencies.

**Sweep Mode:**
- Range: 20Hz–20kHz (configurable start/end)
- Duration: 100ms–30s
- Direction: Up, Down, Ping-Pong
- Curve: Linear or Logarithmic (log recommended for perceptual uniformity)

### Noise Generators (Slots 4-7)

| Type | Spectral Slope | Use Case |
|------|----------------|----------|
| White | Flat (0 dB/oct) | Flat frequency response testing |
| Pink | -3 dB/octave | Perceived "flat" loudness |
| Brown | -6 dB/octave | Low-frequency/rumble handling |
| Blue | +3 dB/octave | Sibilance/de-esser testing |

**Implementation:**
- White: xorshift128+ PRNG
- Pink: Voss-McCartney algorithm (16 octave bands, streaming)
- Brown: Leaky integrator on white noise
- Blue: First-order differentiator on white noise

### Impulse (Slot 8)

Periodic DC-free bipolar pulses for transient response testing.

- Interval: 10ms–5000ms
- Pulse: 2-sample bipolar (+1, -1) for DC-free click

### Chirp (Slot 9)

Logarithmic frequency sweep burst for quick frequency response visualization.

- Duration: 50ms–500ms
- Repeat Interval: configurable
- Envelope: 64-sample cosine ramp for click-free start/end

### Sample (Slot 10)

Playback of loaded WAV samples.

- Max Length: 10 seconds at 48kHz (480,000 samples)
- Loop Modes: Loop, One-Shot, Ping-Pong
- Speed: 0.5x–2.0x with linear interpolation
- Trim: Configurable start/end points (0–1 normalized)

## Master Section

### Gain
- Range: -60 dB to +12 dB
- Smoothing: 5ms linear ramp

### Headroom Compensation
- **None:** Raw sum (can clip with multiple active slots)
- **Auto-Compensate:** -3dB per doubling of active sources
- **Normalize:** Hard clip to ±1.0

### Output Presets
- **Vocal Conversation:** -18 dBFS
- **Vocal Performance:** -12 dBFS
- **Unity:** 0 dBFS

### Mix Mode
- **Replace:** Generator output overwrites input buffer
- **Add:** Generator output mixes with input buffer

## Recording

Input audio is captured to a 10-second ring buffer (at current sample rate). The "Capture" action copies the buffer contents to a sample slot for playback.

**Threading:** Recording uses a lock-free ring buffer. Capture request is queued atomically and processed on the audio thread.

## Sample Loading

WAV files are loaded asynchronously via a concurrent queue:
1. UI thread enqueues `SampleLoadRequest`
2. Audio thread processes queue, loads into `SampleBuffer`
3. Mono conversion applied (stereo → mono average)

## Parameter Layout

Parameters are indexed in blocks of 20 per slot:

| Offset | Parameter | Range | Default |
|--------|-----------|-------|---------|
| 0 | Type | 0–10 | 0 (Sine) |
| 1 | Frequency | 20–20000 Hz | 440 |
| 2 | Gain | -60–12 dB | -12 |
| 3 | Mute | 0/1 | 0 |
| 4 | Solo | 0/1 | 0 |
| 5 | Sweep Enabled | 0/1 | 0 |
| 6 | Sweep Start Hz | 20–20000 | 80 |
| 7 | Sweep End Hz | 20–20000 | 8000 |
| 8 | Sweep Duration ms | 100–30000 | 5000 |
| 9 | Sweep Direction | 0–2 | 0 (Up) |
| 10 | Sweep Curve | 0–1 | 1 (Log) |
| 11 | Pulse Width | 0.1–0.9 | 0.5 |
| 12 | Impulse Interval ms | 10–5000 | 100 |
| 13 | Chirp Duration ms | 50–500 | 200 |
| 14 | Sample Loop Mode | 0–2 | 0 (Loop) |
| 15 | Sample Speed | 0.5–2.0 | 1.0 |
| 16 | Sample Trim Start | 0–0.99 | 0 |
| 17 | Sample Trim End | 0.01–1 | 1 |

Master parameters start at offset 60:

| Index | Parameter | Range | Default |
|-------|-----------|-------|---------|
| 60 | Master Gain | -60–12 dB | 0 |
| 61 | Master Mute | 0/1 | 0 |
| 62 | Headroom Mode | 0–2 | 1 (Auto) |
| 63 | Output Preset | 0–3 | 0 (Custom) |
| 64 | Mix Mode | 0/1 | 0 (Replace) |

## Presets

Built-in presets for common test scenarios:

| Preset | Configuration |
|--------|---------------|
| Frequency Sweep | Slot 1: Sine sweep 80Hz–8kHz, 5s, log |
| Sibilance Test | Slot 1: 4kHz sine, Slot 2: 8kHz sine, Slot 3: Blue noise |
| Proximity Test | Slot 1: Brown noise, Slot 2: 120Hz sine |
| Pitch Test | Slot 1: 220Hz sine (A3) |
| Full Spectrum | Slot 1: Pink noise |
| Transient Test | Slot 1: Impulse @ 100ms |
| Harmonics Test | Slots 1-3: 220Hz + 440Hz + 660Hz (fundamental + harmonics) |

## Code References

- Plugin: `src/HotMic.Core/Plugins/BuiltIn/SignalGeneratorPlugin.cs`
- Oscillator DSP: `src/HotMic.Core/Dsp/Generators/OscillatorCore.cs`
- Noise DSP: `src/HotMic.Core/Dsp/Generators/NoiseGenerator.cs`
- Impulse DSP: `src/HotMic.Core/Dsp/Generators/ImpulseGenerator.cs`
- Chirp DSP: `src/HotMic.Core/Dsp/Generators/ChirpGenerator.cs`
- Sample Buffer: `src/HotMic.Core/Dsp/Generators/SampleBuffer.cs`
- UI Renderer: `src/HotMic.App/UI/PluginComponents/SignalGeneratorRenderer.cs`
- Editor Window: `src/HotMic.App/Views/SignalGeneratorWindow.xaml`
