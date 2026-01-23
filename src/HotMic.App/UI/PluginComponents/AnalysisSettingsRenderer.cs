using System;
using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Renderer for the Analysis Settings window.
/// Displays configurable analysis parameters in a compact panel layout.
/// </summary>
public sealed class AnalysisSettingsRenderer : IDisposable
{
    private const float Padding = 12f;
    private const float TitleBarHeight = 40f;
    private const float PanelSpacing = 10f;
    private const float PanelPadding = 10f;
    private const float PanelHeaderHeight = 22f;
    private const float KnobRadius = 18f;
    private const float ButtonHeight = 22f;
    private const float ButtonSpacing = 4f;
    private const float RowSpacing = 8f;
    private const float LabelHeight = 14f;

    private readonly PluginComponentTheme _theme;
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _panelPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _buttonPaint;
    private readonly SKPaint _buttonActivePaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _closeButtonPaint;
    private readonly SkiaTextPaint _panelHeaderPaint;
    private readonly SkiaTextPaint _buttonTextPaint;
    private readonly SkiaTextPaint _labelPaint;

    // Knobs
    public KnobWidget MinFreqKnob { get; }
    public KnobWidget MaxFreqKnob { get; }
    public KnobWidget TimeKnob { get; }
    public KnobWidget HpfKnob { get; }
    public KnobWidget ReassignThresholdKnob { get; }
    public KnobWidget ReassignSpreadKnob { get; }
    public KnobWidget ClarityNoiseKnob { get; }
    public KnobWidget ClarityHarmonicKnob { get; }
    public KnobWidget ClaritySmoothingKnob { get; }

    public IReadOnlyList<KnobWidget> AllKnobs { get; }

    // Button hit rects
    public SKRect[] FftSizeButtonRects { get; } = new SKRect[4];
    public SKRect[] OverlapButtonRects { get; } = new SKRect[5];
    public SKRect[] WindowButtonRects { get; } = new SKRect[5];
    public SKRect[] ScaleButtonRects { get; } = new SKRect[5];
    public SKRect[] TransformButtonRects { get; } = new SKRect[3];
    public SKRect[] ReassignButtonRects { get; } = new SKRect[3];
    public SKRect[] ClarityButtonRects { get; } = new SKRect[3];
    public SKRect[] SmoothingButtonRects { get; } = new SKRect[3];
    public SKRect[] NormalizationButtonRects { get; } = new SKRect[4];
    public SKRect[] PitchAlgorithmButtonRects { get; } = new SKRect[5];
    public SKRect PreEmphasisButtonRect { get; private set; }
    public SKRect HighPassButtonRect { get; private set; }
    public SKRect CloseButtonRect { get; private set; }

    // Current state
    public int FftSizeIndex { get; set; }
    public int OverlapIndex { get; set; } = 2;
    public int WindowIndex { get; set; }
    public int ScaleIndex { get; set; } = 2;
    public int TransformIndex { get; set; }
    public int ReassignIndex { get; set; }
    public int ClarityIndex { get; set; }
    public int SmoothingIndex { get; set; } = 1;
    public int NormalizationIndex { get; set; }
    public int PitchAlgorithmIndex { get; set; }
    public bool PreEmphasisEnabled { get; set; } = true;
    public bool HighPassEnabled { get; set; } = true;

    private static readonly string[] FftSizeLabels = ["1024", "2048", "4096", "8192"];
    private static readonly string[] OverlapLabels = ["50%", "75%", "87.5%", "93.75%", "96.9%"];
    private static readonly string[] WindowLabels = ["Hann", "Ham", "BH", "Gaus", "Kais"];
    private static readonly string[] ScaleLabels = ["Lin", "Log", "Mel", "Erb", "Bark"];
    private static readonly string[] TransformLabels = ["FFT", "Zoom", "CQT"];
    private static readonly string[] ReassignLabels = ["Off", "Freq", "T+F"];
    private static readonly string[] ClarityLabels = ["Off", "Noise", "Harm"];
    private static readonly string[] SmoothingLabels = ["Off", "EMA", "Bilat"];
    private static readonly string[] NormalizationLabels = ["Off", "Peak", "RMS", "A-Wt"];
    private static readonly string[] PitchAlgorithmLabels = ["YIN", "PYIN", "Auto", "Cep", "SWIPE"];

