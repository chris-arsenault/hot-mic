# Visualizer Architecture

This document describes the refactored visualization system that replaces the monolithic VocalSpectrographPlugin with a modular, composable architecture.


## Goals

1. **Smaller surface area**: Each visualizer has only its relevant parameters
2. **No recomputation**: Analysis runs once, all visualizers consume shared results
3. **Demand-driven**: Analysis stages only run when a visualizer needs them
4. **Composable**: Overlays can be layered on base visualizers
5. **Synchronized**: Multiple windows can view the same timeline

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│                              AudioEngine                                 │
│                                                                          │
│  Mic → PluginChain.Process() ───┬──→ Output                             │
│                                 │                                        │
│                                 ▼                                        │
│                        ┌────────────────┐                               │
│                        │  AnalysisTap   │                               │
│                        └───────┬────────┘                               │
│                                │                                         │
└────────────────────────────────┼────────────────────────────────────────┘
                                 │
                                 ▼
┌────────────────────────────────────────────────────────────────────────┐
│                       AnalysisOrchestrator                              │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │                      Analysis Providers                          │  │
│  │  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌────────────┐  │  │
│  │  │  Provider   │ │  Provider   │ │  Provider   │ │  Provider  │  │  │
│  │  └─────────────┘ └─────────────┘ └─────────────┘ └────────────┘  │  │
│  │  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐                 │  │
│  │  │  Waveform   │ │   Speech    │ │  Spectral   │                 │  │
│  │  │  Provider   │ │  Metrics    │ │  Features   │                 │  │
│  │  └─────────────┘ └─────────────┘ └─────────────┘                 │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│                                 │                                       │
│                                 ▼                                       │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │                    AnalysisResultStore                           │  │
│  │  • Spectrogram frames (versioned ring buffer)                    │  │
│  │  • Pitch/voicing track                                           │  │
│  │  • Harmonic peaks                                                │  │
│  │  • Waveform envelope                                             │  │
│  │  • Speech metrics                                                │  │
│  └──────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────┬───────────────────────────────────────┘
                                  │
                    ┌─────────────┴─────────────┐
                    │    VisualizerSyncHub      │
                    │  (shared timeline state)  │
                    └─────────────┬─────────────┘
                                  │
          ┌───────────────────────┼───────────────────────┐
          │                       │                       │
          ▼                       ▼                       ▼
   ┌────────────┐          ┌────────────┐          ┌────────────┐
   │ Visualizer │          │ Visualizer │          │ Visualizer │
   │  Window 1  │          │  Window 2  │          │  Window 3  │
   │            │          │            │          │            │
   │ Spectrogram│          │  Waveform  │          │Speech Coach│
   │ + Overlays │          │            │          │            │
   └────────────┘          └────────────┘          └────────────┘
```

## Components

### AnalysisTap

Location: `HotMic.Core/Analysis/AnalysisTap.cs`

Fixed interception point in AudioEngine. Not a plugin. Captures post-chain audio for analysis.

```csharp
public sealed class AnalysisTap
{
    private readonly AnalysisOrchestrator _orchestrator;

    /// <summary>
    /// Called from audio thread after plugin chain processing.
    /// Zero-overhead when no visualizers are active.
    /// </summary>
    public void Capture(ReadOnlySpan<float> buffer)
    {
        if (_orchestrator.HasActiveConsumers)
            _orchestrator.EnqueueAudio(buffer);
    }
}
```

Integration point in AudioEngine (after PluginChain.Process):

```csharp
// In AudioEngine processing loop
_pluginChain.Process(buffer);
_analysisTap.Capture(buffer);  // Add this line
// ... continue to output
```

### AnalysisOrchestrator

Location: `HotMic.Core/Analysis/AnalysisOrchestrator.cs`

Owns the analysis thread. Activates providers based on consumer demand.

```csharp
public sealed class AnalysisOrchestrator : IDisposable
{
    // Shared state
    public IAnalysisResultStore Results { get; }
    public bool HasActiveConsumers => _consumerCount > 0;

    // Providers (lazy-initialized, demand-activated)
    private readonly SpectrogramProvider _spectrogram;
    private readonly PitchProvider _pitch;
    private readonly HarmonicProvider _harmonics;
    private readonly WaveformProvider _waveform;
    private readonly SpeechMetricsProvider _speechMetrics;
    private readonly SpectralFeaturesProvider _spectralFeatures;

    // Consumer management
    public IDisposable Subscribe(AnalysisCapabilities required);

