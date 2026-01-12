# AGENTS.md - Implementation Guide for Coding Agents

This document provides structured guidance for AI coding agents working on the HotMic project. Follow these guidelines to ensure consistent, high-quality implementation.

---

## Project Overview

**HotMic** is a Windows audio routing application that:
- Captures 2 hardware mic inputs via WASAPI
- Processes through configurable plugin chains (built-in + VST3)
- Outputs to VB-Cable virtual audio device
- Provides modern WPF UI with SkiaSharp rendering

---

## Architecture Rules

### Strict Separation of Concerns

```
┌─────────────────────────────────────────────────────────────┐
│                    UI LAYER (WPF + SkiaSharp)               │
│         • No audio processing logic                         │
│         • No direct NAudio references                       │
│         • Communicates via ViewModels only                  │
└─────────────────────────────────────────────────────────────┘
                              │
                    ViewModels (CommunityToolkit.Mvvm)
                              │
┌─────────────────────────────────────────────────────────────┐
│                    CORE LAYER (HotMic.Core)              │
│         • Audio engine                                       │
│         • Plugin management                                  │
│         • No WPF references                                  │
└─────────────────────────────────────────────────────────────┘
```

### Audio Thread Safety Rules

**CRITICAL: The audio callback thread has strict requirements.**

**NEVER do these on the audio thread:**
- Allocate memory (`new`, LINQ that allocates, string operations)
- Lock on shared objects (`lock`, `Monitor`, `Mutex`)
- Call UI code or raise events to UI
- File I/O or logging
- Access `Dictionary<>` or other non-thread-safe collections

**ALWAYS do these:**
- Pre-allocate all buffers at initialization
- Use lock-free patterns for cross-thread communication
- Use `Interlocked` operations for atomic updates
- Use pre-allocated `ConcurrentQueue<T>` for parameter changes

### Lock-Free Communication Pattern

```csharp
// Parameter changes: UI → Audio Thread
public class ParameterChange
{
    public int ChannelId { get; init; }
    public ParameterType Type { get; init; }
    public float Value { get; init; }
}

// Pre-allocate queue at startup
private readonly ConcurrentQueue<ParameterChange> _parameterQueue = new();

// UI thread enqueues
_parameterQueue.Enqueue(new ParameterChange { ... });

// Audio thread dequeues (non-blocking)
while (_parameterQueue.TryDequeue(out var change))
{
    ApplyParameter(change);
}
```

### Meter Value Publishing

```csharp
// Audio thread writes (atomic)
Interlocked.Exchange(ref _peakLevel, calculatedPeak);
Interlocked.Exchange(ref _rmsLevel, calculatedRms);

// UI thread reads (atomic)
var peak = Interlocked.CompareExchange(ref _peakLevel, 0, 0);
var rms = Interlocked.CompareExchange(ref _rmsLevel, 0, 0);
```

---

## Implementation Order

Follow this exact order to ensure dependencies are satisfied:

### Phase 1: Foundation
```
1.1  Create solution structure and projects
1.2  Implement AudioEngine skeleton (WASAPI init, callback setup)
1.3  Implement basic audio passthrough (input → output)
1.4  Implement MeterProcessor (peak + RMS calculation)
1.5  Create SkiaSharp meter control
1.6  Wire up meter display to audio engine
1.7  Add device selection (input + VB-Cable output detection)
```

### Phase 2: Channel Strip
```
2.1  Implement ChannelStrip class (gain stages + meter points)
2.2  Implement IPlugin interface
2.3  Implement PluginChain (ordered processing)
2.4  Create ChannelStripViewModel
2.5  Create ChannelStripControl (SkiaSharp)
2.6  Wire up gain controls with lock-free parameter passing
```

### Phase 3: Built-in Plugins
```
3.1  Implement GainPlugin (trivial, validates pipeline)
3.2  Implement NoiseGatePlugin
3.3  Implement CompressorPlugin  
3.4  Implement ThreeBandEqPlugin
3.5  Implement FFTNoiseRemovalPlugin
3.6  Create plugin parameter UI controls
```

### Phase 4: Plugin Chain UI
```
4.1  Create PluginSlotControl
4.2  Implement drag-and-drop reordering
4.3  Create plugin browser/selector dialog
4.4  Implement plugin bypass toggle
4.5  Wire up plugin parameter editing
```

