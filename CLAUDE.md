# CLAUDE.md - Instructions for Claude Code

You are working on **HotMic**, a Windows audio routing application. This file contains critical context and rules you must follow.

---

## What This Project Is

A streamlined alternative to VoiceMeeter Banana that:
- Takes 2 hardware mic inputs
- Routes through plugin chains (built-in DSP + VST3)
- Outputs to VB-Cable virtual audio device
- Has a clean, modern UI with full edit and minimal view modes

**User is experienced developer** - no need for excessive explanation. Be direct, write good code.

---

## Tech Stack (Non-Negotiable)

```
UI:           WPF + SkiaSharp overlay (SkiaSharp.Views.WPF)
Audio:        NAudio (WASAPI)
DSP:          Custom, built on NAudio.Dsp + Math.NET Numerics
VST3:         VST.NET (Jacobi.Vst.Core, Jacobi.Vst.Host.Interop)
MVVM:         CommunityToolkit.Mvvm
Config:       System.Text.Json
Runtime:      .NET 8, x64 only
Output:       VB-Cable (required external dependency)
```

Do not suggest alternative libraries unless there's a critical blocker.

---

## The One Rule That Matters Most

**NEVER ALLOCATE MEMORY ON THE AUDIO THREAD.**

This means in any code that runs in the WASAPI callback or `Process()` methods:

❌ **FORBIDDEN:**
```csharp
var list = new List<float>();           // Allocation
buffer.ToArray();                       // Allocation
string.Format(...);                     // Allocation
$"interpolated {string}";               // Allocation
samples.Where(x => x > 0);              // Allocates iterator
lock (obj) { }                          // Can block
Task.Run(() => { });                    // Allocates, wrong thread
Console.WriteLine();                    // I/O
```

✅ **CORRECT:**
```csharp
// Pre-allocated at init
private readonly float[] _tempBuffer = new float[MaxBlockSize];
private readonly ConcurrentQueue<ParamChange> _paramQueue = new();

// In audio callback - zero allocation
for (int i = 0; i < buffer.Length; i++)
{
    buffer[i] *= _gain;
}

// Cross-thread param changes - lock-free
while (_paramQueue.TryDequeue(out var change))
{
    ApplyParam(change);
}

// Meter values - atomic
Interlocked.Exchange(ref _peakLevel, peak);
```

---

## Project Structure

```
src/
├── HotMic.App/           # WPF application, Views, ViewModels, Controls
├── HotMic.Core/          # Audio engine, plugins, DSP (NO WPF REFS)
├── HotMic.Vst3/          # VST3 hosting via VST.NET
└── HotMic.Common/        # Shared types, configuration
```

**Key rule:** `HotMic.Core` must have ZERO references to WPF or UI. It should be testable in isolation.

---

## Core Interfaces You'll Implement

### IPlugin

```csharp
public interface IPlugin : IDisposable
{
    string Id { get; }
    string Name { get; }
    bool IsBypassed { get; set; }
    
    IReadOnlyList<PluginParameter> Parameters { get; }
    
    void Initialize(int sampleRate, int blockSize);
    void Process(Span<float> buffer);  // IN-PLACE, NO ALLOCATIONS
    void SetParameter(int index, float value);
    
    byte[] GetState();
    void SetState(byte[] state);
}
```

All built-in plugins implement this. VST3 plugins are wrapped to implement this.

### Built-in Plugins to Implement

1. **Compressor** - threshold, ratio, attack, release, makeup
2. **NoiseGate** - threshold, attack, hold, release
3. **ThreeBandEq** - low/mid/high gain + frequency
4. **FFTNoiseRemoval** - learn noise profile, reduction amount

---

## DSP Implementation Patterns

### Envelope Follower (used by compressor/gate)

```csharp
// attack/release coefficients: coeff = 1 - exp(-1 / (time_sec * sampleRate))
float attackCoeff = 1f - MathF.Exp(-1f / (_attackMs * 0.001f * _sampleRate));
float releaseCoeff = 1f - MathF.Exp(-1f / (_releaseMs * 0.001f * _sampleRate));

// Per-sample envelope following
float inputLevel = MathF.Abs(sample);
float coeff = inputLevel > _envelope ? attackCoeff : releaseCoeff;
_envelope += coeff * (inputLevel - _envelope);
```

### dB Conversions

```csharp
// Linear to dB
float dB = 20f * MathF.Log10(linear + 1e-10f);  // +epsilon to avoid log(0)

// dB to linear
float linear = MathF.Pow(10f, dB / 20f);
```

### Biquad Filter (for EQ)

Use NAudio.Dsp.BiQuadFilter or implement:

```csharp
public class BiquadFilter
{
    private float _a0, _a1, _a2, _b1, _b2;
    private float _z1, _z2;  // State
    
    public float Process(float input)
    {
        float output = _a0 * input + _a1 * _z1 + _a2 * _z2 - _b1 * _z1 - _b2 * _z2;
        _z2 = _z1;
        _z1 = input;
        return output;
    }
    
    // Calculate coefficients for low-shelf, high-shelf, peaking
    public void SetLowShelf(float freq, float gainDb, float q) { ... }
    public void SetHighShelf(float freq, float gainDb, float q) { ... }
    public void SetPeaking(float freq, float gainDb, float q) { ... }
}
```

---

## SkiaSharp UI Patterns

### Base Control

```csharp
public abstract class SkiaControl : SKElement
{
    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        Render(canvas, e.Info.Width, e.Info.Height);
    }
    
    protected abstract void Render(SKCanvas canvas, int width, int height);
    
    // Call to trigger repaint
    protected void Redraw() => InvalidateVisual();
}
```

