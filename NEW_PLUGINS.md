# HotMic Broadcast Vocal Chain — Implementation Guide

## Overview

This document describes the plugin chain and processing required to achieve "radio quality" voice audio. The goal is to transform raw microphone input into polished, broadcast-ready output suitable for podcasting, streaming, and video calls.

---

## Signal Chain Order

```
1. High-Pass Filter (HPF)
2. AI Noise Suppression (DNN)
3. Voice Activity Gate (VAD)
4. De-Esser
5. Compressor
6. Equalizer (EQ)
7. Saturation (optional)
8. Limiter
```

Each stage has a specific purpose. Order matters.

---

## Plugin Specifications

### 1. High-Pass Filter

**Purpose:** Remove low-frequency rumble, plosives, handling noise, and HVAC hum before other processing.

**Parameters:**

| Parameter | Range | Default |
|-----------|-------|---------|
| Cutoff Frequency | 40-200 Hz | 100 Hz |
| Slope | 12 or 18 dB/octave | 18 dB/octave |

**Algorithm:** Biquad high-pass filter. Can be integrated into EQ plugin or standalone.

---

### 2. AI Noise Suppression (DNN)

**Purpose:** Continuous noise removal — works during speech, not just pauses. Handles fan noise, room tone, keyboard clicks, background chatter.

**Implementation:** RNNoise for lightweight processing, DeepFilterNet for higher quality. See AI_NOISE_SUPPRESSION_PLAN.md for details.

**Optimization:** Use VAD probability to skip DNN processing during confirmed silence. This saves CPU without affecting quality since there's nothing to clean during true silence anyway.

---

### 3. Voice Activity Gate (VAD)

**Purpose:** Provide true silence during pauses. Catches any residual artifacts from DNN. Also used to signal DNN to skip processing during silence.

**Position in chain:** After DNN, not before. VAD accuracy improves dramatically when analyzing clean (post-DNN) audio rather than noisy raw input.

**Parameters:**

| Parameter | Range | Default |
|-----------|-------|---------|
| Threshold | 0.0-0.95 | 0.5 |
| Hold Time | 50-300 ms | 150 ms |

**Behavior:** When VAD probability falls below threshold, start hold timer. When hold expires, output silence (zeros). Hold time prevents choppy cutoffs at word endings.

---

### 4. De-Esser

**Purpose:** Tame harsh sibilance — the "S", "T", "SH" sounds that condensers (like the NT1) exaggerate. Without this, voices sound thin and piercing.

**Parameters:**

| Parameter | Range | Default |
|-----------|-------|---------|
| Center Frequency | 4-9 kHz | 6 kHz |
| Bandwidth | 1-4 kHz | 2 kHz |
| Threshold | -40 to 0 dB | -30 dB |
| Reduction Amount | 0-12 dB | 6 dB |
| Maximum Range | 0-20 dB | 10 dB |

**Algorithm:**

1. Extract sidechain signal using band-pass filter centered on target frequency
2. Measure sidechain energy using envelope follower (fast attack ~1ms, medium release ~50ms)
3. When sidechain level exceeds threshold, calculate gain reduction
4. Apply gain reduction only to the sibilant frequency band, not the full spectrum
5. Blend processed high band back with untouched low/mid content

**Note:** The NT1 typically needs more de-essing than the SM7B. Consider mic-specific presets.

---

### 5. Compressor

**Purpose:** Even out dynamics — raise quiet parts, tame loud parts. Adds "presence" and the "forward" sound characteristic of broadcast audio.

**Parameters:**

| Parameter | Range | Default (Voice) |
|-----------|-------|-----------------|
| Threshold | -40 to 0 dB | -20 dB |
| Ratio | 1:1 to 20:1 | 4:1 |
| Attack | 0.1-100 ms | 15 ms |
| Release | 10-1000 ms | 120 ms |
| Knee | 0-12 dB | 6 dB (soft) |
| Makeup Gain | 0-24 dB | Auto or +6 dB |

**Algorithm:** Feed-forward design with RMS detection and soft knee.

1. Convert input to dB domain
2. Track envelope using single-pole smoothing filter with attack/release coefficients
3. Compute gain reduction using threshold, ratio, and soft knee curve
4. Apply gain reduction plus makeup gain

**Key design decisions:**

- Use feed-forward topology (compute gain from input, apply to output). Simple and predictable.
- Perform envelope follower math in dB domain. This is perceptually correct and avoids pumping artifacts.
- Implement soft knee as quadratic interpolation in the region around threshold. This creates gradual onset rather than abrupt grabbing.
- Single-pole envelope follower is sufficient for voice. More complex topologies add minimal benefit.

**What to skip:** Analog modeling (VCA/Opto/FET emulation), feedback topology, multiband, sidechain filtering. These add complexity without meaningful improvement for voice.

---

### 6. Equalizer

**Purpose:** Shape the voice — add warmth, remove muddiness, enhance clarity and presence. This is where "expensive mic" sound comes from.

**Band Configuration:**

| Band | Type | Frequency | Gain | Q | Purpose |
|------|------|-----------|------|---|---------|
| 1 | High-pass | 80 Hz | - | - | Rumble removal (if not using separate HPF) |
| 2 | Low shelf | 120 Hz | +3 dB | - | Warmth and body |
| 3 | Peaking | 300 Hz | -3 dB | 1.0 | Remove mud and boxiness |
| 4 | Peaking | 3 kHz | +3 dB | 1.0 | Presence and clarity |
| 5 | High shelf | 10 kHz | +2 dB | - | Air and brightness |

This creates the classic "smile curve" — boosted lows and highs, cut low-mids.

**Mic-specific adjustments:**