    public AnalysisSettingsRenderer(PluginComponentTheme theme)
    {
        _theme = theme;

        _backgroundPaint = new SKPaint
        {
            Color = theme.PanelBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _panelPaint = new SKPaint
        {
            Color = theme.PanelBackgroundLight,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _borderPaint = new SKPaint
        {
            Color = theme.PanelBorder,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };

        _buttonPaint = new SKPaint
        {
            Color = theme.KnobBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _buttonActivePaint = new SKPaint
        {
            Color = theme.KnobArc,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _titlePaint = new SkiaTextPaint(theme.TextPrimary, 14f, SKFontStyle.Bold, SKTextAlign.Left);
        _closeButtonPaint = new SkiaTextPaint(theme.TextSecondary, 18f, SKFontStyle.Normal, SKTextAlign.Center);
        _panelHeaderPaint = new SkiaTextPaint(theme.TextSecondary, 11f, SKFontStyle.Bold, SKTextAlign.Left);
        _buttonTextPaint = new SkiaTextPaint(theme.TextPrimary, 10f, SKFontStyle.Normal, SKTextAlign.Center);
        _labelPaint = new SkiaTextPaint(theme.TextMuted, 10f, SKFontStyle.Normal, SKTextAlign.Left);

        // Create knobs
        MinFreqKnob = new KnobWidget(KnobRadius, 20f, 2000f, "Min", "Hz", theme: theme)
        {
            Value = 80f,
            IsLogarithmic = true,
            ValueFormat = "0"
        };

        MaxFreqKnob = new KnobWidget(KnobRadius, 2000f, 20000f, "Max", "Hz", theme: theme)
        {
            Value = 8000f,
            IsLogarithmic = true,
            ValueFormat = "0"
        };

        TimeKnob = new KnobWidget(KnobRadius, 1f, 60f, "Time", "s", theme: theme)
        {
            Value = 5f,
            IsLogarithmic = true,
            ValueFormat = "0.0"
        };

        HpfKnob = new KnobWidget(KnobRadius, 20f, 120f, "HPF", "Hz", theme: theme)
        {
            Value = 60f,
            ValueFormat = "0"
        };

        ReassignThresholdKnob = new KnobWidget(KnobRadius, -100f, 0f, "Thresh", "dB", theme: theme)
        {
            Value = -60f,
            ValueFormat = "0"
        };

        ReassignSpreadKnob = new KnobWidget(KnobRadius, 0f, 100f, "Spread", "%", theme: theme)
        {
            Value = 100f,
            ValueFormat = "0"
        };

        ClarityNoiseKnob = new KnobWidget(KnobRadius, 0f, 100f, "Noise", "%", theme: theme)
        {
            Value = 100f,
            ValueFormat = "0"
        };

        ClarityHarmonicKnob = new KnobWidget(KnobRadius, 0f, 100f, "Harm", "%", theme: theme)
        {
            Value = 100f,
            ValueFormat = "0"
        };

        ClaritySmoothingKnob = new KnobWidget(KnobRadius, 0f, 100f, "Smooth", "%", theme: theme)
        {
            Value = 30f,
            ValueFormat = "0"
        };

        AllKnobs = new[]
        {
            MinFreqKnob, MaxFreqKnob, TimeKnob, HpfKnob,
            ReassignThresholdKnob, ReassignSpreadKnob,
            ClarityNoiseKnob, ClarityHarmonicKnob, ClaritySmoothingKnob
        };
    }

    public void Render(SKCanvas canvas, int width, int height)
    {
        canvas.Clear(_theme.PanelBackground);

        // Title bar
        DrawTitleBar(canvas, width);

        float y = TitleBarHeight + Padding;
        float contentWidth = width - 2 * Padding;

        // Frequency Range Panel
        y = DrawFrequencyPanel(canvas, Padding, y, contentWidth);

        // Analysis Engine Panel
        y = DrawAnalysisPanel(canvas, Padding, y + PanelSpacing, contentWidth);

        // Clarity Panel
        y = DrawClarityPanel(canvas, Padding, y + PanelSpacing, contentWidth);
    }

    private void DrawTitleBar(SKCanvas canvas, int width)
    {
        // Background
        canvas.DrawRect(0, 0, width, TitleBarHeight, _panelPaint);

        // Title
        _titlePaint.DrawText(canvas, "Analysis Settings", Padding, TitleBarHeight / 2 + 5);

        // Close button
        float btnSize = 24f;
        float btnX = width - Padding - btnSize;
        float btnY = (TitleBarHeight - btnSize) / 2;
        CloseButtonRect = new SKRect(btnX, btnY, btnX + btnSize, btnY + btnSize);
        canvas.DrawText("\u00D7", CloseButtonRect.MidX, CloseButtonRect.MidY + 6, _closeButtonPaint);
    }

    private float DrawFrequencyPanel(SKCanvas canvas, float x, float y, float width)
    {
        float panelHeight = PanelHeaderHeight + PanelPadding * 2 + KnobRadius * 2 + 30 + RowSpacing + ButtonHeight + LabelHeight + RowSpacing + ButtonHeight + LabelHeight;
        DrawPanelBackground(canvas, x, y, width, panelHeight, "Frequency Range");

        float contentY = y + PanelHeaderHeight + PanelPadding;
        float contentX = x + PanelPadding;
        float contentWidth = width - PanelPadding * 2;

        // Knobs row: Min, Max, Time, HPF
        float knobSpacing = contentWidth / 4;
        MinFreqKnob.Center = new SKPoint(contentX + knobSpacing * 0.5f, contentY + KnobRadius);
        MaxFreqKnob.Center = new SKPoint(contentX + knobSpacing * 1.5f, contentY + KnobRadius);
        TimeKnob.Center = new SKPoint(contentX + knobSpacing * 2.5f, contentY + KnobRadius);
        HpfKnob.Center = new SKPoint(contentX + knobSpacing * 3.5f, contentY + KnobRadius);

        foreach (var knob in new[] { MinFreqKnob, MaxFreqKnob, TimeKnob, HpfKnob })
        {
            knob.Render(canvas);
        }

        contentY += KnobRadius * 2 + 30 + RowSpacing;

        // Scale selector
        _labelPaint.DrawText(canvas, "Scale", contentX, contentY + 10);
        contentY += LabelHeight;
        DrawButtonRow(canvas, contentX, contentY, contentWidth, ScaleLabels, ScaleButtonRects, ScaleIndex);
        contentY += ButtonHeight + RowSpacing;

        // Toggles row
        float toggleWidth = (contentWidth - ButtonSpacing) / 2;
        PreEmphasisButtonRect = new SKRect(contentX, contentY, contentX + toggleWidth, contentY + ButtonHeight);
        HighPassButtonRect = new SKRect(contentX + toggleWidth + ButtonSpacing, contentY, contentX + contentWidth, contentY + ButtonHeight);

        DrawToggleButton(canvas, PreEmphasisButtonRect, "Pre-Emphasis", PreEmphasisEnabled);
        DrawToggleButton(canvas, HighPassButtonRect, "High-Pass", HighPassEnabled);

        return y + panelHeight;
    }

    private float DrawAnalysisPanel(SKCanvas canvas, float x, float y, float width)
    {
        float panelHeight = PanelHeaderHeight + PanelPadding * 2 +
            (LabelHeight + ButtonHeight + RowSpacing) * 5 +
            KnobRadius * 2 + 30;

        DrawPanelBackground(canvas, x, y, width, panelHeight, "Analysis Engine");

        float contentY = y + PanelHeaderHeight + PanelPadding;
        float contentX = x + PanelPadding;
        float contentWidth = width - PanelPadding * 2;

        // Transform type
        _labelPaint.DrawText(canvas, "Transform", contentX, contentY + 10);
        contentY += LabelHeight;
        DrawButtonRow(canvas, contentX, contentY, contentWidth, TransformLabels, TransformButtonRects, TransformIndex);
        contentY += ButtonHeight + RowSpacing;

        // FFT Size
        _labelPaint.DrawText(canvas, "FFT Size", contentX, contentY + 10);
        contentY += LabelHeight;
        DrawButtonRow(canvas, contentX, contentY, contentWidth, FftSizeLabels, FftSizeButtonRects, FftSizeIndex);
        contentY += ButtonHeight + RowSpacing;

        // Window function
        _labelPaint.DrawText(canvas, "Window", contentX, contentY + 10);
        contentY += LabelHeight;
        DrawButtonRow(canvas, contentX, contentY, contentWidth, WindowLabels, WindowButtonRects, WindowIndex);
        contentY += ButtonHeight + RowSpacing;

        // Overlap
        _labelPaint.DrawText(canvas, "Overlap", contentX, contentY + 10);
        contentY += LabelHeight;
        DrawButtonRow(canvas, contentX, contentY, contentWidth, OverlapLabels, OverlapButtonRects, OverlapIndex);
        contentY += ButtonHeight + RowSpacing;

        // Reassignment mode
        _labelPaint.DrawText(canvas, "Reassignment", contentX, contentY + 10);
        contentY += LabelHeight;
        float reassignWidth = contentWidth * 0.5f;
        DrawButtonRow(canvas, contentX, contentY, reassignWidth - ButtonSpacing, ReassignLabels, ReassignButtonRects, ReassignIndex);

        // Reassignment knobs (when reassignment is on)
        float knobY = contentY + ButtonHeight / 2;
        ReassignThresholdKnob.Center = new SKPoint(contentX + reassignWidth + (contentWidth - reassignWidth) * 0.25f, knobY + KnobRadius);
        ReassignSpreadKnob.Center = new SKPoint(contentX + reassignWidth + (contentWidth - reassignWidth) * 0.75f, knobY + KnobRadius);

        if (ReassignIndex > 0)
        {
            ReassignThresholdKnob.Render(canvas);
            ReassignSpreadKnob.Render(canvas);
        }
        contentY += KnobRadius * 2 + 30;

        // Pitch algorithm
        _labelPaint.DrawText(canvas, "Pitch Algorithm", contentX, contentY + 10);
        contentY += LabelHeight;
        DrawButtonRow(canvas, contentX, contentY, contentWidth, PitchAlgorithmLabels, PitchAlgorithmButtonRects, PitchAlgorithmIndex);

        return y + panelHeight;
    }

    private float DrawClarityPanel(SKCanvas canvas, float x, float y, float width)
    {
        float panelHeight = PanelHeaderHeight + PanelPadding * 2 +
            (LabelHeight + ButtonHeight + RowSpacing) * 3 +
            KnobRadius * 2 + 30;

        DrawPanelBackground(canvas, x, y, width, panelHeight, "Processing");

        float contentY = y + PanelHeaderHeight + PanelPadding;
        float contentX = x + PanelPadding;
        float contentWidth = width - PanelPadding * 2;

        // Clarity mode
        _labelPaint.DrawText(canvas, "Clarity", contentX, contentY + 10);
        contentY += LabelHeight;
        float clarityWidth = contentWidth * 0.4f;
        DrawButtonRow(canvas, contentX, contentY, clarityWidth - ButtonSpacing, ClarityLabels, ClarityButtonRects, ClarityIndex);

        // Clarity knobs
        float knobX = contentX + clarityWidth;
        float knobY = contentY - 5;
        float knobSpacing = (contentWidth - clarityWidth) / 3;
        ClarityNoiseKnob.Center = new SKPoint(knobX + knobSpacing * 0.5f, knobY + KnobRadius);
        ClarityHarmonicKnob.Center = new SKPoint(knobX + knobSpacing * 1.5f, knobY + KnobRadius);
        ClaritySmoothingKnob.Center = new SKPoint(knobX + knobSpacing * 2.5f, knobY + KnobRadius);

        if (ClarityIndex > 0)
        {
            ClarityNoiseKnob.Render(canvas);
            ClarityHarmonicKnob.Render(canvas);
            ClaritySmoothingKnob.Render(canvas);
        }
        contentY += KnobRadius * 2 + 30;

        // Smoothing mode
        _labelPaint.DrawText(canvas, "Smoothing", contentX, contentY + 10);
        contentY += LabelHeight;
        DrawButtonRow(canvas, contentX, contentY, contentWidth, SmoothingLabels, SmoothingButtonRects, SmoothingIndex);
        contentY += ButtonHeight + RowSpacing;

        // Normalization mode
        _labelPaint.DrawText(canvas, "Normalization", contentX, contentY + 10);
        contentY += LabelHeight;
        DrawButtonRow(canvas, contentX, contentY, contentWidth, NormalizationLabels, NormalizationButtonRects, NormalizationIndex);

        return y + panelHeight;
    }

    private void DrawPanelBackground(SKCanvas canvas, float x, float y, float width, float height, string header)
    {
        var rect = new SKRect(x, y, x + width, y + height);
        var roundRect = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(roundRect, _panelPaint);
        canvas.DrawRoundRect(roundRect, _borderPaint);

        // Header
        _panelHeaderPaint.DrawText(canvas, header, x + PanelPadding, y + PanelHeaderHeight - 6);
    }

    private void DrawButtonRow(SKCanvas canvas, float x, float y, float width, string[] labels, SKRect[] rects, int activeIndex)
    {
        int count = labels.Length;
        float btnWidth = (width - (count - 1) * ButtonSpacing) / count;

        for (int i = 0; i < count; i++)
        {
            float btnX = x + i * (btnWidth + ButtonSpacing);
            var rect = new SKRect(btnX, y, btnX + btnWidth, y + ButtonHeight);
            rects[i] = rect;

            var roundRect = new SKRoundRect(rect, 4f);
            canvas.DrawRoundRect(roundRect, i == activeIndex ? _buttonActivePaint : _buttonPaint);

            _buttonTextPaint.DrawText(canvas, labels[i], rect.MidX, rect.MidY + 4);
        }
    }

    private void DrawToggleButton(SKCanvas canvas, SKRect rect, string label, bool active)
    {
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, active ? _buttonActivePaint : _buttonPaint);
        _buttonTextPaint.DrawText(canvas, label, rect.MidX, rect.MidY + 4);
    }

    public void Dispose()
    {
        _backgroundPaint.Dispose();
        _panelPaint.Dispose();
        _borderPaint.Dispose();
        _buttonPaint.Dispose();
        _buttonActivePaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _panelHeaderPaint.Dispose();
        _buttonTextPaint.Dispose();
        _labelPaint.Dispose();

        foreach (var knob in AllKnobs)
        {
            knob.Dispose();
        }
    }
}
