using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HotMic.Core.Dsp;
using HotMic.Core.Dsp.Mapping;
using HotMic.Core.Dsp.Spectrogram;
using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Renderer for the Analyzer window.
/// </summary>
public sealed class AnalyzerRenderer : IDisposable
{
    // Layout constants - reorganized UI (1440x960)
    private const float CornerRadius = 10f;
    private const float Padding = 16f;
    private const float TitleBarHeight = 40f;
    private const float KnobRadius = 20f;
    private const float AxisWidth = 50f;
    private const float ColorBarWidth = 22f;
    private const float TimeAxisHeight = 22f;
    private const float VoicingLaneHeight = 8f;
    private static readonly float[] PitchLowDash = [6f, 4f];
    private static readonly float[] DiscontinuityDash = [4f, 4f];
    private static readonly float[] SpectrumFillStops = [0f, 1f];

    // New layout structure
    private const float SidebarWidth = 220f;
    private const float ControlPanelHeight = 240f;
    private const float PanelHeaderHeight = 24f;
    private const float PanelSpacing = 10f;
    private const float PanelCornerRadius = 8f;

    // Panel widths (5 panels + Speech panel)
    private const float FrequencyPanelWidth = 240f;
    private const float AnalysisPanelWidth = 300f;
    private const float ClarityPanelWidth = 220f;
    private const float DisplayPanelWidth = 300f;
    // ViewOptionsPanelWidth is calculated as remaining space

    private readonly PluginComponentTheme _theme;
    private readonly SKColor[] _spectrumFillColors;
    private readonly PluginPresetBar _presetBar;
    private readonly TooltipManager _tooltip;
    private readonly SkiaTextPaint _panelHeaderPaint;

    // Tooltip content for all controls
    private static readonly Dictionary<string, (string Title, string Description)> TooltipData = new()
    {
        // Frequency Range Panel
        ["MinFreq"] = ("Min Frequency", "Lower frequency bound (20-2000 Hz). Voice fundamentals: 80-300 Hz"),
        ["MaxFreq"] = ("Max Frequency", "Upper frequency bound (2000-12000 Hz). Harmonics extend to 8-12 kHz"),
        ["HPF"] = ("High-Pass Filter", "Removes low-frequency rumble below the cutoff frequency"),
        ["HpfCutoff"] = ("HPF Cutoff", "High-pass filter cutoff frequency (20-120 Hz)"),
        ["Time"] = ("Time Window", "Visible time span (1-60 seconds)"),
        ["Scale"] = ("Frequency Scale", "Lin=linear, Log=logarithmic, Mel/Erb/Bark=perceptual scales"),
        ["Axis"] = ("Axis Labels", "Display Hz, musical notes, or both on the frequency axis"),

        // Analysis Engine Panel
        ["Transform"] = ("Transform Type", "FFT=standard, ZoomFFT=high resolution, CQT=logarithmic bins"),
        ["FftSize"] = ("FFT Size", "Larger = better frequency resolution but more latency"),
        ["Window"] = ("Window Function", "Hann=general purpose, BlackmanHarris=low sidelobes"),
        ["Overlap"] = ("Overlap", "Higher overlap = smoother time resolution, more CPU usage"),
        ["Reassign"] = ("Reassignment", "Sharpens blurry spectral energy by correcting bin positions"),
        ["ReassignThresh"] = ("Reassign Threshold", "Minimum level (dB) for reassignment processing"),
        ["ReassignSpread"] = ("Reassign Spread", "How far energy can be redistributed (0-100%)"),
        ["PreEmphasis"] = ("Pre-Emphasis", "Boosts high frequencies to compensate for voice rolloff"),
        ["Smoothing"] = ("Smoothing Mode", "EMA=temporal smoothing, Bilateral=edge-preserving"),

        // Clarity Processing Panel
        ["Clarity"] = ("Clarity Mode", "None=off, Noise=suppress broadband, Harmonic=enhance pitch"),
        ["ClarityNoise"] = ("Noise Reduction", "Amount of broadband noise floor suppression (0-100%)"),
        ["ClarityHarmonic"] = ("Harmonic Boost", "Emphasis on pitched/harmonic content (0-100%)"),
        ["ClaritySmooth"] = ("Clarity Smoothing", "Temporal smoothing of clarity processing (0-100%)"),
        ["Normalization"] = ("Normalization", "None/Peak/RMS/A-weighted level normalization"),
        ["DynamicRange"] = ("Dynamic Range", "Preset dB ranges for different use cases"),

        // Display Tuning Panel
        ["MinDb"] = ("Min dB", "Floor level for color mapping. Increase to hide noise"),
        ["MaxDb"] = ("Max dB", "Ceiling level. Decrease to see quiet signal details"),
        ["Brightness"] = ("Brightness", "Overall display intensity multiplier (0.5-2x)"),
        ["Gamma"] = ("Gamma", "Nonlinear mapping. Lower values reveal quiet details"),
        ["Contrast"] = ("Contrast", "Difference between quiet and loud regions (0.8-1.5x)"),
        ["Levels"] = ("Color Levels", "Quantization steps (16-64). More = smoother gradients"),
        ["ColorMap"] = ("Color Map", "Color palette for magnitude visualization"),

        // View Options Panel - Overlays
        ["PitchOverlay"] = ("Pitch Track", "Yellow line showing detected fundamental frequency (F0). Pitch/voicing work is skipped when Pitch/Meter/Harmonics/Voicing/Clarity are all off."),
        ["HarmonicOverlay"] = ("Harmonics", "Dots marking detected harmonic peaks"),
        ["HarmonicMode"] = ("Harmonic Mode", "D=Detected only, T=Theoretical positions, B=Both"),
        ["VoicingOverlay"] = ("Voicing", "Lane showing voiced/unvoiced/silence segments"),
        ["RangeOverlay"] = ("Vocal Range", "Band showing the selected voice range boundaries"),
        ["GuidesOverlay"] = ("Frequency Guides", "Reference lines at semitone intervals"),

        // View Options Panel - Views
        ["SpectrumView"] = ("Spectrum Slice", "Real-time frequency magnitude at current position"),
        ["PitchMeterView"] = ("Pitch Meter", "Current detected pitch with note name indicator"),

        // Other buttons
        ["VoiceRange"] = ("Voice Range", "Select Bass/Baritone/Tenor/Alto/MezzoSoprano/Soprano"),
        ["PitchAlgo"] = ("Pitch Algorithm", "YIN/PYIN/Autocorr/Cepstral are time-domain; SWIPE uses FFT. With CQT, SWIPE is forced to YIN. Pitch/voicing work runs only when Pitch/Meter/Harmonics/Voicing/Clarity are enabled."),
        ["Pause"] = ("Pause/Run", "Pause or resume the spectrograph visualization"),
    };

    // KnobWidgets for all 15 knobs
    public KnobWidget MinFreqKnob { get; }
    public KnobWidget MaxFreqKnob { get; }
    public KnobWidget MinDbKnob { get; }
    public KnobWidget MaxDbKnob { get; }
    public KnobWidget TimeKnob { get; }
    public KnobWidget HpfKnob { get; }
    public KnobWidget ReassignThresholdKnob { get; }
    public KnobWidget ReassignSpreadKnob { get; }
    public KnobWidget ClarityNoiseKnob { get; }
    public KnobWidget ClarityHarmonicKnob { get; }
    public KnobWidget ClaritySmoothingKnob { get; }
    public KnobWidget BrightnessKnob { get; }
    public KnobWidget GammaKnob { get; }
    public KnobWidget ContrastKnob { get; }
    public KnobWidget LevelsKnob { get; }

    /// <summary>All knob widgets for iteration.</summary>
    public IReadOnlyList<KnobWidget> AllKnobs => _allKnobs;

    /// <summary>Tooltip manager for hover text.</summary>
    public TooltipManager Tooltip => _tooltip;

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _panelPaint;
    private readonly SKPaint _borderPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _textPaint;
    private readonly SkiaTextPaint _mutedTextPaint;
    private readonly SkiaTextPaint _dropAlertPaint;
    private readonly SkiaTextPaint _profilingTextPaint;
    private readonly SKPaint _profilingDotPaint;
    private readonly SKPaint _profilingPanelPaint;
    private readonly SKPaint _gridPaint;
    private readonly SKPaint _buttonPaint;
    private readonly SKPaint _buttonActivePaint;
    private readonly SkiaTextPaint _buttonTextPaint;
    private readonly SKPaint _referencePaint;
    private readonly SKPaint _bitmapPaint;
    private readonly SKPaint _pitchPaint;
    private readonly SKPaint _pitchShadowPaint;
    private readonly SKPaint _pitchLowPaint;
    private readonly SKPaint _overlayShadowPaint;
    private readonly SKPaint _harmonicPaint;
    private readonly SKPaint _harmonicDetectedPaint;
    private readonly SKPaint _harmonicTheoreticalPaint;
    private readonly SKPaint _voicedPaint;
    private readonly SKPaint _unvoicedPaint;
    private readonly SKPaint _silencePaint;
    private readonly SKPaint _spectrumPaint;
    private readonly SKPaint _spectrumFillPaint;
    private readonly SKPaint _spectrumPeakPaint;
    private readonly SKPaint _rangeBandPaint;
    private readonly SKPaint _guidePaint;
    private readonly SKPaint _discontinuityPaint;
    private readonly SkiaTextPaint _metricPaint;
    private readonly SkiaTextPaint _iconPaint;
    private readonly SKPaint _axisImagePaint;
    private readonly SKSamplingOptions _axisSampling;
    private readonly SKPaint _colorBarPaint;
    private SKShader? _colorBarShader;
    private SKRect _colorBarRectCache;
    private int _colorBarMapCache = -1;

    private KnobWidget[] _allKnobs = null!; // Initialized in constructor

    private readonly SKPath _spectrumSlicePath = new();
    private readonly SKPath _spectrumFillPath = new();
    private readonly SKPath _pitchHighPath = new();
    private readonly SKPath _pitchLowPath = new();
    private readonly int[] _peakBins = new int[3];
    private readonly float[] _peakValues = new float[3];

    // Spectrum slice ballistics (attack/release smoothing for professional analyzer feel)
    private float[] _spectrumBallistics = Array.Empty<float>();
    private long _lastBallisticsTicks;
    private const float BallisticsAttackMs = 5f;      // Instant attack (5ms)
    private const float BallisticsReleaseMs = 300f;   // Slow release (300ms)

    private long _lastSpectrogramTicks;
    private long _lastBitmapTicks;
    private long _lastOverlayTicks;
    private static readonly double TicksToMicroseconds = 1_000_000.0 / Stopwatch.Frequency;

    private SKRect _titleBarRect;
    private SKRect _closeRect;
    private SKRect _bypassRect;
    private SKRect _fftRect;
    private SKRect _transformRect;
    private SKRect _windowRect;
    private SKRect _overlapRect;
    private SKRect _scaleRect;
    private SKRect _colorRect;
    private SKRect _reassignRect;
    private SKRect _clarityRect;
    private SKRect _pitchAlgoRect;
    private SKRect _axisModeRect;
    private SKRect _smoothingModeRect;
    private SKRect _pauseRect;
    private SKRect _pitchToggleRect;
    private SKRect _harmonicToggleRect;
    private SKRect _harmonicModeRect;
    private SKRect _voicingToggleRect;
    private SKRect _preEmphasisToggleRect;
    private SKRect _hpfToggleRect;
    private SKRect _rangeToggleRect;
    private SKRect _guidesToggleRect;
    private SKRect _voiceRangeRect;
    private SKRect _normalizationRect;
    private SKRect _dynamicRangeRect;
    private SKRect _spectrumToggleRect;
    private SKRect _pitchMeterToggleRect;
    private SKRect _spectrogramRect;

    // New layout rects for reorganized UI
    private SKRect _sidebarRect;
    private SKRect _frequencyPanelRect;
    private SKRect _analysisPanelRect;
    private SKRect _clarityPanelRect;
    private SKRect _displayPanelRect;
    private SKRect _viewOptionsPanelRect;

    private SKBitmap? _spectrogramBitmap;
    private int _bitmapWidth;
    private int _bitmapHeight;
    private int _lastDataVersion = -1;
    private int _lastColorMap = -1;
    private float _lastBrightness = -1f;
    private float _lastGamma = -1f;
    private float _lastContrast = -1f;
    private int _lastColorLevels = -1;
    private long _lastLatestFrameId = -1;
    private int _lastAvailableFrames = -1;
    private int _lastFrameCount = -1;
    private int _lastDisplayBins = -1;
    private int _frameCapacity;
    private int _framePadFrames;
    private int _frameAvailable;
    private int _frameRingStart;
    private int[] _pixelBuffer = Array.Empty<int>();
    // Ring offset for leftmost column to avoid per-frame buffer shifts.
    private int _bitmapRingStart;
    private int[] _rowOffsets = Array.Empty<int>();
    private int[] _colorLut = Array.Empty<int>();
    private byte[] _colorIndexLut = Array.Empty<byte>();

    private SKSurface? _axisSurface;
    private SKImage? _axisImage;
    private SKImageInfo _axisInfo;
    private SKRect _axisDrawRect;
    private float _axisDpiScale = -1f;
    private SpectrogramAxisMode _axisModeCache;
    private FrequencyScale _axisScaleCache;
    private float _axisMinHzCache;
    private float _axisMaxHzCache;
    private float _axisTimeWindowCache;
    private int _axisColorMapCache = -1;
    private bool _axisShowSpectrumCache;
    private bool _axisShowPitchMeterCache;

