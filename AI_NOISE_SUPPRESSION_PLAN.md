# HotMic AI Noise Suppression — Implementation Plan

## Executive Summary

Add AI-powered noise suppression to HotMic as built-in plugins. Two tiers:

1. **RNNoise** — Lightweight baseline, ~2% CPU, good for steady noise
2. **DeepFilterNet** — Higher quality, ~10% CPU, better for transients/keyboard

Both exposed as standard `IPlugin` implementations in the existing plugin chain.

---

## Part 1: RNNoise Integration

### 1.1 What is RNNoise?

RNNoise is a hybrid DSP/neural network noise suppressor created by Jean-Marc Valin (Xiph.org, also created Opus codec). It's designed for real-time voice communication.

**Key characteristics:**
- Operates on 10ms frames (480 samples at 48kHz)
- Uses GRU (Gated Recurrent Unit) neural network
- Predicts gains for 22 Bark-scale frequency bands
- Outputs VAD (Voice Activity Detection) probability
- Model weights are baked into the C library (~85KB total)
- BSD-3 license

**How it works internally:**
1. Input frame → compute spectral features (pitch, spectral shape, band energies)
2. Feed features to GRU network
3. Network outputs gain for each of 22 frequency bands
4. Apply gains in frequency domain (acts like adaptive multi-band gate)
5. Return processed audio + VAD probability

**Paper:** "A Hybrid DSP/Deep Learning Approach to Real-Time Full-Band Speech Enhancement" (Valin, 2018)
https://arxiv.org/abs/1709.08243

### 1.2 Source Code & Pre-built Binaries

**Original repository:**
```
https://github.com/xiph/rnnoise
```

**Pre-built with VST/LADSPA wrappers (recommended starting point):**
```
https://github.com/werman/noise-suppression-for-voice
Releases: https://github.com/werman/noise-suppression-for-voice/releases
```

Download `windows-rnnoise.zip` from releases — includes ready-to-use `rnnoise.dll`.

### 1.3 Native API

RNNoise exposes a minimal C API. Create P/Invoke bindings for these functions:

| Function | Signature | Purpose |
|----------|-----------|---------|
| `rnnoise_create` | `DenoiseState* rnnoise_create(RNNModel* model)` | Create processor state. Pass NULL for model to use built-in weights. |
| `rnnoise_destroy` | `void rnnoise_destroy(DenoiseState* st)` | Free processor state. |
| `rnnoise_process_frame` | `float rnnoise_process_frame(DenoiseState* st, float* out, const float* in)` | Process 480 samples. Returns VAD probability 0.0-1.0. |
| `rnnoise_get_frame_size` | `int rnnoise_get_frame_size()` | Returns 480 (constant). |

**Critical constraints:**
- Sample rate MUST be 48000 Hz — no other rates supported
- Frame size MUST be exactly 480 samples (10ms)
- Input/output are float arrays, values should be in reasonable range (internally scales by 32768)

### 1.4 Implementation Strategy

**Step 1: P/Invoke wrapper**

Create a static class `RNNoiseInterop` in `HotMic.Core/Plugins/AI/`. Use `CallingConvention.Cdecl`. Handle `IntPtr` for the opaque state pointer.

**Step 2: Frame buffer management**

The audio engine provides buffers of arbitrary size (128, 256, 512 samples). RNNoise requires exactly 480. Implement a frame accumulator:

```
Input audio arrives (arbitrary size)
    ↓
Accumulate into ring buffer
    ↓
When buffer has ≥480 samples:
    Extract exactly 480 samples
    Process with rnnoise_process_frame()
    Append result to output queue
    ↓
Drain output queue to audio callback
```

Key requirements:
- Pre-allocate all buffers (no allocations in audio path)
- Handle case where input size doesn't align with 480 (accumulate across callbacks)
- This adds up to 10ms latency (one frame of buffering)

**Step 3: Plugin wrapper class**

Create `RNNoisePlugin : IPlugin` that:
- Calls `rnnoise_create()` in `Initialize()` — store state pointer
- Calls `rnnoise_destroy()` in `Dispose()` — clean up
- Manages frame buffering in `Process(Span<float> buffer)`
- Stores last VAD probability as public property for UI metering
- Exposes reduction amount (wet/dry mix) as parameter

