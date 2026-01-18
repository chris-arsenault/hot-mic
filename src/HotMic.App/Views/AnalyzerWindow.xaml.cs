using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using HotMic.App.Diagnostics;
using HotMic.App.UI.PluginComponents;
using HotMic.Core.Dsp;
using HotMic.Core.Dsp.Analysis.Speech;
using HotMic.Core.Dsp.Spectrogram;
using HotMic.Core.Analysis;
using HotMic.Core.Presets;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using DesktopPaintGLSurfaceEventArgs = SkiaSharp.Views.Desktop.SKPaintGLSurfaceEventArgs;
using WpfToolTip = System.Windows.Controls.ToolTip;

namespace HotMic.App.Views;

public partial class AnalyzerWindow : Window, IDisposable
{
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 0x0002;
    private readonly AnalyzerRenderer _renderer = new(PluginComponentTheme.BlueOnBlack);
    private readonly DisplayPipeline _displayPipeline = new();
    private readonly AnalysisOrchestrator _orchestrator;
    private readonly IAnalysisResultStore _store;
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginPresetHelper _presetHelper;
    private IDisposable? _subscription;
    private bool _disposed;

    private FrameworkElement? _skiaCanvas;
    private WindowsFormsHost? _glHost;
    private SKGLControl? _glControl;
    private bool _backendLocked;
    private bool _usingGpu;

    private bool _isPaused;
    private long _latestFrameId = -1;
    private int _availableFrames;
    private long? _referenceFrameId;
    private WpfToolTip? _spectrogramToolTip;
    private string _currentTooltip = string.Empty;

    private int _lastDataVersion = -1;
    private long _lastCopiedFrameId = -1;
    private long _lastMappedFrameId = -1;
    private bool _lastReassignActive;
    private float[] _spectrogram = Array.Empty<float>();
    private float[] _displayMagnitudes = Array.Empty<float>();
    private float[] _analysisMagnitudes = Array.Empty<float>();
    private float[] _pitchTrack = Array.Empty<float>();
    private float[] _pitchConfidence = Array.Empty<float>();
    private float[] _formantFrequencies = Array.Empty<float>();
    private float[] _formantBandwidths = Array.Empty<float>();
    private byte[] _voicingStates = Array.Empty<byte>();
    private float[] _harmonicFrequencies = Array.Empty<float>();
    private float[] _harmonicMagnitudes = Array.Empty<float>();
    private float[] _waveformMin = Array.Empty<float>();
    private float[] _waveformMax = Array.Empty<float>();
    private float[] _hnrTrack = Array.Empty<float>();
    private float[] _cppTrack = Array.Empty<float>();
    private float[] _spectralCentroid = Array.Empty<float>();
    private float[] _spectralSlope = Array.Empty<float>();
    private float[] _spectralFlux = Array.Empty<float>();
    private float[] _binFrequencies = Array.Empty<float>();
    private IReadOnlyList<DiscontinuityEvent> _discontinuities = Array.Empty<DiscontinuityEvent>();

    // Speech Coach buffers
    private byte[] _speakingStateTrack = Array.Empty<byte>();
    private byte[] _syllableMarkers = Array.Empty<byte>();
    private float _lastSyllableRate;
    private float _lastArticulationRate;
    private float _lastPauseRatio;
    private float _lastMonotoneScore;
    private float _lastClarityScore;
    private float _lastIntelligibilityScore;

    private int _bufferFrameCount;
    private int _bufferBins;
    private int _bufferAnalysisBins;
    private int _bufferMaxFormants;
    private int _bufferMaxHarmonics;
    private int _lastDisplayBins;
    private FrequencyScale _lastScale;
    private float _lastMinFrequency;
    private float _lastMaxFrequency;
    private float _lastMinDb;
    private float _lastMaxDb;
    private SpectrogramDynamicRangeMode _lastDynamicRangeMode;
    private SpectrogramAnalysisDescriptor? _lastAnalysisDescriptor;
    private bool _profilingHotkeyDown;

    // Display state (local to this window, not shared via orchestrator)
    private float _minDb = -80f;
    private float _maxDb;
    private float _brightness = 1f;
    private float _gamma = 0.8f;
    private float _contrast = 1.2f;
    private int _colorLevels = 32;
    private int _colorMap;
    private SpectrogramAxisMode _axisMode = SpectrogramAxisMode.Hz;
    private VocalRangeType _voiceRange = VocalRangeType.Tenor;
    private HarmonicDisplayMode _harmonicDisplayMode = HarmonicDisplayMode.Detected;
    private bool _showPitch = true;
    private bool _showFormants = true;
    private bool _showFormantBandwidths;
    private bool _showHarmonics;
    private bool _showVoicing = true;
    private bool _showRange;
    private bool _showGuides;
    private bool _showWaveform = true;
    private bool _showSpectrum;
    private bool _showPitchMeter = true;
    private bool _showVowelSpace;
    private bool _isBypassed;
    private bool _speechCoachEnabled;
    private bool _showSpeechMetrics;
    private bool _showSyllableMarkers;
    private bool _showPauseOverlay;
    private bool _showFillerMarkers;
    private SpectrogramDynamicRangeMode _dynamicRangeMode = SpectrogramDynamicRangeMode.VoiceOptimized;
    private int _uiTickUs;
    private int _uiCopyUs;
    private int _uiMapUs;
    private bool _applyingPreset;
    private int _debugLogCounter;

    private static readonly double TicksToMicroseconds = 1_000_000.0 / Stopwatch.Frequency;

    // Hidden profiler hotkey: Ctrl+Shift+Alt+P.
    private const ModifierKeys ProfilerHotkeyModifiers = ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt;
    private const Key ProfilerHotkeyKey = Key.P;

    private static readonly int[] FftSizes = { 1024, 2048, 4096, 8192 };
    private static readonly WindowFunction[] WindowFunctions =
    {
        WindowFunction.Hann,
        WindowFunction.Hamming,
        WindowFunction.BlackmanHarris,
        WindowFunction.Gaussian,
        WindowFunction.Kaiser
    };
    private static readonly FrequencyScale[] Scales =
    {
        FrequencyScale.Linear,
        FrequencyScale.Logarithmic,
        FrequencyScale.Mel,
        FrequencyScale.Erb,
        FrequencyScale.Bark
    };
    private static readonly string[] NoteNames =
    {
        "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"
    };

    private static readonly float[] OverlapOptions = { 0.5f, 0.75f, 0.875f, 0.9375f, 0.96875f };

    private const int ParamFftSize = 0;
    private const int ParamWindowFunction = 1;
    private const int ParamOverlap = 2;
    private const int ParamScale = 3;
    private const int ParamMinFrequency = 4;
    private const int ParamMaxFrequency = 5;
    private const int ParamMinDb = 6;
    private const int ParamMaxDb = 7;
    private const int ParamTimeWindow = 8;
    private const int ParamColorMap = 9;
    private const int ParamShowPitch = 10;
    private const int ParamShowFormants = 11;
    private const int ParamShowHarmonics = 12;
    private const int ParamShowVoicing = 13;
    private const int ParamPreEmphasis = 14;
    private const int ParamHighPassEnabled = 15;
    private const int ParamHighPassCutoff = 16;
    private const int ParamReassignMode = 18;
    private const int ParamReassignThreshold = 19;
    private const int ParamReassignSpread = 20;
    private const int ParamClarityMode = 21;
    private const int ParamClarityNoise = 22;
    private const int ParamClarityHarmonic = 23;
    private const int ParamClaritySmoothing = 24;
    private const int ParamPitchAlgorithm = 25;
    private const int ParamAxisMode = 26;
    private const int ParamVoiceRange = 27;
    private const int ParamShowRange = 28;
    private const int ParamShowGuides = 29;
    private const int ParamShowWaveform = 30;
    private const int ParamShowSpectrum = 31;
    private const int ParamShowPitchMeter = 32;
    private const int ParamShowVowelSpace = 33;
    private const int ParamSmoothingMode = 34;
    private const int ParamBrightness = 35;
    private const int ParamGamma = 36;
    private const int ParamContrast = 37;
    private const int ParamColorLevels = 38;
    private const int ParamFormantProfile = 39;
    private const int ParamNormalizationMode = 40;
    private const int ParamDynamicRangeMode = 41;
    private const int ParamTransformType = 42;
    private const int ParamCqtBinsPerOctave = 43;
    private const int ParamShowFormantBandwidths = 44;
    private const int ParamHarmonicDisplayMode = 45;
    private const int ParamSpeechCoachEnabled = 46;
    private const int ParamShowSpeechMetrics = 47;
    private const int ParamShowSyllableMarkers = 48;
    private const int ParamShowPauseOverlay = 49;
    private const int ParamShowFillerMarkers = 50;


    public AnalyzerWindow(AnalysisOrchestrator orchestrator)
    {
        InitializeComponent();
        _orchestrator = orchestrator;
        _store = orchestrator.Results;
        _presetHelper = new PluginPresetHelper(
            "builtin:vocal-spectrograph",
            PluginPresetManager.Default,
            ApplyPreset,
            GetCurrentParameters);
        WireKnobHandlers();
        InitializeSkiaSurface();

        var preferredSize = AnalyzerRenderer.GetPreferredSize();
        Width = preferredSize.Width;
        Height = preferredSize.Height;

        _spectrogramToolTip = new WpfToolTip
        {
            Placement = PlacementMode.Relative,
            StaysOpen = true
        };
        AttachTooltip();

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += OnRenderTick;
        Loaded += (_, _) =>
        {
            UpdateSubscription();
            _renderTimer.Start();
        };
        Closed += (_, _) => Dispose();
    }

    private void InitializeSkiaSurface()
    {
        if (TryCreateGpuCanvas())
        {
            return;
        }

        CreateCpuCanvas();
    }