    // Bloom pass for luminous highlights.
    private readonly SKPaint _bloomAddPaint;
    private const float BloomSigma = 12f;
    private const float BloomIntensity = 0.35f;

    public AnalyzerRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _spectrumFillColors =
        [
            _theme.AccentSecondary.WithAlpha(80),
            _theme.AccentSecondary.WithAlpha(10)
        ];
        _presetBar = new PluginPresetBar(_theme);
        _tooltip = new TooltipManager(_theme);
        _panelHeaderPaint = new SkiaTextPaint(_theme.TextMuted, 11f, SKFontStyle.Bold);

        // Initialize all KnobWidgets with default values for double-click reset
        var knobStyle = KnobStyle.Compact with { ShowLabels = true };
        MinFreqKnob = new KnobWidget(KnobRadius, 20f, 2000f, "MIN FREQ", "Hz", knobStyle, _theme) { IsLogarithmic = true, ValueFormat = "0", DefaultValue = 80f };
        MaxFreqKnob = new KnobWidget(KnobRadius, 2000f, 12000f, "MAX FREQ", "Hz", knobStyle, _theme) { IsLogarithmic = true, ValueFormat = "0", DefaultValue = 8000f };
        MinDbKnob = new KnobWidget(KnobRadius, -120f, -20f, "MIN dB", "dB", knobStyle, _theme) { ValueFormat = "0", DefaultValue = -80f };
        MaxDbKnob = new KnobWidget(KnobRadius, -40f, 0f, "MAX dB", "dB", knobStyle, _theme) { ValueFormat = "0", DefaultValue = 0f };
        TimeKnob = new KnobWidget(KnobRadius, 1f, 60f, "TIME", "s", knobStyle, _theme) { ValueFormat = "0.0", DefaultValue = 5f };
        HpfKnob = new KnobWidget(KnobRadius, 20f, 120f, "HPF", "Hz", knobStyle, _theme) { ValueFormat = "0", DefaultValue = 60f };
        ReassignThresholdKnob = new KnobWidget(KnobRadius, -120f, -20f, "R THRESH", "dB", knobStyle, _theme) { ValueFormat = "0", DefaultValue = -60f };
        ReassignSpreadKnob = new KnobWidget(KnobRadius, 0f, 100f, "R SPREAD", "%", knobStyle, _theme) { ValueFormat = "0", DefaultValue = 50f };
        ClarityNoiseKnob = new KnobWidget(KnobRadius, 0f, 100f, "NOISE", "%", knobStyle, _theme) { ValueFormat = "0", DefaultValue = 50f };
        ClarityHarmonicKnob = new KnobWidget(KnobRadius, 0f, 100f, "HARM", "%", knobStyle, _theme) { ValueFormat = "0", DefaultValue = 50f };
        ClaritySmoothingKnob = new KnobWidget(KnobRadius, 0f, 100f, "SMOOTH", "%", knobStyle, _theme) { ValueFormat = "0", DefaultValue = 50f };
        BrightnessKnob = new KnobWidget(KnobRadius, 0.5f, 2f, "BRIGHT", "x", knobStyle, _theme) { ValueFormat = "0.00", DefaultValue = 1f };
        GammaKnob = new KnobWidget(KnobRadius, 0.6f, 1.2f, "GAMMA", "", knobStyle, _theme) { ValueFormat = "0.00", DefaultValue = 0.85f };
        ContrastKnob = new KnobWidget(KnobRadius, 0.8f, 1.5f, "CONTRAST", "x", knobStyle, _theme) { ValueFormat = "0.00", DefaultValue = 1f };
        LevelsKnob = new KnobWidget(KnobRadius, 16f, 64f, "LEVELS", "", knobStyle, _theme) { ValueFormat = "0", DefaultValue = 64f };
        _allKnobs = new[] { MinFreqKnob, MaxFreqKnob, MinDbKnob, MaxDbKnob, TimeKnob, HpfKnob,
            ReassignThresholdKnob, ReassignSpreadKnob, ClarityNoiseKnob, ClarityHarmonicKnob,
            ClaritySmoothingKnob, BrightnessKnob, GammaKnob, ContrastKnob, LevelsKnob };

        _backgroundPaint = new SKPaint { Color = _theme.PanelBackground, IsAntialias = true, Style = SKPaintStyle.Fill };
        _panelPaint = new SKPaint { Color = _theme.PanelBackgroundLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        _borderPaint = new SKPaint { Color = _theme.PanelBorder, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        _titlePaint = new SkiaTextPaint(_theme.TextPrimary, 14f, SKFontStyle.Bold);
        _textPaint = new SkiaTextPaint(_theme.TextSecondary, 11f, SKFontStyle.Normal);
        _mutedTextPaint = new SkiaTextPaint(_theme.TextMuted, 10f, SKFontStyle.Normal);
        _dropAlertPaint = new SkiaTextPaint(_theme.ThresholdLine, 10f, SKFontStyle.Bold);
        _profilingTextPaint = new SkiaTextPaint(_theme.ThresholdLine, 10f, SKFontStyle.Bold);
        _profilingDotPaint = new SKPaint { Color = _theme.ThresholdLine, IsAntialias = true, Style = SKPaintStyle.Fill };
        _profilingPanelPaint = new SKPaint { Color = _theme.PanelBackground.WithAlpha(200), IsAntialias = true, Style = SKPaintStyle.Fill };
        _gridPaint = new SKPaint
        {
            Color = _theme.PanelBorder.WithAlpha(50),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };
        _buttonPaint = new SKPaint { Color = _theme.PanelBackgroundLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        _buttonActivePaint = new SKPaint { Color = _theme.KnobArc.WithAlpha(120), IsAntialias = true, Style = SKPaintStyle.Fill };
        _buttonTextPaint = new SkiaTextPaint(_theme.TextPrimary, 10f, SKFontStyle.Bold, SKTextAlign.Center);
        _referencePaint = new SKPaint
        {
            Color = _theme.TextPrimary.WithAlpha(180),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f
        };
        _bitmapPaint = new SKPaint
        {
            IsAntialias = true
        };

        // Bloom: blur the image and add it back with additive blend for glow on bright regions
        _bloomAddPaint = new SKPaint
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
            ImageFilter = SKImageFilter.CreateBlur(BloomSigma, BloomSigma),
            ColorFilter = SKColorFilter.CreateBlendMode(
                new SKColor(255, 255, 255, (byte)(255 * BloomIntensity)),
                SKBlendMode.Modulate)
        };

        _pitchPaint = new SKPaint { Color = new SKColor(0x00, 0xFF, 0x88), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        _pitchShadowPaint = new SKPaint
        {
            Color = new SKColor(0x00, 0x00, 0x00, 0x60),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 4f,
            ImageFilter = SKImageFilter.CreateBlur(2f, 2f)
        };
        _pitchLowPaint = new SKPaint
        {
            Color = new SKColor(0x00, 0xFF, 0x88, 0x80),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            PathEffect = SKPathEffect.CreateDash(PitchLowDash, 0)
        };
        _overlayShadowPaint = new SKPaint
        {
            Color = new SKColor(0x00, 0x00, 0x00, 0x50),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3f,
            ImageFilter = SKImageFilter.CreateBlur(1.5f, 1.5f)
        };
        _harmonicPaint = new SKPaint { Color = new SKColor(0xE0, 0xE0, 0xE6, 0x90), IsAntialias = true, Style = SKPaintStyle.Fill };
        _harmonicDetectedPaint = new SKPaint { Color = new SKColor(0x00, 0xD4, 0xAA, 0xD0), IsAntialias = true, Style = SKPaintStyle.Fill };
        _harmonicTheoreticalPaint = new SKPaint { Color = new SKColor(0x80, 0x80, 0x90, 0x40), IsAntialias = true, Style = SKPaintStyle.Fill };
        _voicedPaint = new SKPaint { Color = new SKColor(0x00, 0xD4, 0xAA, 0x50), Style = SKPaintStyle.Fill };
        _unvoicedPaint = new SKPaint { Color = new SKColor(0x80, 0x80, 0x90, 0x40), Style = SKPaintStyle.Fill };
        _silencePaint = new SKPaint { Color = new SKColor(0x00, 0x00, 0x00, 0x60), Style = SKPaintStyle.Fill };
        _spectrumPaint = new SKPaint
        {
            Color = _theme.AccentSecondary,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f
        };
        _spectrumFillPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill
            // Shader set dynamically per-frame based on rect bounds
        };
        _spectrumPeakPaint = new SKPaint
        {
            Color = _theme.TextPrimary,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        _rangeBandPaint = new SKPaint
        {
            Color = _theme.AccentSecondary.WithAlpha(40),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        _guidePaint = new SKPaint
        {
            Color = _theme.PanelBorder.WithAlpha(80),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };
        _discontinuityPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0xAA, 0x00, 0xA0), // Amber/orange for visibility
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            PathEffect = SKPathEffect.CreateDash(DiscontinuityDash, 0f)
        };
        _metricPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Normal);
        _iconPaint = new SkiaTextPaint(_theme.TextPrimary, 12f, SKFontStyle.Normal, SKTextAlign.Center);
        _axisSampling = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
        _axisImagePaint = new SKPaint { IsAntialias = false };
        _colorBarPaint = new SKPaint
        {
            IsAntialias = true
        };
        _colorBarRectCache = SKRect.Empty;
    }

