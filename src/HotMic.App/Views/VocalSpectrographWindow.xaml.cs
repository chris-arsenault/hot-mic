using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HotMic.App.UI.PluginComponents;
using HotMic.Core.Dsp;
using HotMic.Core.Plugins.BuiltIn;
using HotMic.Core.Presets;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using DesktopPaintGLSurfaceEventArgs = SkiaSharp.Views.Desktop.SKPaintGLSurfaceEventArgs;
using WpfToolTip = System.Windows.Controls.ToolTip;

namespace HotMic.App.Views;

public partial class VocalSpectrographWindow : Window
{
    private readonly VocalSpectrographRenderer _renderer = new(PluginComponentTheme.BlueOnBlack);
    private readonly DisplayPipeline _displayPipeline = new();
    private readonly VocalSpectrographPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginPresetHelper _presetHelper;

    private FrameworkElement? _skiaCanvas;
    private WindowsFormsHost? _glHost;
    private SKGLControl? _glControl;
    private bool _backendLocked;
    private bool _usingGpu;

    private int _activeKnob = -1;
    private float _dragStartY;
    private float _dragStartValue;
    private int _hoveredKnob = -1;
    private bool _isPaused;
    private long _latestFrameId = -1;
    private int _availableFrames;
    private long? _referenceFrameId;
    private WpfToolTip? _spectrogramToolTip;
    private string _currentTooltip = string.Empty;

    private int _lastDataVersion = -1;
    private float[] _spectrogram = Array.Empty<float>();
    private float[] _displayMagnitudes = Array.Empty<float>();
    private float[] _analysisMagnitudes = Array.Empty<float>();
    private float[] _cqtFrequencies = Array.Empty<float>();
    private float[] _pitchTrack = Array.Empty<float>();
    private float[] _pitchConfidence = Array.Empty<float>();
    private float[] _formantFrequencies = Array.Empty<float>();
    private float[] _formantBandwidths = Array.Empty<float>();
    private byte[] _voicingStates = Array.Empty<byte>();
    private float[] _harmonicFrequencies = Array.Empty<float>();
    private float[] _waveformMin = Array.Empty<float>();
    private float[] _waveformMax = Array.Empty<float>();
    private float[] _hnrTrack = Array.Empty<float>();
    private float[] _cppTrack = Array.Empty<float>();
    private float[] _spectralCentroid = Array.Empty<float>();
    private float[] _spectralSlope = Array.Empty<float>();
    private float[] _spectralFlux = Array.Empty<float>();
    private float[] _binFrequencies = Array.Empty<float>();
    private int _bufferFrameCount;
    private int _bufferBins;
    private int _bufferAnalysisBins;
    private int _bufferMaxFormants;
    private int _bufferMaxHarmonics;
    private int _lastDisplayBins;
    private int _lastAnalysisBins;
    private float _lastBinResolutionHz;
    private FrequencyScale _lastScale;
    private float _lastMinFrequency;
    private float _lastMaxFrequency;
    private float _lastMinDb;
    private float _lastMaxDb;
    private SpectrogramDynamicRangeMode _lastDynamicRangeMode;
    private SpectrogramTransformType _lastTransformType;
    private int _lastCqtBinsPerOctave;

    private static readonly int[] FftSizes = { 1024, 2048, 4096, 8192 };
    private static readonly WindowFunction[] WindowFunctions =
    {
        WindowFunction.Hann,
        WindowFunction.Hamming,
        WindowFunction.BlackmanHarris,
        WindowFunction.Gaussian,
        WindowFunction.Kaiser
    };
    private static readonly float[] OverlapOptions = { 0.5f, 0.75f, 0.875f };
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

    private static readonly (int paramIndex, float min, float max)[] KnobParams =
    {
        (VocalSpectrographPlugin.MinFrequencyIndex, 20f, 2000f),
        (VocalSpectrographPlugin.MaxFrequencyIndex, 2000f, 12000f),
        (VocalSpectrographPlugin.MinDbIndex, -120f, -20f),
        (VocalSpectrographPlugin.MaxDbIndex, -40f, 0f),
        (VocalSpectrographPlugin.TimeWindowIndex, 1f, 60f),
        (VocalSpectrographPlugin.HighPassCutoffIndex, 20f, 120f),
        (VocalSpectrographPlugin.ReassignThresholdIndex, -120f, -20f),
        (VocalSpectrographPlugin.ReassignSpreadIndex, 0f, 1f),
        (VocalSpectrographPlugin.ClarityNoiseIndex, 0f, 1f),
        (VocalSpectrographPlugin.ClarityHarmonicIndex, 0f, 1f),
        (VocalSpectrographPlugin.ClaritySmoothingIndex, 0f, 1f),
        (VocalSpectrographPlugin.BrightnessIndex, 0.5f, 2f),
        (VocalSpectrographPlugin.GammaIndex, 0.6f, 1.2f),
        (VocalSpectrographPlugin.ContrastIndex, 0.8f, 1.5f),
        (VocalSpectrographPlugin.ColorLevelsIndex, 16f, 64f)
    };