**Step 4: Sample rate validation**

In `Initialize(int sampleRate, int blockSize)`:
- If `sampleRate != 48000`, throw `ArgumentException` with message explaining the requirement
- Do NOT attempt runtime resampling — adds complexity and latency

### 1.5 Parameters to Expose

| Parameter | Range | Default | Purpose |
|-----------|-------|---------|---------|
| Reduction | 0-100% | 100% | Wet/dry mix. 0% = bypass, 100% = full suppression. Blend original and processed. |
| VAD Threshold | 0-95% | 0% | If VAD probability < threshold, output original audio instead of processed. Reduces artifacts during true silence. |

**Read-only output:**
| Property | Type | Purpose |
|----------|------|---------|
| VadProbability | float | Last VAD reading (0-1). Expose for UI speech indicator. |

### 1.6 Testing Approach

1. **Basic functionality:** Process white noise, verify output RMS is significantly lower
2. **Speech preservation:** Process clean speech recording, verify minimal quality loss (measure SNR)
3. **Frame buffering:** Feed various buffer sizes (128, 256, 480, 512, 1000), verify no glitches
4. **Edge cases:** Empty buffer, exact 480 samples, very large buffer
5. **Memory safety:** Run for extended period, verify no memory growth

---

## Part 2: DeepFilterNet Integration

### 2.1 What is DeepFilterNet?

DeepFilterNet is a more advanced neural network noise suppressor developed by Hendrik Schröter et al. at University of Erlangen-Nuremberg. Uses a two-stage architecture with "deep filtering" — applying learned FIR filters in frequency domain, not just gains.

**Key characteristics:**
- Full-band (48kHz) processing
- Significantly better quality than RNNoise, especially for non-stationary noise (keyboard clicks, dogs barking)
- Higher CPU usage (~5-15% single core depending on hardware)
- Available as Rust library (`libdf`) or ONNX models
- MIT license

**Architecture overview:**
1. STFT to convert to frequency domain
2. Encoder network extracts latent features
3. ERB decoder predicts gains for perceptual frequency bands
4. DF decoder predicts complex FIR filter coefficients for low frequencies
5. Apply ERB gains to full spectrum + deep filtering to low frequencies
6. ISTFT back to time domain

**Why "deep filtering" matters:** Regular gain-based suppression (like RNNoise) can only attenuate. Deep filtering can actually reshape the spectrum by applying learned convolution in frequency domain, allowing better reconstruction of speech corrupted by transient noise.

**Papers:**
- DeepFilterNet2: https://arxiv.org/abs/2205.05474
- DeepFilterNet3: https://arxiv.org/abs/2305.08227

### 2.2 Source Code & Models

**Repository:**
```
https://github.com/Rikorose/DeepFilterNet
```

**Pre-trained ONNX models (download from releases):**
```
https://github.com/Rikorose/DeepFilterNet/releases
→ DeepFilterNet3_onnx.tar.gz (~2.5MB)
```

Archive contains three ONNX files:
- `enc.onnx` — Encoder network
- `erb_dec.onnx` — ERB gain decoder
- `df_dec.onnx` — Deep filter coefficient decoder

### 2.3 Integration Approach: ONNX Runtime

Use Microsoft.ML.OnnxRuntime to run the ONNX models directly in C#.

**Why ONNX over native Rust:**
- Pure managed code (easier debugging)
- No Rust toolchain dependency
- Easy model updates (just replace .onnx files)
- Can add GPU acceleration later (DirectML, CUDA)

**NuGet packages needed:**
```
Microsoft.ML.OnnxRuntime         # Core runtime
Microsoft.ML.OnnxRuntime.Managed # Managed wrapper
```

Optional for GPU:
```
Microsoft.ML.OnnxRuntime.DirectML  # AMD/Intel/NVIDIA via DirectX
Microsoft.ML.OnnxRuntime.Gpu       # NVIDIA CUDA
```

### 2.4 STFT/ISTFT Requirement

DeepFilterNet operates in frequency domain. Must implement Short-Time Fourier Transform.

**Parameters for DeepFilterNet3:**
- FFT size: 960 samples
- Hop size: 480 samples (50% overlap)
- Window: Square-root Hann (ensures perfect reconstruction with overlap-add)