    // Analysis configuration (shared by all consumers)
    public AnalysisConfiguration Config { get; }
}
```

Provider activation logic:

```csharp
private AnalysisCapabilities ComputeActiveCapabilities()
{
    var active = AnalysisCapabilities.None;
    foreach (var consumer in _consumers)
        active |= consumer.RequiredCapabilities;
    return active;
}

private void AnalysisLoop()
{
    while (!_cts.IsCancellationRequested)
    {
        var caps = ComputeActiveCapabilities();

        // Only run what's needed
        if (caps.HasFlag(AnalysisCapabilities.Spectrogram))
            _spectrogram.Process(...);

        if (caps.HasFlag(AnalysisCapabilities.Pitch))
            _pitch.Process(...);


        // etc.
    }
}
```

### AnalysisResultStore

Location: `HotMic.Core/Analysis/AnalysisResultStore.cs`

Thread-safe storage for all analysis results. Version-controlled for torn-read prevention.

```csharp
public interface IAnalysisResultStore
{
    // Frame info
    long LatestFrameId { get; }
    int AvailableFrames { get; }
    int FrameCapacity { get; }
    AnalysisConfiguration Config { get; }

    // Bulk reads (for rendering)
    bool TryGetSpectrogramRange(long startFrame, int count,
                                 Span<float> magnitudes,
                                 out int framesCopied);

    bool TryGetPitchRange(long startFrame, int count,
                          Span<float> pitches,
                          Span<float> confidences,
                          Span<byte> voicing,
                          out int framesCopied);

                            Span<float> bandwidths,
                            out int framesCopied);

    bool TryGetHarmonicRange(long startFrame, int count,
                             Span<float> frequencies,
                             Span<float> magnitudes,
                             out int framesCopied);

    bool TryGetWaveformRange(long startFrame, int count,
                             Span<float> min,
                             Span<float> max,
                             out int framesCopied);

    bool TryGetSpeechMetricsRange(long startFrame, int count,
                                   Span<SpeechMetricsFrame> metrics,
                                   out int framesCopied);
}
```

### VisualizerSyncHub

Location: `HotMic.Core/Analysis/VisualizerSyncHub.cs`

Coordinates timeline view across multiple visualizer windows.

```csharp
public sealed class VisualizerSyncHub
{
    // Shared view range (in frames)
    public long ViewStartFrame { get; private set; }
    public long ViewEndFrame { get; private set; }

    // Time window in seconds
    public float TimeWindowSeconds { get; set; } = 5f;

    // Scroll the view (all attached windows move together)
    public void ScrollBy(int frameDelta);
    public void ScrollTo(long frameId);

    // Follow mode: auto-scroll to latest
    public bool FollowLatest { get; set; } = true;

    // Notify all windows to redraw
    public event Action? Invalidated;
    public void Invalidate();

    // View range changed (for scroll sync)
    public event Action<long, long>? ViewRangeChanged;
}
```

### IVisualizer

Location: `HotMic.Core/Visualization/IVisualizer.cs`

Interface for all visualizers (replaces IPlugin for visualization).

```csharp
public interface IVisualizer : IDisposable
{
    string Id { get; }
    string Name { get; }

    /// <summary>
    /// What analysis this visualizer needs. Used by orchestrator
    /// to activate only required providers.
    /// </summary>
    AnalysisCapabilities RequiredCapabilities { get; }

    /// <summary>
    /// Display parameters only. Much smaller than plugin params.
    /// </summary>
    IReadOnlyList<VisualizerParameter> Parameters { get; }

    /// <summary>
    /// Attach to analysis store and sync hub.
    /// </summary>
    void Attach(IAnalysisResultStore store, IVisualizerSyncHub sync);

    /// <summary>
    /// Render to the provided canvas within bounds.
    /// Called at 60Hz from UI thread.
    /// </summary>
    void Render(SKCanvas canvas, SKRect bounds, RenderContext context);
}

public readonly struct RenderContext
{
    public long ViewStartFrame { get; init; }
    public long ViewEndFrame { get; init; }
    public long LatestFrame { get; init; }
    public float TimeWindowSeconds { get; init; }
}