    public VocalSpectrographWindow(VocalSpectrographPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
    {
        InitializeComponent();
        _plugin = plugin;
        _parameterCallback = parameterCallback;
        _bypassCallback = bypassCallback;
        _presetHelper = new PluginPresetHelper(
            plugin.Id,
            PluginPresetManager.Default,
            ApplyPreset,
            GetCurrentParameters);
        InitializeSkiaSurface();

        var preferredSize = VocalSpectrographRenderer.GetPreferredSize();
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
            _plugin.SetVisualizationActive(true);
            _renderTimer.Start();
        };
        Closed += (_, _) =>
        {
            _renderTimer.Stop();
            _plugin.SetVisualizationActive(false);
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
        };
    }

    private void InitializeSkiaSurface()
    {
        if (TryCreateGpuCanvas())
        {
            return;
        }

        CreateCpuCanvas();
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
    }

    private void DetachWinFormsInputHandlers(SKGLControl control)
    {
        control.MouseDown -= SkiaCanvas_MouseDownWinForms;
        control.MouseMove -= SkiaCanvas_MouseMoveWinForms;
        control.MouseUp -= SkiaCanvas_MouseUpWinForms;
        control.MouseLeave -= SkiaCanvas_MouseLeaveWinForms;
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

    private void OnRenderTick(object? sender, EventArgs e)
    {
        EnsureBuffers();

        bool dataUpdated = false;
        int analysisBins = _bufferAnalysisBins;
        SpectrogramTransformType transformType = _plugin.TransformType;
        float binResolutionHz = _plugin.ZoomFftBinResolution;
        if (binResolutionHz <= 0f)
        {
            binResolutionHz = _lastBinResolutionHz;
        }

        if (!_isPaused)
        {
            int dataVersion = _plugin.DataVersion;
            if (dataVersion != _lastDataVersion)
            {
                bool overlayCopied = _plugin.CopySpectrogramData(_displayMagnitudes, _pitchTrack, _pitchConfidence,
                        _formantFrequencies, _formantBandwidths, _voicingStates, _harmonicFrequencies,
                        _waveformMin, _waveformMax, _hnrTrack, _cppTrack, _spectralCentroid,
                        _spectralSlope, _spectralFlux);
                if (overlayCopied)
                {
                    _latestFrameId = _plugin.LatestFrameId;
                    _availableFrames = _plugin.AvailableFrames;
                    CullReferenceLine();
                }

                bool linearCopied = _plugin.CopyLinearMagnitudes(_analysisMagnitudes,
                    out analysisBins, out binResolutionHz, out transformType);
                if (linearCopied)
                {
                    _lastBinResolutionHz = binResolutionHz;
                }

                bool reassignActive = _plugin.ReassignMode != SpectrogramReassignMode.Off;
                if (overlayCopied && (reassignActive || linearCopied))
                {
                    _lastDataVersion = dataVersion;
                    dataUpdated = true;
                }
            }

            if (_lastDataVersion == dataVersion)
            {
                _latestFrameId = _plugin.LatestFrameId;
                _availableFrames = _plugin.AvailableFrames;
                CullReferenceLine();
            }
        }

        bool displayChanged = ConfigureDisplayPipeline(analysisBins, transformType, binResolutionHz);
        if (displayChanged || dataUpdated)
        {
            bool reassignActive = _plugin.ReassignMode != SpectrogramReassignMode.Off;
            if (reassignActive)
            {
                _displayPipeline.ProcessDisplayFrames(_displayMagnitudes, _spectrogram, _bufferFrameCount, _voicingStates);
            }
            else
            {
                _displayPipeline.ProcessFrames(_analysisMagnitudes, _spectrogram, _bufferFrameCount, _voicingStates);
            }
        }

        InvalidateRenderSurface();
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

    private void EnsureBuffers()
    {
        int frames = Math.Max(1, _plugin.FrameCount);
        int bins = Math.Max(1, _plugin.DisplayBins);
        int analysisBins = Math.Max(1, _plugin.AnalysisBins);
        int maxFormants = _plugin.MaxFormantCount;
        int maxHarmonics = _plugin.MaxHarmonicCount;

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
        }

        if (_displayMagnitudes.Length != spectrogramLength)
        {
            _displayMagnitudes = new float[spectrogramLength];
            _lastDataVersion = -1;
        }

        int analysisLength = frames * analysisBins;
        if (_analysisMagnitudes.Length != analysisLength)
        {
            _analysisMagnitudes = new float[analysisLength];
            _lastDataVersion = -1;
        }

        if (_pitchTrack.Length != frames)
        {
            _pitchTrack = new float[frames];
            _pitchConfidence = new float[frames];
            _voicingStates = new byte[frames];
            _lastDataVersion = -1;
        }

        int formantLength = frames * maxFormants;
        if (_formantFrequencies.Length != formantLength)
        {
            _formantFrequencies = new float[formantLength];
            _formantBandwidths = new float[formantLength];
            _lastDataVersion = -1;
        }

        int harmonicLength = frames * maxHarmonics;
        if (_harmonicFrequencies.Length != harmonicLength)
        {
            _harmonicFrequencies = new float[harmonicLength];
            _lastDataVersion = -1;
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
        }

        if (_binFrequencies.Length != bins)
        {
            _binFrequencies = new float[bins];
            _lastDataVersion = -1;
        }
    }

    private bool ConfigureDisplayPipeline(int analysisBins, SpectrogramTransformType transformType, float binResolutionHz)
    {
        if (analysisBins <= 0 || _bufferBins <= 0)
        {
            return false;
        }

        float minHz = _plugin.MinFrequency;
        float maxHz = _plugin.MaxFrequency;
        var scale = _plugin.Scale;
        float minDb = _plugin.MinDb;
        float maxDb = _plugin.MaxDb;
        var dynamicRangeMode = _plugin.DynamicRangeMode;
        int displayBins = _bufferBins;
        int sampleRate = _plugin.SampleRate;
        int cqtBinsPerOctave = _plugin.CqtBinsPerOctave;

        bool mappingChanged = displayBins != _lastDisplayBins
            || analysisBins != _lastAnalysisBins
            || transformType != _lastTransformType
            || scale != _lastScale
            || MathF.Abs(minHz - _lastMinFrequency) > 1e-3f
            || MathF.Abs(maxHz - _lastMaxFrequency) > 1e-3f
            || (transformType == SpectrogramTransformType.Cqt && cqtBinsPerOctave != _lastCqtBinsPerOctave);

        bool processingChanged = MathF.Abs(minDb - _lastMinDb) > 1e-3f
            || MathF.Abs(maxDb - _lastMaxDb) > 1e-3f
            || dynamicRangeMode != _lastDynamicRangeMode;

        bool mappingApplied = false;
        if (mappingChanged)
        {
            switch (transformType)
            {
                case SpectrogramTransformType.Cqt:
                    if (TryGetCqtFrequencies(analysisBins, out var cqtFrequencies))
                    {
                        _displayPipeline.ConfigureForCqt(cqtFrequencies, displayBins, minHz, maxHz, scale,
                            minDb, maxDb, dynamicRangeMode);
                        mappingApplied = true;
                    }
                    break;
                case SpectrogramTransformType.ZoomFft:
                    if (binResolutionHz > 0f)
                    {
                        _displayPipeline.ConfigureForZoomFft(analysisBins, displayBins, sampleRate, minHz, maxHz, scale,
                            minDb, maxDb, dynamicRangeMode, binResolutionHz);
                        mappingApplied = true;
                    }
                    break;
                default:
                    _displayPipeline.ConfigureForFft(analysisBins, displayBins, sampleRate, minHz, maxHz, scale,
                        minDb, maxDb, dynamicRangeMode);
                    mappingApplied = true;
                    break;
            }
        }
        else if (processingChanged)
        {
            _displayPipeline.UpdateProcessing(minDb, maxDb, dynamicRangeMode);
        }

        if (mappingApplied)
        {
            UpdateBinFrequencies();
            _lastDisplayBins = displayBins;
            _lastAnalysisBins = analysisBins;
            _lastTransformType = transformType;
            _lastScale = scale;
            _lastMinFrequency = minHz;
            _lastMaxFrequency = maxHz;
            _lastCqtBinsPerOctave = cqtBinsPerOctave;
        }

        if (mappingApplied || processingChanged)
        {
            _lastMinDb = minDb;
            _lastMaxDb = maxDb;
            _lastDynamicRangeMode = dynamicRangeMode;
        }

        return mappingApplied || processingChanged;
    }

    private bool TryGetCqtFrequencies(int analysisBins, out ReadOnlySpan<float> frequencies)
    {
        if (analysisBins <= 0)
        {
            frequencies = ReadOnlySpan<float>.Empty;
            return false;
        }

        if (_cqtFrequencies.Length < analysisBins)
        {
            _cqtFrequencies = new float[analysisBins];
        }

        int count = _plugin.GetCqtFrequencies(_cqtFrequencies);
        if (count <= 0)
        {
            frequencies = ReadOnlySpan<float>.Empty;
            return false;
        }

        if (count > _cqtFrequencies.Length)
        {
            _cqtFrequencies = new float[count];
            count = _plugin.GetCqtFrequencies(_cqtFrequencies);
        }

        frequencies = _cqtFrequencies.AsSpan(0, Math.Min(count, _cqtFrequencies.Length));
        return count > 0;
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

        var state = new VocalSpectrographState(
            FftSize: _plugin.FftSize,
            WindowFunction: _plugin.WindowFunction,
            Overlap: _plugin.Overlap,
            Scale: _plugin.Scale,
            MinFrequency: _plugin.MinFrequency,
            MaxFrequency: _plugin.MaxFrequency,
            MinDb: _plugin.MinDb,
            MaxDb: _plugin.MaxDb,
            TimeWindowSeconds: _plugin.TimeWindowSeconds,
            DisplayBins: _bufferBins,
            FrameCount: _bufferFrameCount,
            ColorMap: _plugin.ColorMap,
            ReassignMode: _plugin.ReassignMode,
            ClarityMode: _plugin.ClarityMode,
            ReassignThresholdDb: _plugin.ReassignThresholdDb,
            ReassignSpread: _plugin.ReassignSpread,
            ClarityNoise: _plugin.ClarityNoise,
            ClarityHarmonic: _plugin.ClarityHarmonic,
            ClaritySmoothing: _plugin.ClaritySmoothing,
            PitchAlgorithm: _plugin.PitchAlgorithm,
            AxisMode: _plugin.AxisMode,
            VoiceRange: _plugin.VoiceRange,
            ShowRange: _plugin.ShowRange,
            ShowGuides: _plugin.ShowGuides,
            ShowWaveform: _plugin.ShowWaveform,
            ShowSpectrum: _plugin.ShowSpectrum,
            ShowPitchMeter: _plugin.ShowPitchMeter,
            ShowVowelSpace: _plugin.ShowVowelSpace,
            SmoothingMode: _plugin.SmoothingMode,
            Brightness: _plugin.Brightness,
            Gamma: _plugin.Gamma,
            Contrast: _plugin.Contrast,
            ColorLevels: _plugin.ColorLevels,
            NormalizationMode: _plugin.NormalizationMode,
            DynamicRangeMode: _plugin.DynamicRangeMode,
            IsBypassed: _plugin.IsBypassed,
            IsPaused: _isPaused,
            ShowPitch: _plugin.ShowPitch,
            ShowFormants: _plugin.ShowFormants,
            ShowHarmonics: _plugin.ShowHarmonics,
            ShowVoicing: _plugin.ShowVoicing,
            PreEmphasisEnabled: _plugin.PreEmphasisEnabled,
            HighPassEnabled: _plugin.HighPassEnabled,
            HighPassCutoff: _plugin.HighPassCutoff,
            LatestFrameId: _latestFrameId,
            AvailableFrames: _availableFrames,
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
            WaveformMin: _waveformMin,
            WaveformMax: _waveformMax,
            HnrTrack: _hnrTrack,
            CppTrack: _cppTrack,
            SpectralCentroid: _spectralCentroid,
            SpectralSlope: _spectralSlope,
            SpectralFlux: _spectralFlux,
            BinFrequencies: _binFrequencies,
            MaxFormants: _bufferMaxFormants,
            MaxHarmonics: _bufferMaxHarmonics
        );

        _renderer.Render(canvas, size, dpiScale, state);
    }

    private void SkiaCanvas_MouseDown(object? sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || _skiaCanvas is null)
        {
            return;
        }

        var pos = e.GetPosition(_skiaCanvas);
        bool handled = HandlePointerDown((float)pos.X, (float)pos.Y);
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
        HandlePointerMove((float)pos.X, (float)pos.Y, e.LeftButton == MouseButtonState.Pressed);
    }