**STFT Algorithm (Analysis):**
1. Maintain input buffer of FFT_SIZE samples
2. When new samples arrive, shift buffer and append
3. Apply window function (element-wise multiply)
4. Compute FFT → complex spectrum (use MathNet.Numerics.IntegralTransforms)
5. Return spectrum for processing

**ISTFT Algorithm (Synthesis):**
1. Receive modified spectrum
2. Compute inverse FFT → time domain samples
3. Apply window function (same sqrt-Hann)
4. Overlap-add into output buffer
5. Output HOP_SIZE samples per frame

**Why sqrt-Hann window:** When you multiply analysis window × synthesis window, you get full Hann window. With 50% overlap, Hann windows sum to constant 1.0, giving perfect reconstruction (no amplitude modulation).

**Implementation location:** Create `HotMic.Core/Dsp/StftProcessor.cs` as reusable component.

### 2.5 ERB Scale

DeepFilterNet uses ERB (Equivalent Rectangular Bandwidth) frequency scale, which is perceptually motivated (matches human hearing).

**What you need:**
- Mapping from FFT bins to 32 ERB bands (for encoder input)
- Mapping from 32 ERB bands back to FFT bins (for applying gains)

**ERB band edges formula:**
```
erb_freq(n) = 229.0 * (10^(n / 21.4) - 1)
```

Create lookup tables at initialization:
- `int[] erb_to_fft_start` — first FFT bin for each ERB band
- `int[] erb_to_fft_end` — last FFT bin for each ERB band
- `float[] fft_to_erb_weights` — interpolation weights for applying gains

**Implementation location:** Create `HotMic.Core/Dsp/ErbScale.cs`.

### 2.6 Model Inference Pipeline

For each STFT frame (480 new samples):

```
┌─────────────────────────────────────────────────────────────┐
│ 1. STFT                                                     │
│    Input: 960 samples (480 new + 480 previous)              │
│    Output: Complex spectrum [481 bins]                       │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│ 2. Feature Extraction                                       │
│    - Compute magnitude spectrum                             │
│    - Map to 32 ERB bands (sum energy in each band)         │
│    - Normalize / apply any required preprocessing           │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│ 3. Encoder (enc.onnx)                                       │
│    Input: ERB features [1, 1, 32] + hidden state            │
│    Output: Latent representation + new hidden state         │
└─────────────────────────────────────────────────────────────┘
                            ↓
         ┌──────────────────┴──────────────────┐
         ↓                                      ↓
┌─────────────────────────┐    ┌─────────────────────────────┐
│ 4a. ERB Decoder         │    │ 4b. DF Decoder              │
│     (erb_dec.onnx)      │    │     (df_dec.onnx)           │
│     Output: 32 gains    │    │     Output: filter coeffs   │
└─────────────────────────┘    └─────────────────────────────┘
         ↓                                      ↓
┌─────────────────────────────────────────────────────────────┐
│ 5. Apply Processing                                         │
│    - Interpolate ERB gains to FFT bins                     │
│    - Multiply spectrum by gains (all frequencies)          │
│    - Apply deep filtering (low frequencies only)           │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│ 6. ISTFT                                                    │
│    Output: 480 processed samples                            │
└─────────────────────────────────────────────────────────────┘
```

### 2.7 Hidden State Management

All three ONNX models are RNN-based and require hidden state tensors:

**Requirements:**
- Initialize all states to zero at startup
- After each inference, copy output state to input state for next frame
- States must persist across frames (store as class fields)

**How to determine shapes:**
Inspect the ONNX models to find exact tensor shapes. Use Python:
```python
import onnx
model = onnx.load("enc.onnx")
for inp in model.graph.input:
    print(inp.name, [d.dim_value for d in inp.type.tensor_type.shape.dim])
```