[Flags]
public enum AnalysisCapabilities
{
    None = 0,
    Spectrogram = 1 << 0,
    Pitch = 1 << 1,
    Harmonics = 1 << 3,
    Waveform = 1 << 4,
    SpeechMetrics = 1 << 5,
    VoicingState = 1 << 6,
    SpectralFeatures = 1 << 7,
}
```

### IOverlay

Location: `HotMic.Core/Visualization/IOverlay.cs`

Lightweight interface for overlays that render on top of a base visualizer.

```csharp
public interface IOverlay : IVisualizer
{
    /// <summary>
    /// Overlay rendering order. Lower values render first (behind).
    /// </summary>
    int ZOrder { get; }
}
```

## Visualizers

### Base Visualizers

| Visualizer | Required Capabilities | Parameters |
|------------|----------------------|------------|
| `SpectrogramVisualizer` | Spectrogram | TimeWindow, FreqMin, FreqMax, FreqScale, DbMin, DbMax, ColorMap, Brightness, Gamma, Contrast |
| `WaveformVisualizer` | Waveform | TimeWindow, AmplitudeScale, ShowEnvelope |
| `PitchMeterVisualizer` | Pitch, VoicingState | VoiceRange, ShowCents, ReferenceA4 |
| `SpeechCoachVisualizer` | SpeechMetrics, Pitch, VoicingState | RateWindow, ShowMarkers, ShowRate, ShowPauses, ShowClarity |

### Overlays

| Overlay | Required Capabilities | Parameters |
|---------|----------------------|------------|
| `PitchOverlay` | Pitch, VoicingState | Color, LineWidth, ShowConfidence |
| `HarmonicOverlay` | Harmonics, Pitch | DisplayMode (Detected/Theoretical/Both), Color |
| `VoicingOverlay` | VoicingState | Color, Opacity |
| `RangeGuidesOverlay` | None | VoiceRange, ShowNotes |

## Window Architecture

### VisualizerWindow

Location: `HotMic.App/Views/VisualizerWindow.cs`

Base window class for single-visualizer windows.

```csharp
public class VisualizerWindow : Window
{
    protected readonly IVisualizer _visualizer;
    protected readonly IVisualizerSyncHub _sync;
    protected readonly SKGLControl _surface;
    protected readonly OverlayBar _overlayBar;
    protected readonly ParameterPanel _parameterPanel;

    // Rendering at 60Hz
    private readonly DispatcherTimer _renderTimer;

    protected virtual void OnPaint(SKCanvas canvas)
    {
        var bounds = new SKRect(0, 0, _surface.Width, _surface.Height);
        var context = new RenderContext
        {
            ViewStartFrame = _sync.ViewStartFrame,
            ViewEndFrame = _sync.ViewEndFrame,
            LatestFrame = _store.LatestFrameId,
            TimeWindowSeconds = _sync.TimeWindowSeconds,
        };

        _visualizer.Render(canvas, bounds, context);
    }
}
```

### CompositeVisualizerWindow

Location: `HotMic.App/Views/CompositeVisualizerWindow.cs`

Window that renders a base visualizer with overlays.

```csharp
public class CompositeVisualizerWindow : VisualizerWindow
{
    private readonly IVisualizer _baseVisualizer;
    private readonly List<IOverlay> _overlays = new();

    public void AddOverlay(IOverlay overlay);
    public void RemoveOverlay(IOverlay overlay);

    protected override void OnPaint(SKCanvas canvas)
    {
        var bounds = new SKRect(0, 0, _surface.Width, _surface.Height);
        var context = BuildContext();

        // Render base first
        _baseVisualizer.Render(canvas, bounds, context);

        // Render overlays in z-order
        foreach (var overlay in _overlays.OrderBy(o => o.ZOrder))
        {
            if (overlay.IsVisible)
                overlay.Render(canvas, bounds, context);
        }
    }
}
```

## Overlay Bar UX

The overlay bar provides quick toggle access for overlays. Appears at the top of SpectrogramVisualizer windows.

```
┌─────────────────────────────────────────────────────────────────────────┐
│ ┌──────┐ ┌──────┐ ┌────────┐ ┌──────────┐ ┌────────┐ ┌───────┐  ⚙️ ▼ │
│ │Pitch │ │ F1-5 │ │Harmonic│ │ Voicing  │ │ Range  │ │ Guides│      │
│ │ ✓ ON │ │ ✓ ON │ │   OFF  │ │    OFF   │ │ ✓ ON   │ │  OFF  │      │
│ └──────┘ └──────┘ └────────┘ └──────────┘ └────────┘ └───────┘      │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│                         [Spectrogram Content]                           │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### Overlay Bar Behavior