### Phase 5: VST3 Support
```
5.1  Set up VST.NET references
5.2  Implement VST3 plugin scanner (separate process)
5.3  Implement Vst3PluginWrapper : IPlugin
5.4  Implement VST3 editor window hosting
5.5  Implement VST3 state save/restore
```

### Phase 6: Views & Polish
```
6.1  Implement MinimalView
6.2  Implement view switching
6.3  Implement always-on-top toggle
6.4  Implement configuration persistence
6.5  Add settings dialog
6.6  Error handling and edge cases
```

---

## Code Patterns

### Plugin Interface

```csharp
public interface IPlugin : IDisposable
{
    string Id { get; }
    string Name { get; }
    bool IsBypassed { get; set; }
    
    IReadOnlyList<PluginParameter> Parameters { get; }
    
    void Initialize(int sampleRate, int blockSize);
    void Process(Span<float> buffer);
    void SetParameter(int index, float value);
    
    // State serialization
    byte[] GetState();
    void SetState(byte[] state);
}
```

### Plugin Parameter

```csharp
public record PluginParameter
{
    public int Index { get; init; }
    public string Name { get; init; }
    public float MinValue { get; init; }
    public float MaxValue { get; init; }
    public float DefaultValue { get; init; }
    public string Unit { get; init; }  // "dB", "ms", "%", "Hz"
    public Func<float, string> FormatValue { get; init; }
}
```

### Channel Strip Structure

```csharp
public class ChannelStrip
{
    private float _inputGain = 1.0f;
    private float _outputGain = 1.0f;
    private readonly PluginChain _pluginChain;
    private readonly MeterProcessor _inputMeter;
    private readonly MeterProcessor _outputMeter;
    
    public void Process(Span<float> buffer)
    {
        // Apply input gain
        ApplyGain(buffer, _inputGain);
        
        // Measure input
        _inputMeter.Process(buffer);
        
        // Process plugin chain
        _pluginChain.Process(buffer);
        
        // Apply output gain
        ApplyGain(buffer, _outputGain);
        
        // Measure output
        _outputMeter.Process(buffer);
    }
}
```

### SkiaSharp Control Base

```csharp
public abstract class SkiaControl : SKElement
{
    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;
        
        canvas.Clear(SKColors.Transparent);
        Render(canvas, info.Width, info.Height);
    }
    
    protected abstract void Render(SKCanvas canvas, int width, int height);
}
```

### Meter Rendering

```csharp
public class MeterControl : SkiaControl
{
    public float PeakLevel { get; set; }  // 0.0 to 1.0
    public float RmsLevel { get; set; }   // 0.0 to 1.0
    
    protected override void Render(SKCanvas canvas, int width, int height)
    {
        // Background
        canvas.DrawRect(0, 0, width, height, _backgroundPaint);
        
        // RMS bar (filled)
        var rmsHeight = height * RmsLevel;
        var rmsRect = new SKRect(2, height - rmsHeight, width - 2, height);
        canvas.DrawRect(rmsRect, GetMeterPaint(RmsLevel));
        
        // Peak line (thin marker)
        var peakY = height * (1 - PeakLevel);
        canvas.DrawLine(0, peakY, width, peakY, _peakLinePaint);
    }
    
    private SKPaint GetMeterPaint(float level)
    {
        if (level > 0.95f) return _redPaint;
        if (level > 0.7f) return _yellowPaint;
        return _greenPaint;
    }
}
```

---

## DSP Implementation Guidelines

### General DSP Rules

1. **Always process in-place** when possible to avoid allocations
2. **Use `Span<float>`** for all buffer passing
3. **Pre-calculate coefficients** when parameters change, not per-sample
4. **Use `MathF`** not `Math` for float operations
5. **Avoid branches in tight loops** - use branchless techniques where beneficial

### DSP Validation vs UI

- **Do not change DSP algorithms to match UI behavior.** If the UI does not reflect audible behavior, fix the UI scaling/meters/labels first.
- **Do change DSP algorithms when there are real bugs,** but validate changes against reference implementations or standard formulas for that DSP class.
- **Document the reference** with a concise inline comment near the relevant code so intent and expected behavior are clear.

### Compressor Implementation Outline