Or use Netron (https://netron.app/) for visual inspection.

**Typical shapes (verify against actual models):**
- Encoder state: `[2, 1, 128]` or similar
- ERB decoder state: `[2, 1, 128]`
- DF decoder state: `[2, 1, 128]`

### 2.8 Deep Filtering Details

The DF decoder outputs complex filter coefficients for the first ~96 FFT bins (low frequencies where speech fundamentals live).

**Concept:**
- Normal gain-based filtering: `Y[k] = G[k] * X[k]`
- Deep filtering: `Y[k] = Σ H[k,n] * X[k-n]` — FIR filter per frequency bin

This allows the model to "fill in" masked speech content, not just attenuate noise.

**Application:**
The filter coefficients come as complex numbers. For each low-frequency bin:
1. Get current and previous spectral values (requires storing past few frames)
2. Apply convolution with predicted coefficients
3. Result replaces the gain-modified value for that bin

**Frame history:** Need to store past 2-5 STFT frames for the convolution. Pre-allocate circular buffer.

### 2.9 Parameters to Expose

| Parameter | Range | Default | Purpose |
|-----------|-------|---------|---------|
| Reduction | 0-100% | 100% | Wet/dry mix |
| Attenuation Limit | 6-60 dB | 40 dB | Maximum suppression. Prevents over-attenuation artifacts. Clamp gains to not exceed this. |
| Post-Filter | on/off | on | Additional spectral flooring to reduce musical noise artifacts |

### 2.10 Performance Optimization

**Session options for real-time:**
```csharp
var options = new SessionOptions();
options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
options.InterOpNumThreads = 1;  // Single-threaded between ops
options.IntraOpNumThreads = 2;  // Some parallelism within ops
```

**Tensor allocation:**
- Create `DenseTensor<float>` objects once in Initialize()
- Reuse for every frame — only copy data in, don't reallocate
- Use `tensor.Buffer.Span` for direct memory access

**Profiling:**
- Measure inference time per frame (should be <5ms for real-time at 480 sample hop)
- If too slow, try DirectML execution provider
- Consider running inference on background thread with lock-free queue (complex, only if needed)

---

## Part 3: Silero VAD (Optional Enhancement)

### 3.1 Purpose

Silero VAD provides more accurate voice activity detection than RNNoise's built-in VAD. Use it to:

1. **Smart gating:** Only run noise suppression when speech detected (reduces artifacts during silence)
2. **UI feedback:** Accurate speech activity indicator
3. **Combination:** Use Silero VAD to gate RNNoise (more accurate than RNNoise's own VAD)

### 3.2 Model Details

**Repository:** https://github.com/snakers4/silero-vad

**Model:** Single ONNX file, ~2MB, MIT license
```
https://github.com/snakers4/silero-vad/raw/master/src/silero_vad/data/silero_vad.onnx
```

**Constraints:**
- Requires 16kHz audio (NOT 48kHz)
- Accepts chunks of 256, 512, or 768 samples
- Has internal LSTM state that must persist between calls

### 3.3 Integration Strategy

**Resampling requirement:**
Your audio is 48kHz, Silero needs 16kHz. Implement simple 3:1 decimation:

1. Low-pass filter at 8kHz (Nyquist for 16kHz) — simple FIR filter
2. Take every 3rd sample

This doesn't need to be high quality — it's just for VAD detection, not audio output.

**Implementation options:**

Option A: Standalone plugin
- Create `SileroVadPlugin : IPlugin` that only computes VAD
- Doesn't modify audio, just exposes `VadProbability` property
- Other plugins can reference this value

Option B: Integrated into RNNoise
- Add Silero as optional VAD source within RNNoisePlugin
- When enabled, use Silero probability instead of RNNoise's built-in
- Simpler for users (one plugin instead of two)

**Recommendation:** Start with Option B for simplicity.

### 3.4 Parameters

| Parameter | Range | Default | Purpose |
|-----------|-------|---------|---------|
| Enabled | on/off | off | Whether to use Silero VAD |
| Threshold | 0.0-0.95 | 0.5 | Speech probability threshold |
| Padding | 0-500ms | 100ms | Keep "speaking" state for this duration after VAD drops |

---

## Part 4: Implementation Phases

### Phase 1: RNNoise MVP
**Estimated: 2-3 days**

- [ ] Copy `rnnoise.dll` to project, configure build output
- [ ] Create `RNNoiseInterop.cs` with P/Invoke declarations
- [ ] Create `FrameBuffer.cs` — accumulator for 480-sample frames
- [ ] Create `RNNoisePlugin.cs` implementing IPlugin
- [ ] Register plugin in factory/registry
- [ ] Basic integration test with audio file
- [ ] Wire up to UI (add to plugin list, show in chain)

**Exit criteria:** Can add RNNoise to plugin chain, audibly reduces noise.

### Phase 2: RNNoise Polish
**Estimated: 1-2 days**

- [ ] Add Reduction parameter (wet/dry)
- [ ] Add VAD Threshold parameter
- [ ] Expose VAD probability to UI (speech indicator)
- [ ] Handle errors gracefully (DLL not found, wrong sample rate)
- [ ] Unit tests for frame buffer
- [ ] Profile CPU usage

**Exit criteria:** Production-quality RNNoise integration.

### Phase 3: STFT Foundation
**Estimated: 2-3 days**

- [ ] Add MathNet.Numerics NuGet package
- [ ] Implement `StftProcessor.cs` with STFT/ISTFT
- [ ] Implement window function (sqrt-Hann)
- [ ] Test: sine wave round-trip (STFT → ISTFT) should match original
- [ ] Test: verify perfect reconstruction with overlap-add

**Exit criteria:** Working, tested STFT processor.

### Phase 4: DeepFilterNet Models
**Estimated: 2-3 days**

- [ ] Add Microsoft.ML.OnnxRuntime NuGet
- [ ] Download DeepFilterNet3 ONNX models, add to project
- [ ] Create `DeepFilterNetInference.cs` — loads and runs models
- [ ] Inspect models for exact input/output shapes
- [ ] Implement hidden state management
- [ ] Test: models load and run without error

**Exit criteria:** Can load models and run inference (output not yet correct).

### Phase 5: DeepFilterNet Full Pipeline
**Estimated: 3-4 days**

- [ ] Implement `ErbScale.cs` — ERB band mapping
- [ ] Connect STFT → feature extraction → encoder
- [ ] Implement ERB gain application
- [ ] Implement deep filtering (low-frequency FIR)
- [ ] Connect full pipeline → ISTFT
- [ ] Create `DeepFilterNetPlugin.cs` implementing IPlugin
- [ ] Test with real audio

**Exit criteria:** Working DeepFilterNet noise suppression.

### Phase 6: DeepFilterNet Optimization
**Estimated: 2-3 days**

- [ ] Profile CPU usage, identify bottlenecks
- [ ] Optimize tensor allocations (reuse buffers)
- [ ] Add attenuation limit parameter
- [ ] Test DirectML execution provider
- [ ] Handle model loading errors gracefully
- [ ] Extended stability testing

**Exit criteria:** Production-quality DeepFilterNet, acceptable CPU usage.

### Phase 7: Silero VAD Integration (Optional)
**Estimated: 2 days**

- [ ] Add Silero ONNX model to project
- [ ] Implement 48kHz → 16kHz resampler
- [ ] Integrate into RNNoisePlugin as optional VAD source
- [ ] Add UI toggle and speech indicator
- [ ] Test accuracy vs RNNoise built-in VAD

**Exit criteria:** Optional high-quality VAD available.

---

## Part 5: File Structure

```
src/HotMic.Core/
├── Plugins/
│   └── AI/
│       ├── RNNoiseInterop.cs         # P/Invoke for rnnoise.dll
│       ├── RNNoisePlugin.cs          # IPlugin wrapper
│       ├── DeepFilterNetPlugin.cs    # IPlugin wrapper  
│       ├── DeepFilterNetInference.cs # ONNX model handling
│       └── SileroVad.cs              # Optional VAD
├── Dsp/
│   ├── StftProcessor.cs              # STFT/ISTFT
│   ├── ErbScale.cs                   # ERB frequency mapping
│   ├── FrameBuffer.cs                # Accumulator buffer
│   └── Resampler.cs                  # 48k→16k for VAD
├── libs/
│   └── rnnoise/
│       └── rnnoise.dll               # Native library
└── models/
    ├── deepfilternet3/
    │   ├── enc.onnx
    │   ├── erb_dec.onnx
    │   └── df_dec.onnx
    └── silero/
        └── silero_vad.onnx
```

---

## Part 6: Error Handling

### Sample Rate Mismatch
Both models require 48kHz. In `Initialize()`:
- Check `sampleRate == 48000`
- If not, throw with clear message: "AI noise suppression requires 48kHz. Please configure your audio device to 48000 Hz sample rate."

### Missing Native Library
If `rnnoise.dll` not found:
- Catch `DllNotFoundException`
- Log the expected path
- Throw with message: "RNNoise library not found. Please ensure rnnoise.dll is in the application directory."

### Missing ONNX Models
If model files not found:
- Check paths in `Initialize()`
- Throw with message listing missing files and download URL

### Inference Failure
If ONNX inference throws:
- Catch exception, log details
- Set `IsBypassed = true` (graceful degradation)
- Notify user via event/callback

### Performance Issues
If processing takes too long:
- Implement timing measurement in `Process()`
- If consistently >80% of available time, log warning
- Consider auto-bypass with user notification

---

## Part 7: Testing Checklist

### Unit Tests
- [ ] FrameBuffer correctly accumulates various input sizes
- [ ] FrameBuffer outputs exact frame sizes
- [ ] STFT→ISTFT round-trip preserves signal (SNR > 90dB)
- [ ] ERB mapping covers full frequency range
- [ ] ERB interpolation is smooth (no discontinuities)

### Integration Tests  
- [ ] RNNoisePlugin loads and processes without crash
- [ ] DeepFilterNetPlugin loads and processes without crash
- [ ] Plugins work correctly in chain with other plugins
- [ ] Parameter changes take effect immediately
- [ ] State serialization/deserialization works

### Audio Quality Tests
- [ ] White noise: significant reduction (>20dB)
- [ ] Pink noise: significant reduction
- [ ] Fan noise (recording): clearly reduced
- [ ] Clean speech: minimal degradation (SNR loss <1dB)
- [ ] Speech + noise: noise reduced, speech preserved
- [ ] Keyboard clicks (DeepFilterNet): audibly reduced

### Stress Tests
- [ ] Run for 1 hour continuous: no memory growth
- [ ] Run for 1 hour: no audio glitches
- [ ] Rapid bypass toggle: no crashes or glitches
- [ ] Device disconnect/reconnect: graceful handling

---

## Appendix A: Key References

### RNNoise
- Paper: https://arxiv.org/abs/1709.08243
- Code: https://github.com/xiph/rnnoise
- Pre-built: https://github.com/werman/noise-suppression-for-voice
- Demo: https://jmvalin.ca/demo/rnnoise/

### DeepFilterNet  
- Paper v2: https://arxiv.org/abs/2205.05474
- Paper v3: https://arxiv.org/abs/2305.08227
- Code: https://github.com/Rikorose/DeepFilterNet

### Silero VAD
- Code: https://github.com/snakers4/silero-vad

### ONNX Runtime C#
- Docs: https://onnxruntime.ai/docs/api/csharp-api.html
- Examples: https://github.com/microsoft/onnxruntime/tree/main/csharp/sample

### DSP
- STFT overlap-add: https://ccrma.stanford.edu/~jos/sasp/Overlap_Add_OLA_STFT_Processing.html
- Window functions: https://en.wikipedia.org/wiki/Window_function#Hann_and_Hamming_windows
- ERB scale: https://en.wikipedia.org/wiki/Equivalent_rectangular_bandwidth

---

## Appendix B: Example Code Snippets

### P/Invoke Pattern for RNNoise

```csharp
internal static class RNNoiseInterop
{
    private const string DllName = "rnnoise";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr rnnoise_create(IntPtr model);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void rnnoise_destroy(IntPtr state);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float rnnoise_process_frame(
        IntPtr state,
        [Out] float[] output,
        [In] float[] input);
}
```

### ONNX Session Setup

```csharp
var options = new SessionOptions();
options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
options.IntraOpNumThreads = 2;

var session = new InferenceSession(modelPath, options);
```

### Sqrt-Hann Window Generation

```csharp
float[] CreateSqrtHannWindow(int size)
{
    var window = new float[size];
    for (int i = 0; i < size; i++)
    {
        float hann = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (size - 1)));
        window[i] = MathF.Sqrt(hann);
    }
    return window;
}
```

### ERB Frequency Calculation

```csharp
float ErbToHz(float erb)
{
    return 229.0f * (MathF.Pow(10f, erb / 21.4f) - 1f);
}

float HzToErb(float hz)
{
    return 21.4f * MathF.Log10(1f + hz / 229.0f);
}
```
