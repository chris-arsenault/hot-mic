# Spectral Clarity Enhancement Specification

## Purpose
Techniques to reduce spectral "mud" and produce cleaner, more readable spectrograms optimized for vocal analysis. These techniques address noise floor issues, inter-harmonic noise, and spectral smearing that obscure harmonic structure.

---

## 1. Problem Analysis

### 1.1 Sources of Muddy Spectrograms
| Issue | Cause | Visual Symptom |
|-------|-------|----------------|
| Spectral leakage | FFT windowing artifacts | Energy spreading across adjacent bins |
| Noise floor | System noise, quantization, ambient | Low-level color throughout spectrum |
| Temporal smearing | Insufficient time resolution | Transients blurred horizontally |
| Inter-harmonic noise | Non-harmonic content, noise | Random energy between harmonic lines |

### 1.2 Processing Goal
Preserve and enhance harmonic content (horizontal lines in spectrogram) while suppressing noise floor and non-harmonic energy, resulting in clear, distinct harmonic traces against a clean background.

---

## 2. Noise Floor Management

### 2.1 Adaptive Noise Floor Estimation
Continuously estimate the noise floor per-frequency-bin and gate or subtract energy below threshold.

**Method**: Track the minimum or low-percentile magnitude in each bin over a sliding time window.

**Parameters**:
| Parameter | Recommended Value | Description |
|-----------|-------------------|-------------|
| History Length | 50-100 frames (~1-2 sec) | Window for noise estimation |
| Percentile | 10th percentile | Statistical floor estimate |
| Gate Threshold | Noise estimate × 2.0 | Multiplier above noise to display |
| Adaptation Rate | 0.02 (slow) | How quickly estimate updates |

**Behavior**:
- During detected silence: Update noise estimate quickly
- During voiced content: Update slowly, track minimum values
- Apply as either hard gate (zero below threshold) or soft subtraction

### 2.2 Spectral Subtraction
Subtract estimated noise power spectrum from signal power spectrum.

**Formula**: `CleanPower = max(SignalPower - α × NoisePower, β × SignalPower)`

**Parameters**:
| Parameter | Recommended Value | Description |
|-----------|-------------------|-------------|
| α (over-subtraction) | 1.5 – 2.5 | How aggressively to subtract noise |
| β (spectral floor) | 0.01 – 0.02 | Minimum residual to prevent "musical noise" |

**Note**: Higher α removes more noise but risks removing quiet harmonic content. The β floor prevents complete zeroing which causes artifacts.

### 2.3 Hard Display Floor
Simple but effective: Set a hard dB threshold below which nothing is displayed.