    private void SkiaCanvas_MouseLeave(object? sender, System.Windows.Input.MouseEventArgs e)
    {
        HandlePointerLeave();
    }

    private void SkiaCanvas_MouseUp(object? sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        HandlePointerUp();
    }

    private void SkiaCanvas_MouseDownWinForms(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        if (e.Button != System.Windows.Forms.MouseButtons.Left)
        {
            return;
        }

        var (x, y) = ToDipPoint(e.X, e.Y);
        HandlePointerDown(x, y);
    }

    private void SkiaCanvas_MouseMoveWinForms(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        var (x, y) = ToDipPoint(e.X, e.Y);
        bool leftDown = (e.Button & System.Windows.Forms.MouseButtons.Left) != 0;
        HandlePointerMove(x, y, leftDown);
    }

    private void SkiaCanvas_MouseUpWinForms(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        if (e.Button != System.Windows.Forms.MouseButtons.Left)
        {
            return;
        }

        HandlePointerUp();
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
            case SpectrographHitArea.TitleBar:
                DragMove();
                return true;
            case SpectrographHitArea.CloseButton:
                Close();
                return true;
            case SpectrographHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                return true;
            case SpectrographHitArea.FftButton:
                CycleFftSize();
                return true;
            case SpectrographHitArea.WindowButton:
                CycleWindow();
                return true;
            case SpectrographHitArea.OverlapButton:
                CycleOverlap();
                return true;
            case SpectrographHitArea.ScaleButton:
                CycleScale();
                return true;
            case SpectrographHitArea.ColorButton:
                CycleColorMap();
                return true;
            case SpectrographHitArea.ReassignButton:
                CycleReassignMode();
                return true;
            case SpectrographHitArea.ClarityButton:
                CycleClarityMode();
                return true;
            case SpectrographHitArea.PitchAlgorithmButton:
                CyclePitchAlgorithm();
                return true;
            case SpectrographHitArea.AxisModeButton:
                CycleAxisMode();
                return true;
            case SpectrographHitArea.SmoothingModeButton:
                CycleSmoothingMode();
                return true;
            case SpectrographHitArea.PauseButton:
                TogglePause();
                return true;
            case SpectrographHitArea.PresetDropdown:
                if (_skiaCanvas is not null)
                {
                    _presetHelper.ShowPresetMenu(_skiaCanvas, _renderer.GetPresetDropdownRect());
                }
                return true;
            case SpectrographHitArea.PresetSave:
                if (_skiaCanvas is not null)
                {
                    _presetHelper.ShowSaveMenu(_skiaCanvas, this);
                }
                return true;
            case SpectrographHitArea.PitchToggle:
                ToggleParameter(VocalSpectrographPlugin.ShowPitchIndex, _plugin.ShowPitch);
                return true;
            case SpectrographHitArea.FormantToggle:
                ToggleParameter(VocalSpectrographPlugin.ShowFormantsIndex, _plugin.ShowFormants);
                return true;
            case SpectrographHitArea.HarmonicToggle:
                ToggleParameter(VocalSpectrographPlugin.ShowHarmonicsIndex, _plugin.ShowHarmonics);
                return true;
            case SpectrographHitArea.VoicingToggle:
                ToggleParameter(VocalSpectrographPlugin.ShowVoicingIndex, _plugin.ShowVoicing);
                return true;
            case SpectrographHitArea.PreEmphasisToggle:
                ToggleParameter(VocalSpectrographPlugin.PreEmphasisIndex, _plugin.PreEmphasisEnabled);
                return true;
            case SpectrographHitArea.HpfToggle:
                ToggleParameter(VocalSpectrographPlugin.HighPassEnabledIndex, _plugin.HighPassEnabled);
                return true;
            case SpectrographHitArea.RangeToggle:
                ToggleParameter(VocalSpectrographPlugin.ShowRangeIndex, _plugin.ShowRange);
                return true;
            case SpectrographHitArea.GuidesToggle:
                ToggleParameter(VocalSpectrographPlugin.ShowGuidesIndex, _plugin.ShowGuides);
                return true;
            case SpectrographHitArea.VoiceRangeButton:
                CycleVoiceRange();
                return true;
            case SpectrographHitArea.NormalizationButton:
                CycleNormalizationMode();
                return true;
            case SpectrographHitArea.DynamicRangeButton:
                CycleDynamicRangeMode();
                return true;
            case SpectrographHitArea.WaveformToggle:
                ToggleParameter(VocalSpectrographPlugin.ShowWaveformIndex, _plugin.ShowWaveform);
                return true;
            case SpectrographHitArea.SpectrumToggle:
                ToggleParameter(VocalSpectrographPlugin.ShowSpectrumIndex, _plugin.ShowSpectrum);
                return true;
            case SpectrographHitArea.PitchMeterToggle:
                ToggleParameter(VocalSpectrographPlugin.ShowPitchMeterIndex, _plugin.ShowPitchMeter);
                return true;
            case SpectrographHitArea.VowelToggle:
                ToggleParameter(VocalSpectrographPlugin.ShowVowelSpaceIndex, _plugin.ShowVowelSpace);
                return true;
            case SpectrographHitArea.Spectrogram:
                return TrySetReferenceLine(x);
            case SpectrographHitArea.Knob:
                _activeKnob = hit.KnobIndex;
                _dragStartY = y;
                _dragStartValue = GetKnobNormalizedValue(hit.KnobIndex);
                CapturePointer();
                return true;
            default:
                return false;
        }
    }

    private void HandlePointerMove(float x, float y, bool leftDown)
    {
        if (_activeKnob >= 0 && leftDown)
        {
            float deltaY = _dragStartY - y;
            float newNormalized = RotaryKnob.CalculateValueFromDrag(_dragStartValue, -deltaY, 0.004f);
            ApplyKnobValue(_activeKnob, newNormalized);
            SetTooltip(null);
            return;
        }

        var hit = _renderer.HitTest(x, y);
        _hoveredKnob = hit.Area == SpectrographHitArea.Knob ? hit.KnobIndex : -1;
        if (hit.Area == SpectrographHitArea.Spectrogram)
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
        _hoveredKnob = -1;
        SetTooltip(null);
    }

    private void HandlePointerUp()
    {
        _activeKnob = -1;
        ReleasePointer();
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

    private float GetKnobNormalizedValue(int knobIndex)
    {
        if (knobIndex < 0 || knobIndex >= KnobParams.Length)
        {
            return 0f;
        }

        float value = knobIndex switch
        {
            0 => _plugin.MinFrequency,
            1 => _plugin.MaxFrequency,
            2 => _plugin.MinDb,
            3 => _plugin.MaxDb,
            4 => _plugin.TimeWindowSeconds,
            5 => _plugin.HighPassCutoff,
            6 => _plugin.ReassignThresholdDb,
            7 => _plugin.ReassignSpread,
            8 => _plugin.ClarityNoise,
            9 => _plugin.ClarityHarmonic,
            10 => _plugin.ClaritySmoothing,
            11 => _plugin.Brightness,
            12 => _plugin.Gamma,
            13 => _plugin.Contrast,
            14 => _plugin.ColorLevels,
            _ => 0f
        };

        var (_, min, max) = KnobParams[knobIndex];
        return (value - min) / (max - min);
    }

    private void ApplyKnobValue(int knobIndex, float normalizedValue)
    {
        if (knobIndex < 0 || knobIndex >= KnobParams.Length)
        {
            return;
        }

        normalizedValue = Math.Clamp(normalizedValue, 0f, 1f);
        var (paramIndex, min, max) = KnobParams[knobIndex];
        float value = min + normalizedValue * (max - min);
        _parameterCallback(paramIndex, value);
        _presetHelper.MarkAsCustom();
    }

    private void ToggleParameter(int index, bool current)
    {
        _parameterCallback(index, current ? 0f : 1f);
        _presetHelper.MarkAsCustom();
    }

    private void CycleFftSize()
    {
        int current = _plugin.FftSize;
        int index = Array.IndexOf(FftSizes, current);
        int next = FftSizes[(index + 1) % FftSizes.Length];
        _parameterCallback(VocalSpectrographPlugin.FftSizeIndex, next);
        _presetHelper.MarkAsCustom();
    }

    private void CycleWindow()
    {
        var current = _plugin.WindowFunction;
        int index = Array.IndexOf(WindowFunctions, current);
        int nextIndex = (index + 1) % WindowFunctions.Length;
        _parameterCallback(VocalSpectrographPlugin.WindowFunctionIndex, (float)WindowFunctions[nextIndex]);
        _presetHelper.MarkAsCustom();
    }

    private void CycleOverlap()
    {
        float current = _plugin.Overlap;
        int index = Array.IndexOf(OverlapOptions, current);
        int nextIndex = (index + 1) % OverlapOptions.Length;
        _parameterCallback(VocalSpectrographPlugin.OverlapIndex, OverlapOptions[nextIndex]);
        _presetHelper.MarkAsCustom();
    }

    private void CycleScale()
    {
        var current = _plugin.Scale;
        int index = Array.IndexOf(Scales, current);
        int nextIndex = (index + 1) % Scales.Length;
        _parameterCallback(VocalSpectrographPlugin.ScaleIndex, (float)Scales[nextIndex]);
        _presetHelper.MarkAsCustom();
    }

    private void CycleColorMap()
    {
        int current = _plugin.ColorMap;
        int count = Enum.GetValues<SpectrogramColorMap>().Length;
        int next = (current + 1) % count;
        _parameterCallback(VocalSpectrographPlugin.ColorMapIndex, next);
        _presetHelper.MarkAsCustom();
    }

    private void CycleReassignMode()
    {
        SpectrogramReassignMode next = _plugin.ReassignMode switch
        {
            SpectrogramReassignMode.Off => SpectrogramReassignMode.Frequency,
            SpectrogramReassignMode.Frequency => SpectrogramReassignMode.Time,
            SpectrogramReassignMode.Time => SpectrogramReassignMode.TimeFrequency,
            _ => SpectrogramReassignMode.Off
        };
        _parameterCallback(VocalSpectrographPlugin.ReassignModeIndex, (float)next);
        _presetHelper.MarkAsCustom();
    }

    private void CycleClarityMode()
    {
        ClarityProcessingMode next = _plugin.ClarityMode switch
        {
            ClarityProcessingMode.None => ClarityProcessingMode.Noise,
            ClarityProcessingMode.Noise => ClarityProcessingMode.Harmonic,
            ClarityProcessingMode.Harmonic => ClarityProcessingMode.Full,
            _ => ClarityProcessingMode.None
        };
        _parameterCallback(VocalSpectrographPlugin.ClarityModeIndex, (float)next);
        _presetHelper.MarkAsCustom();
    }

    private void CyclePitchAlgorithm()
    {
        PitchDetectorType next = _plugin.PitchAlgorithm switch
        {
            PitchDetectorType.Yin => PitchDetectorType.Pyin,
            PitchDetectorType.Pyin => PitchDetectorType.Autocorrelation,
            PitchDetectorType.Autocorrelation => PitchDetectorType.Cepstral,
            PitchDetectorType.Cepstral => PitchDetectorType.Swipe,
            _ => PitchDetectorType.Yin
        };
        _parameterCallback(VocalSpectrographPlugin.PitchAlgorithmIndex, (float)next);
        _presetHelper.MarkAsCustom();
    }

    private void CycleAxisMode()
    {
        SpectrogramAxisMode next = _plugin.AxisMode switch
        {
            SpectrogramAxisMode.Hz => SpectrogramAxisMode.Note,
            SpectrogramAxisMode.Note => SpectrogramAxisMode.Both,
            _ => SpectrogramAxisMode.Hz
        };
        _parameterCallback(VocalSpectrographPlugin.AxisModeIndex, (float)next);
        _presetHelper.MarkAsCustom();
    }

    private void CycleVoiceRange()
    {
        VocalRangeType next = _plugin.VoiceRange switch
        {
            VocalRangeType.Bass => VocalRangeType.Baritone,
            VocalRangeType.Baritone => VocalRangeType.Tenor,
            VocalRangeType.Tenor => VocalRangeType.Alto,
            VocalRangeType.Alto => VocalRangeType.MezzoSoprano,
            VocalRangeType.MezzoSoprano => VocalRangeType.Soprano,
            _ => VocalRangeType.Bass
        };
        _parameterCallback(VocalSpectrographPlugin.VoiceRangeIndex, (float)next);
        _presetHelper.MarkAsCustom();
    }

    private void CycleSmoothingMode()
    {
        SpectrogramSmoothingMode next = _plugin.SmoothingMode switch
        {
            SpectrogramSmoothingMode.Off => SpectrogramSmoothingMode.Ema,
            SpectrogramSmoothingMode.Ema => SpectrogramSmoothingMode.Bilateral,
            _ => SpectrogramSmoothingMode.Off
        };
        _parameterCallback(VocalSpectrographPlugin.SmoothingModeIndex, (float)next);
        _presetHelper.MarkAsCustom();
    }

    private void CycleNormalizationMode()
    {
        SpectrogramNormalizationMode next = _plugin.NormalizationMode switch
        {
            SpectrogramNormalizationMode.None => SpectrogramNormalizationMode.Peak,
            SpectrogramNormalizationMode.Peak => SpectrogramNormalizationMode.Rms,
            SpectrogramNormalizationMode.Rms => SpectrogramNormalizationMode.AWeighted,
            _ => SpectrogramNormalizationMode.None
        };
        _parameterCallback(VocalSpectrographPlugin.NormalizationModeIndex, (float)next);
        _presetHelper.MarkAsCustom();
    }

    private void CycleDynamicRangeMode()
    {
        SpectrogramDynamicRangeMode next = _plugin.DynamicRangeMode switch
        {
            SpectrogramDynamicRangeMode.Custom => SpectrogramDynamicRangeMode.VoiceOptimized,
            SpectrogramDynamicRangeMode.VoiceOptimized => SpectrogramDynamicRangeMode.Full,
            SpectrogramDynamicRangeMode.Full => SpectrogramDynamicRangeMode.Compressed,
            SpectrogramDynamicRangeMode.Compressed => SpectrogramDynamicRangeMode.NoiseFloor,
            _ => SpectrogramDynamicRangeMode.Custom
        };
        _parameterCallback(VocalSpectrographPlugin.DynamicRangeModeIndex, (float)next);
        _presetHelper.MarkAsCustom();
    }

    private void TogglePause()
    {
        _isPaused = !_isPaused;
        if (!_isPaused)
        {
            _plugin.SetVisualizationActive(true);
            ClearLocalBuffers();
        }
        else
        {
            _plugin.SetVisualizationActive(false);
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
        Array.Clear(_waveformMin, 0, _waveformMin.Length);
        Array.Clear(_waveformMax, 0, _waveformMax.Length);
        Array.Clear(_hnrTrack, 0, _hnrTrack.Length);
        Array.Clear(_cppTrack, 0, _cppTrack.Length);
        Array.Clear(_spectralCentroid, 0, _spectralCentroid.Length);
        Array.Clear(_spectralSlope, 0, _spectralSlope.Length);
        Array.Clear(_spectralFlux, 0, _spectralFlux.Length);
        _lastDataVersion = -1;
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
        float minHz = _plugin.MinFrequency;
        float maxHz = _plugin.MaxFrequency;
        float scaledMin = FrequencyScaleUtils.ToScale(_plugin.Scale, minHz);
        float scaledMax = FrequencyScaleUtils.ToScale(_plugin.Scale, maxHz);
        float range = scaledMax - scaledMin;
        if (MathF.Abs(range) < 1e-6f)
        {
            return minHz;
        }

        float scaled = scaledMin + range * norm;
        return FrequencyScaleUtils.FromScale(_plugin.Scale, scaled);
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
        foreach (var (name, value) in parameters)
        {
            int paramIndex = name switch
            {
                "FFT Size" => VocalSpectrographPlugin.FftSizeIndex,
                "Window" => VocalSpectrographPlugin.WindowFunctionIndex,
                "Overlap" => VocalSpectrographPlugin.OverlapIndex,
                "Scale" => VocalSpectrographPlugin.ScaleIndex,
                "Min Freq" => VocalSpectrographPlugin.MinFrequencyIndex,
                "Max Freq" => VocalSpectrographPlugin.MaxFrequencyIndex,
                "Min dB" => VocalSpectrographPlugin.MinDbIndex,
                "Max dB" => VocalSpectrographPlugin.MaxDbIndex,
                "Time Window" => VocalSpectrographPlugin.TimeWindowIndex,
                "Color Map" => VocalSpectrographPlugin.ColorMapIndex,
                "Pitch Overlay" => VocalSpectrographPlugin.ShowPitchIndex,
                "Formants" => VocalSpectrographPlugin.ShowFormantsIndex,
                "Harmonics" => VocalSpectrographPlugin.ShowHarmonicsIndex,
                "Voicing" => VocalSpectrographPlugin.ShowVoicingIndex,
                "Pre-Emphasis" => VocalSpectrographPlugin.PreEmphasisIndex,
                "HPF Enabled" => VocalSpectrographPlugin.HighPassEnabledIndex,
                "HPF Cutoff" => VocalSpectrographPlugin.HighPassCutoffIndex,
                "LPC Order" => VocalSpectrographPlugin.LpcOrderIndex,
                "Reassign" => VocalSpectrographPlugin.ReassignModeIndex,
                "Reassign Threshold" => VocalSpectrographPlugin.ReassignThresholdIndex,
                "Reassign Spread" => VocalSpectrographPlugin.ReassignSpreadIndex,
                "Clarity Mode" => VocalSpectrographPlugin.ClarityModeIndex,
                "Clarity Noise" => VocalSpectrographPlugin.ClarityNoiseIndex,
                "Clarity Harmonic" => VocalSpectrographPlugin.ClarityHarmonicIndex,
                "Clarity Smoothing" => VocalSpectrographPlugin.ClaritySmoothingIndex,
                "Pitch Algorithm" => VocalSpectrographPlugin.PitchAlgorithmIndex,
                "Axis Mode" => VocalSpectrographPlugin.AxisModeIndex,
                "Voice Range" => VocalSpectrographPlugin.VoiceRangeIndex,
                "Range Overlay" => VocalSpectrographPlugin.ShowRangeIndex,
                "Guides" => VocalSpectrographPlugin.ShowGuidesIndex,
                "Waveform View" => VocalSpectrographPlugin.ShowWaveformIndex,
                "Spectrum View" => VocalSpectrographPlugin.ShowSpectrumIndex,
                "Pitch Meter" => VocalSpectrographPlugin.ShowPitchMeterIndex,
                "Vowel View" => VocalSpectrographPlugin.ShowVowelSpaceIndex,
                "Smoothing Mode" => VocalSpectrographPlugin.SmoothingModeIndex,
                "Brightness" => VocalSpectrographPlugin.BrightnessIndex,
                "Gamma" => VocalSpectrographPlugin.GammaIndex,
                "Contrast" => VocalSpectrographPlugin.ContrastIndex,
                "Color Levels" => VocalSpectrographPlugin.ColorLevelsIndex,
                "Normalization" => VocalSpectrographPlugin.NormalizationModeIndex,
                "Dynamic Range" => VocalSpectrographPlugin.DynamicRangeModeIndex,
                _ => -1
            };

            if (paramIndex >= 0)
            {
                _parameterCallback(paramIndex, value);
            }
        }
    }

    private Dictionary<string, float> GetCurrentParameters()
    {
        return new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            ["FFT Size"] = _plugin.FftSize,
            ["Window"] = (float)_plugin.WindowFunction,
            ["Overlap"] = _plugin.Overlap,
            ["Scale"] = (float)_plugin.Scale,
            ["Min Freq"] = _plugin.MinFrequency,
            ["Max Freq"] = _plugin.MaxFrequency,
            ["Min dB"] = _plugin.MinDb,
            ["Max dB"] = _plugin.MaxDb,
            ["Time Window"] = _plugin.TimeWindowSeconds,
            ["Color Map"] = _plugin.ColorMap,
            ["Pitch Overlay"] = _plugin.ShowPitch ? 1f : 0f,
            ["Formants"] = _plugin.ShowFormants ? 1f : 0f,
            ["Harmonics"] = _plugin.ShowHarmonics ? 1f : 0f,
            ["Voicing"] = _plugin.ShowVoicing ? 1f : 0f,
            ["Pre-Emphasis"] = _plugin.PreEmphasisEnabled ? 1f : 0f,
            ["HPF Enabled"] = _plugin.HighPassEnabled ? 1f : 0f,
            ["HPF Cutoff"] = _plugin.HighPassCutoff,
            ["LPC Order"] = _plugin.LpcOrder,
            ["Reassign"] = (float)_plugin.ReassignMode,
            ["Reassign Threshold"] = _plugin.ReassignThresholdDb,
            ["Reassign Spread"] = _plugin.ReassignSpread,
            ["Clarity Mode"] = (float)_plugin.ClarityMode,
            ["Clarity Noise"] = _plugin.ClarityNoise,
            ["Clarity Harmonic"] = _plugin.ClarityHarmonic,
            ["Clarity Smoothing"] = _plugin.ClaritySmoothing,
            ["Pitch Algorithm"] = (float)_plugin.PitchAlgorithm,
            ["Axis Mode"] = (float)_plugin.AxisMode,
            ["Voice Range"] = (float)_plugin.VoiceRange,
            ["Range Overlay"] = _plugin.ShowRange ? 1f : 0f,
            ["Guides"] = _plugin.ShowGuides ? 1f : 0f,
            ["Waveform View"] = _plugin.ShowWaveform ? 1f : 0f,
            ["Spectrum View"] = _plugin.ShowSpectrum ? 1f : 0f,
            ["Pitch Meter"] = _plugin.ShowPitchMeter ? 1f : 0f,
            ["Vowel View"] = _plugin.ShowVowelSpace ? 1f : 0f,
            ["Smoothing Mode"] = (float)_plugin.SmoothingMode,
            ["Brightness"] = _plugin.Brightness,
            ["Gamma"] = _plugin.Gamma,
            ["Contrast"] = _plugin.Contrast,
            ["Color Levels"] = _plugin.ColorLevels,
            ["Normalization"] = (float)_plugin.NormalizationMode,
            ["Dynamic Range"] = (float)_plugin.DynamicRangeMode
        };
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