- SM7B: Needs more presence boost (4-5 kHz), already has rolled-off highs so less air boost needed
- NT1: Brighter mic, may need less high shelf, benefits from the mud cut

---

### 7. Saturation (Optional)

**Purpose:** Add subtle harmonic distortion — the "analog warmth" or "tube" character. Makes digital audio feel less sterile.

**Parameters:**

| Parameter | Range | Default |
|-----------|-------|---------|
| Drive | 0-100% | 15% |
| Mix | 0-100% | 50% |

**Algorithm:** Soft clipping using hyperbolic tangent (tanh) waveshaping. This rounds off peaks and adds odd harmonics.

For "tube-style" even harmonics, use asymmetric clipping — positive and negative halves of the waveform are shaped differently.

**Usage:** Apply sparingly. A little adds warmth; too much sounds fuzzy and distorted. This is a polish effect, not a core processor.

---

### 8. Limiter

**Purpose:** Brick-wall ceiling to prevent clipping. Allows maximizing loudness while guaranteeing no overs.

**Parameters:**

| Parameter | Range | Default |
|-----------|-------|---------|
| Ceiling | -3 to 0 dB | -1 dB |
| Release | 10-200 ms | 50 ms |

**Algorithm:** Fast compressor with infinite ratio and instant attack.

1. If sample exceeds ceiling, reduce gain to bring it to ceiling
2. Smooth gain changes with release time to avoid distortion
3. Optionally use 1-5ms lookahead for transparent limiting (adds latency)

For voice without strict latency requirements, lookahead limiting sounds more transparent. For real-time communication, skip lookahead and accept slightly less transparent limiting.

---

## Presets

### Broadcast Radio (General Purpose)

| Plugin | Settings |
|--------|----------|
| HPF | 100 Hz, 18 dB/oct |
| DNN | DeepFilterNet, 80% reduction |
| VAD | 0.5 threshold, 150ms hold |
| De-Esser | 6 kHz, -30 dB threshold, 6 dB reduction |
| Compressor | -20 dB threshold, 4:1 ratio, 15ms attack, 120ms release, soft knee |
| EQ | +3 dB @ 120 Hz, -3 dB @ 300 Hz, +3 dB @ 3 kHz, +2 dB @ 10 kHz |
| Limiter | -1 dB ceiling |

### Clean/Natural (Minimal Processing)

| Plugin | Settings |
|--------|----------|
| HPF | 80 Hz, 12 dB/oct |
| DNN | RNNoise, 60% reduction |
| VAD | 0.4 threshold, 200ms hold |
| De-Esser | Bypassed or very light |
| Compressor | -24 dB threshold, 2:1 ratio, 20ms attack, 150ms release |
| EQ | Flat or minimal |
| Limiter | -1 dB ceiling |

### Podcast/Voiceover (Maximum Polish)

| Plugin | Settings |
|--------|----------|
| HPF | 100 Hz, 18 dB/oct |
| DNN | DeepFilterNet, 100% reduction |
| VAD | 0.5 threshold, 100ms hold |
| De-Esser | 6.5 kHz, -28 dB threshold, 8 dB reduction |
| Compressor | -18 dB threshold, 5:1 ratio, 10ms attack, 100ms release |
| EQ | +4 dB @ 100 Hz, -4 dB @ 300 Hz, +4 dB @ 3.5 kHz, +3 dB @ 12 kHz |
| Saturation | 20% drive, 40% mix |
| Limiter | -0.5 dB ceiling |

### SM7B Optimized

| Plugin | Adjustment from Broadcast preset |
|--------|----------------------------------|
| De-Esser | Reduce to 4 dB reduction or bypass |
| EQ | Increase presence to +5 dB @ 4 kHz |
| EQ | Reduce air shelf to +1 dB |

### NT1 Optimized

| Plugin | Adjustment from Broadcast preset |
|--------|----------------------------------|
| De-Esser | Increase to 8 dB reduction |
| EQ | Reduce presence to +2 dB |
| EQ | Consider narrow cut at 8-10 kHz if harsh |

---

## Metering

### Voice-Optimized dB Scale

Standard dBFS meters waste visual range on levels that never occur in speech. Use a non-linear scale that expands the speech range.

**Allocation:**

| dB Range | Meter Range | Description |
|----------|-------------|-------------|
| -40 to -30 dB | 0% to 15% | Silence and noise floor (compressed) |
| -30 to -12 dB | 15% to 75% | Speech range (expanded) |
| -12 to 0 dB | 75% to 100% | Loud and clipping (compressed) |

This gives speech 60% of the visual range instead of ~30% with linear scaling.

**Color zones:**

| Level | Color | Meaning |
|-------|-------|---------|
| > -6 dB | Red | Clipping danger |
| -12 to -6 dB | Yellow | Loud |
| -30 to -12 dB | Green | Good speech level |
| < -30 dB | Gray | Too quiet |

**Target level:** -18 dBFS RMS. Draw a reference line here.

---

## Latency Budget

**Target for entire HotMic chain: < 30ms**

| Stage | Typical Latency |
|-------|-----------------|
| Audio driver buffer | 5-10 ms |
| DNN processing | 10-20 ms |
| Other plugins | < 1 ms each |
| Safety margin | 5 ms |

This leaves headroom for network latency in communication applications.

---

## Implementation Priority

| Priority | Plugin | Effort | Impact |
|----------|--------|--------|--------|
| 1 | Compressor (with presets) | Medium | High |
| 2 | EQ (with presets) | Medium | High |
| 3 | De-Esser | Medium | High (especially for condensers) |
| 4 | Limiter | Low | Medium |
| 5 | Voice-optimized metering | Low | Medium (UX improvement) |
| 6 | Saturation | Low | Low (polish) |

HPF can be built into EQ. DNN and VAD are covered in separate document.