    private void WireKnobHandlers()
    {
        _renderer.MinFreqKnob.ValueChanged += value => OnKnobValueChanged(ParamMinFrequency, value);
        _renderer.MaxFreqKnob.ValueChanged += value => OnKnobValueChanged(ParamMaxFrequency, value);
        _renderer.MinDbKnob.ValueChanged += value => OnKnobValueChanged(ParamMinDb, value);
        _renderer.MaxDbKnob.ValueChanged += value => OnKnobValueChanged(ParamMaxDb, value);
        _renderer.TimeKnob.ValueChanged += value => OnKnobValueChanged(ParamTimeWindow, value);
        _renderer.HpfKnob.ValueChanged += value => OnKnobValueChanged(ParamHighPassCutoff, value);
        _renderer.ReassignThresholdKnob.ValueChanged += value => OnKnobValueChanged(ParamReassignThreshold, value);
        _renderer.ReassignSpreadKnob.ValueChanged += value => OnKnobValueChanged(ParamReassignSpread, value / 100f);
        _renderer.ClarityNoiseKnob.ValueChanged += value => OnKnobValueChanged(ParamClarityNoise, value / 100f);
        _renderer.ClarityHarmonicKnob.ValueChanged += value => OnKnobValueChanged(ParamClarityHarmonic, value / 100f);
        _renderer.ClaritySmoothingKnob.ValueChanged += value => OnKnobValueChanged(ParamClaritySmoothing, value / 100f);
        _renderer.BrightnessKnob.ValueChanged += value => OnKnobValueChanged(ParamBrightness, value);
        _renderer.GammaKnob.ValueChanged += value => OnKnobValueChanged(ParamGamma, value);
        _renderer.ContrastKnob.ValueChanged += value => OnKnobValueChanged(ParamContrast, value);
        _renderer.LevelsKnob.ValueChanged += value => OnKnobValueChanged(ParamColorLevels, value);
    }

    private void OnKnobValueChanged(int index, float value)
    {
        SetParameter(index, value);
    }

    /// <summary>
    /// Sets a parameter value by index, updating either orchestrator config or local display state.
    /// </summary>
    private void SetParameter(int index, float value)
    {
        var config = _orchestrator.Config;

        // Analysis parameters (affect shared orchestrator config)
        if (index == ParamFftSize) config.FftSize = (int)value;
        else if (index == ParamWindowFunction) config.WindowFunction = (WindowFunction)(int)value;
        else if (index == ParamOverlap) config.OverlapIndex = Array.IndexOf(AnalysisConfiguration.OverlapOptions, value);
        else if (index == ParamScale) config.FrequencyScale = (FrequencyScale)(int)value;
        else if (index == ParamMinFrequency) config.MinFrequency = value;
        else if (index == ParamMaxFrequency) config.MaxFrequency = value;
        else if (index == ParamTimeWindow) config.TimeWindow = value;
        else if (index == ParamPreEmphasis) config.PreEmphasis = value > 0.5f;
        else if (index == ParamHighPassEnabled) config.HighPassEnabled = value > 0.5f;
        else if (index == ParamHighPassCutoff) config.HighPassCutoff = value;
        else if (index == ParamReassignMode) config.ReassignMode = (SpectrogramReassignMode)(int)value;
        else if (index == ParamReassignThreshold) config.ReassignThreshold = value;
        else if (index == ParamReassignSpread) config.ReassignSpread = value;
        else if (index == ParamClarityMode) config.ClarityMode = (ClarityProcessingMode)(int)value;
        else if (index == ParamClarityNoise) config.ClarityNoise = value;
        else if (index == ParamClarityHarmonic) config.ClarityHarmonic = value;
        else if (index == ParamClaritySmoothing) config.ClaritySmoothing = value;
        else if (index == ParamPitchAlgorithm) config.PitchAlgorithm = (PitchDetectorType)(int)value;
        else if (index == ParamFormantProfile) config.FormantProfile = (FormantProfile)(int)value;
        else if (index == ParamSmoothingMode) config.SmoothingMode = (SpectrogramSmoothingMode)(int)value;
        else if (index == ParamNormalizationMode) config.NormalizationMode = (SpectrogramNormalizationMode)(int)value;
        else if (index == ParamTransformType) config.TransformType = (SpectrogramTransformType)(int)value;
        else if (index == ParamCqtBinsPerOctave) config.CqtBinsPerOctave = (int)value;

        // Display parameters (local to this window)
        else if (index == ParamMinDb) _minDb = value;
        else if (index == ParamMaxDb) _maxDb = value;
        else if (index == ParamColorMap) _colorMap = (int)value;
        else if (index == ParamBrightness) _brightness = value;
        else if (index == ParamGamma) _gamma = value;
        else if (index == ParamContrast) _contrast = value;
        else if (index == ParamColorLevels) _colorLevels = (int)value;
        else if (index == ParamAxisMode) _axisMode = (SpectrogramAxisMode)(int)value;
        else if (index == ParamVoiceRange) _voiceRange = (VocalRangeType)(int)value;
        else if (index == ParamHarmonicDisplayMode) _harmonicDisplayMode = (HarmonicDisplayMode)(int)value;
        else if (index == ParamDynamicRangeMode) _dynamicRangeMode = (SpectrogramDynamicRangeMode)(int)value;

        // Toggle overlays/views (local) - these affect required capabilities
        else if (index == ParamShowPitch) { _showPitch = value > 0.5f; UpdateSubscription(); }
        else if (index == ParamShowFormants) { _showFormants = value > 0.5f; UpdateSubscription(); }
        else if (index == ParamShowFormantBandwidths) _showFormantBandwidths = value > 0.5f;
        else if (index == ParamShowHarmonics) { _showHarmonics = value > 0.5f; UpdateSubscription(); }
        else if (index == ParamShowVoicing) { _showVoicing = value > 0.5f; UpdateSubscription(); }
        else if (index == ParamShowRange) _showRange = value > 0.5f;
        else if (index == ParamShowGuides) _showGuides = value > 0.5f;
        else if (index == ParamShowWaveform) { _showWaveform = value > 0.5f; UpdateSubscription(); }
        else if (index == ParamShowSpectrum) { _showSpectrum = value > 0.5f; UpdateSubscription(); }
        else if (index == ParamShowPitchMeter) { _showPitchMeter = value > 0.5f; UpdateSubscription(); }
        else if (index == ParamShowVowelSpace) { _showVowelSpace = value > 0.5f; UpdateSubscription(); }
        else if (index == ParamSpeechCoachEnabled) { _speechCoachEnabled = value > 0.5f; UpdateSubscription(); }
        else if (index == ParamShowSpeechMetrics) { _showSpeechMetrics = value > 0.5f; UpdateSubscription(); }
        else if (index == ParamShowSyllableMarkers) _showSyllableMarkers = value > 0.5f;
        else if (index == ParamShowPauseOverlay) _showPauseOverlay = value > 0.5f;
        else if (index == ParamShowFillerMarkers) _showFillerMarkers = value > 0.5f;

        if (!_applyingPreset)
        {
            _presetHelper.MarkAsCustom();
        }
    }

    private bool TryCreateGpuCanvas()
    {
        SKGLControl? glControl = null;
        WindowsFormsHost? host = null;
        try
        {
            glControl = new SKGLControl
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                BackColor = System.Drawing.Color.FromArgb(10, 13, 18)
            };
            glControl.CreateControl();
            host = new WindowsFormsHost
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
                Child = glControl
            };