```csharp
public class CompressorPlugin : IPlugin
{
    // Parameters
    private float _thresholdDb = -20f;
    private float _ratio = 4f;
    private float _attackMs = 10f;
    private float _releaseMs = 100f;
    private float _makeupDb = 0f;
    
    // Computed coefficients (update when params change)
    private float _thresholdLinear;
    private float _attackCoeff;
    private float _releaseCoeff;
    private float _makeupLinear;
    
    // State
    private float _envelope = 0f;
    
    public void Process(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float input = buffer[i];
            float inputAbs = MathF.Abs(input);
            
            // Envelope follower
            float coeff = inputAbs > _envelope ? _attackCoeff : _releaseCoeff;
            _envelope = _envelope + coeff * (inputAbs - _envelope);
            
            // Gain computation (log domain)
            float gainDb = 0f;
            if (_envelope > _thresholdLinear)
            {
                float overDb = 20f * MathF.Log10(_envelope / _thresholdLinear);
                gainDb = overDb * (1f - 1f / _ratio);
            }
            
            // Apply gain
            float gainLinear = MathF.Pow(10f, -gainDb / 20f) * _makeupLinear;
            buffer[i] = input * gainLinear;
        }
    }
}
```

### FFT Noise Removal Outline

```csharp
public class FFTNoiseRemovalPlugin : IPlugin
{
    private const int FFTSize = 2048;
    private const int HopSize = FFTSize / 2;  // 50% overlap
    
    private float[] _inputBuffer;      // Circular buffer
    private float[] _outputBuffer;     // Overlap-add buffer
    private float[] _fftReal;
    private float[] _fftImag;
    private float[] _window;           // Hann window
    private float[] _noiseProfile;     // Learned noise magnitude spectrum
    private bool _learning = false;
    
    public void LearnNoiseProfile()
    {
        _learning = true;
        // Accumulate frames, average magnitude spectrum
    }
    
    public void Process(Span<float> buffer)
    {
        // Overlap-add STFT processing
        // 1. Window input
        // 2. FFT
        // 3. Spectral subtraction: mag = max(mag - noiseProfile * reduction, floor)
        // 4. IFFT
        // 5. Overlap-add to output
    }
}
```

---

## File Organization

```
HotMic/
├── src/
│   ├── HotMic.App/
│   │   ├── App.xaml(.cs)
│   │   ├── MainWindow.xaml(.cs)
│   │   ├── Views/
│   │   │   ├── FullEditView.xaml(.cs)
│   │   │   └── MinimalView.xaml(.cs)
│   │   ├── ViewModels/
│   │   │   ├── MainViewModel.cs
│   │   │   ├── ChannelStripViewModel.cs
│   │   │   └── PluginViewModel.cs
│   │   ├── Controls/
│   │   │   ├── SkiaControl.cs
│   │   │   ├── MeterControl.cs
│   │   │   ├── KnobControl.cs
│   │   │   ├── ChannelStripControl.cs
│   │   │   └── PluginSlotControl.cs
│   │   ├── Converters/
│   │   ├── Themes/
│   │   │   └── Dark.xaml
│   │   └── Resources/
│   │
│   ├── HotMic.Core/
│   │   ├── Engine/
│   │   │   ├── AudioEngine.cs
│   │   │   ├── AudioGraph.cs
│   │   │   ├── ChannelStrip.cs
│   │   │   └── DeviceManager.cs
│   │   ├── Plugins/
│   │   │   ├── IPlugin.cs
│   │   │   ├── PluginChain.cs
│   │   │   ├── PluginParameter.cs
│   │   │   └── BuiltIn/
│   │   │       ├── CompressorPlugin.cs
│   │   │       ├── NoiseGatePlugin.cs
│   │   │       ├── ThreeBandEqPlugin.cs
│   │   │       └── FFTNoiseRemovalPlugin.cs
│   │   ├── Metering/
│   │   │   └── MeterProcessor.cs
│   │   ├── Threading/
│   │   │   └── LockFreeQueue.cs
│   │   └── Dsp/
│   │       ├── DspUtils.cs
│   │       ├── EnvelopeFollower.cs
│   │       └── BiquadFilter.cs
│   │
│   ├── HotMic.Vst3/
│   │   ├── Vst3PluginHost.cs
│   │   ├── Vst3PluginWrapper.cs
│   │   └── Vst3Scanner.cs
│   │
│   └── HotMic.Common/
│       ├── Configuration/
│       │   ├── AppConfig.cs
│       │   ├── ChannelConfig.cs
│       │   └── ConfigManager.cs
│       └── Models/
│           └── AudioDevice.cs
│
├── tests/ (verification-only; not used for long-term behavior)
│   ├── HotMic.Core.Tests/
│   │   ├── Plugins/
│   │   │   ├── CompressorPluginTests.cs
│   │   │   └── NoiseGatePluginTests.cs
│   │   └── Engine/
│   │       └── ChannelStripTests.cs
│   └── HotMic.Dsp.Tests/
│
└── docs/
    ├── REQUIREMENTS.md
    ├── AGENTS.md
    └── CLAUDE.md
```

