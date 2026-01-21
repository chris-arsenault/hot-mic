# Signal Generator

Test signal generator plugin for evaluating vocal processing chains. Provides 3 independent generator slots with internal summing, plus a master output section. This doc focuses on DSP behavior; UI wiring and parameter indexing are intentionally omitted.

## Signal Flow

```
┌─────────────────────────────────────────────────────────────┐
│                   SignalGeneratorPlugin                      │
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

### Mix Mode
- **Replace:** Generator output overwrites input buffer
- **Add:** Generator output mixes with input buffer