    public static SKSize GetPreferredSize() => new(1440, 960);

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, SpectroState state)
    {
        long renderStartTicks = Stopwatch.GetTimestamp();

        canvas.Clear(_theme.PanelBackground);
        canvas.Save();
        canvas.Scale(dpiScale);
        size = new SKSize(size.Width / dpiScale, size.Height / dpiScale);
        UpdateFrameMapping(state);

        var outerRect = new SKRect(0, 0, size.Width, size.Height);
        canvas.DrawRoundRect(new SKRoundRect(outerRect, CornerRadius), _backgroundPaint);
        canvas.DrawRoundRect(new SKRoundRect(outerRect, CornerRadius), _borderPaint);

        // New reorganized layout
        DrawTitleBar(canvas, size, state);
        DrawMainContent(canvas, size, dpiScale, state);
        DrawControlPanels(canvas, size, state);
        _tooltip.Render(canvas, size);

        long renderTicks = Stopwatch.GetTimestamp() - renderStartTicks;
        if (state.IsProfiling)
        {
            DrawPerformanceOverlay(canvas, renderTicks, state);
        }

        canvas.Restore();
    }

    private void DrawMainContent(SKCanvas canvas, SKSize size, float dpiScale, SpectroState state)
    {
        bool profiling = state.IsProfiling;
        long spectrogramStartTicks = profiling ? Stopwatch.GetTimestamp() : 0;

        // Calculate sidebar rect (right side)
        bool showSidebar = state.ShowSpectrum || state.ShowPitchMeter;
        float mainRight;

        if (showSidebar)
        {
            float sidebarRight = size.Width - Padding;
            _sidebarRect = new SKRect(
                sidebarRight - SidebarWidth,
                TitleBarHeight + Padding,
                sidebarRight,
                size.Height - Padding - ControlPanelHeight - PanelSpacing);
            DrawSidebar(canvas, _sidebarRect, state);
            mainRight = _sidebarRect.Left - PanelSpacing;
        }
        else
        {
            mainRight = size.Width - Padding;
        }

        // Calculate spectrogram area
        float top = TitleBarHeight + Padding;
        float bottom = size.Height - Padding - ControlPanelHeight - PanelSpacing;

        var spectrumRect = new SKRect(Padding + AxisWidth, top, mainRight - ColorBarWidth, bottom);
        var axisRect = new SKRect(Padding, top, Padding + AxisWidth, bottom);
        var colorRect = new SKRect(spectrumRect.Right + 6f, top, spectrumRect.Right + 6f + ColorBarWidth, bottom);
        _spectrogramRect = spectrumRect;

        canvas.DrawRoundRect(new SKRoundRect(spectrumRect, 6f), _panelPaint);

        if (profiling)
        {
            long bitmapStartTicks = Stopwatch.GetTimestamp();
            UpdateSpectrogramBitmap(state);
            _lastBitmapTicks = Stopwatch.GetTimestamp() - bitmapStartTicks;
        }
        else
        {
            UpdateSpectrogramBitmap(state);
        }
        DrawSpectrogramBitmap(canvas, spectrumRect);
        EnsureAxisLayer(size, dpiScale, state, spectrumRect, axisRect, colorRect);
        DrawAxisLayer(canvas);

        if (state.ShowRange)
        {
            DrawVocalRangeBand(canvas, spectrumRect, state);
        }
        if (state.ShowGuides)
        {
            DrawFrequencyGuides(canvas, spectrumRect, state);
        }
        if (profiling)
        {
            long overlayStartTicks = Stopwatch.GetTimestamp();
            DrawOverlays(canvas, spectrumRect, state);
            _lastOverlayTicks = Stopwatch.GetTimestamp() - overlayStartTicks;
        }
        else
        {
            DrawOverlays(canvas, spectrumRect, state);
        }
        DrawReferenceLine(canvas, spectrumRect, state);
        DrawDiscontinuityMarkers(canvas, spectrumRect, state);

        if (profiling)
        {
            _lastSpectrogramTicks = Stopwatch.GetTimestamp() - spectrogramStartTicks;
        }
        else if (_lastSpectrogramTicks != 0 || _lastBitmapTicks != 0 || _lastOverlayTicks != 0)
        {
            _lastSpectrogramTicks = 0;
            _lastBitmapTicks = 0;
            _lastOverlayTicks = 0;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DrawPerformanceOverlay(SKCanvas canvas, long renderTicks, SpectroState state)
    {
        var analysis = state.AnalysisTiming;
        var ui = state.UiTiming;

        int renderUs = ToMicroseconds(renderTicks);
        int spectrogramUs = ToMicroseconds(_lastSpectrogramTicks);
        int bitmapUs = ToMicroseconds(_lastBitmapTicks);
        int overlayUs = ToMicroseconds(_lastOverlayTicks);

        string header = "TIMINGS (us)";
        string line1 = $"ANL avg frame {analysis.FrameUs} pre {analysis.PreprocessUs} xform {analysis.TransformUs} norm {analysis.NormalizationUs}";
        string line2 = $"ANL avg pitch {analysis.PitchUs} clarity {analysis.ClarityUs} reas {analysis.ReassignUs} feat {analysis.FeaturesUs} wb {analysis.WritebackUs}";
        string line3 = $"UI tick {ui.TickUs} copy {ui.CopyUs} map {ui.MapUs}";
        string line4 = $"UI draw {renderUs} spec {spectrogramUs} bmp {bitmapUs} ovl {overlayUs}";

        float padding = 6f;
        float headerHeight = _profilingTextPaint.Font.Size + 4f;
        float lineHeight = _metricPaint.Font.Size + 4f;

        float width = _profilingTextPaint.MeasureText(header);
        width = MathF.Max(width, _metricPaint.MeasureText(line1));
        width = MathF.Max(width, _metricPaint.MeasureText(line2));
        width = MathF.Max(width, _metricPaint.MeasureText(line3));
        width = MathF.Max(width, _metricPaint.MeasureText(line4));

        float height = padding * 2f + headerHeight + lineHeight * 4f;
        float panelX = _spectrogramRect.Left + 8f;
        float panelY = _spectrogramRect.Top + 8f;
        var panelRect = new SKRect(panelX, panelY, panelX + width + padding * 2f, panelY + height);

        canvas.DrawRoundRect(new SKRoundRect(panelRect, 6f), _profilingPanelPaint);

        float textX = panelRect.Left + padding;
        float textY = panelRect.Top + padding + headerHeight - 4f;
        canvas.DrawText(header, textX, textY, _profilingTextPaint);

        textY += lineHeight;
        canvas.DrawText(line1, textX, textY, _metricPaint);
        textY += lineHeight;
        canvas.DrawText(line2, textX, textY, _metricPaint);
        textY += lineHeight;
        canvas.DrawText(line3, textX, textY, _metricPaint);
        textY += lineHeight;
        canvas.DrawText(line4, textX, textY, _metricPaint);
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

    private void DrawSidebar(SKCanvas canvas, SKRect rect, SpectroState state)
    {
        float y = rect.Top;
        float viewHeight = 140f;
        int frameIndex = GetSliceFrameIndex(state);

        if (state.ShowSpectrum)
        {
            var panel = new SKRect(rect.Left, y, rect.Right, y + viewHeight);
            DrawSpectrumSlice(canvas, panel, state, frameIndex);
            y = panel.Bottom + PanelSpacing;
        }

        if (state.ShowPitchMeter)
        {
            float remainingHeight = Math.Max(viewHeight, rect.Bottom - y);
            var panel = new SKRect(rect.Left, y, rect.Right, y + remainingHeight);
            DrawPitchMeter(canvas, panel, state, frameIndex);
        }
    }

    private void DrawControlPanels(SKCanvas canvas, SKSize size, SpectroState state)
    {
        float panelTop = size.Height - Padding - ControlPanelHeight;
        float x = Padding;
        float buttonHeight = 22f;
        float toggleHeight = 20f;
        float buttonSpacing = 4f;

        // Panel 1: Frequency Range
        _frequencyPanelRect = new SKRect(x, panelTop, x + FrequencyPanelWidth, size.Height - Padding);
        DrawPanelBackground(canvas, _frequencyPanelRect, "FREQUENCY");
        DrawFrequencyPanelContents(canvas, _frequencyPanelRect, state, buttonHeight, toggleHeight, buttonSpacing);
        x = _frequencyPanelRect.Right + PanelSpacing;

        // Panel 2: Analysis Engine
        _analysisPanelRect = new SKRect(x, panelTop, x + AnalysisPanelWidth, size.Height - Padding);
        DrawPanelBackground(canvas, _analysisPanelRect, "ANALYSIS");
        DrawAnalysisPanelContents(canvas, _analysisPanelRect, state, buttonHeight, toggleHeight, buttonSpacing);
        x = _analysisPanelRect.Right + PanelSpacing;

        // Panel 3: Clarity Processing
        _clarityPanelRect = new SKRect(x, panelTop, x + ClarityPanelWidth, size.Height - Padding);
        DrawPanelBackground(canvas, _clarityPanelRect, "CLARITY");
        DrawClarityPanelContents(canvas, _clarityPanelRect, state, buttonHeight, buttonSpacing);
        x = _clarityPanelRect.Right + PanelSpacing;

        // Panel 4: Display Tuning
        _displayPanelRect = new SKRect(x, panelTop, x + DisplayPanelWidth, size.Height - Padding);
        DrawPanelBackground(canvas, _displayPanelRect, "DISPLAY");
        DrawDisplayPanelContents(canvas, _displayPanelRect, state, buttonHeight, buttonSpacing);
        x = _displayPanelRect.Right + PanelSpacing;

        // Panel 5: View Options (remaining width)
        float viewPanelWidth = size.Width - Padding - x;
        _viewOptionsPanelRect = new SKRect(x, panelTop, size.Width - Padding, size.Height - Padding);
        DrawPanelBackground(canvas, _viewOptionsPanelRect, "VIEW");
        DrawViewOptionsPanelContents(canvas, _viewOptionsPanelRect, state, toggleHeight, buttonSpacing);
    }

    private void DrawPanelBackground(SKCanvas canvas, SKRect rect, string header)
    {
        canvas.DrawRoundRect(new SKRoundRect(rect, PanelCornerRadius), _panelPaint);
        canvas.DrawText(header, rect.Left + 10f, rect.Top + 16f, _panelHeaderPaint);
    }

    private void DrawFrequencyPanelContents(SKCanvas canvas, SKRect rect, SpectroState state,
        float buttonHeight, float toggleHeight, float buttonSpacing)
    {
        // Knobs: MinFreq, MaxFreq, HPF, Time (2x2 grid)
        float knobY1 = rect.Top + PanelHeaderHeight + 35f;
        float knobY2 = knobY1 + 70f;
        float knobSpacingX = rect.Width / 3f;

        MinFreqKnob.Value = state.MinFrequency;
        MinFreqKnob.Center = new SKPoint(rect.Left + knobSpacingX, knobY1);
        MinFreqKnob.Render(canvas);

        MaxFreqKnob.Value = state.MaxFrequency;
        MaxFreqKnob.Center = new SKPoint(rect.Left + knobSpacingX * 2, knobY1);
        MaxFreqKnob.Render(canvas);

        HpfKnob.Value = state.HighPassCutoff;
        HpfKnob.Center = new SKPoint(rect.Left + knobSpacingX, knobY2);
        HpfKnob.Render(canvas);

        TimeKnob.Value = state.TimeWindowSeconds;
        TimeKnob.Center = new SKPoint(rect.Left + knobSpacingX * 2, knobY2);
        TimeKnob.Render(canvas);

        // Buttons row
        float buttonY = rect.Bottom - 32f;
        float buttonWidth = 58f;
        float bx = rect.Left + 8f;

        _hpfToggleRect = new SKRect(bx, buttonY, bx + buttonWidth, buttonY + toggleHeight);
        DrawPillButton(canvas, _hpfToggleRect, "HPF", state.HighPassEnabled);
        bx = _hpfToggleRect.Right + buttonSpacing;

        _scaleRect = new SKRect(bx, buttonY, bx + buttonWidth, buttonY + buttonHeight);
        DrawPillButton(canvas, _scaleRect, state.Scale.ToString(), false);
        bx = _scaleRect.Right + buttonSpacing;

        _axisModeRect = new SKRect(bx, buttonY, bx + buttonWidth, buttonY + buttonHeight);
        DrawPillButton(canvas, _axisModeRect, FormatAxisLabel(state.AxisMode), false);
    }

    private void DrawAnalysisPanelContents(SKCanvas canvas, SKRect rect, SpectroState state,
        float buttonHeight, float toggleHeight, float buttonSpacing)
    {
        // Knobs: Reassign Threshold, Reassign Spread
        float knobY = rect.Top + PanelHeaderHeight + 35f;
        float knobSpacingX = rect.Width / 3f;

        ReassignThresholdKnob.Value = state.ReassignThresholdDb;
        ReassignThresholdKnob.Center = new SKPoint(rect.Left + knobSpacingX, knobY);
        ReassignThresholdKnob.Render(canvas);

        ReassignSpreadKnob.Value = state.ReassignSpread * 100f;
        ReassignSpreadKnob.Center = new SKPoint(rect.Left + knobSpacingX * 2, knobY);
        ReassignSpreadKnob.Render(canvas);

        // Buttons row 1
        float buttonY1 = rect.Top + PanelHeaderHeight + 75f;
        float buttonWidth = 54f;
        float bx = rect.Left + 6f;

        _transformRect = new SKRect(bx, buttonY1, bx + buttonWidth, buttonY1 + buttonHeight);
        DrawPillButton(canvas, _transformRect, FormatTransformLabel(state.TransformType),
            state.TransformType != SpectrogramTransformType.Fft);
        bx = _transformRect.Right + buttonSpacing;

        _fftRect = new SKRect(bx, buttonY1, bx + buttonWidth, buttonY1 + buttonHeight);
        DrawPillButton(canvas, _fftRect, $"FFT {state.FftSize}", false);
        bx = _fftRect.Right + buttonSpacing;

        _windowRect = new SKRect(bx, buttonY1, bx + buttonWidth + 8f, buttonY1 + buttonHeight);
        DrawPillButton(canvas, _windowRect, state.WindowFunction.ToString(), false);
        bx = _windowRect.Right + buttonSpacing;

        _overlapRect = new SKRect(bx, buttonY1, bx + buttonWidth, buttonY1 + buttonHeight);
        DrawPillButton(canvas, _overlapRect, $"{state.Overlap * 100f:0}%", false);

        // Buttons row 2
        float buttonY2 = buttonY1 + buttonHeight + buttonSpacing;
        bx = rect.Left + 6f;

        _reassignRect = new SKRect(bx, buttonY2, bx + buttonWidth + 10f, buttonY2 + buttonHeight);
        DrawPillButton(canvas, _reassignRect, FormatReassignLabel(state.ReassignMode),
            state.ReassignMode != SpectrogramReassignMode.Off);
        bx = _reassignRect.Right + buttonSpacing;

        _smoothingModeRect = new SKRect(bx, buttonY2, bx + buttonWidth + 10f, buttonY2 + buttonHeight);
        DrawPillButton(canvas, _smoothingModeRect, FormatSmoothingLabel(state.SmoothingMode),
            state.SmoothingMode != SpectrogramSmoothingMode.Off);
        bx = _smoothingModeRect.Right + buttonSpacing;

        _preEmphasisToggleRect = new SKRect(bx, buttonY2, bx + buttonWidth, buttonY2 + toggleHeight);
        DrawPillButton(canvas, _preEmphasisToggleRect, "Emph", state.PreEmphasisEnabled);
    }

    private void DrawClarityPanelContents(SKCanvas canvas, SKRect rect, SpectroState state,
        float buttonHeight, float buttonSpacing)
    {
        // Knobs: Noise, Harmonic, Smoothing
        float knobY = rect.Top + PanelHeaderHeight + 35f;
        float knobSpacingX = rect.Width / 4f;

        ClarityNoiseKnob.Value = state.ClarityNoise * 100f;
        ClarityNoiseKnob.Center = new SKPoint(rect.Left + knobSpacingX, knobY);
        ClarityNoiseKnob.Render(canvas);

        ClarityHarmonicKnob.Value = state.ClarityHarmonic * 100f;
        ClarityHarmonicKnob.Center = new SKPoint(rect.Left + knobSpacingX * 2, knobY);
        ClarityHarmonicKnob.Render(canvas);

        ClaritySmoothingKnob.Value = state.ClaritySmoothing * 100f;
        ClaritySmoothingKnob.Center = new SKPoint(rect.Left + knobSpacingX * 3, knobY);
        ClaritySmoothingKnob.Render(canvas);

        // Buttons row
        float buttonY = rect.Bottom - 32f;
        float buttonWidth = 60f;
        float bx = rect.Left + 6f;

        _clarityRect = new SKRect(bx, buttonY, bx + buttonWidth, buttonY + buttonHeight);
        DrawPillButton(canvas, _clarityRect, FormatClarityLabel(state.ClarityMode),
            state.ClarityMode != ClarityProcessingMode.None);
        bx = _clarityRect.Right + buttonSpacing;

        _normalizationRect = new SKRect(bx, buttonY, bx + buttonWidth, buttonY + buttonHeight);
        DrawPillButton(canvas, _normalizationRect, FormatNormalizationLabel(state.NormalizationMode),
            state.NormalizationMode != SpectrogramNormalizationMode.None);
        bx = _normalizationRect.Right + buttonSpacing;

        _dynamicRangeRect = new SKRect(bx, buttonY, bx + buttonWidth, buttonY + buttonHeight);
        DrawPillButton(canvas, _dynamicRangeRect, FormatDynamicRangeLabel(state.DynamicRangeMode),
            state.DynamicRangeMode != SpectrogramDynamicRangeMode.Custom);
    }

    private void DrawDisplayPanelContents(SKCanvas canvas, SKRect rect, SpectroState state,
        float buttonHeight, float buttonSpacing)
    {
        // Knobs: MinDb, MaxDb, Brightness, Gamma, Contrast, Levels (2 rows of 3)
        float knobY1 = rect.Top + PanelHeaderHeight + 35f;
        float knobY2 = knobY1 + 70f;
        float knobSpacingX = rect.Width / 4f;

        MinDbKnob.Value = state.MinDb;
        MinDbKnob.Center = new SKPoint(rect.Left + knobSpacingX, knobY1);
        MinDbKnob.Render(canvas);

        MaxDbKnob.Value = state.MaxDb;
        MaxDbKnob.Center = new SKPoint(rect.Left + knobSpacingX * 2, knobY1);
        MaxDbKnob.Render(canvas);

        BrightnessKnob.Value = state.Brightness;
        BrightnessKnob.Center = new SKPoint(rect.Left + knobSpacingX * 3, knobY1);
        BrightnessKnob.Render(canvas);

        GammaKnob.Value = state.Gamma;
        GammaKnob.Center = new SKPoint(rect.Left + knobSpacingX, knobY2);
        GammaKnob.Render(canvas);

        ContrastKnob.Value = state.Contrast;
        ContrastKnob.Center = new SKPoint(rect.Left + knobSpacingX * 2, knobY2);
        ContrastKnob.Render(canvas);

        LevelsKnob.Value = state.ColorLevels;
        LevelsKnob.Center = new SKPoint(rect.Left + knobSpacingX * 3, knobY2);
        LevelsKnob.Render(canvas);

        // Color map button
        float buttonY = rect.Bottom - 32f;
        float bx = rect.Left + 6f;
        float buttonWidth = 70f;

        _colorRect = new SKRect(bx, buttonY, bx + buttonWidth, buttonY + buttonHeight);
        DrawPillButton(canvas, _colorRect, ((SpectrogramColorMap)state.ColorMap).ToString(), false);
    }

    private void DrawViewOptionsPanelContents(SKCanvas canvas, SKRect rect, SpectroState state,
        float toggleHeight, float buttonSpacing)
    {
        float toggleWidth = 60f;
        float buttonHeight = 22f;
        float subHeaderY = rect.Top + PanelHeaderHeight + 4f;

        // Sub-section: OVERLAYS
        canvas.DrawText("Overlays", rect.Left + 10f, subHeaderY + 10f, _mutedTextPaint);
        float row1Y = subHeaderY + 16f;
        float tx = rect.Left + 8f;

        _pitchToggleRect = new SKRect(tx, row1Y, tx + toggleWidth, row1Y + toggleHeight);
        DrawPillButton(canvas, _pitchToggleRect, "Pitch", state.ShowPitch);
        tx = _pitchToggleRect.Right + buttonSpacing;

        _harmonicToggleRect = new SKRect(tx, row1Y, tx + toggleWidth, row1Y + toggleHeight);
        DrawPillButton(canvas, _harmonicToggleRect, "Harm", state.ShowHarmonics);
        tx = _harmonicToggleRect.Right + buttonSpacing;

        // Harmonic mode toggle (D=Detected, T=Theoretical, B=Both)
        float modeButtonWidth = 28f;
        _harmonicModeRect = new SKRect(tx, row1Y, tx + modeButtonWidth, row1Y + toggleHeight);
        string modeLabel = state.HarmonicDisplayMode switch
        {
            HarmonicDisplayMode.Detected => "D",
            HarmonicDisplayMode.Theoretical => "T",
            HarmonicDisplayMode.Both => "B",
            _ => "?"
        };
        DrawPillButton(canvas, _harmonicModeRect, modeLabel, state.ShowHarmonics);
        tx = _harmonicModeRect.Right + buttonSpacing;

        _voicingToggleRect = new SKRect(tx, row1Y, tx + toggleWidth, row1Y + toggleHeight);
        DrawPillButton(canvas, _voicingToggleRect, "Voice", state.ShowVoicing);
        tx = _voicingToggleRect.Right + buttonSpacing;

        _rangeToggleRect = new SKRect(tx, row1Y, tx + toggleWidth, row1Y + toggleHeight);
        DrawPillButton(canvas, _rangeToggleRect, "Range", state.ShowRange);
        tx = _rangeToggleRect.Right + buttonSpacing;

        _guidesToggleRect = new SKRect(tx, row1Y, tx + toggleWidth, row1Y + toggleHeight);
        DrawPillButton(canvas, _guidesToggleRect, "Guides", state.ShowGuides);

        // Sub-section: DISPLAYS
        float displaysHeaderY = row1Y + toggleHeight + 8f;
        canvas.DrawText("Displays", rect.Left + 10f, displaysHeaderY + 10f, _mutedTextPaint);
        float row2Y = displaysHeaderY + 16f;
        tx = rect.Left + 8f;

        _spectrumToggleRect = new SKRect(tx, row2Y, tx + toggleWidth, row2Y + toggleHeight);
        DrawPillButton(canvas, _spectrumToggleRect, "Slice", state.ShowSpectrum);
        tx = _spectrumToggleRect.Right + buttonSpacing;

        _pitchMeterToggleRect = new SKRect(tx, row2Y, tx + toggleWidth, row2Y + toggleHeight);
        DrawPillButton(canvas, _pitchMeterToggleRect, "Meter", state.ShowPitchMeter);

        // Sub-section: SETTINGS
        float settingsHeaderY = row2Y + toggleHeight + 8f;
        canvas.DrawText("Settings", rect.Left + 10f, settingsHeaderY + 10f, _mutedTextPaint);
        float row3Y = settingsHeaderY + 16f;
        tx = rect.Left + 8f;

        _voiceRangeRect = new SKRect(tx, row3Y, tx + 70f, row3Y + buttonHeight);
        DrawPillButton(canvas, _voiceRangeRect, FormatVoiceRangeLabel(state.VoiceRange), state.ShowRange);
        tx = _voiceRangeRect.Right + buttonSpacing;

        _pitchAlgoRect = new SKRect(tx, row3Y, tx + 70f, row3Y + buttonHeight);
        DrawPillButton(canvas, _pitchAlgoRect, FormatPitchLabel(state.PitchAlgorithm), false);
        tx = _pitchAlgoRect.Right + buttonSpacing;

        _pauseRect = new SKRect(tx, row3Y, tx + 60f, row3Y + buttonHeight);
        DrawPillButton(canvas, _pauseRect, state.IsPaused ? "PAUSE" : "RUN", state.IsPaused);
    }

    public SpectroHitTest HitTest(float x, float y)
    {
        if (_closeRect.Contains(x, y))
        {
            return new SpectroHitTest(SpectroHitArea.CloseButton, -1);
        }

        if (_bypassRect.Contains(x, y))
        {
            return new SpectroHitTest(SpectroHitArea.BypassButton, -1);
        }

        var presetHit = _presetBar.HitTest(x, y);
        if (presetHit == PresetBarHitArea.Dropdown)
        {
            return new SpectroHitTest(SpectroHitArea.PresetDropdown, -1);
        }
        if (presetHit == PresetBarHitArea.SaveButton)
        {
            return new SpectroHitTest(SpectroHitArea.PresetSave, -1);
        }

        if (_titleBarRect.Contains(x, y))
        {
            return new SpectroHitTest(SpectroHitArea.TitleBar, -1);
        }

        if (_fftRect.Contains(x, y)) return new SpectroHitTest(SpectroHitArea.FftButton, -1);
        if (_transformRect.Contains(x, y)) return new SpectroHitTest(SpectroHitArea.TransformButton, -1);
        if (_windowRect.Contains(x, y)) return new SpectroHitTest(SpectroHitArea.WindowButton, -1);
        if (_overlapRect.Contains(x, y)) return new SpectroHitTest(SpectroHitArea.OverlapButton, -1);
        if (_scaleRect.Contains(x, y)) return new SpectroHitTest(SpectroHitArea.ScaleButton, -1);
        if (_colorRect.Contains(x, y)) return new SpectroHitTest(SpectroHitArea.ColorButton, -1);
        if (_reassignRect.Contains(x, y)) return new SpectroHitTest(SpectroHitArea.ReassignButton, -1);
        if (_clarityRect.Contains(x, y)) return new SpectroHitTest(SpectroHitArea.ClarityButton, -1);
        if (_pitchAlgoRect.Contains(x, y)) return new SpectroHitTest(SpectroHitArea.PitchAlgorithmButton, -1);
        if (_axisModeRect.Contains(x, y)) return new SpectroHitTest(SpectroHitArea.AxisModeButton, -1);
        if (_smoothingModeRect.Contains(x, y)) return new SpectroHitTest(SpectroHitArea.SmoothingModeButton, -1);
        if (_pauseRect.Contains(x, y)) return new SpectroHitTest(SpectroHitArea.PauseButton, -1);
        if (_pitchToggleRect.Contains(x, y)) return new SpectroHitTest(SpectroHitArea.PitchToggle, -1);
        if (_harmonicToggleRect.Contains(x, y)) return new SpectroHitTest(SpectroHitArea.HarmonicToggle, -1);
        if (_harmonicModeRect.Contains(x, y)) return new SpectroHitTest(SpectroHitArea.HarmonicModeToggle, -1);
        if (_voicingToggleRect.Contains(x, y)) return new SpectroHitTest(SpectroHitArea.VoicingToggle, -1);
        if (_preEmphasisToggleRect.Contains(x, y)) return new SpectroHitTest(SpectroHitArea.PreEmphasisToggle, -1);
        if (_hpfToggleRect.Contains(x, y)) return new SpectroHitTest(SpectroHitArea.HpfToggle, -1);
        if (_rangeToggleRect.Contains(x, y)) return new SpectroHitTest(SpectroHitArea.RangeToggle, -1);
        if (_guidesToggleRect.Contains(x, y)) return new SpectroHitTest(SpectroHitArea.GuidesToggle, -1);
        if (_voiceRangeRect.Contains(x, y)) return new SpectroHitTest(SpectroHitArea.VoiceRangeButton, -1);
        if (_normalizationRect.Contains(x, y)) return new SpectroHitTest(SpectroHitArea.NormalizationButton, -1);
        if (_dynamicRangeRect.Contains(x, y)) return new SpectroHitTest(SpectroHitArea.DynamicRangeButton, -1);
        if (_spectrumToggleRect.Contains(x, y)) return new SpectroHitTest(SpectroHitArea.SpectrumToggle, -1);
        if (_pitchMeterToggleRect.Contains(x, y)) return new SpectroHitTest(SpectroHitArea.PitchMeterToggle, -1);

        if (_spectrogramRect.Contains(x, y)) return new SpectroHitTest(SpectroHitArea.Spectrogram, -1);

        for (int i = 0; i < _allKnobs.Length; i++)
        {
            if (_allKnobs[i].HitTest(x, y))
            {
                return new SpectroHitTest(SpectroHitArea.Knob, i);
            }
        }

        return new SpectroHitTest(SpectroHitArea.None, -1);
    }

    /// <summary>
    /// Returns the tooltip control ID for the element at the given position.
    /// </summary>
    public string? GetControlIdAt(float x, float y)
    {
        // Check buttons and toggles
        if (_fftRect.Contains(x, y)) return "FftSize";
        if (_transformRect.Contains(x, y)) return "Transform";
        if (_windowRect.Contains(x, y)) return "Window";
        if (_overlapRect.Contains(x, y)) return "Overlap";
        if (_scaleRect.Contains(x, y)) return "Scale";
        if (_colorRect.Contains(x, y)) return "ColorMap";
        if (_reassignRect.Contains(x, y)) return "Reassign";
        if (_clarityRect.Contains(x, y)) return "Clarity";
        if (_pitchAlgoRect.Contains(x, y)) return "PitchAlgo";
        if (_axisModeRect.Contains(x, y)) return "Axis";
        if (_smoothingModeRect.Contains(x, y)) return "Smoothing";
        if (_pauseRect.Contains(x, y)) return "Pause";
        if (_pitchToggleRect.Contains(x, y)) return "PitchOverlay";
        if (_harmonicToggleRect.Contains(x, y)) return "HarmonicOverlay";
        if (_harmonicModeRect.Contains(x, y)) return "HarmonicMode";
        if (_voicingToggleRect.Contains(x, y)) return "VoicingOverlay";
        if (_preEmphasisToggleRect.Contains(x, y)) return "PreEmphasis";
        if (_hpfToggleRect.Contains(x, y)) return "HPF";
        if (_rangeToggleRect.Contains(x, y)) return "RangeOverlay";
        if (_guidesToggleRect.Contains(x, y)) return "GuidesOverlay";
        if (_voiceRangeRect.Contains(x, y)) return "VoiceRange";
        if (_normalizationRect.Contains(x, y)) return "Normalization";
        if (_dynamicRangeRect.Contains(x, y)) return "DynamicRange";
        if (_spectrumToggleRect.Contains(x, y)) return "SpectrumView";
        if (_pitchMeterToggleRect.Contains(x, y)) return "PitchMeterView";

        // Check knobs
        if (MinFreqKnob.HitTest(x, y)) return "MinFreq";
        if (MaxFreqKnob.HitTest(x, y)) return "MaxFreq";
        if (MinDbKnob.HitTest(x, y)) return "MinDb";
        if (MaxDbKnob.HitTest(x, y)) return "MaxDb";
        if (TimeKnob.HitTest(x, y)) return "Time";
        if (HpfKnob.HitTest(x, y)) return "HpfCutoff";
        if (ReassignThresholdKnob.HitTest(x, y)) return "ReassignThresh";
        if (ReassignSpreadKnob.HitTest(x, y)) return "ReassignSpread";
        if (ClarityNoiseKnob.HitTest(x, y)) return "ClarityNoise";
        if (ClarityHarmonicKnob.HitTest(x, y)) return "ClarityHarmonic";
        if (ClaritySmoothingKnob.HitTest(x, y)) return "ClaritySmooth";
        if (BrightnessKnob.HitTest(x, y)) return "Brightness";
        if (GammaKnob.HitTest(x, y)) return "Gamma";
        if (ContrastKnob.HitTest(x, y)) return "Contrast";
        if (LevelsKnob.HitTest(x, y)) return "Levels";

        return null;
    }

    /// <summary>
    /// Updates tooltip hover state based on mouse position.
    /// </summary>
    public void UpdateTooltipHover(float x, float y)
    {
        var controlId = GetControlIdAt(x, y);
        if (controlId != null && TooltipData.TryGetValue(controlId, out var info))
        {
            _tooltip.StartHover(controlId, info.Title, info.Description, new SKPoint(x, y));
        }
        else
        {
            _tooltip.EndHover();
        }
    }

    private void DrawTitleBar(SKCanvas canvas, SKSize size, SpectroState state)
    {
        _titleBarRect = new SKRect(0, 0, size.Width, TitleBarHeight);
        canvas.DrawRect(_titleBarRect, _panelPaint);
        canvas.DrawLine(0, TitleBarHeight, size.Width, TitleBarHeight, _borderPaint);
        canvas.DrawText("Analyzer", Padding, TitleBarHeight / 2f + 5f, _titlePaint);

        float presetBarX = Padding + 150f;
        float presetBarY = (TitleBarHeight - PluginPresetBar.TotalHeight) / 2f;
        _presetBar.Render(canvas, presetBarX, presetBarY, state.PresetName);

        float buttonSize = 18f;
        float right = size.Width - Padding;
        _closeRect = new SKRect(right - buttonSize, TitleBarHeight / 2f - buttonSize / 2f, right, TitleBarHeight / 2f + buttonSize / 2f);
        DrawIconButton(canvas, _closeRect, "X", _theme.TextPrimary);
        right -= buttonSize + 8f;

        _bypassRect = new SKRect(right - 48f, TitleBarHeight / 2f - 9f, right, TitleBarHeight / 2f + 9f);
        DrawPillButton(canvas, _bypassRect, state.IsBypassed ? "BYP" : "ON", state.IsBypassed);

        string backendLabel = state.UsingGpu ? "GPU" : "CPU";
        float backendWidth = _mutedTextPaint.MeasureText(backendLabel);
        float backendX = _bypassRect.Left - 8f - backendWidth;
        canvas.DrawText(backendLabel, backendX, TitleBarHeight / 2f + 5f, _mutedTextPaint);

        string dropLabel = $"DROP {state.DroppedHops}";
        var dropPaint = state.DroppedHops > 0 ? _dropAlertPaint : _mutedTextPaint;
        float dropWidth = dropPaint.MeasureText(dropLabel);
        float dropX = backendX - 12f - dropWidth;
        canvas.DrawText(dropLabel, dropX, TitleBarHeight / 2f + 5f, dropPaint);

        if (state.IsProfiling)
        {
            const string profilingLabel = "PROF";
            float profilingWidth = _profilingTextPaint.MeasureText(profilingLabel);
            float profilingX = dropX - 12f - profilingWidth;
            float dotX = profilingX - 8f;
            float dotY = TitleBarHeight / 2f + 1f;
            canvas.DrawCircle(dotX, dotY, 3f, _profilingDotPaint);
            canvas.DrawText(profilingLabel, profilingX, TitleBarHeight / 2f + 5f, _profilingTextPaint);
        }
    }

    private void EnsureAxisLayer(SKSize size, float dpiScale, SpectroState state, SKRect spectrumRect, SKRect axisRect, SKRect colorRect)
    {
        int pixelWidth = (int)MathF.Ceiling(size.Width * dpiScale);
        int pixelHeight = (int)MathF.Ceiling(size.Height * dpiScale);
        bool sizeChanged = _axisInfo.Width != pixelWidth || _axisInfo.Height != pixelHeight;
        bool dpiChanged = MathF.Abs(_axisDpiScale - dpiScale) > 1e-3f;
        bool layoutChanged = state.ShowSpectrum != _axisShowSpectrumCache
            || state.ShowPitchMeter != _axisShowPitchMeterCache;
        bool axisChanged = state.AxisMode != _axisModeCache
            || state.Scale != _axisScaleCache
            || MathF.Abs(state.MinFrequency - _axisMinHzCache) > 1e-3f
            || MathF.Abs(state.MaxFrequency - _axisMaxHzCache) > 1e-3f
            || MathF.Abs(state.TimeWindowSeconds - _axisTimeWindowCache) > 1e-3f
            || state.ColorMap != _axisColorMapCache;

        if (!sizeChanged && !dpiChanged && !layoutChanged && !axisChanged && _axisImage is not null)
        {
            return;
        }

        InvalidateAxisCache();
        _axisInfo = new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        _axisSurface = SKSurface.Create(_axisInfo);
        if (_axisSurface is null)
        {
            return;
        }

        var axisCanvas = _axisSurface.Canvas;
        axisCanvas.Clear(SKColors.Transparent);
        axisCanvas.Save();
        axisCanvas.Scale(dpiScale);
        DrawFrequencyAxis(axisCanvas, axisRect, state);
        DrawColorBar(axisCanvas, colorRect, state);
        DrawTimeAxis(axisCanvas, spectrumRect, state);
        axisCanvas.Restore();
        _axisImage = _axisSurface.Snapshot();
        _axisDrawRect = new SKRect(0, 0, size.Width, size.Height);

        _axisDpiScale = dpiScale;
        _axisModeCache = state.AxisMode;
        _axisScaleCache = state.Scale;
        _axisMinHzCache = state.MinFrequency;
        _axisMaxHzCache = state.MaxFrequency;
        _axisTimeWindowCache = state.TimeWindowSeconds;
        _axisColorMapCache = state.ColorMap;
        _axisShowSpectrumCache = state.ShowSpectrum;
        _axisShowPitchMeterCache = state.ShowPitchMeter;
    }

    private void DrawAxisLayer(SKCanvas canvas)
    {
        if (_axisImage is null)
        {
            return;
        }

        canvas.DrawImage(_axisImage, _axisDrawRect, _axisSampling, _axisImagePaint);
    }

    private void InvalidateAxisCache()
    {
        _axisImage?.Dispose();
        _axisImage = null;
        _axisSurface?.Dispose();
        _axisSurface = null;
    }

    private void DrawVocalRangeBand(SKCanvas canvas, SKRect rect, SpectroState state)
    {
        var (minHz, maxHz) = VocalRangeInfo.GetFundamentalRange(state.VoiceRange);
        if (maxHz < state.MinFrequency || minHz > state.MaxFrequency)
        {
            return;
        }

        float clampedMin = MathF.Max(minHz, state.MinFrequency);
        float clampedMax = MathF.Min(maxHz, state.MaxFrequency);
        float normMin = FrequencyToNorm(state, clampedMin);
        float normMax = FrequencyToNorm(state, clampedMax);
        float y1 = rect.Bottom - normMin * rect.Height;
        float y2 = rect.Bottom - normMax * rect.Height;
        float top = MathF.Min(y1, y2);
        float bottom = MathF.Max(y1, y2);
        canvas.DrawRect(new SKRect(rect.Left, top, rect.Right, bottom), _rangeBandPaint);
        canvas.DrawText(VocalRangeInfo.GetLabel(state.VoiceRange), rect.Left + 6f, top + 12f, _mutedTextPaint);
    }

    private void DrawFrequencyGuides(SKCanvas canvas, SKRect rect, SpectroState state)
    {
        foreach (float freq in VocalZoneGuides)
        {
            if (freq < state.MinFrequency || freq > state.MaxFrequency)
            {
                continue;
            }

            float norm = FrequencyToNorm(state, freq);
            float y = rect.Bottom - norm * rect.Height;
            canvas.DrawLine(rect.Left, y, rect.Right, y, _guidePaint);
        }
    }

    /// <summary>
    /// Update spectrum slice display values with attack/release ballistics.
    /// Attack is nearly instant, release is slow (300ms) for smooth professional animation.
    /// </summary>
    private void UpdateSpectrumBallistics(float[] spectrogram, int offset, int bins)
    {
        // Ensure buffer is sized correctly
        if (_spectrumBallistics.Length != bins)
        {
            _spectrumBallistics = new float[bins];
            _lastBallisticsTicks = Stopwatch.GetTimestamp();
        }

        // Calculate delta time
        long currentTicks = Stopwatch.GetTimestamp();
        float deltaMs = (float)((currentTicks - _lastBallisticsTicks) * 1000.0 / Stopwatch.Frequency);
        _lastBallisticsTicks = currentTicks;

        // Clamp delta to avoid huge jumps after pause/lag
        deltaMs = MathF.Min(deltaMs, 100f);

        // Calculate attack and release coefficients (exponential smoothing)
        // coeff = 1 - exp(-deltaMs / timeConstant)
        float attackCoeff = 1f - MathF.Exp(-deltaMs / BallisticsAttackMs);
        float releaseCoeff = 1f - MathF.Exp(-deltaMs / BallisticsReleaseMs);

        for (int i = 0; i < bins; i++)
        {
            float target = spectrogram[offset + i];
            float current = _spectrumBallistics[i];

            if (target >= current)
            {
                // Attack: fast response to rising signal
                _spectrumBallistics[i] = current + (target - current) * attackCoeff;
            }
            else
            {
                // Release: slow decay for smooth falling
                _spectrumBallistics[i] = current + (target - current) * releaseCoeff;
            }
        }
    }

    private void DrawSpectrumSlice(SKCanvas canvas, SKRect rect, SpectroState state, int frameIndex)
    {
        canvas.DrawRoundRect(new SKRoundRect(rect, 6f), _panelPaint);
        canvas.DrawText("Spectrum", rect.Left + 6f, rect.Top + 12f, _mutedTextPaint);

        if (state.Spectrogram is null || state.BinFrequencies is null || frameIndex < 0)
        {
            return;
        }

        if (!TryGetRingIndex(frameIndex, out int ringIndex))
        {
            return;
        }

        int bins = state.DisplayBins;
        int offset = ringIndex * bins;
        if (offset < 0 || offset + bins > state.Spectrogram.Length)
        {
            return;
        }

        // Update ballistics for smooth attack/release animation
        UpdateSpectrumBallistics(state.Spectrogram, offset, bins);

        float minDb = state.MinDb;
        float maxDb = state.MaxDb;
        float dbRange = MathF.Max(1f, maxDb - minDb);
        float freqRange = MathF.Max(1f, state.MaxFrequency - state.MinFrequency);

        _spectrumSlicePath.Reset();
        _spectrumFillPath.Reset();
        bool started = false;
        float firstX = 0f, lastX = 0f;
        for (int i = 0; i < bins && i < state.BinFrequencies.Length; i++)
        {
            float freq = state.BinFrequencies[i];
            if (freq < state.MinFrequency || freq > state.MaxFrequency)
            {
                continue;
            }

            float normX = (freq - state.MinFrequency) / freqRange;
            // Use ballistic-smoothed value for display
            float value = _spectrumBallistics[i];
            float db = minDb + value * dbRange;
            float normY = (db - minDb) / dbRange;

            float x = rect.Left + rect.Width * normX;
            float y = rect.Bottom - rect.Height * normY;

            if (!started)
            {
                _spectrumSlicePath.MoveTo(x, y);
                _spectrumFillPath.MoveTo(x, rect.Bottom);
                _spectrumFillPath.LineTo(x, y);
                firstX = x;
                started = true;
            }
            else
            {
                _spectrumSlicePath.LineTo(x, y);
                _spectrumFillPath.LineTo(x, y);
            }
            lastX = x;
        }

        if (!_spectrumSlicePath.IsEmpty)
        {
            // Close fill path and draw gradient fill
            _spectrumFillPath.LineTo(lastX, rect.Bottom);
            _spectrumFillPath.Close();

            // Create vertical gradient from accent color (top) to transparent (bottom)
            var gradientShader = SKShader.CreateLinearGradient(
                new SKPoint(0, rect.Top),
                new SKPoint(0, rect.Bottom),
                _spectrumFillColors,
                SpectrumFillStops,
                SKShaderTileMode.Clamp);
            _spectrumFillPaint.Shader = gradientShader;
            canvas.DrawPath(_spectrumFillPath, _spectrumFillPaint);
            gradientShader.Dispose();

            // Draw the stroke on top
            canvas.DrawPath(_spectrumSlicePath, _spectrumPaint);
        }

        for (int i = 0; i < _peakBins.Length; i++)
        {
            _peakBins[i] = -1;
            _peakValues[i] = 0f;
        }

        for (int i = 1; i < bins - 1 && i + offset + 1 < state.Spectrogram.Length; i++)
        {
            float value = state.Spectrogram[offset + i];
            if (value < 0.15f)
            {
                continue;
            }

            if (value >= state.Spectrogram[offset + i - 1] && value > state.Spectrogram[offset + i + 1])
            {
                for (int k = 0; k < _peakBins.Length; k++)
                {
                    if (value > _peakValues[k])
                    {
                        for (int shift = _peakBins.Length - 1; shift > k; shift--)
                        {
                            _peakBins[shift] = _peakBins[shift - 1];
                            _peakValues[shift] = _peakValues[shift - 1];
                        }
                        _peakBins[k] = i;
                        _peakValues[k] = value;
                        break;
                    }
                }
            }
        }

        for (int i = 0; i < _peakBins.Length; i++)
        {
            int bin = _peakBins[i];
            if (bin < 0 || bin >= state.BinFrequencies.Length)
            {
                continue;
            }

            float freq = state.BinFrequencies[bin];
            float normX = (freq - state.MinFrequency) / freqRange;
            float value = state.Spectrogram[offset + bin];
            float db = minDb + value * dbRange;
            float normY = (db - minDb) / dbRange;
            float x = rect.Left + rect.Width * normX;
            float y = rect.Bottom - rect.Height * normY;
            canvas.DrawCircle(x, y, 3f, _spectrumPeakPaint);
            canvas.DrawText($"{FormatHz(freq)} {GetNoteName(freq)}", x + 4f, y - 4f, _metricPaint);
        }

        float centroid = GetFrameValue(state.SpectralCentroid, frameIndex);
        float slope = GetFrameValue(state.SpectralSlope, frameIndex);
        float flux = GetFrameValue(state.SpectralFlux, frameIndex);
        canvas.DrawText($"Centroid {centroid:0} Hz", rect.Left + 6f, rect.Bottom - 28f, _metricPaint);
        canvas.DrawText($"Slope {slope:0.0} dB/k", rect.Left + 6f, rect.Bottom - 14f, _metricPaint);
        canvas.DrawText($"Flux {flux:0.000}", rect.Right - 80f, rect.Bottom - 14f, _metricPaint);
    }

    private void DrawPitchMeter(SKCanvas canvas, SKRect rect, SpectroState state, int frameIndex)
    {
        canvas.DrawRoundRect(new SKRoundRect(rect, 6f), _panelPaint);
        canvas.DrawText("Pitch", rect.Left + 6f, rect.Top + 12f, _mutedTextPaint);

        float pitch = GetFrameValue(state.PitchTrack, frameIndex);
        float confidence = GetFrameValue(state.PitchConfidence, frameIndex);
        float hnr = GetFrameValue(state.HnrTrack, frameIndex);
        float cpp = GetFrameValue(state.CppTrack, frameIndex);

        string note = pitch > 0f ? GetNoteName(pitch) : "--";
        string label = pitch > 0f ? $"{note} {pitch:0.0} Hz" : "--";
        canvas.DrawText(label, rect.Left + 8f, rect.Top + 32f, _textPaint);

        float barWidth = (rect.Width - 16f) * Math.Clamp(confidence, 0f, 1f);
        var barRect = new SKRect(rect.Left + 8f, rect.Top + 40f, rect.Left + 8f + barWidth, rect.Top + 48f);
        canvas.DrawRect(barRect, _buttonActivePaint);
        canvas.DrawText($"Conf {confidence:0.00}", rect.Left + 8f, rect.Top + 60f, _metricPaint);
        canvas.DrawText($"HNR {hnr:0.0} dB", rect.Left + 8f, rect.Top + 74f, _metricPaint);
        canvas.DrawText($"CPP {cpp:0.0} dB", rect.Left + 8f, rect.Top + 88f, _metricPaint);
    }

    private void UpdateFrameMapping(SpectroState state)
    {
        _frameCapacity = state.FrameCount;
        if (state.AvailableFrames <= 0 || state.LatestFrameId < 0 || _frameCapacity <= 0)
        {
            _frameAvailable = 0;
            _framePadFrames = Math.Max(0, _frameCapacity);
            _frameRingStart = 0;
            return;
        }

        _frameAvailable = Math.Min(state.AvailableFrames, _frameCapacity);
        _framePadFrames = Math.Max(0, _frameCapacity - _frameAvailable);
        long oldestFrameId = state.LatestFrameId - _frameAvailable + 1;
        int ringStart = (int)(oldestFrameId % _frameCapacity);
        if (ringStart < 0)
        {
            ringStart += _frameCapacity;
        }

        _frameRingStart = ringStart;
    }

    private bool TryGetRingIndex(int displayIndex, out int ringIndex)
    {
        ringIndex = -1;
        if (_frameAvailable <= 0 || _frameCapacity <= 0 || displayIndex < _framePadFrames)
        {
            return false;
        }

        int relative = displayIndex - _framePadFrames;
        if (relative < 0 || relative >= _frameAvailable)
        {
            return false;
        }

        ringIndex = _frameRingStart + relative;
        if (ringIndex >= _frameCapacity)
        {
            ringIndex -= _frameCapacity;
        }

        return true;
    }

    private static int GetSliceFrameIndex(SpectroState state)
    {
        if (state.AvailableFrames <= 0 || state.LatestFrameId < 0 || state.FrameCount <= 0)
        {
            return -1;
        }

        int padFrames = Math.Max(0, state.FrameCount - state.AvailableFrames);
        long oldestFrame = state.LatestFrameId - state.AvailableFrames + 1;

        if (state.ReferenceFrameId is not null)
        {
            long offset = state.ReferenceFrameId.Value - oldestFrame;
            if (offset >= 0 && offset < state.AvailableFrames)
            {
                return padFrames + (int)offset;
            }
        }

        return padFrames + Math.Max(0, state.AvailableFrames - 1);
    }

    private float GetFrameValue(float[]? values, int frameIndex)
    {
        if (values is null || frameIndex < 0)
        {
            return 0f;
        }

        if (!TryGetRingIndex(frameIndex, out int ringIndex))
        {
            return 0f;
        }

        if (ringIndex < 0 || ringIndex >= values.Length)
        {
            return 0f;
        }

        return values[ringIndex];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void UpdateSpectrogramBitmap(SpectroState state)
    {
        if (state.Spectrogram is null || state.FrameCount <= 0 || state.DisplayBins <= 0)
        {
            return;
        }

        bool sizeChanged = _spectrogramBitmap is null || state.FrameCount != _bitmapWidth || state.DisplayBins != _bitmapHeight;
        if (sizeChanged)
        {
            _spectrogramBitmap?.Dispose();
            _spectrogramBitmap = new SKBitmap(state.FrameCount, state.DisplayBins, SKColorType.Bgra8888, SKAlphaType.Premul);
            _bitmapWidth = state.FrameCount;
            _bitmapHeight = state.DisplayBins;
            _bitmapRingStart = 0;
            _lastDataVersion = -1;
            _lastLatestFrameId = -1;
            _lastAvailableFrames = -1;
            _lastFrameCount = state.FrameCount;
            _lastDisplayBins = state.DisplayBins;
            _pixelBuffer = new int[_bitmapWidth * _bitmapHeight];
            UpdateRowOffsets(_bitmapWidth, _bitmapHeight);
        }

        if (state.DataVersion == _lastDataVersion && state.ColorMap == _lastColorMap
            && state.LatestFrameId == _lastLatestFrameId && state.AvailableFrames == _lastAvailableFrames)
        {
            return;
        }

        int width = _bitmapWidth;
        int bins = state.DisplayBins;
        if (state.Spectrogram.Length < width * bins)
        {
            return;
        }

        bool colorChanged = state.ColorMap != _lastColorMap;
        bool displayChanged = colorChanged
            || MathF.Abs(state.Brightness - _lastBrightness) > 1e-3f
            || MathF.Abs(state.Gamma - _lastGamma) > 1e-3f
            || MathF.Abs(state.Contrast - _lastContrast) > 1e-3f
            || state.ColorLevels != _lastColorLevels;
        if (displayChanged || _colorLut.Length != 256)
        {
            UpdateColorLut(state.ColorMap, state.Brightness, state.Gamma, state.Contrast, state.ColorLevels);
        }

        if (state.AvailableFrames <= 0 || state.LatestFrameId < 0)
        {
            if (_pixelBuffer.Length > 0)
            {
                Array.Clear(_pixelBuffer, 0, _pixelBuffer.Length);
            }

            _bitmapRingStart = 0;
            var emptyPtr = _spectrogramBitmap!.GetPixels();
            if (emptyPtr != IntPtr.Zero)
            {
                Marshal.Copy(_pixelBuffer, 0, emptyPtr, _pixelBuffer.Length);
                _spectrogramBitmap.NotifyPixelsChanged();
            }

            _lastDataVersion = state.DataVersion;
            _lastColorMap = state.ColorMap;
            _lastBrightness = state.Brightness;
            _lastGamma = state.Gamma;
            _lastContrast = state.Contrast;
            _lastColorLevels = state.ColorLevels;
            _lastLatestFrameId = state.LatestFrameId;
            _lastAvailableFrames = state.AvailableFrames;
            _lastFrameCount = state.FrameCount;
            _lastDisplayBins = state.DisplayBins;
            return;
        }

        bool fullRebuild = sizeChanged
            || displayChanged
            || _lastLatestFrameId < 0
            || state.LatestFrameId < 0
            || state.FrameCount != _lastFrameCount
            || state.DisplayBins != _lastDisplayBins
            || state.AvailableFrames < _lastAvailableFrames;

        long deltaFrames = state.LatestFrameId - _lastLatestFrameId;
        if (!fullRebuild)
        {
            if (deltaFrames <= 0 || deltaFrames >= width)
            {
                fullRebuild = true;
            }
        }

        if (fullRebuild)
        {
            _bitmapRingStart = 0;
            RenderFullSpectrogram(state, width, bins);
        }
        else
        {
            int delta = (int)deltaFrames;
            _bitmapRingStart = (_bitmapRingStart + delta) % width;
            UpdateSpectrogramColumns(state, width, bins, delta, _bitmapRingStart);
        }

        var pixelsPtr = _spectrogramBitmap!.GetPixels();
        if (pixelsPtr != IntPtr.Zero)
        {
            Marshal.Copy(_pixelBuffer, 0, pixelsPtr, _pixelBuffer.Length);
            _spectrogramBitmap.NotifyPixelsChanged();
        }

        _lastDataVersion = state.DataVersion;
        _lastColorMap = state.ColorMap;
        _lastBrightness = state.Brightness;
        _lastGamma = state.Gamma;
        _lastContrast = state.Contrast;
        _lastColorLevels = state.ColorLevels;
        _lastLatestFrameId = state.LatestFrameId;
        _lastAvailableFrames = state.AvailableFrames;
        _lastFrameCount = state.FrameCount;
        _lastDisplayBins = state.DisplayBins;
    }

    private void DrawSpectrogramBitmap(SKCanvas canvas, SKRect destRect)
    {
        if (_spectrogramBitmap is null || _bitmapWidth <= 0 || _bitmapHeight <= 0)
        {
            return;
        }

        if (destRect.Width <= 0 || destRect.Height <= 0)
        {
            return;
        }

        if (_bitmapRingStart <= 0 || _bitmapRingStart >= _bitmapWidth)
        {
            canvas.DrawBitmap(_spectrogramBitmap, destRect, _bitmapPaint);
            canvas.DrawBitmap(_spectrogramBitmap, destRect, _bloomAddPaint);
            return;
        }

        float totalWidth = destRect.Width;
        float leftWidth = totalWidth * (_bitmapWidth - _bitmapRingStart) / _bitmapWidth;

        var srcLeft = new SKRect(_bitmapRingStart, 0, _bitmapWidth, _bitmapHeight);
        var dstLeft = new SKRect(destRect.Left, destRect.Top, destRect.Left + leftWidth, destRect.Bottom);
        canvas.DrawBitmap(_spectrogramBitmap, srcLeft, dstLeft, _bitmapPaint);

        var srcRight = new SKRect(0, 0, _bitmapRingStart, _bitmapHeight);
        var dstRight = new SKRect(destRect.Left + leftWidth, destRect.Top, destRect.Right, destRect.Bottom);
        canvas.DrawBitmap(_spectrogramBitmap, srcRight, dstRight, _bitmapPaint);

        canvas.DrawBitmap(_spectrogramBitmap, srcLeft, dstLeft, _bloomAddPaint);
        canvas.DrawBitmap(_spectrogramBitmap, srcRight, dstRight, _bloomAddPaint);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RenderFullSpectrogram(SpectroState state, int width, int bins)
    {
        if (_pixelBuffer.Length > 0)
        {
            Array.Clear(_pixelBuffer, 0, _pixelBuffer.Length);
        }

        if (_frameAvailable <= 0)
        {
            return;
        }

        int endFrame = Math.Min(width, _framePadFrames + _frameAvailable);
        for (int frame = _framePadFrames; frame < endFrame; frame++)
        {
            if (!TryGetRingIndex(frame, out int ringIndex))
            {
                continue;
            }

            int frameOffset = ringIndex * bins;
            int column = (frame + _bitmapRingStart) % width;
            for (int bin = 0; bin < bins; bin++)
            {
                float value = state.Spectrogram![frameOffset + bin];
                int colorIndex = value <= 0f ? 0 : value >= 1f ? 255 : (int)(value * 255f);
                int mappedIndex = _colorIndexLut[colorIndex];
                _pixelBuffer[_rowOffsets[bin] + column] = _colorLut[mappedIndex];
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void UpdateSpectrogramColumns(SpectroState state, int width, int bins, int deltaFrames, int ringStart)
    {
        if (deltaFrames <= 0)
        {
            return;
        }

        int startColumn = Math.Max(0, width - deltaFrames);
        for (int frame = startColumn; frame < width; frame++)
        {
            if (!TryGetRingIndex(frame, out int ringIndex))
            {
                continue;
            }

            int frameOffset = ringIndex * bins;
            int column = (ringStart + frame) % width;
            for (int bin = 0; bin < bins; bin++)
            {
                float value = state.Spectrogram![frameOffset + bin];
                int colorIndex = value <= 0f ? 0 : value >= 1f ? 255 : (int)(value * 255f);
                int mappedIndex = _colorIndexLut[colorIndex];
                _pixelBuffer[_rowOffsets[bin] + column] = _colorLut[mappedIndex];
            }
        }
    }

    private void UpdateRowOffsets(int width, int height)
    {
        if (_rowOffsets.Length != height)
        {
            _rowOffsets = new int[height];
        }

        for (int bin = 0; bin < height; bin++)
        {
            int y = height - 1 - bin;
            _rowOffsets[bin] = y * width;
        }
    }

    private void UpdateColorLut(int colorMap, float brightness, float gamma, float contrast, int colorLevels)
    {
        var colors = SpectrogramColorMaps.GetColors((SpectrogramColorMap)colorMap);
        if (_colorLut.Length != 256)
        {
            _colorLut = new int[256];
        }
        if (_colorIndexLut.Length != 256)
        {
            _colorIndexLut = new byte[256];
        }

        for (int i = 0; i < 256; i++)
        {
            var color = colors[i];
            _colorLut[i] = color.Blue | (color.Green << 8) | (color.Red << 16) | (color.Alpha << 24);
        }

        int levels = Math.Max(2, colorLevels);
        float invLevels = 1f / (levels - 1);
        for (int i = 0; i < 256; i++)
        {
            float value = i / 255f;
            value = Math.Clamp(value * brightness, 0f, 1f);
            value = MathF.Pow(value, gamma);
            value = (value - 0.5f) * contrast + 0.5f;
            value = Math.Clamp(value, 0f, 1f);
            int quant = (int)MathF.Round(value * (levels - 1));
            float quantValue = quant * invLevels;
            int mappedIndex = (int)MathF.Round(quantValue * 255f);
            _colorIndexLut[i] = (byte)Math.Clamp(mappedIndex, 0, 255);
        }
    }

    private void DrawFrequencyAxis(SKCanvas canvas, SKRect axisRect, SpectroState state)
    {
        canvas.DrawRect(axisRect, _panelPaint);
        float[] labels = state.AxisMode == SpectrogramAxisMode.Hz
            ? AxisHzLabels
            : AxisNoteLabels;
        foreach (float freq in labels)
        {
            if (freq < state.MinFrequency || freq > state.MaxFrequency)
            {
                continue;
            }

            float norm = FrequencyToNorm(state, freq);
            float y = axisRect.Bottom - norm * axisRect.Height;
            canvas.DrawLine(axisRect.Right - 6f, y, axisRect.Right, y, _gridPaint);
            string label = state.AxisMode switch
            {
                SpectrogramAxisMode.Note => GetNoteName(freq),
                SpectrogramAxisMode.Both => $"{GetNoteName(freq)} {FormatHz(freq)}",
                _ => FormatHz(freq)
            };
            canvas.DrawText(label, axisRect.Left + 4f, y + 4f, _mutedTextPaint);
        }
    }

    private void DrawColorBar(SKCanvas canvas, SKRect rect, SpectroState state)
    {
        EnsureColorBarShader(rect, state.ColorMap);
        canvas.DrawRoundRect(new SKRoundRect(rect, 4f), _colorBarPaint);
        canvas.DrawRoundRect(new SKRoundRect(rect, 4f), _borderPaint);
    }

    private void EnsureColorBarShader(SKRect rect, int colorMap)
    {
        if (_colorBarShader is not null && _colorBarMapCache == colorMap && RectEquals(_colorBarRectCache, rect))
        {
            return;
        }

        _colorBarShader?.Dispose();
        var colors = SpectrogramColorMaps.GetColors((SpectrogramColorMap)colorMap);
        _colorBarShader = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.Top),
            new SKPoint(rect.Left, rect.Bottom),
            new[] { colors[255], colors[0] },
            null,
            SKShaderTileMode.Clamp);
        _colorBarPaint.Shader = _colorBarShader;
        _colorBarRectCache = rect;
        _colorBarMapCache = colorMap;
    }

    private void DrawTimeAxis(SKCanvas canvas, SKRect rect, SpectroState state)
    {
        float bottom = rect.Bottom + 6f;
        canvas.DrawLine(rect.Left, rect.Bottom, rect.Right, rect.Bottom, _gridPaint);
        canvas.DrawText("0s", rect.Left, bottom + 12f, _mutedTextPaint);
        canvas.DrawText($"{state.TimeWindowSeconds:0.0}s", rect.Right - 24f, bottom + 12f, _mutedTextPaint);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DrawOverlays(SKCanvas canvas, SKRect rect, SpectroState state)
    {
        if (_frameAvailable <= 0)
        {
            return;
        }

        if (state.PitchTrack is { Length: > 0 } && state.ShowPitch)
        {
            int frames = Math.Min(state.FrameCount, state.PitchTrack.Length);
            int startFrame = _framePadFrames;
            int endFrame = Math.Min(frames, _framePadFrames + _frameAvailable);
            int step = GetOverlayStep(rect, frames);
            _pitchHighPath.Reset();
            _pitchLowPath.Reset();
            bool highStarted = false;
            bool lowStarted = false;

            for (int frame = startFrame; frame < endFrame; frame += step)
            {
                if (!TryGetRingIndex(frame, out int ringIndex))
                {
                    continue;
                }

                float pitch = state.PitchTrack[ringIndex];
                if (pitch <= 0f)
                {
                    highStarted = false;
                    lowStarted = false;
                    continue;
                }

                float confidence = state.PitchConfidence is { Length: > 0 } ? state.PitchConfidence[ringIndex] : 1f;
                bool high = confidence >= 0.6f;

                float norm = FrequencyToNorm(state, pitch);
                float x = rect.Left + rect.Width * frame / Math.Max(1, state.FrameCount - 1);
                float y = rect.Bottom - norm * rect.Height;

                if (high)
                {
                    if (!highStarted)
                    {
                        _pitchHighPath.MoveTo(x, y);
                        highStarted = true;
                    }
                    else
                    {
                        _pitchHighPath.LineTo(x, y);
                    }
                    lowStarted = false;
                }
                else
                {
                    if (!lowStarted)
                    {
                        _pitchLowPath.MoveTo(x, y);
                        lowStarted = true;
                    }
                    else
                    {
                        _pitchLowPath.LineTo(x, y);
                    }
                    highStarted = false;
                }
            }

            // Draw shadow first for depth effect
            canvas.DrawPath(_pitchHighPath, _pitchShadowPaint);
            canvas.DrawPath(_pitchHighPath, _pitchPaint);
            canvas.DrawPath(_pitchLowPath, _pitchLowPaint);
        }

        if (state.HarmonicFrequencies is { Length: > 0 } && state.ShowHarmonics)
        {
            int maxHarmonics = state.MaxHarmonics;
            if (maxHarmonics > 0)
            {
                int frames = Math.Min(state.FrameCount, state.HarmonicFrequencies.Length / maxHarmonics);
                int startFrame = _framePadFrames;
                int endFrame = Math.Min(frames, _framePadFrames + _frameAvailable);
                int step = GetOverlayStep(rect, frames);
                var displayMode = state.HarmonicDisplayMode;
                bool hasMagnitudes = state.HarmonicMagnitudes is { Length: > 0 }
                    && state.HarmonicMagnitudes.Length >= state.HarmonicFrequencies.Length;

                // Detection threshold: -40 dB relative to fundamental
                const float DetectionThresholdDb = -40f;

                for (int frame = startFrame; frame < endFrame; frame += step)
                {
                    if (!TryGetRingIndex(frame, out int ringIndex))
                    {
                        continue;
                    }

                    float x = rect.Left + rect.Width * frame / Math.Max(1, state.FrameCount - 1);
                    int offset = ringIndex * maxHarmonics;
                    for (int h = 0; h < maxHarmonics; h++)
                    {
                        float freq = state.HarmonicFrequencies[offset + h];
                        if (freq <= 0f)
                        {
                            continue;
                        }

                        float magDb = hasMagnitudes ? state.HarmonicMagnitudes![offset + h] : float.MinValue;
                        bool isDetected = magDb > DetectionThresholdDb;

                        // Determine which paint to use based on mode
                        SKPaint? paint = displayMode switch
                        {
                            HarmonicDisplayMode.Detected => isDetected ? _harmonicDetectedPaint : null,
                            HarmonicDisplayMode.Theoretical => _harmonicTheoreticalPaint,
                            HarmonicDisplayMode.Both => isDetected ? _harmonicDetectedPaint : _harmonicTheoreticalPaint,
                            _ => _harmonicPaint
                        };

                        if (paint is null)
                        {
                            continue;
                        }

                        float norm = FrequencyToNorm(state, freq);
                        float y = rect.Bottom - norm * rect.Height;

                        // Use larger radius for detected harmonics
                        float radius = isDetected ? 2.0f : 1.5f;
                        canvas.DrawCircle(x, y, radius, paint);
                    }
                }
            }
        }

        if (state.VoicingStates is { Length: > 0 } && state.ShowVoicing)
        {
            int frames = Math.Min(state.FrameCount, state.VoicingStates.Length);
            float laneTop = rect.Bottom - VoicingLaneHeight;
            int startFrame = _framePadFrames;
            int endFrame = Math.Min(frames, _framePadFrames + _frameAvailable);
            int step = GetOverlayStep(rect, frames);
            float frameWidth = rect.Width / Math.Max(1, state.FrameCount);
            for (int frame = startFrame; frame < endFrame; frame += step)
            {
                byte stateValue = 0;
                int segment = Math.Min(step, endFrame - frame);
                for (int i = 0; i < segment; i++)
                {
                    if (!TryGetRingIndex(frame + i, out int ringIndex))
                    {
                        continue;
                    }

                    byte next = state.VoicingStates[ringIndex];
                    if (next > stateValue)
                    {
                        stateValue = next;
                    }
                }
                SKPaint paint = stateValue switch
                {
                    2 => _voicedPaint,
                    1 => _unvoicedPaint,
                    _ => _silencePaint
                };
                float x = rect.Left + frameWidth * frame;
                float width = frameWidth * segment;
                canvas.DrawRect(new SKRect(x, laneTop, x + width, rect.Bottom), paint);
            }
        }
    }

    private void DrawReferenceLine(SKCanvas canvas, SKRect rect, SpectroState state)
    {
        if (state.ReferenceFrameId is null || state.AvailableFrames <= 0 || state.FrameCount <= 1)
        {
            return;
        }

        long latestFrame = state.LatestFrameId;
        long referenceFrame = state.ReferenceFrameId.Value;
        int availableFrames = state.AvailableFrames;
        int padFrames = Math.Max(0, state.FrameCount - availableFrames);
        long oldestFrame = latestFrame - availableFrames + 1;
        long offset = referenceFrame - oldestFrame;
        if (offset < 0 || offset >= availableFrames)
        {
            return;
        }

        int columnIndex = padFrames + (int)offset;
        float x = rect.Left + rect.Width * columnIndex / Math.Max(1, state.FrameCount - 1);
        canvas.DrawLine(x, rect.Top, x, rect.Bottom, _referencePaint);
    }

    private void DrawDiscontinuityMarkers(SKCanvas canvas, SKRect rect, SpectroState state)
    {
        if (state.Discontinuities is not { Count: > 0 } || state.AvailableFrames <= 0 || state.FrameCount <= 1)
        {
            return;
        }

        long latestFrame = state.LatestFrameId;
        int availableFrames = state.AvailableFrames;
        int padFrames = Math.Max(0, state.FrameCount - availableFrames);
        long oldestFrame = latestFrame - availableFrames + 1;

        foreach (var evt in state.Discontinuities)
        {
            long offset = evt.FrameId - oldestFrame;
            if (offset < 0 || offset >= availableFrames)
            {
                continue;
            }

            int columnIndex = padFrames + (int)offset;
            float x = rect.Left + rect.Width * columnIndex / Math.Max(1, state.FrameCount - 1);
            canvas.DrawLine(x, rect.Top, x, rect.Bottom, _discontinuityPaint);

            // Draw small indicator at top with short label
            float labelY = rect.Top + 10f;
            using var labelFont = new SKFont(SKTypeface.Default, 9f);
            using var labelPaint = new SKPaint
            {
                Color = _discontinuityPaint.Color,
                IsAntialias = true
            };
            canvas.DrawText(evt.ShortLabel, x + 3f, labelY, SKTextAlign.Left, labelFont, labelPaint);
        }
    }

    private void DrawPillButton(SKCanvas canvas, SKRect rect, string label, bool active)
    {
        canvas.DrawRoundRect(new SKRoundRect(rect, rect.Height / 2f), active ? _buttonActivePaint : _buttonPaint);
        canvas.DrawRoundRect(new SKRoundRect(rect, rect.Height / 2f), _borderPaint);
        canvas.DrawText(label, rect.MidX, rect.MidY + 4f, _buttonTextPaint);
    }

    private void DrawIconButton(SKCanvas canvas, SKRect rect, string label, SKColor color)
    {
        _iconPaint.Color = color;
        canvas.DrawText(label, rect.MidX, rect.MidY + 4f, _iconPaint);
    }

    private static float Normalize(float value, float min, float max)
    {
        if (max <= min)
        {
            return 0f;
        }
        return Math.Clamp((value - min) / (max - min), 0f, 1f);
    }

    /// <summary>
    /// Convert frequency to normalized Y position using the unified display mapping.
    /// Falls back to scale-based calculation if mapper not yet configured.
    /// </summary>
    private static float FrequencyToNorm(SpectroState state, float frequencyHz)
    {
        var mapper = state.FrequencyMapper;
        if (mapper is { IsConfigured: true }
            && mapper.ConfiguredScale == state.Scale
            && MathF.Abs(mapper.ConfiguredMinHz - state.MinFrequency) < 1f
            && MathF.Abs(mapper.ConfiguredMaxHz - state.MaxFrequency) < 1f)
        {
            return mapper.FrequencyToNormalizedY(frequencyHz);
        }

        // Fallback: use frequency scale directly when mapper not configured or config mismatch
        return FrequencyScaleUtils.Normalize(state.Scale, frequencyHz, state.MinFrequency, state.MaxFrequency);
    }

    private static readonly float[] AxisHzLabels = { 80f, 150f, 250f, 500f, 1000f, 2000f, 4000f, 8000f };
    private static readonly float[] AxisNoteLabels = { 65.4f, 130.8f, 261.6f, 523.3f, 1046.5f };
    private static readonly string[] NoteNames =
    {
        "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"
    };

    private static readonly float[] VocalZoneGuides = { 80f, 500f, 2000f, 4000f, 8000f };

    private static string FormatHz(float hz)
    {
        return hz >= 1000f ? $"{hz / 1000f:0.#}k" : $"{hz:0}";
    }

    private static string GetNoteName(float frequency)
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

    private static string FormatReassignLabel(SpectrogramReassignMode mode)
    {
        return mode switch
        {
            SpectrogramReassignMode.Frequency => "SYNC",
            SpectrogramReassignMode.Time => "R:T",
            SpectrogramReassignMode.TimeFrequency => "R:TF",
            _ => "R:Off"
        };
    }

    private static string FormatTransformLabel(SpectrogramTransformType transform)
    {
        return transform switch
        {
            SpectrogramTransformType.ZoomFft => "XF:Zoom",
            SpectrogramTransformType.Cqt => "XF:CQT",
            _ => "XF:FFT"
        };
    }

    private static string FormatPitchLabel(PitchDetectorType algorithm)
    {
        return algorithm switch
        {
            PitchDetectorType.Autocorrelation => "P:ACF",
            PitchDetectorType.Cepstral => "P:CEP",
            PitchDetectorType.Pyin => "P:pYIN",
            PitchDetectorType.Swipe => "P:SWP",
            _ => "P:YIN"
        };
    }

    private static string FormatAxisLabel(SpectrogramAxisMode mode)
    {
        return mode switch
        {
            SpectrogramAxisMode.Note => "AX:Note",
            SpectrogramAxisMode.Both => "AX:Both",
            _ => "AX:Hz"
        };
    }

    private static string FormatSmoothingLabel(SpectrogramSmoothingMode mode)
    {
        return mode switch
        {
            SpectrogramSmoothingMode.Ema => "SM:EMA",
            SpectrogramSmoothingMode.Bilateral => "SM:BIL",
            _ => "SM:Off"
        };
    }

    private static string FormatNormalizationLabel(SpectrogramNormalizationMode mode)
    {
        return mode switch
        {
            SpectrogramNormalizationMode.Peak => "N:Peak",
            SpectrogramNormalizationMode.Rms => "N:RMS",
            SpectrogramNormalizationMode.AWeighted => "N:A",
            _ => "N:Off"
        };
    }

    private static string FormatDynamicRangeLabel(SpectrogramDynamicRangeMode mode)
    {
        return mode switch
        {
            SpectrogramDynamicRangeMode.Full => "DR:Full",
            SpectrogramDynamicRangeMode.VoiceOptimized => "DR:Vox",
            SpectrogramDynamicRangeMode.Compressed => "DR:Cmp",
            SpectrogramDynamicRangeMode.NoiseFloor => "DR:Auto",
            _ => "DR:Man"
        };
    }

    private static string FormatVoiceRangeLabel(VocalRangeType range)
    {
        return range switch
        {
            VocalRangeType.Bass => "Bass",
            VocalRangeType.Baritone => "Bari",
            VocalRangeType.Tenor => "Tenor",
            VocalRangeType.Alto => "Alto",
            VocalRangeType.MezzoSoprano => "Mezzo",
            VocalRangeType.Soprano => "Sop",
            _ => "Vocal"
        };
    }

    private static string FormatClarityLabel(ClarityProcessingMode mode)
    {
        return mode switch
        {
            ClarityProcessingMode.Noise => "CLR:N",
            ClarityProcessingMode.Harmonic => "CLR:H",
            ClarityProcessingMode.Full => "CLR:All",
            _ => "CLR:Off"
        };
    }

    private static float Distance(SKPoint center, float x, float y)
    {
        float dx = center.X - x;
        float dy = center.Y - y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static bool RectEquals(SKRect a, SKRect b)
    {
        return MathF.Abs(a.Left - b.Left) < 1e-3f
            && MathF.Abs(a.Top - b.Top) < 1e-3f
            && MathF.Abs(a.Right - b.Right) < 1e-3f
            && MathF.Abs(a.Bottom - b.Bottom) < 1e-3f;
    }

    private static int GetOverlayStep(SKRect rect, int frameCount)
    {
        int pixels = Math.Max(1, (int)MathF.Round(rect.Width));
        if (frameCount <= pixels)
        {
            return 1;
        }

        return Math.Max(1, frameCount / pixels);
    }

    public bool TryGetSpectrogramRect(out SKRect rect)
    {
        rect = _spectrogramRect;
        return rect.Width > 0 && rect.Height > 0;
    }

    public SKRect GetPresetDropdownRect()
    {
        return _presetBar.GetDropdownRect();
    }

    public void Dispose()
    {
        foreach (var knob in _allKnobs)
        {
            knob.Dispose();
        }
        _presetBar.Dispose();
        _tooltip.Dispose();
        _panelHeaderPaint.Dispose();
        _backgroundPaint.Dispose();
        _panelPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _textPaint.Dispose();
        _mutedTextPaint.Dispose();
        _dropAlertPaint.Dispose();
        _profilingTextPaint.Dispose();
        _profilingDotPaint.Dispose();
        _profilingPanelPaint.Dispose();
        _gridPaint.Dispose();
        _buttonPaint.Dispose();
        _buttonActivePaint.Dispose();
        _buttonTextPaint.Dispose();
        _referencePaint.Dispose();
        _bitmapPaint.Dispose();
        _bloomAddPaint.Dispose();
        _pitchPaint.Dispose();
        _pitchShadowPaint.Dispose();
        _pitchLowPaint.Dispose();
        _overlayShadowPaint.Dispose();
        _harmonicPaint.Dispose();
        _harmonicDetectedPaint.Dispose();
        _harmonicTheoreticalPaint.Dispose();
        _voicedPaint.Dispose();
        _unvoicedPaint.Dispose();
        _silencePaint.Dispose();
        _spectrumPaint.Dispose();
        _spectrumFillPaint.Dispose();
        _spectrumPeakPaint.Dispose();
        _rangeBandPaint.Dispose();
        _guidePaint.Dispose();
        _discontinuityPaint.Dispose();
        _metricPaint.Dispose();
        _iconPaint.Dispose();
        _axisImagePaint.Dispose();
        _colorBarPaint.Dispose();
        _colorBarShader?.Dispose();
        _spectrumSlicePath.Dispose();
        _spectrumFillPath.Dispose();
        _pitchHighPath.Dispose();
        _pitchLowPath.Dispose();
        InvalidateAxisCache();
        _spectrogramBitmap?.Dispose();
    }
}

public enum SpectroHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    PresetDropdown,
    PresetSave,
    FftButton,
    TransformButton,
    WindowButton,
    OverlapButton,
    ScaleButton,
    AxisModeButton,
    ColorButton,
    ReassignButton,
    ClarityButton,
    PitchAlgorithmButton,
    SmoothingModeButton,
    PauseButton,
    PitchToggle,
    HarmonicToggle,
    HarmonicModeToggle,
    VoicingToggle,
    PreEmphasisToggle,
    HpfToggle,
    RangeToggle,
    GuidesToggle,
    VoiceRangeButton,
    NormalizationButton,
    DynamicRangeButton,
    SpectrumToggle,
    PitchMeterToggle,
    Spectrogram,
    Knob
}

public record struct SpectroHitTest(SpectroHitArea Area, int KnobIndex);

public readonly record struct SpectroUiTimingSnapshot(
    int TickUs,
    int CopyUs,
    int MapUs);

public record struct SpectroState(
    int FftSize,
    SpectrogramTransformType TransformType,
    WindowFunction WindowFunction,
    float Overlap,
    FrequencyScale Scale,
    float MinFrequency,
    float MaxFrequency,
    DisplayPipeline? FrequencyMapper,
    float MinDb,
    float MaxDb,
    float TimeWindowSeconds,
    int DisplayBins,
    int FrameCount,
    int ColorMap,
    SpectrogramReassignMode ReassignMode,
    ClarityProcessingMode ClarityMode,
    float ReassignThresholdDb,
    float ReassignSpread,
    float ClarityNoise,
    float ClarityHarmonic,
    float ClaritySmoothing,
    PitchDetectorType PitchAlgorithm,
    SpectrogramAxisMode AxisMode,
    VocalRangeType VoiceRange,
    bool ShowRange,
    bool ShowGuides,
    bool ShowWaveform,
    bool ShowSpectrum,
    bool ShowPitchMeter,
    SpectrogramSmoothingMode SmoothingMode,
    float Brightness,
    float Gamma,
    float Contrast,
    int ColorLevels,
    SpectrogramNormalizationMode NormalizationMode,
    SpectrogramDynamicRangeMode DynamicRangeMode,
    bool IsBypassed,
    bool IsPaused,
    bool UsingGpu,
    bool IsProfiling,
    SpectroTimingSnapshot AnalysisTiming,
    SpectroUiTimingSnapshot UiTiming,
    bool ShowPitch,
    bool ShowHarmonics,
    HarmonicDisplayMode HarmonicDisplayMode,
    bool ShowVoicing,
    bool PreEmphasisEnabled,
    bool HighPassEnabled,
    float HighPassCutoff,
    long LatestFrameId,
    int AvailableFrames,
    long DroppedHops,
    long? ReferenceFrameId,
    int DataVersion,
    string PresetName,
    float[]? Spectrogram,
    float[]? PitchTrack,
    float[]? PitchConfidence,
    byte[]? VoicingStates,
    float[]? HarmonicFrequencies,
    float[]? HarmonicMagnitudes,
    float[]? WaveformMin,
    float[]? WaveformMax,
    float[]? HnrTrack,
    float[]? CppTrack,
    float[]? SpectralCentroid,
    float[]? SpectralSlope,
    float[]? SpectralFlux,
    float[]? BinFrequencies,
    int MaxHarmonics,
    IReadOnlyList<DiscontinuityEvent>? Discontinuities,
    bool SpeechCoachEnabled,
    bool ShowSpeechMetrics,
    bool ShowSyllableMarkers,
    bool ShowPauseOverlay,
    bool ShowFillerMarkers,
    float WordsPerMinute,
    float ArticulationWpm,
    float PauseRatio,
    float MonotoneScore,
    float ClarityScore,
    float IntelligibilityScore,
    byte[]? SpeakingStateTrack,
    byte[]? SyllableMarkers);

public readonly record struct SpectroTimingSnapshot(
    int FrameUs,
    int PreprocessUs,
    int TransformUs,
    int NormalizationUs,
    int PitchUs,
    int ClarityUs,
    int ReassignUs,
    int FeaturesUs,
    int WritebackUs);
