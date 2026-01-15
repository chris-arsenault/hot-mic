using HotMic.Core.Dsp;
using SkiaSharp;
using System.Runtime.InteropServices;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Renderer for the Vocal Spectrograph plugin window.
/// </summary>
public sealed class VocalSpectrographRenderer : IDisposable
{
    private const float CornerRadius = 10f;
    private const float Padding = 14f;
    private const float TitleBarHeight = 36f;
    private const float ControlBarHeight = 86f;
    private const float KnobRadius = 24f;
    private const float AxisWidth = 50f;
    private const float ColorBarWidth = 22f;
    private const float TimeAxisHeight = 22f;
    private const float WaveformHeight = 80f;
    private const float AuxViewHeight = 110f;
    private const float KnobAreaHeight = 160f;
    private const float VoicingLaneHeight = 8f;

    private readonly PluginComponentTheme _theme;
    private readonly PluginPresetBar _presetBar;

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

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _panelPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _titlePaint;
    private readonly SKPaint _textPaint;
    private readonly SKPaint _mutedTextPaint;
    private readonly SKPaint _gridPaint;
    private readonly SKPaint _buttonPaint;
    private readonly SKPaint _buttonActivePaint;
    private readonly SKPaint _buttonTextPaint;
    private readonly SKPaint _referencePaint;
    private readonly SKPaint _bitmapPaint;
    private readonly SKPaint _pitchPaint;
    private readonly SKPaint _pitchLowPaint;
    private readonly SKPaint _formantPaint1;
    private readonly SKPaint _formantPaint2;
    private readonly SKPaint _formantPaint3;
    private readonly SKPaint _formantLinePaint1;
    private readonly SKPaint _formantLinePaint2;
    private readonly SKPaint _formantLinePaint3;
    private readonly SKPaint _harmonicPaint;
    private readonly SKPaint _voicedPaint;
    private readonly SKPaint _unvoicedPaint;
    private readonly SKPaint _silencePaint;
    private readonly SKPaint _waveformPaint;
    private readonly SKPaint _waveformFillPaint;
    private readonly SKPaint _waveformEnvelopePaint;
    private readonly SKPaint _waveformZeroPaint;
    private readonly SKPaint _spectrumPaint;
    private readonly SKPaint _spectrumPeakPaint;
    private readonly SKPaint _rangeBandPaint;
    private readonly SKPaint _guidePaint;
    private readonly SKPaint _metricPaint;
    private readonly SKPaint _vowelTrailPaint;
    private readonly SKPaint _iconPaint;
    private readonly SKPaint _axisImagePaint;
    private readonly SKPaint _colorBarPaint;
    private SKShader? _colorBarShader;
    private SKRect _colorBarRectCache;
    private int _colorBarMapCache = -1;

    private KnobWidget[] _allKnobs = null!; // Initialized in constructor

    private readonly SKPath _waveformEnvelopeTop = new();
    private readonly SKPath _waveformEnvelopeBottom = new();
    private readonly SKPath _spectrumSlicePath = new();
    private readonly SKPath _pitchHighPath = new();
    private readonly SKPath _pitchLowPath = new();
    private readonly SKPath _formantPath1 = new();
    private readonly SKPath _formantPath2 = new();
    private readonly SKPath _formantPath3 = new();
    private readonly SKPath _vowelTrailPath = new();
    private readonly int[] _peakBins = new int[3];
    private readonly float[] _peakValues = new float[3];

    private SKRect _titleBarRect;
    private SKRect _closeRect;
    private SKRect _bypassRect;
    private SKRect _fftRect;
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
    private SKRect _formantToggleRect;
    private SKRect _harmonicToggleRect;
    private SKRect _voicingToggleRect;
    private SKRect _preEmphasisToggleRect;
    private SKRect _hpfToggleRect;
    private SKRect _rangeToggleRect;
    private SKRect _guidesToggleRect;
    private SKRect _voiceRangeRect;
    private SKRect _normalizationRect;
    private SKRect _dynamicRangeRect;
    private SKRect _waveformToggleRect;
    private SKRect _spectrumToggleRect;
    private SKRect _pitchMeterToggleRect;
    private SKRect _vowelToggleRect;
    private SKRect _spectrogramRect;

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
    private bool _axisShowWaveformCache;
    private bool _axisShowSpectrumCache;
    private bool _axisShowPitchMeterCache;
    private bool _axisShowVowelSpaceCache;
    public VocalSpectrographRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _presetBar = new PluginPresetBar(_theme);

        // Initialize all KnobWidgets
        var knobStyle = KnobStyle.Compact;
        MinFreqKnob = new KnobWidget(KnobRadius, 20f, 2000f, "MIN FREQ", "Hz", knobStyle, _theme) { IsLogarithmic = true, ValueFormat = "0" };
        MaxFreqKnob = new KnobWidget(KnobRadius, 2000f, 12000f, "MAX FREQ", "Hz", knobStyle, _theme) { IsLogarithmic = true, ValueFormat = "0" };
        MinDbKnob = new KnobWidget(KnobRadius, -120f, -20f, "MIN dB", "dB", knobStyle, _theme) { ValueFormat = "0" };
        MaxDbKnob = new KnobWidget(KnobRadius, -40f, 0f, "MAX dB", "dB", knobStyle, _theme) { ValueFormat = "0" };
        TimeKnob = new KnobWidget(KnobRadius, 1f, 60f, "TIME", "s", knobStyle, _theme) { ValueFormat = "0.0" };
        HpfKnob = new KnobWidget(KnobRadius, 20f, 120f, "HPF", "Hz", knobStyle, _theme) { ValueFormat = "0" };
        ReassignThresholdKnob = new KnobWidget(KnobRadius, -120f, -20f, "R THRESH", "dB", knobStyle, _theme) { ValueFormat = "0" };
        ReassignSpreadKnob = new KnobWidget(KnobRadius, 0f, 100f, "R SPREAD", "%", knobStyle, _theme) { ValueFormat = "0" };
        ClarityNoiseKnob = new KnobWidget(KnobRadius, 0f, 100f, "NOISE", "%", knobStyle, _theme) { ValueFormat = "0" };
        ClarityHarmonicKnob = new KnobWidget(KnobRadius, 0f, 100f, "HARM", "%", knobStyle, _theme) { ValueFormat = "0" };
        ClaritySmoothingKnob = new KnobWidget(KnobRadius, 0f, 100f, "SMOOTH", "%", knobStyle, _theme) { ValueFormat = "0" };
        BrightnessKnob = new KnobWidget(KnobRadius, 0.5f, 2f, "BRIGHT", "x", knobStyle, _theme) { ValueFormat = "0.00" };
        GammaKnob = new KnobWidget(KnobRadius, 0.6f, 1.2f, "GAMMA", "", knobStyle, _theme) { ValueFormat = "0.00" };
        ContrastKnob = new KnobWidget(KnobRadius, 0.8f, 1.5f, "CONTRAST", "x", knobStyle, _theme) { ValueFormat = "0.00" };
        LevelsKnob = new KnobWidget(KnobRadius, 16f, 64f, "LEVELS", "", knobStyle, _theme) { ValueFormat = "0" };
        _allKnobs = new[] { MinFreqKnob, MaxFreqKnob, MinDbKnob, MaxDbKnob, TimeKnob, HpfKnob,
            ReassignThresholdKnob, ReassignSpreadKnob, ClarityNoiseKnob, ClarityHarmonicKnob,
            ClaritySmoothingKnob, BrightnessKnob, GammaKnob, ContrastKnob, LevelsKnob };