---

## Testing Policy

This project should not rely on long-term unit or integration tests to control behavior.
Temporary, isolated verification tests are allowed (for example, DSP math or FFT correctness), but should be clearly scoped and may be removed once validated.

Still document complex DSP or UI behavior with concise inline comments near the relevant code so intent and assumptions are captured in context.

---

## Error Handling

### Audio Device Errors

```csharp
try
{
    _wasapiCapture.StartRecording();
}
catch (COMException ex) when (ex.HResult == AUDCLNT_E_DEVICE_INVALIDATED)
{
    // Device disconnected - notify UI, attempt recovery
    _eventAggregator.Publish(new DeviceDisconnectedEvent(deviceId));
    ScheduleDeviceRecovery();
}
```

### VST3 Plugin Errors

```csharp
public class Vst3PluginWrapper : IPlugin
{
    public void Process(Span<float> buffer)
    {
        try
        {
            _vstPlugin.Process(buffer);
        }
        catch (Exception ex)
        {
            // Log error, bypass plugin, don't crash audio thread
            _logger.Error($"VST3 plugin {Name} crashed: {ex.Message}");
            IsBypassed = true;
            _eventAggregator.Publish(new PluginCrashedEvent(Id, ex.Message));
        }
    }
}
```

---

## Common Pitfalls to Avoid

| Pitfall | Correct Approach |
|---------|------------------|
| Creating `List<T>` in audio callback | Pre-allocate arrays at init |
| Using `Task.Run` in audio thread | Use lock-free queues |
| Binding directly to audio engine | Bind to ViewModels, poll values |
| `Math.Pow` for dB conversion | Use lookup tables or `MathF` |
| Catching all exceptions in audio | Only catch specific, recoverable errors |
| Updating UI from audio callback | Use `Dispatcher.InvokeAsync` or timer polling |
| String formatting in audio thread | Never - defer to UI thread |
| Using `Dictionary` for plugin params | Use fixed-size array indexed by param ID |
| Tweaking DSP to make UI "look right" | Fix the UI or meters; only change DSP when validated against a reference |

---

## WSL Build Mirror

This repo uses a WSL source tree with build outputs redirected to a Windows NTFS path so JetBrains Rider on Windows can build/run while keeping all tooling in WSL. The mapping is defined in `Directory.Build.props` via `BaseOutputPath` and `BaseIntermediateOutputPath`.

---

## Build & Run Commands

```bash
# Restore dependencies
dotnet restore

# Build debug
dotnet build

# Build release
dotnet build -c Release

# Run application
dotnet run --project src/HotMic.App

# Publish self-contained
dotnet publish src/HotMic.App -c Release -r win-x64 --self-contained
```

---

## Definition of Done

A feature is complete when:

- [ ] Implementation matches requirements in REQUIREMENTS.md
- [ ] No memory allocations in audio callback path (verify with profiler)
- [ ] UI remains responsive during audio processing
- [ ] No audio glitches under normal operation
- [ ] Code follows patterns in this document
- [ ] XML documentation on public APIs
- [ ] Complex functionality has concise inline comments explaining intent

## Landing the Plane (Session Completion)

**When ending a work session**, you MUST complete ALL steps below. Work is NOT complete until `git push` succeeds.

**MANDATORY WORKFLOW:**

1. **File issues for remaining work** - Create issues for anything that needs follow-up
2. **Run quality gates** (if code changed) - Builds, linters, manual validation notes
3. **Update issue status** - Close finished work, update in-progress items
4. **PUSH TO REMOTE** - This is MANDATORY:
   ```bash
   git pull --rebase
   bd sync
   git push
   git status  # MUST show "up to date with origin"
   ```
5. **Clean up** - Clear stashes, prune remote branches
6. **Verify** - All changes committed AND pushed
7. **Hand off** - Provide context for next session

**CRITICAL RULES:**
- Work is NOT complete until `git push` succeeds
- NEVER stop before pushing - that leaves work stranded locally
- NEVER say "ready to push when you are" - YOU must push
- If push fails, resolve and retry until it succeeds
Use 'bd' for task tracking