### Meter Control (Peak + RMS)

```csharp
public class MeterControl : SkiaControl
{
    public static readonly DependencyProperty PeakLevelProperty = ...;
    public static readonly DependencyProperty RmsLevelProperty = ...;
    
    public float PeakLevel { get; set; }  // 0-1 range
    public float RmsLevel { get; set; }   // 0-1 range
    
    protected override void Render(SKCanvas canvas, int width, int height)
    {
        // Background
        using var bgPaint = new SKPaint { Color = new SKColor(0x1a, 0x1a, 0x1a) };
        canvas.DrawRect(0, 0, width, height, bgPaint);
        
        // RMS bar
        float rmsHeight = height * RmsLevel;
        using var rmsPaint = new SKPaint { Color = GetLevelColor(RmsLevel) };
        canvas.DrawRect(2, height - rmsHeight, width - 4, rmsHeight, rmsPaint);
        
        // Peak marker
        float peakY = height * (1 - PeakLevel);
        using var peakPaint = new SKPaint { Color = SKColors.White, StrokeWidth = 2 };
        canvas.DrawLine(0, peakY, width, peakY, peakPaint);
    }
    
    private SKColor GetLevelColor(float level) =>
        level > 0.95f ? new SKColor(0xff, 0x00, 0x00) :  // Red
        level > 0.70f ? new SKColor(0xff, 0xff, 0x00) :  // Yellow
                        new SKColor(0x00, 0xff, 0x00);   // Green
}
```

---

## Audio Engine Skeleton

```csharp
public class AudioEngine : IDisposable
{
    private WasapiCapture _capture1;
    private WasapiCapture _capture2;
    private WasapiOut _output;
    
    private readonly ChannelStrip _channel1;
    private readonly ChannelStrip _channel2;
    
    private readonly ConcurrentQueue<ParameterChange> _paramQueue = new();
    
    public void Start()
    {
        // Init WASAPI devices
        // Hook DataAvailable callbacks
        // Start capture and playback
    }
    
    private void OnDataAvailable(object sender, WaveInEventArgs e)
    {
        // Convert byte[] to float[]
        // Process parameter queue
        // Process channel strip
        // Publish meter values
        // Write to output
    }
}
```

---

## Configuration Schema

```json
{
  "audioSettings": {
    "inputDevice1": "device-guid",
    "inputDevice2": "device-guid",
    "outputDevice": "VB-Cable",
    "sampleRate": 48000,
    "bufferSize": 256
  },
  "channels": [
    {
      "id": 1,
      "name": "Mic 1",
      "inputGain": 0.0,
      "outputGain": 0.0,
      "muted": false,
      "plugins": [
        {
          "type": "builtin:noisegate",
          "bypassed": false,
          "parameters": {
            "threshold": -40,
            "attack": 1,
            "hold": 50,
            "release": 100
          }
        },
        {
          "type": "builtin:compressor",
          "bypassed": false,
          "parameters": { ... }
        }
      ]
    }
  ],
  "ui": {
    "viewMode": "full",
    "alwaysOnTop": false,
    "windowPosition": { "x": 100, "y": 100 },
    "windowSize": { "width": 800, "height": 600 }
  }
}
```

---

## Build & Run (Windows only)

- This agent runs in WSL and cannot build, run, test, or publish the Windows app here.
- Ask the user to run the appropriate commands in their Windows environment and share the results.
- Never claim build/test success unless the user ran the commands and reported the output.
- If the user cannot run them, mark the change as unverified.

---

## Git Hygiene

- Prefer small, logical commits; avoid noisy debug commits.
- Push only when changes are coherent and relevant checks pass; otherwise record status and reason in `bd`.
- Do not push broken builds to shared branches.

---

## When Implementing Features

1. **Read REQUIREMENTS.md first** for the full spec
2. **Check AGENTS.md** for detailed patterns and phase order
3. **Core before UI** - always implement in HotMic.Core first, then wire up
4. **Test DSP** - write unit tests for all plugins with known signal inputs
5. **Profile audio path** - use VS profiler to verify zero allocations

---

## Quick Reference: What Goes Where

| Thing | Location |
|-------|----------|
| Audio capture/playback | `Core/Engine/AudioEngine.cs` |
| Individual channel processing | `Core/Engine/ChannelStrip.cs` |
| Plugin interface | `Core/Plugins/IPlugin.cs` |
| Built-in plugins | `Core/Plugins/BuiltIn/` |
| Meter calculation | `Core/Metering/MeterProcessor.cs` |
| ViewModels | `App/ViewModels/` |
| SkiaSharp controls | `App/Controls/` |
| Configuration types | `Common/Configuration/` |

---

## Things I Should NOT Do

- Don't add NuGet packages not in the tech stack without asking
- Don't use `async/await` in the audio processing path
- Don't create new threads for audio - NAudio handles this
- Don't use `dynamic` or reflection in hot paths
- Don't add excessive comments - the code should be self-documenting
- Don't create abstractions that aren't needed yet (YAGNI)

---

## Definition of Done Checklist

Before considering any feature complete:

- [ ] Builds without warnings (verified by a user-run Windows build)
- [ ] No allocations in audio callback (verify via profiler or code review)
- [ ] Unit tests for any DSP/logic code
- [ ] Works with actual audio devices (manual test)
- [ ] UI updates smoothly without blocking audio
- [ ] Configuration saves and loads correctly