        _backgroundPaint = new SKPaint { Color = _theme.PanelBackground, IsAntialias = true, Style = SKPaintStyle.Fill };
        _panelPaint = new SKPaint { Color = _theme.PanelBackgroundLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        _borderPaint = new SKPaint { Color = _theme.PanelBorder, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        _titlePaint = new SKPaint
        {
            Color = _theme.TextPrimary,
            IsAntialias = true,
            TextSize = 14f,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };
        _textPaint = new SKPaint
        {
            Color = _theme.TextSecondary,
            IsAntialias = true,
            TextSize = 11f,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };
        _mutedTextPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            TextSize = 10f,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };
        _gridPaint = new SKPaint
        {
            Color = _theme.PanelBorder.WithAlpha(50),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };
        _buttonPaint = new SKPaint { Color = _theme.PanelBackgroundLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        _buttonActivePaint = new SKPaint { Color = _theme.KnobArc.WithAlpha(120), IsAntialias = true, Style = SKPaintStyle.Fill };
        _buttonTextPaint = new SKPaint
        {
            Color = _theme.TextPrimary,
            IsAntialias = true,
            TextSize = 10f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };
        _referencePaint = new SKPaint
        {
            Color = _theme.TextPrimary.WithAlpha(180),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f
        };
        _bitmapPaint = new SKPaint
        {
            FilterQuality = SKFilterQuality.None,
            IsAntialias = false
        };
        _pitchPaint = new SKPaint { Color = new SKColor(0x00, 0xFF, 0x88), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        _pitchLowPaint = new SKPaint
        {
            Color = new SKColor(0x00, 0xFF, 0x88, 0x80),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            PathEffect = SKPathEffect.CreateDash(new[] { 6f, 4f }, 0)
        };
        _formantPaint1 = new SKPaint { Color = new SKColor(0xFF, 0x4B, 0x4B), IsAntialias = true, Style = SKPaintStyle.Fill };
        _formantPaint2 = new SKPaint { Color = new SKColor(0xFF, 0x96, 0x2A), IsAntialias = true, Style = SKPaintStyle.Fill };
        _formantPaint3 = new SKPaint { Color = new SKColor(0xFF, 0xD0, 0x3A), IsAntialias = true, Style = SKPaintStyle.Fill };
        _formantLinePaint1 = new SKPaint { Color = new SKColor(0xFF, 0x4B, 0x4B, 0xB0), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
        _formantLinePaint2 = new SKPaint { Color = new SKColor(0xFF, 0x96, 0x2A, 0xB0), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
        _formantLinePaint3 = new SKPaint { Color = new SKColor(0xFF, 0xD0, 0x3A, 0xB0), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
        _harmonicPaint = new SKPaint { Color = new SKColor(0xE0, 0xE0, 0xE6, 0x90), IsAntialias = true, Style = SKPaintStyle.Fill };
        _voicedPaint = new SKPaint { Color = new SKColor(0x00, 0xD4, 0xAA, 0x50), Style = SKPaintStyle.Fill };
        _unvoicedPaint = new SKPaint { Color = new SKColor(0x80, 0x80, 0x90, 0x40), Style = SKPaintStyle.Fill };
        _silencePaint = new SKPaint { Color = new SKColor(0x00, 0x00, 0x00, 0x60), Style = SKPaintStyle.Fill };
        _waveformPaint = new SKPaint
        {
            Color = _theme.WaveformLine,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f
        };
        _waveformFillPaint = new SKPaint
        {
            Color = _theme.WaveformFill,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        _waveformEnvelopePaint = new SKPaint
        {
            Color = _theme.TextPrimary.WithAlpha(140),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.2f
        };
        _waveformZeroPaint = new SKPaint
        {
            Color = _theme.TextSecondary.WithAlpha(180),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };
        _spectrumPaint = new SKPaint
        {
            Color = _theme.AccentSecondary,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f
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
        _metricPaint = new SKPaint
        {
            Color = _theme.TextSecondary,
            IsAntialias = true,
            TextSize = 10f,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };
        _vowelTrailPaint = new SKPaint
        {
            Color = _theme.AccentSecondary.WithAlpha(160),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.2f
        };
        _iconPaint = new SKPaint
        {
            Color = _theme.TextPrimary,
            IsAntialias = true,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center
        };
        _axisImagePaint = new SKPaint
        {
            IsAntialias = false,
            FilterQuality = SKFilterQuality.None
        };
        _colorBarPaint = new SKPaint
        {
            IsAntialias = true
        };
        _colorBarRectCache = SKRect.Empty;
    }

    public static SKSize GetPreferredSize() => new(920, 820);

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, VocalSpectrographState state)
    {
        canvas.Clear(_theme.PanelBackground);
        canvas.Save();
        canvas.Scale(dpiScale);
        size = new SKSize(size.Width / dpiScale, size.Height / dpiScale);

        var outerRect = new SKRect(0, 0, size.Width, size.Height);
        canvas.DrawRoundRect(new SKRoundRect(outerRect, CornerRadius), _backgroundPaint);
        canvas.DrawRoundRect(new SKRoundRect(outerRect, CornerRadius), _borderPaint);

        DrawTitleBar(canvas, size, state);
        DrawControlBar(canvas, size, state);
        DrawSpectrogram(canvas, size, dpiScale, state);
        DrawKnobs(canvas, size, state);

        canvas.Restore();
    }

    public VocalSpectrographHitTest HitTest(float x, float y)
    {
        if (_closeRect.Contains(x, y))
        {
            return new VocalSpectrographHitTest(SpectrographHitArea.CloseButton, -1);
        }

        if (_bypassRect.Contains(x, y))
        {
            return new VocalSpectrographHitTest(SpectrographHitArea.BypassButton, -1);
        }

        var presetHit = _presetBar.HitTest(x, y);
        if (presetHit == PresetBarHitArea.Dropdown)
        {
            return new VocalSpectrographHitTest(SpectrographHitArea.PresetDropdown, -1);
        }
        if (presetHit == PresetBarHitArea.SaveButton)
        {
            return new VocalSpectrographHitTest(SpectrographHitArea.PresetSave, -1);
        }

        if (_titleBarRect.Contains(x, y))
        {
            return new VocalSpectrographHitTest(SpectrographHitArea.TitleBar, -1);
        }

        if (_fftRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.FftButton, -1);
        if (_windowRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.WindowButton, -1);
        if (_overlapRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.OverlapButton, -1);
        if (_scaleRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.ScaleButton, -1);
        if (_colorRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.ColorButton, -1);
        if (_reassignRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.ReassignButton, -1);
        if (_clarityRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.ClarityButton, -1);
        if (_pitchAlgoRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.PitchAlgorithmButton, -1);
        if (_axisModeRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.AxisModeButton, -1);
        if (_smoothingModeRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.SmoothingModeButton, -1);
        if (_pauseRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.PauseButton, -1);
        if (_pitchToggleRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.PitchToggle, -1);
        if (_formantToggleRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.FormantToggle, -1);
        if (_harmonicToggleRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.HarmonicToggle, -1);
        if (_voicingToggleRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.VoicingToggle, -1);
        if (_preEmphasisToggleRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.PreEmphasisToggle, -1);
        if (_hpfToggleRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.HpfToggle, -1);
        if (_rangeToggleRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.RangeToggle, -1);
        if (_guidesToggleRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.GuidesToggle, -1);
        if (_voiceRangeRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.VoiceRangeButton, -1);
        if (_normalizationRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.NormalizationButton, -1);
        if (_dynamicRangeRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.DynamicRangeButton, -1);
        if (_waveformToggleRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.WaveformToggle, -1);
        if (_spectrumToggleRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.SpectrumToggle, -1);
        if (_pitchMeterToggleRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.PitchMeterToggle, -1);
        if (_vowelToggleRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.VowelToggle, -1);
        if (_spectrogramRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.Spectrogram, -1);

        for (int i = 0; i < _allKnobs.Length; i++)
        {
            if (_allKnobs[i].HitTest(x, y))
            {
                return new VocalSpectrographHitTest(SpectrographHitArea.Knob, i);
            }
        }

        return new VocalSpectrographHitTest(SpectrographHitArea.None, -1);
    }

    private void DrawTitleBar(SKCanvas canvas, SKSize size, VocalSpectrographState state)
    {
        _titleBarRect = new SKRect(0, 0, size.Width, TitleBarHeight);
        canvas.DrawRect(_titleBarRect, _panelPaint);
        canvas.DrawLine(0, TitleBarHeight, size.Width, TitleBarHeight, _borderPaint);
        canvas.DrawText("Vocal Spectrograph", Padding, TitleBarHeight / 2f + 5f, _titlePaint);

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
    }

    private void DrawControlBar(SKCanvas canvas, SKSize size, VocalSpectrographState state)
    {
        float top = TitleBarHeight + Padding;
        var controlRect = new SKRect(Padding, top, size.Width - Padding, top + ControlBarHeight);
        canvas.DrawRoundRect(new SKRoundRect(controlRect, 6f), _panelPaint);

        float buttonWidth = 70f;
        float buttonHeight = 22f;
        float row1Y = controlRect.Top + 6f;
        float x = controlRect.Left + 8f;

        _fftRect = new SKRect(x, row1Y, x + buttonWidth, row1Y + buttonHeight);
        DrawPillButton(canvas, _fftRect, $"FFT {state.FftSize}", false);
        x = _fftRect.Right + 6f;

        _windowRect = new SKRect(x, row1Y, x + buttonWidth, row1Y + buttonHeight);
        DrawPillButton(canvas, _windowRect, state.WindowFunction.ToString(), false);
        x = _windowRect.Right + 6f;

        _overlapRect = new SKRect(x, row1Y, x + buttonWidth, row1Y + buttonHeight);
        DrawPillButton(canvas, _overlapRect, $"{state.Overlap * 100f:0.#}%", false);
        x = _overlapRect.Right + 6f;

        _scaleRect = new SKRect(x, row1Y, x + buttonWidth, row1Y + buttonHeight);
        DrawPillButton(canvas, _scaleRect, state.Scale.ToString(), false);
        x = _scaleRect.Right + 6f;

        _axisModeRect = new SKRect(x, row1Y, x + buttonWidth, row1Y + buttonHeight);
        DrawPillButton(canvas, _axisModeRect, FormatAxisLabel(state.AxisMode), false);
        x = _axisModeRect.Right + 6f;

        _colorRect = new SKRect(x, row1Y, x + buttonWidth, row1Y + buttonHeight);
        DrawPillButton(canvas, _colorRect, ((SpectrogramColorMap)state.ColorMap).ToString(), false);
        x = _colorRect.Right + 6f;

        _reassignRect = new SKRect(x, row1Y, x + buttonWidth, row1Y + buttonHeight);
        DrawPillButton(canvas, _reassignRect, FormatReassignLabel(state.ReassignMode),
            state.ReassignMode != SpectrogramReassignMode.Off);
        x = _reassignRect.Right + 6f;

        _clarityRect = new SKRect(x, row1Y, x + buttonWidth, row1Y + buttonHeight);
        DrawPillButton(canvas, _clarityRect, FormatClarityLabel(state.ClarityMode),
            state.ClarityMode != ClarityProcessingMode.None);
        x = _clarityRect.Right + 6f;

        _pitchAlgoRect = new SKRect(x, row1Y, x + buttonWidth, row1Y + buttonHeight);
        DrawPillButton(canvas, _pitchAlgoRect, FormatPitchLabel(state.PitchAlgorithm), false);
        x = _pitchAlgoRect.Right + 6f;

        _smoothingModeRect = new SKRect(x, row1Y, x + buttonWidth, row1Y + buttonHeight);
        DrawPillButton(canvas, _smoothingModeRect, FormatSmoothingLabel(state.SmoothingMode),
            state.SmoothingMode != SpectrogramSmoothingMode.Off);
        x = _smoothingModeRect.Right + 6f;

        _pauseRect = new SKRect(x, row1Y, x + buttonWidth, row1Y + buttonHeight);
        DrawPillButton(canvas, _pauseRect, state.IsPaused ? "PAUSE" : "RUN", state.IsPaused);

        float toggleWidth = 66f;
        float toggleHeight = 20f;
        float row2Y = row1Y + buttonHeight + 8f;
        float toggleX = controlRect.Left + 8f;

        _pitchToggleRect = new SKRect(toggleX, row2Y, toggleX + toggleWidth, row2Y + toggleHeight);
        DrawPillButton(canvas, _pitchToggleRect, "Pitch", state.ShowPitch);
        toggleX = _pitchToggleRect.Right + 6f;

        _formantToggleRect = new SKRect(toggleX, row2Y, toggleX + toggleWidth, row2Y + toggleHeight);
        DrawPillButton(canvas, _formantToggleRect, "Formant", state.ShowFormants);
        toggleX = _formantToggleRect.Right + 6f;

        _harmonicToggleRect = new SKRect(toggleX, row2Y, toggleX + toggleWidth, row2Y + toggleHeight);
        DrawPillButton(canvas, _harmonicToggleRect, "Harm", state.ShowHarmonics);
        toggleX = _harmonicToggleRect.Right + 6f;

        _voicingToggleRect = new SKRect(toggleX, row2Y, toggleX + toggleWidth, row2Y + toggleHeight);
        DrawPillButton(canvas, _voicingToggleRect, "Voice", state.ShowVoicing);
        toggleX = _voicingToggleRect.Right + 6f;

        _preEmphasisToggleRect = new SKRect(toggleX, row2Y, toggleX + toggleWidth, row2Y + toggleHeight);
        DrawPillButton(canvas, _preEmphasisToggleRect, "Emph", state.PreEmphasisEnabled);
        toggleX = _preEmphasisToggleRect.Right + 6f;

        _hpfToggleRect = new SKRect(toggleX, row2Y, toggleX + toggleWidth, row2Y + toggleHeight);
        DrawPillButton(canvas, _hpfToggleRect, "HPF", state.HighPassEnabled);
        toggleX = _hpfToggleRect.Right + 6f;

        _rangeToggleRect = new SKRect(toggleX, row2Y, toggleX + toggleWidth, row2Y + toggleHeight);
        DrawPillButton(canvas, _rangeToggleRect, "Range", state.ShowRange);
        toggleX = _rangeToggleRect.Right + 6f;

        _guidesToggleRect = new SKRect(toggleX, row2Y, toggleX + toggleWidth, row2Y + toggleHeight);
        DrawPillButton(canvas, _guidesToggleRect, "Guides", state.ShowGuides);
        toggleX = _guidesToggleRect.Right + 6f;

        _voiceRangeRect = new SKRect(toggleX, row2Y, toggleX + toggleWidth, row2Y + toggleHeight);
        DrawPillButton(canvas, _voiceRangeRect, FormatVoiceRangeLabel(state.VoiceRange), state.ShowRange);
        toggleX = _voiceRangeRect.Right + 6f;

        _normalizationRect = new SKRect(toggleX, row2Y, toggleX + toggleWidth, row2Y + toggleHeight);
        DrawPillButton(canvas, _normalizationRect, FormatNormalizationLabel(state.NormalizationMode),
            state.NormalizationMode != SpectrogramNormalizationMode.None);
        toggleX = _normalizationRect.Right + 6f;

        _dynamicRangeRect = new SKRect(toggleX, row2Y, toggleX + toggleWidth, row2Y + toggleHeight);
        DrawPillButton(canvas, _dynamicRangeRect, FormatDynamicRangeLabel(state.DynamicRangeMode),
            state.DynamicRangeMode != SpectrogramDynamicRangeMode.Custom);

        float row3Y = row2Y + toggleHeight + 6f;
        float viewWidth = 78f;
        float viewX = controlRect.Left + 8f;

        _waveformToggleRect = new SKRect(viewX, row3Y, viewX + viewWidth, row3Y + toggleHeight);
        DrawPillButton(canvas, _waveformToggleRect, "Wave", state.ShowWaveform);
        viewX = _waveformToggleRect.Right + 6f;

        _spectrumToggleRect = new SKRect(viewX, row3Y, viewX + viewWidth, row3Y + toggleHeight);
        DrawPillButton(canvas, _spectrumToggleRect, "Slice", state.ShowSpectrum);
        viewX = _spectrumToggleRect.Right + 6f;

        _pitchMeterToggleRect = new SKRect(viewX, row3Y, viewX + viewWidth, row3Y + toggleHeight);
        DrawPillButton(canvas, _pitchMeterToggleRect, "Meter", state.ShowPitchMeter);
        viewX = _pitchMeterToggleRect.Right + 6f;

        _vowelToggleRect = new SKRect(viewX, row3Y, viewX + viewWidth, row3Y + toggleHeight);
        DrawPillButton(canvas, _vowelToggleRect, "Vowel", state.ShowVowelSpace);
    }

    private void DrawSpectrogram(SKCanvas canvas, SKSize size, float dpiScale, VocalSpectrographState state)
    {
        float top = TitleBarHeight + Padding + ControlBarHeight + Padding;
        float waveformHeight = state.ShowWaveform ? WaveformHeight : 0f;
        if (waveformHeight > 0f)
        {
            var waveformRect = new SKRect(Padding + AxisWidth, top, size.Width - Padding - ColorBarWidth, top + waveformHeight);
            DrawWaveform(canvas, waveformRect, state);
            top = waveformRect.Bottom + Padding;
        }

        bool showAuxViews = state.ShowSpectrum || state.ShowPitchMeter || state.ShowVowelSpace;
        float auxHeight = showAuxViews ? AuxViewHeight : 0f;
        float bottom = size.Height - Padding - KnobAreaHeight - (showAuxViews ? Padding + auxHeight : 0f);

        var spectrumRect = new SKRect(Padding + AxisWidth, top, size.Width - Padding - ColorBarWidth, bottom);
        var axisRect = new SKRect(Padding, top, Padding + AxisWidth, bottom);
        var colorRect = new SKRect(spectrumRect.Right + 6f, top, spectrumRect.Right + 6f + ColorBarWidth, bottom);
        _spectrogramRect = spectrumRect;

        canvas.DrawRoundRect(new SKRoundRect(spectrumRect, 6f), _panelPaint);

        UpdateSpectrogramBitmap(state);
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
        DrawOverlays(canvas, spectrumRect, state);
        DrawReferenceLine(canvas, spectrumRect, state);

        if (showAuxViews)
        {
            var auxRect = new SKRect(Padding, spectrumRect.Bottom + Padding, size.Width - Padding, spectrumRect.Bottom + Padding + auxHeight);
            DrawAuxViews(canvas, auxRect, state);
        }
    }

    private void EnsureAxisLayer(SKSize size, float dpiScale, VocalSpectrographState state, SKRect spectrumRect, SKRect axisRect, SKRect colorRect)
    {
        int pixelWidth = (int)MathF.Ceiling(size.Width * dpiScale);
        int pixelHeight = (int)MathF.Ceiling(size.Height * dpiScale);
        bool sizeChanged = _axisInfo.Width != pixelWidth || _axisInfo.Height != pixelHeight;
        bool dpiChanged = MathF.Abs(_axisDpiScale - dpiScale) > 1e-3f;
        bool layoutChanged = state.ShowWaveform != _axisShowWaveformCache
            || state.ShowSpectrum != _axisShowSpectrumCache
            || state.ShowPitchMeter != _axisShowPitchMeterCache
            || state.ShowVowelSpace != _axisShowVowelSpaceCache;
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
        _axisShowWaveformCache = state.ShowWaveform;
        _axisShowSpectrumCache = state.ShowSpectrum;
        _axisShowPitchMeterCache = state.ShowPitchMeter;
        _axisShowVowelSpaceCache = state.ShowVowelSpace;
    }

    private void DrawAxisLayer(SKCanvas canvas)
    {
        if (_axisImage is null)
        {
            return;
        }

        canvas.DrawImage(_axisImage, _axisDrawRect, _axisImagePaint);
    }

    private void InvalidateAxisCache()
    {
        _axisImage?.Dispose();
        _axisImage = null;
        _axisSurface?.Dispose();
        _axisSurface = null;
    }

    private void DrawWaveform(SKCanvas canvas, SKRect rect, VocalSpectrographState state)
    {
        canvas.DrawRoundRect(new SKRoundRect(rect, 6f), _panelPaint);
        canvas.DrawLine(rect.Left, rect.MidY, rect.Right, rect.MidY, _guidePaint);
        canvas.DrawText("Waveform", rect.Left + 6f, rect.Top + 12f, _mutedTextPaint);

        if (state.WaveformMin is null || state.WaveformMax is null || state.WaveformMin.Length == 0)
        {
            return;
        }

        int frames = Math.Min(state.FrameCount, state.WaveformMin.Length);
        if (frames <= 1)
        {
            return;
        }

        int step = GetOverlayStep(rect, frames);
        float xStep = rect.Width / Math.Max(1, state.FrameCount - 1);
        float center = rect.MidY;
        float half = rect.Height * 0.45f;
        _waveformEnvelopeTop.Reset();
        _waveformEnvelopeBottom.Reset();
        bool envelopeStarted = false;

        for (int frame = 0; frame < frames; frame += step)
        {
            float min = state.WaveformMin[frame];
            float max = state.WaveformMax[frame];
            float x = rect.Left + xStep * frame;
            float y1 = center - max * half;
            float y2 = center - min * half;
            canvas.DrawLine(x, y1, x, y2, _waveformPaint);

            float envelope = MathF.Max(MathF.Abs(min), MathF.Abs(max));
            float yEnvTop = center - envelope * half;
            float yEnvBottom = center + envelope * half;
            if (!envelopeStarted)
            {
                _waveformEnvelopeTop.MoveTo(x, yEnvTop);
                _waveformEnvelopeBottom.MoveTo(x, yEnvBottom);
                envelopeStarted = true;
            }
            else
            {
                _waveformEnvelopeTop.LineTo(x, yEnvTop);
                _waveformEnvelopeBottom.LineTo(x, yEnvBottom);
            }

            if (min < 0f && max > 0f)
            {
                canvas.DrawLine(x, center - 3f, x, center + 3f, _waveformZeroPaint);
            }
        }

        if (envelopeStarted)
        {
            canvas.DrawPath(_waveformEnvelopeTop, _waveformEnvelopePaint);
            canvas.DrawPath(_waveformEnvelopeBottom, _waveformEnvelopePaint);
        }
    }

    private void DrawVocalRangeBand(SKCanvas canvas, SKRect rect, VocalSpectrographState state)
    {
        var (minHz, maxHz) = VocalRangeInfo.GetFundamentalRange(state.VoiceRange);
        if (maxHz < state.MinFrequency || minHz > state.MaxFrequency)
        {
            return;
        }

        float clampedMin = MathF.Max(minHz, state.MinFrequency);
        float clampedMax = MathF.Min(maxHz, state.MaxFrequency);
        float normMin = FrequencyScaleUtils.Normalize(state.Scale, clampedMin, state.MinFrequency, state.MaxFrequency);
        float normMax = FrequencyScaleUtils.Normalize(state.Scale, clampedMax, state.MinFrequency, state.MaxFrequency);
        float y1 = rect.Bottom - normMin * rect.Height;
        float y2 = rect.Bottom - normMax * rect.Height;
        float top = MathF.Min(y1, y2);
        float bottom = MathF.Max(y1, y2);
        canvas.DrawRect(new SKRect(rect.Left, top, rect.Right, bottom), _rangeBandPaint);
        canvas.DrawText(VocalRangeInfo.GetLabel(state.VoiceRange), rect.Left + 6f, top + 12f, _mutedTextPaint);
    }

    private void DrawFrequencyGuides(SKCanvas canvas, SKRect rect, VocalSpectrographState state)
    {
        foreach (float freq in VocalZoneGuides)
        {
            if (freq < state.MinFrequency || freq > state.MaxFrequency)
            {
                continue;
            }

            float norm = FrequencyScaleUtils.Normalize(state.Scale, freq, state.MinFrequency, state.MaxFrequency);
            float y = rect.Bottom - norm * rect.Height;
            canvas.DrawLine(rect.Left, y, rect.Right, y, _guidePaint);
        }
    }

    private void DrawAuxViews(SKCanvas canvas, SKRect rect, VocalSpectrographState state)
    {
        int panelCount = 0;
        if (state.ShowSpectrum) panelCount++;
        if (state.ShowPitchMeter) panelCount++;
        if (state.ShowVowelSpace) panelCount++;

        if (panelCount == 0)
        {
            return;
        }

        float gap = 10f;
        float panelWidth = (rect.Width - gap * (panelCount - 1)) / panelCount;
        float x = rect.Left;
        int frameIndex = GetSliceFrameIndex(state);

        if (state.ShowSpectrum)
        {
            var panel = new SKRect(x, rect.Top, x + panelWidth, rect.Bottom);
            DrawSpectrumSlice(canvas, panel, state, frameIndex);
            x = panel.Right + gap;
        }

        if (state.ShowPitchMeter)
        {
            var panel = new SKRect(x, rect.Top, x + panelWidth, rect.Bottom);
            DrawPitchMeter(canvas, panel, state, frameIndex);
            x = panel.Right + gap;
        }

        if (state.ShowVowelSpace)
        {
            var panel = new SKRect(x, rect.Top, x + panelWidth, rect.Bottom);
            DrawVowelSpace(canvas, panel, state, frameIndex);
        }
    }

    private void DrawSpectrumSlice(SKCanvas canvas, SKRect rect, VocalSpectrographState state, int frameIndex)
    {
        canvas.DrawRoundRect(new SKRoundRect(rect, 6f), _panelPaint);
        canvas.DrawText("Spectrum", rect.Left + 6f, rect.Top + 12f, _mutedTextPaint);

        if (state.Spectrogram is null || state.BinFrequencies is null || frameIndex < 0)
        {
            return;
        }

        int bins = state.DisplayBins;
        int offset = frameIndex * bins;
        if (offset < 0 || offset + bins > state.Spectrogram.Length)
        {
            return;
        }

        float minDb = state.MinDb;
        float maxDb = state.MaxDb;
        float dbRange = MathF.Max(1f, maxDb - minDb);
        float freqRange = MathF.Max(1f, state.MaxFrequency - state.MinFrequency);

        _spectrumSlicePath.Reset();
        bool started = false;
        for (int i = 0; i < bins && i < state.BinFrequencies.Length; i++)
        {
            float freq = state.BinFrequencies[i];
            if (freq < state.MinFrequency || freq > state.MaxFrequency)
            {
                continue;
            }

            float normX = (freq - state.MinFrequency) / freqRange;
            float value = state.Spectrogram[offset + i];
            float db = minDb + value * dbRange;
            float normY = (db - minDb) / dbRange;

            float x = rect.Left + rect.Width * normX;
            float y = rect.Bottom - rect.Height * normY;

            if (!started)
            {
                _spectrumSlicePath.MoveTo(x, y);
                started = true;
            }
            else
            {
                _spectrumSlicePath.LineTo(x, y);
            }
        }

        if (!_spectrumSlicePath.IsEmpty)
        {
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

    private void DrawPitchMeter(SKCanvas canvas, SKRect rect, VocalSpectrographState state, int frameIndex)
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

    private void DrawVowelSpace(SKCanvas canvas, SKRect rect, VocalSpectrographState state, int frameIndex)
    {
        canvas.DrawRoundRect(new SKRoundRect(rect, 6f), _panelPaint);
        canvas.DrawText("Vowel Space", rect.Left + 6f, rect.Top + 12f, _mutedTextPaint);

        float f1 = 0f;
        float f2 = 0f;
        if (state.FormantFrequencies is { Length: > 0 } && frameIndex >= 0)
        {
            int offset = frameIndex * state.MaxFormants;
            if (offset + 1 < state.FormantFrequencies.Length)
            {
                f1 = state.FormantFrequencies[offset];
                f2 = state.FormantFrequencies[offset + 1];
            }
        }

        float f1Min = 250f;
        float f1Max = 1000f;
        float f2Min = 700f;
        float f2Max = 2500f;

        var plotRect = new SKRect(rect.Left + 8f, rect.Top + 20f, rect.Right - 8f, rect.Bottom - 8f);
        canvas.DrawRect(plotRect, _guidePaint);

        foreach (var vowel in VowelReferencePoints)
        {
            float xNorm = (vowel.F2 - f2Min) / (f2Max - f2Min);
            float yNorm = (vowel.F1 - f1Min) / (f1Max - f1Min);
            float x = plotRect.Left + plotRect.Width * xNorm;
            float y = plotRect.Bottom - plotRect.Height * yNorm;
            canvas.DrawCircle(x, y, 2.5f, _spectrumPeakPaint);
            canvas.DrawText(vowel.Label, x + 3f, y - 3f, _mutedTextPaint);
        }

        if (state.FormantFrequencies is { Length: > 0 } && state.MaxFormants > 1)
        {
            int frames = Math.Min(state.FrameCount, state.FormantFrequencies.Length / state.MaxFormants);
            int trailFrames = Math.Min(frames, 60);
            int start = Math.Max(0, frames - trailFrames);
            _vowelTrailPath.Reset();
            bool started = false;
            for (int frame = start; frame < frames; frame++)
            {
                int offset = frame * state.MaxFormants;
                if (offset + 1 >= state.FormantFrequencies.Length)
                {
                    break;
                }

                float tf1 = state.FormantFrequencies[offset];
                float tf2 = state.FormantFrequencies[offset + 1];
                if (tf1 <= 0f || tf2 <= 0f)
                {
                    started = false;
                    continue;
                }

                float xNorm = (tf2 - f2Min) / (f2Max - f2Min);
                float yNorm = (tf1 - f1Min) / (f1Max - f1Min);
                float x = plotRect.Left + plotRect.Width * xNorm;
                float y = plotRect.Bottom - plotRect.Height * yNorm;
                if (!started)
                {
                    _vowelTrailPath.MoveTo(x, y);
                    started = true;
                }
                else
                {
                    _vowelTrailPath.LineTo(x, y);
                }
            }

            if (!_vowelTrailPath.IsEmpty)
            {
                canvas.DrawPath(_vowelTrailPath, _vowelTrailPaint);
            }
        }

        if (f1 > 0f && f2 > 0f)
        {
            float xNorm = (f2 - f2Min) / (f2Max - f2Min);
            float yNorm = (f1 - f1Min) / (f1Max - f1Min);
            float x = plotRect.Left + plotRect.Width * xNorm;
            float y = plotRect.Bottom - plotRect.Height * yNorm;
            canvas.DrawCircle(x, y, 4f, _pitchPaint);
            canvas.DrawText($"F1 {f1:0}", plotRect.Left + 4f, plotRect.Bottom - 6f, _metricPaint);
            canvas.DrawText($"F2 {f2:0}", plotRect.Left + 60f, plotRect.Bottom - 6f, _metricPaint);
        }
    }

    private static int GetSliceFrameIndex(VocalSpectrographState state)
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

    private static float GetFrameValue(float[]? values, int frameIndex)
    {
        if (values is null || frameIndex < 0 || frameIndex >= values.Length)
        {
            return 0f;
        }

        return values[frameIndex];
    }

    private void DrawKnobs(SKCanvas canvas, SKSize size, VocalSpectrographState state)
    {
        float knobsTop = size.Height - Padding - KnobAreaHeight;
        int total = _allKnobs.Length;
        int columns = 6;
        float rowSpacing = 46f;
        int rows = (total + columns - 1) / columns;

        // Calculate knob positions
        for (int row = 0; row < rows; row++)
        {
            int rowStart = row * columns;
            int rowCount = Math.Min(columns, total - rowStart);
            float knobSpacing = (size.Width - Padding * 2f) / (rowCount + 1);
            float y = knobsTop + KnobRadius + row * rowSpacing;
            for (int col = 0; col < rowCount; col++)
            {
                int index = rowStart + col;
                _allKnobs[index].Center = new SKPoint(Padding + knobSpacing * (col + 1), y);
            }
        }

        // Update knob values from state and render
        MinFreqKnob.Value = state.MinFrequency;
        MinFreqKnob.Render(canvas);

        MaxFreqKnob.Value = state.MaxFrequency;
        MaxFreqKnob.Render(canvas);

        MinDbKnob.Value = state.MinDb;
        MinDbKnob.Render(canvas);

        MaxDbKnob.Value = state.MaxDb;
        MaxDbKnob.Render(canvas);

        TimeKnob.Value = state.TimeWindowSeconds;
        TimeKnob.Render(canvas);

        HpfKnob.Value = state.HighPassCutoff;
        HpfKnob.Render(canvas);

        ReassignThresholdKnob.Value = state.ReassignThresholdDb;
        ReassignThresholdKnob.Render(canvas);

        // ReassignSpread comes in as 0-1, but knob expects 0-100 (%)
        ReassignSpreadKnob.Value = state.ReassignSpread * 100f;
        ReassignSpreadKnob.Render(canvas);

        // ClarityNoise comes in as 0-1, but knob expects 0-100 (%)
        ClarityNoiseKnob.Value = state.ClarityNoise * 100f;
        ClarityNoiseKnob.Render(canvas);

        // ClarityHarmonic comes in as 0-1, but knob expects 0-100 (%)
        ClarityHarmonicKnob.Value = state.ClarityHarmonic * 100f;
        ClarityHarmonicKnob.Render(canvas);

        // ClaritySmoothing comes in as 0-1, but knob expects 0-100 (%)
        ClaritySmoothingKnob.Value = state.ClaritySmoothing * 100f;
        ClaritySmoothingKnob.Render(canvas);

        BrightnessKnob.Value = state.Brightness;
        BrightnessKnob.Render(canvas);

        GammaKnob.Value = state.Gamma;
        GammaKnob.Render(canvas);

        ContrastKnob.Value = state.Contrast;
        ContrastKnob.Render(canvas);

        LevelsKnob.Value = state.ColorLevels;
        LevelsKnob.Render(canvas);
    }

    private void UpdateSpectrogramBitmap(VocalSpectrographState state)
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

        if (_bitmapRingStart <= 0 || _bitmapRingStart >= _bitmapWidth)
        {
            canvas.DrawBitmap(_spectrogramBitmap, destRect, _bitmapPaint);
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
    }

    private void RenderFullSpectrogram(VocalSpectrographState state, int width, int bins)
    {
        int totalFrames = width;
        for (int frame = 0; frame < totalFrames; frame++)
        {
            int frameOffset = frame * bins;
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

    private void UpdateSpectrogramColumns(VocalSpectrographState state, int width, int bins, int deltaFrames, int ringStart)
    {
        if (deltaFrames <= 0)
        {
            return;
        }

        int startColumn = Math.Max(0, width - deltaFrames);
        for (int frame = startColumn; frame < width; frame++)
        {
            int frameOffset = frame * bins;
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

    private void DrawFrequencyAxis(SKCanvas canvas, SKRect axisRect, VocalSpectrographState state)
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

            float norm = FrequencyScaleUtils.Normalize(state.Scale, freq, state.MinFrequency, state.MaxFrequency);
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

    private void DrawColorBar(SKCanvas canvas, SKRect rect, VocalSpectrographState state)
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

    private void DrawTimeAxis(SKCanvas canvas, SKRect rect, VocalSpectrographState state)
    {
        float bottom = rect.Bottom + 6f;
        canvas.DrawLine(rect.Left, rect.Bottom, rect.Right, rect.Bottom, _gridPaint);
        canvas.DrawText("0s", rect.Left, bottom + 12f, _mutedTextPaint);
        canvas.DrawText($"{state.TimeWindowSeconds:0.0}s", rect.Right - 24f, bottom + 12f, _mutedTextPaint);
    }

    private void DrawOverlays(SKCanvas canvas, SKRect rect, VocalSpectrographState state)
    {
        if (state.PitchTrack is { Length: > 0 } && state.ShowPitch)
        {
            int frames = Math.Min(state.FrameCount, state.PitchTrack.Length);
            int step = GetOverlayStep(rect, frames);
            _pitchHighPath.Reset();
            _pitchLowPath.Reset();
            bool highStarted = false;
            bool lowStarted = false;

            for (int frame = 0; frame < frames; frame += step)
            {
                float pitch = state.PitchTrack[frame];
                if (pitch <= 0f)
                {
                    highStarted = false;
                    lowStarted = false;
                    continue;
                }

                float confidence = state.PitchConfidence is { Length: > 0 } ? state.PitchConfidence[frame] : 1f;
                bool high = confidence >= 0.6f;

                float norm = FrequencyScaleUtils.Normalize(state.Scale, pitch, state.MinFrequency, state.MaxFrequency);
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

            canvas.DrawPath(_pitchHighPath, _pitchPaint);
            canvas.DrawPath(_pitchLowPath, _pitchLowPaint);
        }

        if (state.FormantFrequencies is { Length: > 0 } && state.ShowFormants)
        {
            int maxFormants = state.MaxFormants;
            if (maxFormants > 0)
            {
                int frames = Math.Min(state.FrameCount, state.FormantFrequencies.Length / maxFormants);
                int step = GetOverlayStep(rect, frames);

                int trackCount = Math.Min(3, maxFormants);
                _formantPath1.Reset();
                _formantPath2.Reset();
                _formantPath3.Reset();
                bool started1 = false;
                bool started2 = false;
                bool started3 = false;

                for (int frame = 0; frame < frames; frame += step)
                {
                    float x = rect.Left + rect.Width * frame / Math.Max(1, state.FrameCount - 1);
                    int offset = frame * maxFormants;

                    if (trackCount > 0)
                    {
                        float freq = state.FormantFrequencies[offset];
                        if (freq > 0f)
                        {
                            float norm = FrequencyScaleUtils.Normalize(state.Scale, freq, state.MinFrequency, state.MaxFrequency);
                            float y = rect.Bottom - norm * rect.Height;
                            if (!started1)
                            {
                                _formantPath1.MoveTo(x, y);
                                started1 = true;
                            }
                            else
                            {
                                _formantPath1.LineTo(x, y);
                            }
                        }
                        else
                        {
                            started1 = false;
                        }
                    }

                    if (trackCount > 1)
                    {
                        float freq = state.FormantFrequencies[offset + 1];
                        if (freq > 0f)
                        {
                            float norm = FrequencyScaleUtils.Normalize(state.Scale, freq, state.MinFrequency, state.MaxFrequency);
                            float y = rect.Bottom - norm * rect.Height;
                            if (!started2)
                            {
                                _formantPath2.MoveTo(x, y);
                                started2 = true;
                            }
                            else
                            {
                                _formantPath2.LineTo(x, y);
                            }
                        }
                        else
                        {
                            started2 = false;
                        }
                    }

                    if (trackCount > 2)
                    {
                        float freq = state.FormantFrequencies[offset + 2];
                        if (freq > 0f)
                        {
                            float norm = FrequencyScaleUtils.Normalize(state.Scale, freq, state.MinFrequency, state.MaxFrequency);
                            float y = rect.Bottom - norm * rect.Height;
                            if (!started3)
                            {
                                _formantPath3.MoveTo(x, y);
                                started3 = true;
                            }
                            else
                            {
                                _formantPath3.LineTo(x, y);
                            }
                        }
                        else
                        {
                            started3 = false;
                        }
                    }
                }

                if (!_formantPath1.IsEmpty)
                {
                    canvas.DrawPath(_formantPath1, _formantLinePaint1);
                }
                if (!_formantPath2.IsEmpty)
                {
                    canvas.DrawPath(_formantPath2, _formantLinePaint2);
                }
                if (!_formantPath3.IsEmpty)
                {
                    canvas.DrawPath(_formantPath3, _formantLinePaint3);
                }

                for (int frame = 0; frame < frames; frame += step)
                {
                    float x = rect.Left + rect.Width * frame / Math.Max(1, state.FrameCount - 1);
                    int offset = frame * maxFormants;
                    for (int f = 0; f < maxFormants; f++)
                    {
                        float freq = state.FormantFrequencies[offset + f];
                        if (freq <= 0f)
                        {
                            continue;
                        }
                        float norm = FrequencyScaleUtils.Normalize(state.Scale, freq, state.MinFrequency, state.MaxFrequency);
                        float y = rect.Bottom - norm * rect.Height;
                        var paint = f switch
                        {
                            0 => _formantPaint1,
                            1 => _formantPaint2,
                            _ => _formantPaint3
                        };
                        canvas.DrawCircle(x, y, 2.5f, paint);

                        if (state.FormantBandwidths is { Length: > 0 })
                        {
                            float bw = state.FormantBandwidths[offset + f];
                            if (bw > 1f)
                            {
                                float low = freq - bw * 0.5f;
                                float high = freq + bw * 0.5f;
                                float lowNorm = FrequencyScaleUtils.Normalize(state.Scale, low, state.MinFrequency, state.MaxFrequency);
                                float highNorm = FrequencyScaleUtils.Normalize(state.Scale, high, state.MinFrequency, state.MaxFrequency);
                                float y1 = rect.Bottom - lowNorm * rect.Height;
                                float y2 = rect.Bottom - highNorm * rect.Height;
                                canvas.DrawLine(x, y1, x, y2, paint);
                            }
                        }
                    }
                }
            }
        }

        if (state.HarmonicFrequencies is { Length: > 0 } && state.ShowHarmonics)
        {
            int maxHarmonics = state.MaxHarmonics;
            if (maxHarmonics > 0)
            {
                int frames = Math.Min(state.FrameCount, state.HarmonicFrequencies.Length / maxHarmonics);
                int step = GetOverlayStep(rect, frames);
                for (int frame = 0; frame < frames; frame += step)
                {
                    float x = rect.Left + rect.Width * frame / Math.Max(1, state.FrameCount - 1);
                    int offset = frame * maxHarmonics;
                    for (int h = 0; h < maxHarmonics; h++)
                    {
                        float freq = state.HarmonicFrequencies[offset + h];
                        if (freq <= 0f)
                        {
                            continue;
                        }
                        float norm = FrequencyScaleUtils.Normalize(state.Scale, freq, state.MinFrequency, state.MaxFrequency);
                        float y = rect.Bottom - norm * rect.Height;
                        canvas.DrawCircle(x, y, 1.5f, _harmonicPaint);
                    }
                }
            }
        }

        if (state.VoicingStates is { Length: > 0 } && state.ShowVoicing)
        {
            int frames = Math.Min(state.FrameCount, state.VoicingStates.Length);
            float laneTop = rect.Bottom - VoicingLaneHeight;
            int step = GetOverlayStep(rect, frames);
            float frameWidth = rect.Width / Math.Max(1, state.FrameCount);
            for (int frame = 0; frame < frames; frame += step)
            {
                byte stateValue = state.VoicingStates[frame];
                for (int i = 1; i < step && frame + i < frames; i++)
                {
                    byte next = state.VoicingStates[frame + i];
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
                float width = frameWidth * step;
                canvas.DrawRect(new SKRect(x, laneTop, x + width, rect.Bottom), paint);
            }
        }
    }

    private void DrawReferenceLine(SKCanvas canvas, SKRect rect, VocalSpectrographState state)
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

    private static readonly float[] AxisHzLabels = { 80f, 150f, 250f, 500f, 1000f, 2000f, 4000f, 8000f };
    private static readonly float[] AxisNoteLabels = { 65.4f, 130.8f, 261.6f, 523.3f, 1046.5f };
    private static readonly string[] NoteNames =
    {
        "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"
    };

    private static readonly (string Label, float F1, float F2)[] VowelReferencePoints =
    {
        ("i", 300f, 2200f),
        ("u", 350f, 800f),
        ("e", 400f, 2000f),
        ("o", 500f, 1000f),
        ("ae", 700f, 1800f),
        ("a", 800f, 1200f)
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
        _backgroundPaint.Dispose();
        _panelPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _textPaint.Dispose();
        _mutedTextPaint.Dispose();
        _gridPaint.Dispose();
        _buttonPaint.Dispose();
        _buttonActivePaint.Dispose();
        _buttonTextPaint.Dispose();
        _referencePaint.Dispose();
        _bitmapPaint.Dispose();
        _pitchPaint.Dispose();
        _pitchLowPaint.Dispose();
        _formantPaint1.Dispose();
        _formantPaint2.Dispose();
        _formantPaint3.Dispose();
        _formantLinePaint1.Dispose();
        _formantLinePaint2.Dispose();
        _formantLinePaint3.Dispose();
        _harmonicPaint.Dispose();
        _voicedPaint.Dispose();
        _unvoicedPaint.Dispose();
        _silencePaint.Dispose();
        _waveformPaint.Dispose();
        _waveformFillPaint.Dispose();
        _waveformEnvelopePaint.Dispose();
        _waveformZeroPaint.Dispose();
        _spectrumPaint.Dispose();
        _spectrumPeakPaint.Dispose();
        _rangeBandPaint.Dispose();
        _guidePaint.Dispose();
        _metricPaint.Dispose();
        _vowelTrailPaint.Dispose();
        _iconPaint.Dispose();
        _axisImagePaint.Dispose();
        _colorBarPaint.Dispose();
        _colorBarShader?.Dispose();
        _waveformEnvelopeTop.Dispose();
        _waveformEnvelopeBottom.Dispose();
        _spectrumSlicePath.Dispose();
        _pitchHighPath.Dispose();
        _pitchLowPath.Dispose();
        _formantPath1.Dispose();
        _formantPath2.Dispose();
        _formantPath3.Dispose();
        _vowelTrailPath.Dispose();
        InvalidateAxisCache();
        _spectrogramBitmap?.Dispose();
    }
}

public enum SpectrographHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    PresetDropdown,
    PresetSave,
    FftButton,
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
    FormantToggle,
    HarmonicToggle,
    VoicingToggle,
    PreEmphasisToggle,
    HpfToggle,
    RangeToggle,
    GuidesToggle,
    VoiceRangeButton,
    NormalizationButton,
    DynamicRangeButton,
    WaveformToggle,
    SpectrumToggle,
    PitchMeterToggle,
    VowelToggle,
    Spectrogram,
    Knob
}

public record struct VocalSpectrographHitTest(SpectrographHitArea Area, int KnobIndex);

public record struct VocalSpectrographState(
    int FftSize,
    WindowFunction WindowFunction,
    float Overlap,
    FrequencyScale Scale,
    float MinFrequency,
    float MaxFrequency,
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
    bool ShowVowelSpace,
    SpectrogramSmoothingMode SmoothingMode,
    float Brightness,
    float Gamma,
    float Contrast,
    int ColorLevels,
    SpectrogramNormalizationMode NormalizationMode,
    SpectrogramDynamicRangeMode DynamicRangeMode,
    bool IsBypassed,
    bool IsPaused,
    bool ShowPitch,
    bool ShowFormants,
    bool ShowHarmonics,
    bool ShowVoicing,
    bool PreEmphasisEnabled,
    bool HighPassEnabled,
    float HighPassCutoff,
    long LatestFrameId,
    int AvailableFrames,
    long? ReferenceFrameId,
    int DataVersion,
    string PresetName,
    float[]? Spectrogram,
    float[]? PitchTrack,
    float[]? PitchConfidence,
    float[]? FormantFrequencies,
    float[]? FormantBandwidths,
    byte[]? VoicingStates,
    float[]? HarmonicFrequencies,
    float[]? WaveformMin,
    float[]? WaveformMax,
    float[]? HnrTrack,
    float[]? CppTrack,
    float[]? SpectralCentroid,
    float[]? SpectralSlope,
    float[]? SpectralFlux,
    float[]? BinFrequencies,
    int MaxFormants,
    int MaxHarmonics);