1. **Toggle buttons**: Click to enable/disable overlay. Visual state (filled = on, outline = off)
2. **Gear icon (⚙️)**: Opens parameter panel for the currently selected overlay
3. **Dropdown (▼)**: Quick access to:
   - Analysis settings (FFT size, overlap, etc.)
   - Open additional windows (Waveform, Speech Coach, etc.)
   - Presets

### Implementation

Location: `HotMic.App/UI/VisualizerComponents/OverlayBar.xaml`

```xml
<UserControl x:Class="HotMic.App.UI.VisualizerComponents.OverlayBar">
    <Border Background="{StaticResource ToolbarBackground}"
            BorderThickness="0,0,0,1"
            BorderBrush="{StaticResource BorderBrush}">
        <DockPanel>
            <!-- Overlay toggles -->
            <StackPanel Orientation="Horizontal" DockPanel.Dock="Left">
                <ToggleButton x:Name="PitchToggle"
                              Style="{StaticResource OverlayToggleStyle}"
                              Content="Pitch"
                              IsChecked="{Binding PitchOverlayEnabled}"/>
                              Style="{StaticResource OverlayToggleStyle}"
                              Content="F1-5"
                <ToggleButton x:Name="HarmonicToggle"
                              Style="{StaticResource OverlayToggleStyle}"
                              Content="Harmonic"
                              IsChecked="{Binding HarmonicOverlayEnabled}"/>
                <ToggleButton x:Name="VoicingToggle"
                              Style="{StaticResource OverlayToggleStyle}"
                              Content="Voicing"
                              IsChecked="{Binding VoicingOverlayEnabled}"/>
                <ToggleButton x:Name="RangeToggle"
                              Style="{StaticResource OverlayToggleStyle}"
                              Content="Range"
                              IsChecked="{Binding RangeOverlayEnabled}"/>
                <ToggleButton x:Name="GuidesToggle"
                              Style="{StaticResource OverlayToggleStyle}"
                              Content="Guides"
                              IsChecked="{Binding GuidesOverlayEnabled}"/>
            </StackPanel>

            <!-- Settings and menu -->
            <StackPanel Orientation="Horizontal"
                        DockPanel.Dock="Right"
                        HorizontalAlignment="Right">
                <Button Style="{StaticResource IconButtonStyle}"
                        Command="{Binding OpenSettingsCommand}"
                        ToolTip="Overlay Settings">
                    <Path Data="{StaticResource GearIcon}" Fill="White"/>
                </Button>
                <Button Style="{StaticResource IconButtonStyle}"
                        Command="{Binding OpenMenuCommand}"
                        ToolTip="More Options">
                    <Path Data="{StaticResource ChevronDownIcon}" Fill="White"/>
                </Button>
            </StackPanel>
        </DockPanel>
    </Border>
</UserControl>
```

### Toggle Button States

```
 ┌─────────┐      ┌─────────┐
 │ ██████  │      │         │
 │ █Pitch█ │  vs  │  Pitch  │
 │ ██████  │      │         │
 └─────────┘      └─────────┘
   ENABLED         DISABLED
 (filled bg)     (outline only)
```

### Context Menu (▼ Dropdown)

```
┌────────────────────────┐
│ ▶ Open Window          │
│   ├ Waveform           │
│   ├ Pitch Meter        │
│   ├ Speech Coach       │
│   └ Singing Coach      │
├────────────────────────┤
│ ▶ Analysis             │
│   ├ FFT Size: 2048   ▶ │
│   ├ Overlap: 87.5%   ▶ │
│   ├ Transform: FFT   ▶ │
│   └ Pitch: YIN       ▶ │
├────────────────────────┤
│ ▶ Presets              │
│   ├ Voice Optimized    │
│   ├ Music/Singing      │
│   ├ Speech Analysis    │
│   └ Save Current...    │
├────────────────────────┤
│   Sync Windows         │
│   Follow Latest      ✓ │
└────────────────────────┘
```

## Preset System

### Preset Types

1. **Visualizer Presets**: Per-visualizer display settings
   - Stored in: `%AppData%\HotMic\visualizer-presets\{visualizer-id}\*.json`
   - Contains: Only display parameters for that visualizer

2. **Analysis Presets**: Shared analysis configuration
   - Stored in: `%AppData%\HotMic\analysis-presets\*.json`

3. **Chain Presets** (existing): Plugin chain configuration
   - Does NOT contain visualizer or analysis settings
   - Unchanged from current system

### Preset Schema