            _usingGpu = true;
            _backendLocked = false;
            _skiaCanvas = host;
            _glControl = glControl;
            _glHost = host;
            glControl.PaintSurface += SkiaCanvas_PaintSurfaceGpu;
            AttachWinFormsInputHandlers(glControl);
            SkiaHost.Children.Clear();
            SkiaHost.Children.Add(host);
            AttachTooltip();
            return true;
        }
        catch (Exception)
        {
            glControl?.Dispose();
            host?.Dispose();
            _usingGpu = false;
            _glControl = null;
            _glHost = null;
            return false;
        }
    }

    private void CreateCpuCanvas()
    {
        var element = new SKElement
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch
        };

        _usingGpu = false;
        _backendLocked = true;
        _glControl = null;
        _glHost = null;
        _skiaCanvas = element;
        element.PaintSurface += SkiaCanvas_PaintSurface;
        AttachInputHandlers(element);
        SkiaHost.Children.Clear();
        SkiaHost.Children.Add(element);
        AttachTooltip();
    }

    private void AttachInputHandlers(UIElement element)
    {
        element.MouseDown += SkiaCanvas_MouseDown;
        element.MouseMove += SkiaCanvas_MouseMove;
        element.MouseUp += SkiaCanvas_MouseUp;
        element.MouseLeave += SkiaCanvas_MouseLeave;
    }

    private void DetachInputHandlers(UIElement element)
    {
        element.MouseDown -= SkiaCanvas_MouseDown;
        element.MouseMove -= SkiaCanvas_MouseMove;
        element.MouseUp -= SkiaCanvas_MouseUp;
        element.MouseLeave -= SkiaCanvas_MouseLeave;
    }

    private void AttachWinFormsInputHandlers(SKGLControl control)
    {
        control.MouseDown += SkiaCanvas_MouseDownWinForms;
        control.MouseMove += SkiaCanvas_MouseMoveWinForms;
        control.MouseUp += SkiaCanvas_MouseUpWinForms;
        control.MouseLeave += SkiaCanvas_MouseLeaveWinForms;
        control.KeyDown += SkiaCanvas_KeyDownWinForms;
        control.KeyUp += SkiaCanvas_KeyUpWinForms;
    }

    private void DetachWinFormsInputHandlers(SKGLControl control)
    {
        control.MouseDown -= SkiaCanvas_MouseDownWinForms;
        control.MouseMove -= SkiaCanvas_MouseMoveWinForms;
        control.MouseUp -= SkiaCanvas_MouseUpWinForms;
        control.MouseLeave -= SkiaCanvas_MouseLeaveWinForms;
        control.KeyDown -= SkiaCanvas_KeyDownWinForms;
        control.KeyUp -= SkiaCanvas_KeyUpWinForms;
    }

    private bool TryHandleKnobMouseDown(float x, float y, MouseButton button)
    {
        if (_skiaCanvas is null)
        {
            return false;
        }

        foreach (var knob in _renderer.AllKnobs)
        {
            if (knob.HandleMouseDown(x, y, button, _skiaCanvas))
            {
                SetTooltip(null);
                return true;
            }
        }

        return false;
    }

    private bool HandleKnobMouseMove(float x, float y, bool leftDown, bool shiftHeld = false)
    {
        bool anyHover = false;
        bool anyDragging = false;

        foreach (var knob in _renderer.AllKnobs)
        {
            knob.HandleMouseMove(x, y, leftDown, shiftHeld);
            anyHover |= knob.IsHovered;
            anyDragging |= knob.IsDragging;
        }

        return anyHover || anyDragging;
    }

    private bool HandleKnobDoubleClick(float x, float y)
    {
        foreach (var knob in _renderer.AllKnobs)
        {
            if (knob.HandleDoubleClick(x, y))
            {
                return true;
            }
        }

        return false;
    }

    private void HandleKnobMouseUp(MouseButton button)
    {
        foreach (var knob in _renderer.AllKnobs)
        {
            knob.HandleMouseUp(button);
        }
    }

    private void ResetKnobHover()
    {
        foreach (var knob in _renderer.AllKnobs)
        {
            knob.UpdateHover(float.NegativeInfinity, float.NegativeInfinity);
        }
    }

    private static MouseButton? ToWpfMouseButton(System.Windows.Forms.MouseButtons button)
    {
        if ((button & System.Windows.Forms.MouseButtons.Left) != 0)
        {
            return MouseButton.Left;
        }

        if ((button & System.Windows.Forms.MouseButtons.Right) != 0)
        {
            return MouseButton.Right;
        }

        if ((button & System.Windows.Forms.MouseButtons.Middle) != 0)
        {
            return MouseButton.Middle;
        }

        return null;
    }

    private void AttachTooltip()
    {
        if (_spectrogramToolTip is null || _skiaCanvas is null)
        {
            return;
        }

        _spectrogramToolTip.PlacementTarget = _skiaCanvas;
        ToolTipService.SetInitialShowDelay(_skiaCanvas, 0);
        ToolTipService.SetShowDuration(_skiaCanvas, int.MaxValue);
        _skiaCanvas.ToolTip = _spectrogramToolTip;
    }

    private void FallbackToCpu()
    {
        if (_backendLocked || !_usingGpu)
        {
            return;
        }

        _backendLocked = true;
        if (_glControl is not null)
        {
            _glControl.PaintSurface -= SkiaCanvas_PaintSurfaceGpu;
            DetachWinFormsInputHandlers(_glControl);
            _glControl.Dispose();
            _glControl = null;
        }
        if (_glHost is not null)
        {
            _glHost.Child = null;
            SkiaHost.Children.Clear();
            _glHost.Dispose();
            _glHost = null;
        }

        CreateCpuCanvas();
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (TryHandleProfilerHotkey(e))
        {
            e.Handled = true;
        }
    }

    protected override void OnPreviewKeyUp(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);

        ReleaseProfilerHotkey(e);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        _profilingHotkeyDown = false;
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        bool profiling = SpectrographProfiler.IsCollecting;
        long tickStartTicks = profiling ? Stopwatch.GetTimestamp() : 0;
        long copyTicks = 0;
        long mapTicks = 0;

        bool buffersResized = EnsureBuffers();
        if (buffersResized)
        {
            _lastCopiedFrameId = -1;
            _lastMappedFrameId = -1;
        }

        bool dataUpdated = false;
        bool forceFullMap = false;
        bool reassignActive = _orchestrator.Config.ReassignMode != SpectrogramReassignMode.Off;
        if (reassignActive != _lastReassignActive)
        {
            _lastCopiedFrameId = -1;
            _lastMappedFrameId = -1;
            _lastDataVersion = -1;
            forceFullMap = true;
            _lastReassignActive = reassignActive;
        }

        if (!_isPaused)
        {
            int dataVersion = _store.DataVersion;
            if (dataVersion != _lastDataVersion || buffersResized)
            {
                long copyStartTicks = profiling ? Stopwatch.GetTimestamp() : 0;

                // Copy data from the shared analysis result store
                bool spectrogramCopied = _store.TryGetSpectrogramRange(
                    _lastCopiedFrameId, _displayMagnitudes,
                    out long latestFrameId, out int availableFrames, out bool spectrogramFullCopy);

                bool pitchCopied = _store.TryGetPitchRange(
                    _lastCopiedFrameId, _pitchTrack, _pitchConfidence, _voicingStates,
                    out _, out _, out _);

                bool formantCopied = _store.TryGetFormantRange(
                    _lastCopiedFrameId, _formantFrequencies, _formantBandwidths,
                    out _, out _, out _);

                bool harmonicCopied = _store.TryGetHarmonicRange(
                    _lastCopiedFrameId, _harmonicFrequencies, _harmonicMagnitudes,
                    out _, out _, out _);

                bool waveformCopied = _store.TryGetWaveformRange(
                    _lastCopiedFrameId, _waveformMin, _waveformMax,
                    out _, out _, out _);

                bool spectralCopied = _store.TryGetSpectralFeatures(
                    _lastCopiedFrameId, _spectralCentroid, _spectralSlope, _spectralFlux,
                    _hnrTrack, _cppTrack,
                    out _, out _, out _);

                bool linearCopied = true;
                bool linearFullCopy = false;
                if (!reassignActive)
                {
                    linearCopied = _store.TryGetLinearMagnitudes(
                        _lastCopiedFrameId, _analysisMagnitudes,
                        out _, out _, out _,
                        out long linearLatestFrameId, out int linearAvailableFrames, out linearFullCopy);

                    if (!spectrogramCopied && linearCopied)
                    {
                        latestFrameId = linearLatestFrameId;
                        availableFrames = linearAvailableFrames;
                    }
                }
                if (profiling)
                {
                    copyTicks = Stopwatch.GetTimestamp() - copyStartTicks;
                }

                bool overlayCopied = spectrogramCopied || pitchCopied || formantCopied;
                if (overlayCopied)
                {
                    _latestFrameId = latestFrameId;
                    _availableFrames = availableFrames;
                    CullReferenceLine();

                    // Discontinuities not available from shared store - use empty list
                    _discontinuities = Array.Empty<DiscontinuityEvent>();

                    bool ready = reassignActive || linearCopied;
                    if (ready)
                    {
                        _lastDataVersion = dataVersion;
                        dataUpdated = _latestFrameId > _lastCopiedFrameId || spectrogramFullCopy || linearFullCopy;
                        _lastCopiedFrameId = _latestFrameId;
                        forceFullMap |= spectrogramFullCopy || linearFullCopy || buffersResized;
                    }
                }
            }

            if (_lastDataVersion == dataVersion)
            {
                _latestFrameId = _store.LatestFrameId;
                _availableFrames = _store.AvailableFrames;
                CullReferenceLine();
            }

            // Copy speech metrics if enabled
            if (_speechCoachEnabled)
            {
                // Speech metrics from store - use TryGetSpeechMetrics if available
                // For now, speech metrics are tracked per-frame in the store
                // but we don't have a single-value getter, so leave at defaults
            }
        }

        bool displayChanged = ConfigureDisplayPipeline(_store.GetAnalysisDescriptor());
        if (displayChanged)
        {
            forceFullMap = true;
        }

        if (displayChanged || dataUpdated || forceFullMap)
        {
            long mapStartTicks = profiling ? Stopwatch.GetTimestamp() : 0;
            MapUpdatedFrames(reassignActive, forceFullMap);
            if (profiling)
            {
                mapTicks = Stopwatch.GetTimestamp() - mapStartTicks;
            }
        }

        if (profiling)
        {
            _uiCopyUs = ToMicroseconds(copyTicks);
            _uiMapUs = ToMicroseconds(mapTicks);
            _uiTickUs = ToMicroseconds(Stopwatch.GetTimestamp() - tickStartTicks);
        }
        else if (_uiTickUs != 0 || _uiCopyUs != 0 || _uiMapUs != 0)
        {
            _uiTickUs = 0;
            _uiCopyUs = 0;
            _uiMapUs = 0;
        }

        InvalidateRenderSurface();

        // Debug logging - every ~60 ticks (~1 second)
        _debugLogCounter++;
        if (_debugLogCounter >= 60)
        {
            _debugLogCounter = 0;
            var tap = _orchestrator.DebugTap;
            Console.WriteLine("=== ANALYZER DEBUG ===");
            Console.WriteLine($"TAP: calls={tap?.DebugCaptureCallCount ?? 0} noOrc={tap?.DebugSkippedNoOrchestrator ?? 0} noCons={tap?.DebugSkippedNoConsumers ?? 0} fwd={tap?.DebugForwardedCount ?? 0} buf={tap?.DebugLastBufferLength ?? 0}");
            Console.WriteLine($"ENQ: calls={_orchestrator.DebugEnqueueCalls} skipCh={_orchestrator.DebugEnqueueSkippedChannel} skipE={_orchestrator.DebugEnqueueSkippedEmpty} written={_orchestrator.DebugEnqueueWritten} samp={_orchestrator.DebugEnqueueSamplesWritten}");
            Console.WriteLine($"LOOP: iter={_orchestrator.DebugLoopIterations} noCons={_orchestrator.DebugLoopNoConsumers} noData={_orchestrator.DebugLoopNotEnoughData} proc={_orchestrator.DebugLoopFramesProcessed} written={_orchestrator.DebugLoopFramesWritten}");
            Console.WriteLine($"ORC: capBuf={_orchestrator.DebugCaptureBufferAvailable} hop={_orchestrator.DebugActiveHopSize} frames={_orchestrator.DebugActiveFrameCapacity} disp={_orchestrator.DebugActiveDisplayBins} anl={_orchestrator.DebugActiveAnalysisBins} cons={_orchestrator.DebugConsumerCount}");
            Console.WriteLine($"VALUES: hopMax={_orchestrator.DebugLastHopMax:F6} fftMax={_orchestrator.DebugLastFftMax:F6} displayMax={_orchestrator.DebugLastDisplayMax:F6}");
            string transformName = _orchestrator.DebugTransformPath switch { 0 => "FFT", 1 => "CQT", 2 => "ZoomFFT", _ => "?" };
            Console.WriteLine($"FFT: analysisBuf={_orchestrator.DebugLastAnalysisBufMax:F6} window={_orchestrator.DebugLastWindowMax:F6} fftReal={_orchestrator.DebugLastFftRealMax:F6} fftNull={_orchestrator.DebugFftNull} transform={transformName}");
            Console.WriteLine($"PROC: processedMax={_orchestrator.DebugLastProcessedMax:F6} filled={_orchestrator.DebugAnalysisFilled} sampleRate={_orchestrator.SampleRate}");
            Console.WriteLine($"STORE: latest={_store.LatestFrameId} avail={_store.AvailableFrames} cap={_store.FrameCapacity} disp={_store.DisplayBins} anl={_store.AnalysisBins} ver={_store.DataVersion}");
            Console.WriteLine($"PIPE: configured={_displayPipeline.IsConfigured} dispBins={_displayPipeline.DisplayBins}");
            Console.WriteLine($"LOCAL: latestFrame={_latestFrameId} availFrames={_availableFrames} lastDataVer={_lastDataVersion} lastCopied={_lastCopiedFrameId} lastMapped={_lastMappedFrameId}");

            // Check actual data values
            float maxDisplayMag = 0f, maxAnalysisMag = 0f, maxSpectrogram = 0f;
            int nonZeroDisplay = 0, nonZeroAnalysis = 0, nonZeroSpectro = 0;
            for (int i = 0; i < Math.Min(10000, _displayMagnitudes.Length); i++)
            {
                if (_displayMagnitudes[i] != 0) { nonZeroDisplay++; maxDisplayMag = MathF.Max(maxDisplayMag, MathF.Abs(_displayMagnitudes[i])); }
            }
            for (int i = 0; i < Math.Min(10000, _analysisMagnitudes.Length); i++)
            {
                if (_analysisMagnitudes[i] != 0) { nonZeroAnalysis++; maxAnalysisMag = MathF.Max(maxAnalysisMag, MathF.Abs(_analysisMagnitudes[i])); }
            }
            for (int i = 0; i < Math.Min(10000, _spectrogram.Length); i++)
            {
                if (_spectrogram[i] != 0) { nonZeroSpectro++; maxSpectrogram = MathF.Max(maxSpectrogram, MathF.Abs(_spectrogram[i])); }
            }
            Console.WriteLine($"DATA: displayMag[nonZero={nonZeroDisplay} max={maxDisplayMag:F4}] analysisMag[nonZero={nonZeroAnalysis} max={maxAnalysisMag:F4}] spectro[nonZero={nonZeroSpectro} max={maxSpectrogram:F4}]");

            // Check TryGet return values
            bool spectroCopied = _store.TryGetSpectrogramRange(-1, _displayMagnitudes, out long specLatest, out int specAvail, out bool specFull);
            bool linearCopied = _store.TryGetLinearMagnitudes(-1, _analysisMagnitudes, out _, out _, out _, out long linLatest, out int linAvail, out bool linFull);
            Console.WriteLine($"TRYGET: spectro={spectroCopied}(latest={specLatest} avail={specAvail} full={specFull}) linear={linearCopied}(latest={linLatest} avail={linAvail} full={linFull})");
        }
    }

    private void MapUpdatedFrames(bool reassignActive, bool forceFullMap)
    {
        if (_latestFrameId < 0 || _availableFrames <= 0 || _bufferFrameCount <= 0)
        {
            _lastMappedFrameId = -1;
            return;
        }

        int frameCapacity = _bufferFrameCount;
        int displayBins = _bufferBins;
        int analysisBins = _bufferAnalysisBins;

        long oldestFrameId = _latestFrameId - _availableFrames + 1;
        long startFrameId = forceFullMap ? oldestFrameId : _lastMappedFrameId + 1;
        if (_lastMappedFrameId < oldestFrameId - 1)
        {
            startFrameId = oldestFrameId;
        }

        if (startFrameId > _latestFrameId)
        {
            return;
        }

        for (long frameId = startFrameId; frameId <= _latestFrameId; frameId++)
        {
            int ringIndex = (int)(frameId % frameCapacity);
            if (ringIndex < 0)
            {
                ringIndex += frameCapacity;
            }

            if (reassignActive)
            {
                var src = _displayMagnitudes.AsSpan(ringIndex * displayBins, displayBins);
                var dst = _spectrogram.AsSpan(ringIndex * displayBins, displayBins);
                byte voicing = ringIndex < _voicingStates.Length ? _voicingStates[ringIndex] : (byte)VoicingState.Silence;
                _displayPipeline.ProcessDisplayFrame(src, dst, voicing);
            }
            else
            {
                var src = _analysisMagnitudes.AsSpan(ringIndex * analysisBins, analysisBins);
                var dst = _spectrogram.AsSpan(ringIndex * displayBins, displayBins);
                byte voicing = ringIndex < _voicingStates.Length ? _voicingStates[ringIndex] : (byte)VoicingState.Silence;
                _displayPipeline.ProcessFrame(src, dst, voicing);
            }
        }

        _lastMappedFrameId = _latestFrameId;
    }

    private void InvalidateRenderSurface()
    {
        if (_usingGpu)
        {
            _glControl?.Invalidate();
            return;
        }

        _skiaCanvas?.InvalidateVisual();
    }

    private static int ToMicroseconds(long ticks)
    {
        if (ticks <= 0)
        {
            return 0;
        }

        double us = ticks * TicksToMicroseconds;
        if (us >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return (int)Math.Round(us);
    }

    private bool TryHandleProfilerHotkey(System.Windows.Input.KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (!IsProfilerHotkey(key, Keyboard.Modifiers))
        {
            return false;
        }

        if (_profilingHotkeyDown || e.IsRepeat)
        {
            return true;
        }

        _profilingHotkeyDown = true;
        SpectrographProfiler.Toggle();
        return true;
    }

    private void ReleaseProfilerHotkey(System.Windows.Input.KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == ProfilerHotkeyKey || Keyboard.Modifiers != ProfilerHotkeyModifiers)
        {
            _profilingHotkeyDown = false;
        }
    }

    private void SkiaCanvas_KeyDownWinForms(object? sender, System.Windows.Forms.KeyEventArgs e)
    {
        if (TryHandleProfilerHotkey(e))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void SkiaCanvas_KeyUpWinForms(object? sender, System.Windows.Forms.KeyEventArgs e)
    {
        ReleaseProfilerHotkey(e);
    }

    private bool TryHandleProfilerHotkey(System.Windows.Forms.KeyEventArgs e)
    {
        if (!IsProfilerHotkey(e))
        {
            return false;
        }

        if (_profilingHotkeyDown)
        {
            return true;
        }

        _profilingHotkeyDown = true;
        SpectrographProfiler.Toggle();
        return true;
    }

    private void ReleaseProfilerHotkey(System.Windows.Forms.KeyEventArgs e)
    {
        if (e.KeyCode == System.Windows.Forms.Keys.P
            || e.Modifiers != (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Shift | System.Windows.Forms.Keys.Alt))
        {
            _profilingHotkeyDown = false;
        }
    }

    private static bool IsProfilerHotkey(Key key, ModifierKeys modifiers)
    {
        return key == ProfilerHotkeyKey && modifiers == ProfilerHotkeyModifiers;
    }

    private static bool IsProfilerHotkey(System.Windows.Forms.KeyEventArgs e)
    {
        return e.KeyCode == System.Windows.Forms.Keys.P
            && e.Modifiers == (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Shift | System.Windows.Forms.Keys.Alt);
    }

    private bool EnsureBuffers()
    {
        bool resized = false;
        int frames = Math.Max(1, _store.FrameCapacity);
        int bins = Math.Max(1, _store.DisplayBins);
        int analysisBins = Math.Max(1, _store.AnalysisBins);
        int maxFormants = AnalysisConfiguration.MaxFormants;
        int maxHarmonics = AnalysisConfiguration.MaxHarmonics;

        _bufferFrameCount = frames;
        _bufferBins = bins;
        _bufferAnalysisBins = analysisBins;
        _bufferMaxFormants = maxFormants;
        _bufferMaxHarmonics = maxHarmonics;

        int spectrogramLength = frames * bins;
        if (_spectrogram.Length != spectrogramLength)
        {
            _spectrogram = new float[spectrogramLength];
            _lastDataVersion = -1;
            resized = true;
        }

        if (_displayMagnitudes.Length != spectrogramLength)
        {
            _displayMagnitudes = new float[spectrogramLength];
            _lastDataVersion = -1;
            resized = true;
        }

        int analysisLength = frames * analysisBins;
        if (_analysisMagnitudes.Length != analysisLength)
        {
            _analysisMagnitudes = new float[analysisLength];
            _lastDataVersion = -1;
            resized = true;
        }

        if (_pitchTrack.Length != frames)
        {
            _pitchTrack = new float[frames];
            _pitchConfidence = new float[frames];
            _voicingStates = new byte[frames];
            _lastDataVersion = -1;
            resized = true;
        }

        int formantLength = frames * maxFormants;
        if (_formantFrequencies.Length != formantLength)
        {
            _formantFrequencies = new float[formantLength];
            _formantBandwidths = new float[formantLength];
            _lastDataVersion = -1;
            resized = true;
        }

        int harmonicLength = frames * maxHarmonics;
        if (_harmonicFrequencies.Length != harmonicLength)
        {
            _harmonicFrequencies = new float[harmonicLength];
            _harmonicMagnitudes = new float[harmonicLength];
            _lastDataVersion = -1;
            resized = true;
        }

        if (_waveformMin.Length != frames)
        {
            _waveformMin = new float[frames];
            _waveformMax = new float[frames];
            _hnrTrack = new float[frames];
            _cppTrack = new float[frames];
            _spectralCentroid = new float[frames];
            _spectralSlope = new float[frames];
            _spectralFlux = new float[frames];
            _lastDataVersion = -1;
            resized = true;
        }

        if (_binFrequencies.Length != bins)
        {
            _binFrequencies = new float[bins];
            _lastDataVersion = -1;
            resized = true;
        }

        // Speech Coach buffers
        if (_speakingStateTrack.Length != frames)
        {
            _speakingStateTrack = new byte[frames];
            _syllableMarkers = new byte[frames];
            _lastDataVersion = -1;
            resized = true;
        }

        return resized;
    }

    private bool ConfigureDisplayPipeline(SpectrogramAnalysisDescriptor? analysis)
    {
        if (analysis is null || analysis.BinCount <= 0 || _bufferBins <= 0)
        {
            return false;
        }

        float minHz = _orchestrator.Config.MinFrequency;
        float maxHz = _orchestrator.Config.MaxFrequency;
        var scale = _orchestrator.Config.FrequencyScale;
        float minDb = _minDb;
        float maxDb = _maxDb;
        var dynamicRangeMode = _dynamicRangeMode;
        int displayBins = _bufferBins;

        bool mappingChanged = displayBins != _lastDisplayBins
            || !ReferenceEquals(analysis, _lastAnalysisDescriptor)
            || scale != _lastScale
            || MathF.Abs(minHz - _lastMinFrequency) > 1e-3f
            || MathF.Abs(maxHz - _lastMaxFrequency) > 1e-3f;

        bool processingChanged = MathF.Abs(minDb - _lastMinDb) > 1e-3f
            || MathF.Abs(maxDb - _lastMaxDb) > 1e-3f
            || dynamicRangeMode != _lastDynamicRangeMode;

        bool mappingApplied = false;
        if (mappingChanged)
        {
            _displayPipeline.Configure(analysis, displayBins, minHz, maxHz, scale,
                minDb, maxDb, dynamicRangeMode);
            mappingApplied = true;
        }
        else if (processingChanged)
        {
            _displayPipeline.UpdateProcessing(minDb, maxDb, dynamicRangeMode);
        }

        if (mappingApplied)
        {
            UpdateBinFrequencies();
            _lastDisplayBins = displayBins;
            _lastAnalysisDescriptor = analysis;
            _lastScale = scale;
            _lastMinFrequency = minHz;
            _lastMaxFrequency = maxHz;
        }

        if (mappingApplied || processingChanged)
        {
            _lastMinDb = minDb;
            _lastMaxDb = maxDb;
            _lastDynamicRangeMode = dynamicRangeMode;
        }

        return mappingApplied || processingChanged;
    }

    private void UpdateBinFrequencies()
    {
        var centers = _displayPipeline.CenterFrequencies;
        if (centers.Length == 0)
        {
            return;
        }

        if (_binFrequencies.Length != centers.Length)
        {
            _binFrequencies = new float[centers.Length];
        }

        centers.CopyTo(_binFrequencies);
    }

    private void SkiaCanvas_PaintSurfaceGpu(object? sender, DesktopPaintGLSurfaceEventArgs e)
    {
        if (!_backendLocked)
        {
            if (_glControl?.GRContext is null)
            {
                FallbackToCpu();
                return;
            }

            _backendLocked = true;
        }

        RenderSurface(e.Surface.Canvas, e.Info);
    }

    private void SkiaCanvas_PaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        RenderSurface(e.Surface.Canvas, e.Info);
    }

    private void RenderSurface(SKCanvas canvas, SKImageInfo info)
    {
        var size = new SKSize(info.Width, info.Height);
        float dpiScale = GetDpiScale();
        long droppedSamples = 0; // Not tracked in shared orchestrator
        int hopSize = _orchestrator.Config.ComputeHopSize();
        long droppedHops = hopSize > 0 ? droppedSamples / hopSize : droppedSamples;

        var state = new SpectroState(
            FftSize: _orchestrator.Config.FftSize,
            TransformType: _orchestrator.Config.TransformType,
            WindowFunction: _orchestrator.Config.WindowFunction,
            Overlap: _orchestrator.Config.Overlap,
            Scale: _orchestrator.Config.FrequencyScale,
            MinFrequency: _orchestrator.Config.MinFrequency,
            MaxFrequency: _orchestrator.Config.MaxFrequency,
            FrequencyMapper: _displayPipeline,
            MinDb: _minDb,
            MaxDb: _maxDb,
            TimeWindowSeconds: _orchestrator.Config.TimeWindow,
            DisplayBins: _bufferBins,
            FrameCount: _bufferFrameCount,
            ColorMap: _colorMap,
            ReassignMode: _orchestrator.Config.ReassignMode,
            ClarityMode: _orchestrator.Config.ClarityMode,
            ReassignThresholdDb: _orchestrator.Config.ReassignThreshold,
            ReassignSpread: _orchestrator.Config.ReassignSpread,
            ClarityNoise: _orchestrator.Config.ClarityNoise,
            ClarityHarmonic: _orchestrator.Config.ClarityHarmonic,
            ClaritySmoothing: _orchestrator.Config.ClaritySmoothing,
            PitchAlgorithm: _orchestrator.Config.PitchAlgorithm,
            AxisMode: _axisMode,
            VoiceRange: _voiceRange,
            FormantProfile: _orchestrator.Config.FormantProfile,
            ShowRange: _showRange,
            ShowGuides: _showGuides,
            ShowWaveform: _showWaveform,
            ShowSpectrum: _showSpectrum,
            ShowPitchMeter: _showPitchMeter,
            ShowVowelSpace: _showVowelSpace,
            SmoothingMode: _orchestrator.Config.SmoothingMode,
            Brightness: _brightness,
            Gamma: _gamma,
            Contrast: _contrast,
            ColorLevels: _colorLevels,
            NormalizationMode: _orchestrator.Config.NormalizationMode,
            DynamicRangeMode: _dynamicRangeMode,
            IsBypassed: _isBypassed,
            IsPaused: _isPaused,
            UsingGpu: _usingGpu,
            IsProfiling: SpectrographProfiler.IsCollecting,
            AnalysisTiming: default, // Timing not available from shared orchestrator
            UiTiming: new SpectroUiTimingSnapshot(_uiTickUs, _uiCopyUs, _uiMapUs),
            ShowPitch: _showPitch,
            ShowFormants: _showFormants,
            ShowFormantBandwidths: _showFormantBandwidths,
            ShowHarmonics: _showHarmonics,
            HarmonicDisplayMode: _harmonicDisplayMode,
            ShowVoicing: _showVoicing,
            PreEmphasisEnabled: _orchestrator.Config.PreEmphasis,
            HighPassEnabled: _orchestrator.Config.HighPassEnabled,
            HighPassCutoff: _orchestrator.Config.HighPassCutoff,
            LatestFrameId: _latestFrameId,
            AvailableFrames: _availableFrames,
            DroppedHops: droppedHops,
            ReferenceFrameId: _referenceFrameId,
            DataVersion: _lastDataVersion,
            PresetName: _presetHelper.CurrentPresetName,
            Spectrogram: _spectrogram,
            PitchTrack: _pitchTrack,
            PitchConfidence: _pitchConfidence,
            FormantFrequencies: _formantFrequencies,
            FormantBandwidths: _formantBandwidths,
            VoicingStates: _voicingStates,
            HarmonicFrequencies: _harmonicFrequencies,
            HarmonicMagnitudes: _harmonicMagnitudes,
            WaveformMin: _waveformMin,
            WaveformMax: _waveformMax,
            HnrTrack: _hnrTrack,
            CppTrack: _cppTrack,
            SpectralCentroid: _spectralCentroid,
            SpectralSlope: _spectralSlope,
            SpectralFlux: _spectralFlux,
            BinFrequencies: _binFrequencies,
            MaxFormants: _bufferMaxFormants,
            MaxHarmonics: _bufferMaxHarmonics,
            Discontinuities: _discontinuities,
            // Speech Coach
            SpeechCoachEnabled: _speechCoachEnabled,
            ShowSpeechMetrics: _showSpeechMetrics,
            ShowSyllableMarkers: _showSyllableMarkers,
            ShowPauseOverlay: _showPauseOverlay,
            ShowFillerMarkers: _showFillerMarkers,
            SyllableRate: _lastSyllableRate,
            ArticulationRate: _lastArticulationRate,
            PauseRatio: _lastPauseRatio,
            MonotoneScore: _lastMonotoneScore,
            ClarityScore: _lastClarityScore,
            IntelligibilityScore: _lastIntelligibilityScore,
            SpeakingStateTrack: _speakingStateTrack,
            SyllableMarkers: _syllableMarkers
        );

        _renderer.Render(canvas, size, dpiScale, state);
    }

    private void SkiaCanvas_MouseDown(object? sender, MouseButtonEventArgs e)
    {
        if (_skiaCanvas is null)
        {
            return;
        }

        var pos = e.GetPosition(_skiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        // Handle double-click to reset knob to default
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            if (HandleKnobDoubleClick(x, y))
            {
                e.Handled = true;
                return;
            }
        }

        if (TryHandleKnobMouseDown(x, y, e.ChangedButton))
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                CapturePointer();
            }
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        bool handled = HandlePointerDown(x, y);
        if (handled)
        {
            e.Handled = true;
        }
    }

    private void SkiaCanvas_MouseMove(object? sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_skiaCanvas is null)
        {
            return;
        }

        var pos = e.GetPosition(_skiaCanvas);
        bool shiftHeld = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        HandlePointerMove((float)pos.X, (float)pos.Y, e.LeftButton == MouseButtonState.Pressed, shiftHeld);
    }

    private void SkiaCanvas_MouseLeave(object? sender, System.Windows.Input.MouseEventArgs e)
    {
        HandlePointerLeave();
    }

    private void SkiaCanvas_MouseUp(object? sender, MouseButtonEventArgs e)
    {
        HandlePointerUp(e.ChangedButton);
    }

    private void SkiaCanvas_MouseDownWinForms(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        var (x, y) = ToDipPoint(e.X, e.Y);
        var button = ToWpfMouseButton(e.Button);

        // Handle double-click to reset knob to default
        if (e.Button == System.Windows.Forms.MouseButtons.Left && e.Clicks == 2)
        {
            if (HandleKnobDoubleClick(x, y))
            {
                return;
            }
        }

        if (button.HasValue && TryHandleKnobMouseDown(x, y, button.Value))
        {
            if (button == MouseButton.Left)
            {
                CapturePointer();
            }
            return;
        }

        if (e.Button != System.Windows.Forms.MouseButtons.Left)
        {
            return;
        }

        HandlePointerDown(x, y);
    }

    private void SkiaCanvas_MouseMoveWinForms(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        var (x, y) = ToDipPoint(e.X, e.Y);
        bool leftDown = (e.Button & System.Windows.Forms.MouseButtons.Left) != 0;
        bool shiftHeld = System.Windows.Forms.Control.ModifierKeys.HasFlag(System.Windows.Forms.Keys.Shift);
        HandlePointerMove(x, y, leftDown, shiftHeld);
    }

    private void SkiaCanvas_MouseUpWinForms(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        var button = ToWpfMouseButton(e.Button);
        if (button.HasValue)
        {
            HandlePointerUp(button.Value);
        }
    }

    private void SkiaCanvas_MouseLeaveWinForms(object? sender, EventArgs e)
    {
        HandlePointerLeave();
    }

    private bool HandlePointerDown(float x, float y)
    {
        var hit = _renderer.HitTest(x, y);
        switch (hit.Area)
        {
            case SpectroHitArea.TitleBar:
                if (_usingGpu)
                {
                    BeginWinFormsDragMove();
                }
                else
                {
                    DragMove();
                }
                return true;
            case SpectroHitArea.CloseButton:
                Close();
                return true;
            case SpectroHitArea.BypassButton:
                _isBypassed = !_isBypassed; // Local display bypass only
                return true;
            case SpectroHitArea.FftButton:
                CycleFftSize();
                return true;
            case SpectroHitArea.TransformButton:
                CycleTransformType();
                return true;
            case SpectroHitArea.WindowButton:
                CycleWindow();
                return true;
            case SpectroHitArea.OverlapButton:
                CycleOverlap();
                return true;
            case SpectroHitArea.ScaleButton:
                CycleScale();
                return true;
            case SpectroHitArea.ColorButton:
                CycleColorMap();
                return true;
            case SpectroHitArea.ReassignButton:
                CycleReassignMode();
                return true;
            case SpectroHitArea.ClarityButton:
                CycleClarityMode();
                return true;
            case SpectroHitArea.PitchAlgorithmButton:
                CyclePitchAlgorithm();
                return true;
            case SpectroHitArea.AxisModeButton:
                CycleAxisMode();
                return true;
            case SpectroHitArea.SmoothingModeButton:
                CycleSmoothingMode();
                return true;
            case SpectroHitArea.PauseButton:
                TogglePause();
                return true;
            case SpectroHitArea.PresetDropdown:
                if (_skiaCanvas != null)
                {
                    _presetHelper.ShowPresetMenu(_skiaCanvas, _renderer.GetPresetDropdownRect());
                }
                return true;
            case SpectroHitArea.PresetSave:
                if (_skiaCanvas != null)
                {
                    _presetHelper.ShowSaveMenu(_skiaCanvas, this);
                }
                return true;
            case SpectroHitArea.PitchToggle:
                ToggleParameter(ParamShowPitch, _showPitch);
                return true;
            case SpectroHitArea.FormantToggle:
                ToggleParameter(ParamShowFormants, _showFormants);
                return true;
            case SpectroHitArea.HarmonicToggle:
                ToggleParameter(ParamShowHarmonics, _showHarmonics);
                return true;
            case SpectroHitArea.HarmonicModeToggle:
                CycleHarmonicDisplayMode();
                return true;
            case SpectroHitArea.VoicingToggle:
                ToggleParameter(ParamShowVoicing, _showVoicing);
                return true;
            case SpectroHitArea.PreEmphasisToggle:
                ToggleParameter(ParamPreEmphasis, _orchestrator.Config.PreEmphasis);
                return true;
            case SpectroHitArea.HpfToggle:
                ToggleParameter(ParamHighPassEnabled, _orchestrator.Config.HighPassEnabled);
                return true;
            case SpectroHitArea.RangeToggle:
                ToggleParameter(ParamShowRange, _showRange);
                return true;
            case SpectroHitArea.GuidesToggle:
                ToggleParameter(ParamShowGuides, _showGuides);
                return true;
            case SpectroHitArea.VoiceRangeButton:
                CycleVoiceRange();
                return true;
            case SpectroHitArea.FormantProfileButton:
                CycleFormantProfile();
                return true;
            case SpectroHitArea.NormalizationButton:
                CycleNormalizationMode();
                return true;
            case SpectroHitArea.DynamicRangeButton:
                CycleDynamicRangeMode();
                return true;
            case SpectroHitArea.WaveformToggle:
                ToggleParameter(ParamShowWaveform, _showWaveform);
                return true;
            case SpectroHitArea.SpectrumToggle:
                ToggleParameter(ParamShowSpectrum, _showSpectrum);
                return true;
            case SpectroHitArea.PitchMeterToggle:
                ToggleParameter(ParamShowPitchMeter, _showPitchMeter);
                return true;
            case SpectroHitArea.VowelToggle:
                ToggleParameter(ParamShowVowelSpace, _showVowelSpace);
                return true;
            case SpectroHitArea.SpeechToggle:
                ToggleParameter(ParamSpeechCoachEnabled, _speechCoachEnabled);
                return true;
            // Speech Coach toggles
            case SpectroHitArea.SpeechCoachToggle:
                ToggleParameter(ParamSpeechCoachEnabled, _speechCoachEnabled);
                return true;
            case SpectroHitArea.SpeechMetricsToggle:
                ToggleParameter(ParamShowSpeechMetrics, _showSpeechMetrics);
                return true;
            case SpectroHitArea.SyllableMarkersToggle:
                ToggleParameter(ParamShowSyllableMarkers, _showSyllableMarkers);
                return true;
            case SpectroHitArea.PauseOverlayToggle:
                ToggleParameter(ParamShowPauseOverlay, _showPauseOverlay);
                return true;
            case SpectroHitArea.FillerMarkersToggle:
                ToggleParameter(ParamShowFillerMarkers, _showFillerMarkers);
                return true;
            case SpectroHitArea.Spectrogram:
                return TrySetReferenceLine(x);
            default:
                return false;
        }
    }

    private void BeginWinFormsDragMove()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        ReleaseCapture();
        SendMessage(handle, WmNcLButtonDown, new IntPtr(HtCaption), IntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private void HandlePointerMove(float x, float y, bool leftDown, bool shiftHeld = false)
    {
        // Update tooltip hover state for controls
        _renderer.UpdateTooltipHover(x, y);

        if (HandleKnobMouseMove(x, y, leftDown, shiftHeld))
        {
            SetTooltip(null);
            return;
        }

        var hit = _renderer.HitTest(x, y);
        if (hit.Area == SpectroHitArea.Spectrogram)
        {
            UpdateSpectrogramTooltip(x, y);
        }
        else
        {
            SetTooltip(null);
        }
    }

    private void HandlePointerLeave()
    {
        ResetKnobHover();
        SetTooltip(null);
        _renderer.Tooltip.EndHover();
    }

    private void HandlePointerUp(MouseButton button)
    {
        HandleKnobMouseUp(button);
        if (button == MouseButton.Left)
        {
            ReleasePointer();
        }
    }

    private void CapturePointer()
    {
        if (_usingGpu)
        {
            if (_glControl is not null)
            {
                _glControl.Capture = true;
            }
            return;
        }

        _skiaCanvas?.CaptureMouse();
    }

    private void ReleasePointer()
    {
        if (_usingGpu)
        {
            if (_glControl is not null)
            {
                _glControl.Capture = false;
            }
            return;
        }

        _skiaCanvas?.ReleaseMouseCapture();
    }

    private (float x, float y) ToDipPoint(int x, int y)
    {
        float dpiScale = GetDpiScale();
        return (x / dpiScale, y / dpiScale);
    }

    private void ToggleParameter(int index, bool current)
    {
        SetParameter(index, current ? 0f : 1f);
        // Preset tracking removed - using shared orchestrator config
    }

    private void CycleHarmonicDisplayMode()
    {
        var current = _harmonicDisplayMode;
        var next = current switch
        {
            HarmonicDisplayMode.Detected => HarmonicDisplayMode.Theoretical,
            HarmonicDisplayMode.Theoretical => HarmonicDisplayMode.Both,
            HarmonicDisplayMode.Both => HarmonicDisplayMode.Detected,
            _ => HarmonicDisplayMode.Detected
        };
        SetParameter(ParamHarmonicDisplayMode, (float)next);
        // Preset tracking removed - using shared orchestrator config
    }

    private void CycleFftSize()
    {
        int current = _orchestrator.Config.FftSize;
        int index = Array.IndexOf(FftSizes, current);
        int next = FftSizes[(index + 1) % FftSizes.Length];
        SetParameter(ParamFftSize, next);
        // Preset tracking removed - using shared orchestrator config
    }

    private void CycleTransformType()
    {
        SpectrogramTransformType next = _orchestrator.Config.TransformType switch
        {
            SpectrogramTransformType.Fft => SpectrogramTransformType.ZoomFft,
            SpectrogramTransformType.ZoomFft => SpectrogramTransformType.Cqt,
            _ => SpectrogramTransformType.Fft
        };
        SetParameter(ParamTransformType, (float)next);
        if (next == SpectrogramTransformType.Cqt && _orchestrator.Config.PitchAlgorithm == PitchDetectorType.Swipe)
        {
            SetParameter(ParamPitchAlgorithm, (float)PitchDetectorType.Yin);
        }
        // Preset tracking removed - using shared orchestrator config
    }

    private void CycleWindow()
    {
        var current = _orchestrator.Config.WindowFunction;
        int index = Array.IndexOf(WindowFunctions, current);
        int nextIndex = (index + 1) % WindowFunctions.Length;
        SetParameter(ParamWindowFunction, (float)WindowFunctions[nextIndex]);
        // Preset tracking removed - using shared orchestrator config
    }

    private void CycleOverlap()
    {
        float current = _orchestrator.Config.Overlap;
        int index = Array.IndexOf(OverlapOptions, current);
        int nextIndex = (index + 1) % OverlapOptions.Length;
        SetParameter(ParamOverlap, OverlapOptions[nextIndex]);
        // Preset tracking removed - using shared orchestrator config
    }

    private void CycleScale()
    {
        var current = _orchestrator.Config.FrequencyScale;
        int index = Array.IndexOf(Scales, current);
        int nextIndex = (index + 1) % Scales.Length;
        SetParameter(ParamScale, (float)Scales[nextIndex]);
        // Preset tracking removed - using shared orchestrator config
    }

    private void CycleColorMap()
    {
        int current = _colorMap;
        int count = Enum.GetValues<SpectrogramColorMap>().Length;
        int next = (current + 1) % count;
        SetParameter(ParamColorMap, next);
        // Preset tracking removed - using shared orchestrator config
    }

    private void CycleReassignMode()
    {
        SpectrogramReassignMode next = _orchestrator.Config.ReassignMode switch
        {
            SpectrogramReassignMode.Off => SpectrogramReassignMode.Frequency,
            SpectrogramReassignMode.Frequency => SpectrogramReassignMode.Time,
            SpectrogramReassignMode.Time => SpectrogramReassignMode.TimeFrequency,
            _ => SpectrogramReassignMode.Off
        };
        SetParameter(ParamReassignMode, (float)next);
        // Preset tracking removed - using shared orchestrator config
    }

    private void CycleClarityMode()
    {
        ClarityProcessingMode next = _orchestrator.Config.ClarityMode switch
        {
            ClarityProcessingMode.None => ClarityProcessingMode.Noise,
            ClarityProcessingMode.Noise => ClarityProcessingMode.Harmonic,
            ClarityProcessingMode.Harmonic => ClarityProcessingMode.Full,
            _ => ClarityProcessingMode.None
        };
        SetParameter(ParamClarityMode, (float)next);
        // Preset tracking removed - using shared orchestrator config
    }

    private void CyclePitchAlgorithm()
    {
        bool allowSwipe = _orchestrator.Config.TransformType != SpectrogramTransformType.Cqt;
        PitchDetectorType next = _orchestrator.Config.PitchAlgorithm switch
        {
            PitchDetectorType.Yin => PitchDetectorType.Pyin,
            PitchDetectorType.Pyin => PitchDetectorType.Autocorrelation,
            PitchDetectorType.Autocorrelation => PitchDetectorType.Cepstral,
            PitchDetectorType.Cepstral => allowSwipe ? PitchDetectorType.Swipe : PitchDetectorType.Yin,
            _ => PitchDetectorType.Yin
        };
        if (!allowSwipe && next == PitchDetectorType.Swipe)
        {
            next = PitchDetectorType.Yin;
        }
        SetParameter(ParamPitchAlgorithm, (float)next);
        // Preset tracking removed - using shared orchestrator config
    }

    private void CycleAxisMode()
    {
        SpectrogramAxisMode next = _axisMode switch
        {
            SpectrogramAxisMode.Hz => SpectrogramAxisMode.Note,
            SpectrogramAxisMode.Note => SpectrogramAxisMode.Both,
            _ => SpectrogramAxisMode.Hz
        };
        SetParameter(ParamAxisMode, (float)next);
        // Preset tracking removed - using shared orchestrator config
    }

    private void CycleVoiceRange()
    {
        VocalRangeType next = _voiceRange switch
        {
            VocalRangeType.Bass => VocalRangeType.Baritone,
            VocalRangeType.Baritone => VocalRangeType.Tenor,
            VocalRangeType.Tenor => VocalRangeType.Alto,
            VocalRangeType.Alto => VocalRangeType.MezzoSoprano,
            VocalRangeType.MezzoSoprano => VocalRangeType.Soprano,
            _ => VocalRangeType.Bass
        };
        SetParameter(ParamVoiceRange, (float)next);
        // Preset tracking removed - using shared orchestrator config
    }

    private void CycleFormantProfile()
    {
        FormantProfile next = _orchestrator.Config.FormantProfile switch
        {
            FormantProfile.BassBaritone => FormantProfile.Tenor,
            FormantProfile.Tenor => FormantProfile.Alto,
            FormantProfile.Alto => FormantProfile.Soprano,
            _ => FormantProfile.BassBaritone
        };
        SetParameter(ParamFormantProfile, (float)next);
        // Preset tracking removed - using shared orchestrator config
    }

    private void CycleSmoothingMode()
    {
        SpectrogramSmoothingMode next = _orchestrator.Config.SmoothingMode switch
        {
            SpectrogramSmoothingMode.Off => SpectrogramSmoothingMode.Ema,
            SpectrogramSmoothingMode.Ema => SpectrogramSmoothingMode.Bilateral,
            _ => SpectrogramSmoothingMode.Off
        };
        SetParameter(ParamSmoothingMode, (float)next);
        // Preset tracking removed - using shared orchestrator config
    }

    private void CycleNormalizationMode()
    {
        SpectrogramNormalizationMode next = _orchestrator.Config.NormalizationMode switch
        {
            SpectrogramNormalizationMode.None => SpectrogramNormalizationMode.Peak,
            SpectrogramNormalizationMode.Peak => SpectrogramNormalizationMode.Rms,
            SpectrogramNormalizationMode.Rms => SpectrogramNormalizationMode.AWeighted,
            _ => SpectrogramNormalizationMode.None
        };
        SetParameter(ParamNormalizationMode, (float)next);
        // Preset tracking removed - using shared orchestrator config
    }

    private void CycleDynamicRangeMode()
    {
        SpectrogramDynamicRangeMode next = _dynamicRangeMode switch
        {
            SpectrogramDynamicRangeMode.Custom => SpectrogramDynamicRangeMode.VoiceOptimized,
            SpectrogramDynamicRangeMode.VoiceOptimized => SpectrogramDynamicRangeMode.Full,
            SpectrogramDynamicRangeMode.Full => SpectrogramDynamicRangeMode.Compressed,
            SpectrogramDynamicRangeMode.Compressed => SpectrogramDynamicRangeMode.NoiseFloor,
            _ => SpectrogramDynamicRangeMode.Custom
        };
        SetParameter(ParamDynamicRangeMode, (float)next);
        // Preset tracking removed - using shared orchestrator config
    }

    private void TogglePause()
    {
        _isPaused = !_isPaused;
        if (!_isPaused)
        {
            ClearLocalBuffers();
        }
        else
        {
            SetTooltip(null);
        }
    }

    private void ClearLocalBuffers()
    {
        Array.Clear(_spectrogram, 0, _spectrogram.Length);
        Array.Clear(_displayMagnitudes, 0, _displayMagnitudes.Length);
        Array.Clear(_analysisMagnitudes, 0, _analysisMagnitudes.Length);
        Array.Clear(_pitchTrack, 0, _pitchTrack.Length);
        Array.Clear(_pitchConfidence, 0, _pitchConfidence.Length);
        Array.Clear(_formantFrequencies, 0, _formantFrequencies.Length);
        Array.Clear(_formantBandwidths, 0, _formantBandwidths.Length);
        Array.Clear(_voicingStates, 0, _voicingStates.Length);
        Array.Clear(_harmonicFrequencies, 0, _harmonicFrequencies.Length);
        Array.Clear(_harmonicMagnitudes, 0, _harmonicMagnitudes.Length);
        Array.Clear(_waveformMin, 0, _waveformMin.Length);
        Array.Clear(_waveformMax, 0, _waveformMax.Length);
        Array.Clear(_hnrTrack, 0, _hnrTrack.Length);
        Array.Clear(_cppTrack, 0, _cppTrack.Length);
        Array.Clear(_spectralCentroid, 0, _spectralCentroid.Length);
        Array.Clear(_spectralSlope, 0, _spectralSlope.Length);
        Array.Clear(_spectralFlux, 0, _spectralFlux.Length);
        // Speech Coach buffers
        Array.Clear(_speakingStateTrack, 0, _speakingStateTrack.Length);
        Array.Clear(_syllableMarkers, 0, _syllableMarkers.Length);
        _lastSyllableRate = 0f;
        _lastArticulationRate = 0f;
        _lastPauseRatio = 0f;
        _lastMonotoneScore = 0f;
        _lastClarityScore = 0f;
        _lastIntelligibilityScore = 0f;
        _lastDataVersion = -1;
        _lastCopiedFrameId = -1;
        _lastMappedFrameId = -1;
        _latestFrameId = -1;
        _availableFrames = 0;
        _referenceFrameId = null;
    }

    private bool TrySetReferenceLine(float x)
    {
        if (_availableFrames <= 0 || _latestFrameId < 0)
        {
            return false;
        }

        if (!_renderer.TryGetSpectrogramRect(out var rect))
        {
            return false;
        }

        float t = Math.Clamp((x - rect.Left) / rect.Width, 0f, 1f);
        int columnIndex = (int)MathF.Round(t * Math.Max(1, _bufferFrameCount - 1));
        int padFrames = Math.Max(0, _bufferFrameCount - _availableFrames);
        int visibleIndex = columnIndex - padFrames;
        if (visibleIndex < 0 || visibleIndex >= _availableFrames)
        {
            return false;
        }

        long oldestFrameId = _latestFrameId - _availableFrames + 1;
        _referenceFrameId = oldestFrameId + visibleIndex;
        return true;
    }

    private void CullReferenceLine()
    {
        if (_referenceFrameId is null || _availableFrames <= 0 || _latestFrameId < 0)
        {
            return;
        }

        long oldestFrameId = _latestFrameId - _availableFrames + 1;
        if (_referenceFrameId.Value < oldestFrameId)
        {
            _referenceFrameId = null;
        }
    }

    private void UpdateSpectrogramTooltip(float x, float y)
    {
        if (_spectrogramToolTip is null)
        {
            return;
        }

        if (!_renderer.TryGetSpectrogramRect(out var rect) || !rect.Contains(x, y))
        {
            SetTooltip(null);
            return;
        }

        float frequency = GetFrequencyAtPosition(y, rect);
        string note = GetNearestNoteName(frequency);
        string text = $"{FormatFrequency(frequency)} ({note})";
        if (!string.Equals(_currentTooltip, text, StringComparison.Ordinal))
        {
            _currentTooltip = text;
            _spectrogramToolTip.Content = text;
        }

        _spectrogramToolTip.HorizontalOffset = x + 12f;
        _spectrogramToolTip.VerticalOffset = y + 12f;
        _spectrogramToolTip.IsOpen = true;
    }

    private void SetTooltip(string? text)
    {
        if (_spectrogramToolTip is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            _spectrogramToolTip.IsOpen = false;
            _currentTooltip = string.Empty;
            return;
        }

        if (!string.Equals(_currentTooltip, text, StringComparison.Ordinal))
        {
            _currentTooltip = text;
            _spectrogramToolTip.Content = text;
        }
    }

    private float GetFrequencyAtPosition(float y, SkiaSharp.SKRect rect)
    {
        float norm = Math.Clamp((rect.Bottom - y) / rect.Height, 0f, 1f);
        float minHz = _orchestrator.Config.MinFrequency;
        float maxHz = _orchestrator.Config.MaxFrequency;
        float scaledMin = FrequencyScaleUtils.ToScale(_orchestrator.Config.FrequencyScale, minHz);
        float scaledMax = FrequencyScaleUtils.ToScale(_orchestrator.Config.FrequencyScale, maxHz);
        float range = scaledMax - scaledMin;
        if (MathF.Abs(range) < 1e-6f)
        {
            return minHz;
        }

        float scaled = scaledMin + range * norm;
        return FrequencyScaleUtils.FromScale(_orchestrator.Config.FrequencyScale, scaled);
    }

    private static string FormatFrequency(float frequency)
    {
        return frequency >= 1000f ? $"{frequency / 1000f:0.#} kHz" : $"{frequency:0} Hz";
    }

    private static string GetNearestNoteName(float frequency)
    {
        if (frequency <= 0f || float.IsNaN(frequency) || float.IsInfinity(frequency))
        {
            return "--";
        }

        float note = 69f + 12f * MathF.Log2(frequency / 440f);
        int midi = Math.Clamp((int)MathF.Round(note), 0, 127);
        int octave = midi / 12 - 1;
        string name = NoteNames[midi % 12];
        return $"{name}{octave}";
    }

    private void ApplyPreset(string presetName, IReadOnlyDictionary<string, float> parameters)
    {
        _applyingPreset = true;
        try
        {
            var config = _orchestrator.Config;
            foreach (var (name, value) in parameters)
            {
                switch (name)
                {
                    // Analysis parameters (shared orchestrator config)
                    case "FFT Size": config.FftSize = (int)value; break;
                    case "Window": config.WindowFunction = (WindowFunction)(int)value; break;
                    case "Overlap": config.OverlapIndex = Array.IndexOf(AnalysisConfiguration.OverlapOptions, value); break;
                    case "Scale": config.FrequencyScale = (FrequencyScale)(int)value; break;
                    case "Min Freq": config.MinFrequency = value; break;
                    case "Max Freq": config.MaxFrequency = value; break;
                    case "Time Window": config.TimeWindow = value; break;
                    case "Pre-Emphasis": config.PreEmphasis = value > 0.5f; break;
                    case "HPF Enabled": config.HighPassEnabled = value > 0.5f; break;
                    case "HPF Cutoff": config.HighPassCutoff = value; break;
                    case "Reassign": config.ReassignMode = (SpectrogramReassignMode)(int)value; break;
                    case "Reassign Threshold": config.ReassignThreshold = value; break;
                    case "Reassign Spread": config.ReassignSpread = value; break;
                    case "Clarity Mode": config.ClarityMode = (ClarityProcessingMode)(int)value; break;
                    case "Clarity Noise": config.ClarityNoise = value; break;
                    case "Clarity Harmonic": config.ClarityHarmonic = value; break;
                    case "Clarity Smoothing": config.ClaritySmoothing = value; break;
                    case "Pitch Algorithm": config.PitchAlgorithm = (PitchDetectorType)(int)value; break;
                    case "Formant Profile": config.FormantProfile = (FormantProfile)(int)value; break;
                    case "Smoothing Mode": config.SmoothingMode = (SpectrogramSmoothingMode)(int)value; break;
                    case "Normalization": config.NormalizationMode = (SpectrogramNormalizationMode)(int)value; break;
                    case "Transform": config.TransformType = (SpectrogramTransformType)(int)value; break;
                    case "CQT Bins/Oct": config.CqtBinsPerOctave = (int)value; break;

                    // Display parameters (local to this window)
                    case "Min dB": _minDb = value; break;
                    case "Max dB": _maxDb = value; break;
                    case "Color Map": _colorMap = (int)value; break;
                    case "Brightness": _brightness = value; break;
                    case "Gamma": _gamma = value; break;
                    case "Contrast": _contrast = value; break;
                    case "Color Levels": _colorLevels = (int)value; break;
                    case "Axis Mode": _axisMode = (SpectrogramAxisMode)(int)value; break;
                    case "Voice Range": _voiceRange = (VocalRangeType)(int)value; break;
                    case "Dynamic Range": _dynamicRangeMode = (SpectrogramDynamicRangeMode)(int)value; break;

                    // Toggle overlays/views (local)
                    case "Pitch Overlay": _showPitch = value > 0.5f; break;
                    case "Formants": _showFormants = value > 0.5f; break;
                    case "Harmonics": _showHarmonics = value > 0.5f; break;
                    case "Voicing": _showVoicing = value > 0.5f; break;
                    case "Range Overlay": _showRange = value > 0.5f; break;
                    case "Guides": _showGuides = value > 0.5f; break;
                    case "Waveform View": _showWaveform = value > 0.5f; break;
                    case "Spectrum View": _showSpectrum = value > 0.5f; break;
                    case "Pitch Meter": _showPitchMeter = value > 0.5f; break;
                    case "Vowel View": _showVowelSpace = value > 0.5f; break;
                }
            }
        }
        finally
        {
            _applyingPreset = false;
            UpdateSubscription();
        }
    }

    private Dictionary<string, float> GetCurrentParameters()
    {
        return new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            ["FFT Size"] = _orchestrator.Config.FftSize,
            ["Window"] = (float)_orchestrator.Config.WindowFunction,
            ["Overlap"] = _orchestrator.Config.Overlap,
            ["Scale"] = (float)_orchestrator.Config.FrequencyScale,
            ["Min Freq"] = _orchestrator.Config.MinFrequency,
            ["Max Freq"] = _orchestrator.Config.MaxFrequency,
            ["Min dB"] = _minDb,
            ["Max dB"] = _maxDb,
            ["Time Window"] = _orchestrator.Config.TimeWindow,
            ["Color Map"] = _colorMap,
            ["Pitch Overlay"] = _showPitch ? 1f : 0f,
            ["Formants"] = _showFormants ? 1f : 0f,
            ["Harmonics"] = _showHarmonics ? 1f : 0f,
            ["Voicing"] = _showVoicing ? 1f : 0f,
            ["Pre-Emphasis"] = _orchestrator.Config.PreEmphasis ? 1f : 0f,
            ["HPF Enabled"] = _orchestrator.Config.HighPassEnabled ? 1f : 0f,
            ["HPF Cutoff"] = _orchestrator.Config.HighPassCutoff,
            ["LPC Order"] = 14f, // Default LPC order for formant analysis
            ["Reassign"] = (float)_orchestrator.Config.ReassignMode,
            ["Reassign Threshold"] = _orchestrator.Config.ReassignThreshold,
            ["Reassign Spread"] = _orchestrator.Config.ReassignSpread,
            ["Clarity Mode"] = (float)_orchestrator.Config.ClarityMode,
            ["Clarity Noise"] = _orchestrator.Config.ClarityNoise,
            ["Clarity Harmonic"] = _orchestrator.Config.ClarityHarmonic,
            ["Clarity Smoothing"] = _orchestrator.Config.ClaritySmoothing,
            ["Pitch Algorithm"] = (float)_orchestrator.Config.PitchAlgorithm,
            ["Axis Mode"] = (float)_axisMode,
            ["Voice Range"] = (float)_voiceRange,
            ["Formant Profile"] = (float)_orchestrator.Config.FormantProfile,
            ["Range Overlay"] = _showRange ? 1f : 0f,
            ["Guides"] = _showGuides ? 1f : 0f,
            ["Waveform View"] = _showWaveform ? 1f : 0f,
            ["Spectrum View"] = _showSpectrum ? 1f : 0f,
            ["Pitch Meter"] = _showPitchMeter ? 1f : 0f,
            ["Vowel View"] = _showVowelSpace ? 1f : 0f,
            ["Smoothing Mode"] = (float)_orchestrator.Config.SmoothingMode,
            ["Brightness"] = _brightness,
            ["Gamma"] = _gamma,
            ["Contrast"] = _contrast,
            ["Color Levels"] = _colorLevels,
            ["Normalization"] = (float)_orchestrator.Config.NormalizationMode,
            ["Dynamic Range"] = (float)_dynamicRangeMode,
            ["Transform"] = (float)_orchestrator.Config.TransformType,
            ["CQT Bins/Oct"] = _orchestrator.Config.CqtBinsPerOctave
        };
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }

    private AnalysisCapabilities ComputeRequiredCapabilities()
    {
        var caps = AnalysisCapabilities.Spectrogram;

        if (_showPitch || _showPitchMeter)
            caps |= AnalysisCapabilities.Pitch;

        if (_showFormants || _showVowelSpace)
            caps |= AnalysisCapabilities.Formants;

        if (_showHarmonics)
            caps |= AnalysisCapabilities.Harmonics;

        if (_showVoicing)
            caps |= AnalysisCapabilities.VoicingState;

        if (_showWaveform)
            caps |= AnalysisCapabilities.Waveform;

        if (_showSpectrum)
            caps |= AnalysisCapabilities.SpectralFeatures;

        if (_speechCoachEnabled || _showSpeechMetrics)
            caps |= AnalysisCapabilities.SpeechMetrics;

        return caps;
    }

    private void UpdateSubscription()
    {
        var requiredCaps = ComputeRequiredCapabilities();

        // Dispose old subscription and create new one with updated capabilities
        _subscription?.Dispose();
        _subscription = _orchestrator.Subscribe(requiredCaps);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _renderTimer.Stop();
        _subscription?.Dispose();
        _subscription = null;
        _renderer.Dispose();
        if (_glControl is not null)
        {
            DetachWinFormsInputHandlers(_glControl);
            _glControl.PaintSurface -= SkiaCanvas_PaintSurfaceGpu;
            _glControl.Dispose();
            _glControl = null;
        }
        if (_glHost is not null)
        {
            _glHost.Child = null;
            _glHost.Dispose();
            _glHost = null;
        }
        GC.SuppressFinalize(this);
    }

}