**Parameters**:
| Parameter | Recommended Value | Description |
|-----------|-------------------|-------------|
| Display Floor | -60 to -70 dB | Absolute cutoff for rendering |
| Floor Color | Pure black (#000000) | Not dark blue—true black |

**Behavior**: Any bin below the display floor renders as background color, eliminating visual noise from low-energy content.

---

## 3. Harmonic/Percussive Source Separation (HPSS)

### 3.1 Concept
Harmonics appear as horizontal lines in a spectrogram (stable frequency over time). Percussive/noise content appears as vertical lines or random speckle. Median filtering in different directions separates these components.

### 3.2 Median Filtering Method
- **Horizontal median filter** (across time axis): Preserves horizontal structures → Harmonic content
- **Vertical median filter** (across frequency axis): Preserves vertical structures → Percussive content

**Parameters**:
| Parameter | Recommended Value | Description |
|-----------|-------------------|-------------|
| Horizontal Kernel | 17-31 frames | Wider = stronger harmonic extraction |
| Vertical Kernel | 17-31 bins | Wider = stronger percussive extraction |
| Mask Power | 2.0 | Soft mask exponent for blending |

### 3.3 Soft Masking
Rather than binary selection, compute a soft mask:

`HarmonicMask = H^p / (H^p + P^p)`

Where H = horizontal median result, P = vertical median result, p = mask power.

Apply mask to original spectrogram to extract harmonic component.

### 3.4 Real-Time Considerations
- Requires buffering `HorizontalKernel / 2` frames of latency
- For 17-frame kernel at 75% overlap (512 hop @ 48kHz): ~90ms latency
- Can reduce kernel size for lower latency at cost of separation quality

---

## 4. Harmonic Comb Enhancement

### 4.1 Concept
When fundamental frequency (pitch) is known, harmonics occur at predictable integer multiples. Enhance only those frequency regions; attenuate everything else.

### 4.2 Requirements
- Reliable pitch detection must be running (YIN, pYIN, etc.)
- Only applies during voiced segments

### 4.3 Parameters
| Parameter | Recommended Value | Description |
|-----------|-------------------|-------------|
| Tolerance | ±50 cents | Width around expected harmonic frequency |
| Max Harmonics | 20-30 | How many harmonics to track |
| Harmonic Boost | 1.0 – 1.5× | Gain applied to harmonic bins |
| Non-Harmonic Attenuation | 0.1 – 0.3× | Gain applied to non-harmonic bins |

### 4.4 Tolerance Calculation
For a given harmonic frequency, the tolerance window in Hz:
- Lower bound: `HarmonicFreq / 2^(cents/1200)`
- Upper bound: `HarmonicFreq × 2^(cents/1200)`

At ±50 cents, this is approximately ±3% of the harmonic frequency.

### 4.5 Behavior
- For each detected F0, calculate expected frequencies: F0, 2×F0, 3×F0, ... N×F0
- Mark bins within tolerance of each expected harmonic
- Apply boost to harmonic bins, attenuation to others
- During unvoiced/silence: Bypass or apply gentle uniform attenuation

---

## 5. Reassignment Method Optimization

### 5.1 Current Implementation Status
Reassignment method is already implemented. This section covers optimizations to improve its effectiveness.

### 5.2 Magnitude Thresholding
Only reassign bins above a magnitude threshold. Low-energy bins create noise when reassigned.

| Parameter | Recommended Value | Description |
|-----------|-------------------|-------------|
| Min Magnitude | -50 to -60 dB | Don't reassign bins below this |

### 5.3 Displacement Limits
Limit how far energy can be reassigned to prevent artifacts.

| Parameter | Recommended Value | Description |
|-----------|-------------------|-------------|
| Max Frequency Shift | 0.5 bins | Maximum reassignment distance in frequency |
| Max Time Shift | 0.5 frames | Maximum reassignment distance in time |

### 5.4 Synchrosqueezing (Alternative)
A variant of reassignment that only reassigns in frequency (not time), providing sharper frequency resolution while preserving time resolution.

**Key Difference**: Standard reassignment moves energy in both time and frequency. Synchrosqueezing only concentrates energy in frequency dimension using instantaneous frequency estimates.

**Advantage**: Often cleaner results for tonal content like voice, less temporal smearing.

---

## 6. Temporal Smoothing

### 6.1 Purpose
Reduce frame-to-frame noise/flicker while preserving legitimate spectral changes.

### 6.2 Exponential Moving Average (Simple)
Blend current frame with previous frame.

| Parameter | Recommended Value | Description |
|-----------|-------------------|-------------|
| Smoothing Factor | 0.2 – 0.4 | 0 = no smoothing, 1 = frozen |

**Tradeoff**: Higher smoothing reduces noise but blurs transients.

### 6.3 Bilateral Filtering (Edge-Preserving)
Smooth only when neighboring values are similar; preserve edges/transients.

| Parameter | Recommended Value | Description |
|-----------|-------------------|-------------|
| Temporal Radius | 2-3 frames | Time neighborhood |
| Frequency Radius | 2-3 bins | Frequency neighborhood |
| Spatial Sigma | 1.5 | Distance weighting falloff |
| Intensity Sigma | 6-10 dB | Similarity threshold |

**Behavior**: Pixels only averaged with neighbors that have similar magnitude. Edges between loud and quiet regions preserved.

---

## 7. Contrast Enhancement (Display-Level)

### 7.1 Gamma Correction
Apply power curve to normalized magnitude values.

| Gamma Value | Effect |
|-------------|--------|
| < 1.0 (e.g., 0.7) | Brightens mid-tones, reveals quiet detail |
| = 1.0 | Linear (no change) |
| > 1.0 (e.g., 1.3) | Darkens mid-tones, increases contrast |

**Recommended**: 0.7 – 0.85 for vocal spectrograms (reveals harmonic detail)

### 7.2 Contrast Stretch
Expand the displayed dynamic range around the midpoint.

| Parameter | Recommended Value | Description |
|-----------|-------------------|-------------|
| Contrast Factor | 1.1 – 1.3 | Multiplier for deviation from midpoint |

### 7.3 Color Quantization
Reduce continuous color gradient to discrete levels. Can reduce perception of noise.

| Parameter | Recommended Value | Description |
|-----------|-------------------|-------------|
| Color Levels | 24 – 48 | Number of discrete color steps |

**Tradeoff**: Too few levels creates banding; too many preserves noise.

---

## 8. Recommended Processing Pipeline

### 8.1 Pipeline Order
```
Raw FFT Magnitude
       │
       ▼
┌──────────────────────┐
│  1. Noise Floor      │  Percentile-based estimation
│     Subtraction      │  Remove constant background
└──────────┬───────────┘
           │
           ▼
┌──────────────────────┐
│  2. HPSS Harmonic    │  Median filtering method
│     Extraction       │  Isolate harmonic content
└──────────┬───────────┘
           │
           ▼
┌──────────────────────┐
│  3. Reassignment     │  Already implemented
│     (optimized)      │  Add magnitude threshold
└──────────┬───────────┘
           │
           ▼
┌──────────────────────┐
│  4. Harmonic Comb    │  Only if pitch detected
│     Enhancement      │  Boost harmonics, attenuate rest
└──────────┬───────────┘
           │
           ▼
┌──────────────────────┐
│  5. Temporal         │  Bilateral or EMA
│     Smoothing        │  Reduce flicker
└──────────┬───────────┘
           │
           ▼
┌──────────────────────┐
│  6. Display          │  Gamma, contrast, floor
│     Enhancement      │  Perceptual optimization
└──────────┬───────────┘
           │
           ▼
    Color Mapping & Render
```

### 8.2 Optional/Configurable Steps
| Step | When to Skip |
|------|--------------|
| HPSS | CPU constrained, acceptable mud level |
| Harmonic Comb | No reliable pitch, polyphonic content |
| Temporal Smoothing | Analyzing fast transients |

### 8.3 Priority Implementation Order
For maximum impact with minimum effort:

1. **Hard Display Floor** - Immediate improvement, trivial to implement
2. **Noise Floor Subtraction** - High impact on mud reduction  
3. **HPSS** - Dramatic clarity improvement for harmonics
4. **Harmonic Comb** - Polish step when pitch tracking is solid
5. **Bilateral Smoothing** - Final polish for clean appearance

---

## 9. Preset Configurations

### 9.1 Maximum Clarity
| Setting | Value |
|---------|-------|
| Noise Subtraction | Enabled, α = 2.0 |
| HPSS | Enabled, kernel = 17 |
| Harmonic Comb | Enabled when pitched |
| Temporal Smoothing | Bilateral |
| Gamma | 0.75 |
| Display Floor | -65 dB |

### 9.2 Balanced (Default)
| Setting | Value |
|---------|-------|
| Noise Subtraction | Enabled, α = 1.5 |
| HPSS | Enabled, kernel = 11 |
| Harmonic Comb | Disabled |
| Temporal Smoothing | EMA 0.3 |
| Gamma | 0.8 |
| Display Floor | -70 dB |

### 9.3 Low Latency
| Setting | Value |
|---------|-------|
| Noise Subtraction | Simple gate only |
| HPSS | Disabled |
| Harmonic Comb | Disabled |
| Temporal Smoothing | EMA 0.2 |
| Gamma | 0.85 |
| Display Floor | -60 dB |

### 9.4 Analysis Mode
| Setting | Value |
|---------|-------|
| Noise Subtraction | Disabled |
| HPSS | Optional |
| Harmonic Comb | Disabled |
| Temporal Smoothing | Disabled |
| Gamma | 1.0 |
| Display Floor | -80 dB |

---

## 10. Performance Considerations

### 10.1 CPU Cost by Technique
| Technique | Relative Cost | Notes |
|-----------|---------------|-------|
| Hard Display Floor | Negligible | Per-pixel comparison |
| Noise Subtraction | Low | Per-bin subtraction |
| HPSS | Medium-High | Median filters are expensive |
| Harmonic Comb | Low | Per-bin with pitch lookup |
| EMA Smoothing | Negligible | Per-bin multiply-add |
| Bilateral Filter | Medium | Neighborhood comparisons |
| Gamma/Contrast | Negligible | Per-pixel power function |

### 10.2 Optimization Notes
- HPSS median filters can use running median algorithms for efficiency
- Harmonic comb bins can be precomputed when pitch changes
- Bilateral filter can be approximated with separable passes
- Consider processing at reduced frequency resolution for display if full resolution not needed

### 10.3 Latency Impact
| Technique | Added Latency |
|-----------|---------------|
| Noise Floor (percentile) | 0 (uses history) |
| HPSS | kernel_size / 2 frames |
| Temporal Smoothing | 0-1 frames |
| Others | 0 |

---

## Appendix: Algorithm References

- **HPSS**: Fitzgerald, D. (2010). "Harmonic/Percussive Separation using Median Filtering"
- **Reassignment**: Auger, F. & Flandrin, P. (1995). "Improving the Readability of Time-Frequency and Time-Scale Representations by the Reassignment Method"
- **Synchrosqueezing**: Daubechies, I., Lu, J., & Wu, H.T. (2011). "Synchrosqueezed Wavelet Transforms"
- **Bilateral Filter**: Tomasi, C. & Manduchi, R. (1998). "Bilateral Filtering for Gray and Color Images"