```json
// visualizer-presets/spectrogram/voice-optimized.json
{
  "name": "Voice Optimized",
  "visualizer": "spectrogram",
  "version": 1,
  "parameters": {
    "timeWindow": 5.0,
    "freqMin": 80,
    "freqMax": 8000,
    "freqScale": "mel",
    "dbMin": -80,
    "dbMax": 0,
    "colorMap": 6,
    "brightness": 1.0,
    "gamma": 0.8,
    "contrast": 1.2
  },
  "overlays": {
    "pitch": true,
    "harmonics": false,
    "voicing": false,
    "range": true,
    "guides": true
  }
}
```

```json
// analysis-presets/speech-analysis.json
{
  "name": "Speech Analysis",
  "version": 1,
  "parameters": {
    "fftSize": 2048,
    "overlap": 0.875,
    "transformType": "fft",
    "pitchAlgorithm": "yin",
    "clarityMode": "none",
    "preEmphasis": true,
    "highPassEnabled": true,
    "highPassCutoff": 60
  }
}
```

## Provider Extraction

Each provider is extracted from the current VocalSpectrographPlugin.Analysis.cs:

### SpectrogramProvider (~600 lines)

Responsibilities:
- FFT/CQT/ZoomFFT transform
- Reassignment (optional)
- Clarity processing (noise reduction, HPSS, smoothing, harmonic comb)
- Writes to: spectrogram frames in AnalysisResultStore

Source extraction:
- `ComputeFftFrame()` → `SpectrogramProvider.ComputeFrame()`
- `ProcessClarity()` → `SpectrogramProvider.ApplyClarity()`
- Clarity sub-processors remain separate classes

### PitchProvider (~300 lines)

Responsibilities:
- Pitch detection (YIN, PYIN, Autocorrelation, Cepstral, SWIPE)
- Voicing state detection
- Writes to: pitch track, confidence, voicing state

Source extraction:
- `AnalyzePitch()` → `PitchProvider.Detect()`
- `_voicingDetector` moves to this provider


Responsibilities:
- LPC analysis with decimation
- Beam search tracking for continuity

Source extraction:
- Decimation logic moves here

### HarmonicProvider (~150 lines)

Responsibilities:
- Harmonic peak detection from spectrogram + pitch
- Writes to: harmonic frequencies, magnitudes

Source extraction:
- Harmonic detection from `AnalyzePitchAndOverlays()` → `HarmonicProvider.Detect()`

### WaveformProvider (~100 lines)

Responsibilities:
- Min/max envelope extraction
- Writes to: waveform min/max buffers

Source extraction:
- Waveform tracking from `WriteOverlayData()` → `WaveformProvider.Process()`

### SpeechMetricsProvider (~250 lines)

Responsibilities:
- Syllable detection
- Articulation rate
- Pause detection
- Clarity scoring
- Writes to: speech metrics frames

Source extraction:
- `_speechCoach` integration → `SpeechMetricsProvider`
- Already mostly encapsulated in `SpeechCoach` class

### SpectralFeaturesProvider (~100 lines)

Responsibilities:
- Spectral centroid
- Spectral slope
- Spectral flux
- Writes to: spectral feature tracks

Source extraction:
- `_featureExtractor` calls → `SpectralFeaturesProvider.Extract()`

## File Structure

```
src/HotMic.Core/
├── Analysis/
│   ├── AnalysisTap.cs
│   ├── AnalysisOrchestrator.cs
│   ├── AnalysisResultStore.cs
│   ├── AnalysisConfiguration.cs
│   ├── AnalysisCapabilities.cs
│   ├── VisualizerSyncHub.cs
│   └── Providers/
│       ├── SpectrogramProvider.cs
│       ├── PitchProvider.cs
│       ├── HarmonicProvider.cs
│       ├── WaveformProvider.cs
│       ├── SpeechMetricsProvider.cs
│       └── SpectralFeaturesProvider.cs
├── Visualization/
│   ├── IVisualizer.cs
│   ├── IOverlay.cs
│   ├── VisualizerParameter.cs
│   └── RenderContext.cs
└── Dsp/
    └── (existing DSP classes, unchanged)

src/HotMic.App/
├── Views/
│   ├── VisualizerWindow.cs
│   ├── CompositeVisualizerWindow.cs
│   ├── SpectrogramWindow.cs        (replaces VocalSpectrographWindow)
│   ├── WaveformWindow.cs
│   ├── PitchMeterWindow.cs
│   ├── SpeechCoachWindow.cs
│   └── SingingCoachWindow.cs
├── UI/
│   └── VisualizerComponents/
│       ├── OverlayBar.xaml(.cs)
│       ├── ParameterPanel.xaml(.cs)
│       └── VisualizerMenu.xaml(.cs)
└── Visualizers/
    ├── SpectrogramVisualizer.cs
    ├── WaveformVisualizer.cs
    ├── PitchMeterVisualizer.cs
    ├── SpeechCoachVisualizer.cs
    ├── SingingCoachVisualizer.cs
    └── Overlays/
        ├── PitchOverlay.cs
        ├── HarmonicOverlay.cs
        ├── VoicingOverlay.cs
        └── RangeGuidesOverlay.cs
```

## Migration Checklist

### Phase 1: Core Infrastructure
- [ ] Create `AnalysisCapabilities` enum
- [ ] Create `IAnalysisResultStore` interface
- [ ] Create `AnalysisResultStore` implementation
- [ ] Create `VisualizerSyncHub`
- [ ] Create `AnalysisOrchestrator` skeleton
- [ ] Create `AnalysisTap`

### Phase 2: Providers (extract from VocalSpectrographPlugin)
- [ ] Extract `SpectrogramProvider`
- [ ] Extract `PitchProvider`
- [ ] Extract `HarmonicProvider`
- [ ] Extract `WaveformProvider`
- [ ] Extract `SpeechMetricsProvider`
- [ ] Extract `SpectralFeaturesProvider`
- [ ] Wire providers into `AnalysisOrchestrator`

### Phase 3: Visualization Infrastructure
- [ ] Create `IVisualizer` interface
- [ ] Create `IOverlay` interface
- [ ] Create `VisualizerParameter` class
- [ ] Create `VisualizerWindow` base class
- [ ] Create `CompositeVisualizerWindow`
- [ ] Create `OverlayBar` component

### Phase 4: Visualizers (extract rendering from VocalSpectrographRenderer)
- [ ] Create `SpectrogramVisualizer`
- [ ] Create `PitchOverlay`
- [ ] Create `HarmonicOverlay`
- [ ] Create `VoicingOverlay`
- [ ] Create `RangeGuidesOverlay`
- [ ] Create `WaveformVisualizer`
- [ ] Create `PitchMeterVisualizer`
- [ ] Create `SpeechCoachVisualizer`
- [ ] Create `SingingCoachVisualizer`

### Phase 5: Windows
- [ ] Create `SpectrogramWindow`
- [ ] Create `WaveformWindow`
- [ ] Create `PitchMeterWindow`
- [ ] Create `SpeechCoachWindow`
- [ ] Create `SingingCoachWindow`

### Phase 6: Integration
- [ ] Add `AnalysisTap` to `AudioEngine`
- [ ] Update `MainViewModel` to launch new windows
- [ ] Implement preset loading/saving
- [ ] Add visualizer menu to main window

### Phase 7: Cleanup
- [ ] Remove `VocalSpectrographPlugin` (all 4 partial files)
- [ ] Remove `VocalSpectrographWindow`
- [ ] Remove `VocalSpectrographRenderer`
- [ ] Remove plugin from `PluginFactory`
- [ ] Update documentation

## Threading Model

```
┌─────────────────────────────────────────────────────────────────┐
│                        Audio Thread                              │
│  PluginChain.Process() → AnalysisTap.Capture()                  │
│                              │                                   │
│                              ▼                                   │
│                    LockFreeRingBuffer                           │
└─────────────────────────────────────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                       Analysis Thread                            │
│  AnalysisOrchestrator.AnalysisLoop()                            │
│    → SpectrogramProvider.Process()                              │
│    → PitchProvider.Detect()                                     │
│    → etc.                                                       │
│    → AnalysisResultStore.Publish()  (version increment)         │
└─────────────────────────────────────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                         UI Thread                                │
│  DispatcherTimer (60Hz)                                         │
│    → VisualizerWindow.OnRender()                                │
│    → IVisualizer.Render(canvas, bounds, context)                │
│      → AnalysisResultStore.TryGet*() (version-checked read)     │
└─────────────────────────────────────────────────────────────────┘
```

Same threading model as current, just cleaner separation.

## Performance Considerations

1. **Zero-overhead when inactive**: `AnalysisTap.Capture()` is a single branch when no consumers
2. **Demand-driven providers**: Only active capabilities run
3. **Shared buffers**: All visualizers read from same store, no duplication
4. **Version-based reads**: Lock-free, 2-attempt retry on torn read
5. **Single render surface**: Overlays composite in one paint pass
