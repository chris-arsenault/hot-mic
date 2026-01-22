using System;
using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Renderer for the standalone Speech Coach window.
/// Displays real-time speech metrics and visualization.
/// </summary>
public sealed class SpeechCoachRenderer : IDisposable
{
    private const float Padding = 12f;
    private const float TitleBarHeight = 36f;
    private const float MetricCardHeight = 70f;
    private const float MetricCardSpacing = 8f;
    private const float TimelineHeight = 60f;

    private readonly PluginComponentTheme _theme;
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _cardPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _meterBackgroundPaint;
    private readonly SKPaint _meterFillPaint;
    private readonly SKPaint _goodPaint;
    private readonly SKPaint _warningPaint;
    private readonly SKPaint _speakingPaint;
    private readonly SKPaint _silentPausePaint;
    private readonly SKPaint _filledPausePaint;
    private readonly SKPaint _syllableMarkerPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _cardLabelPaint;
    private readonly SkiaTextPaint _cardValuePaint;
    private readonly SkiaTextPaint _cardUnitPaint;

    public SKRect CloseButtonRect { get; private set; }

    public SpeechCoachRenderer(PluginComponentTheme theme)
    {
        _theme = theme;

        _backgroundPaint = new SKPaint
        {
            Color = theme.PanelBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _cardPaint = new SKPaint
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

        _meterBackgroundPaint = new SKPaint
        {
            Color = theme.MeterBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _meterFillPaint = new SKPaint
        {
            Color = theme.KnobArc,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _goodPaint = new SKPaint
        {
            Color = new SKColor(0x4C, 0xC9, 0x4C), // Green
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _warningPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0xA5, 0x00), // Orange
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _speakingPaint = new SKPaint
        {
            Color = theme.WaveformGateOpen,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _silentPausePaint = new SKPaint
        {
            Color = theme.WaveformGateClosed,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _filledPausePaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0xA5, 0x00, 0x60), // Semi-transparent orange
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _syllableMarkerPaint = new SKPaint
        {
            Color = theme.AccentSecondary,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f
        };

        _titlePaint = new SkiaTextPaint(theme.TextPrimary, 14f, SKFontStyle.Bold, SKTextAlign.Left);
        _cardLabelPaint = new SkiaTextPaint(theme.TextSecondary, 10f, SKFontStyle.Normal, SKTextAlign.Left);
        _cardValuePaint = new SkiaTextPaint(theme.TextPrimary, 22f, SKFontStyle.Bold, SKTextAlign.Left);
        _cardUnitPaint = new SkiaTextPaint(theme.TextMuted, 10f, SKFontStyle.Normal, SKTextAlign.Left);
    }

    public void Render(
        SKCanvas canvas,
        int width,
        int height,
        byte[] speakingStateTrack,
        byte[] syllableMarkers,
        int availableFrames,
        float syllableRate,
        float articulationRate,
        float pauseRatio,
        float monotoneScore,
        float clarityScore,
        float intelligibilityScore)
    {
        canvas.Clear(_theme.PanelBackground);

        // Title bar
        DrawTitleBar(canvas, width);

        float y = TitleBarHeight + Padding;
        float cardWidth = (width - Padding * 3) / 2;

        // Row 1: Speaking Rate & Articulation Rate
        DrawMetricCard(canvas, Padding, y, cardWidth, "Speaking Rate", syllableRate, "syl/min", 80, 200, true);
        DrawMetricCard(canvas, Padding * 2 + cardWidth, y, cardWidth, "Articulation Rate", articulationRate, "syl/min", 120, 250, true);

        y += MetricCardHeight + MetricCardSpacing;

        // Row 2: Pause Ratio & Monotone Score
        DrawMetricCard(canvas, Padding, y, cardWidth, "Pause Ratio", pauseRatio * 100, "%", 0, 40, false);
        DrawMetricCard(canvas, Padding * 2 + cardWidth, y, cardWidth, "Monotone Score", monotoneScore * 100, "%", 0, 30, false);

        y += MetricCardHeight + MetricCardSpacing;

        // Row 3: Clarity & Intelligibility
        DrawMetricCard(canvas, Padding, y, cardWidth, "Clarity", clarityScore, "%", 60, 100, true);
        DrawMetricCard(canvas, Padding * 2 + cardWidth, y, cardWidth, "Intelligibility", intelligibilityScore, "%", 70, 100, true);

        y += MetricCardHeight + MetricCardSpacing;

        // Timeline section
        float timelineTop = y;
        float timelineBottom = height - Padding;
        DrawTimeline(canvas, Padding, timelineTop, width - Padding * 2, timelineBottom - timelineTop,
            speakingStateTrack, syllableMarkers, availableFrames);
    }

    private void DrawTitleBar(SKCanvas canvas, int width)
    {
        using var panelPaint = new SKPaint
        {
            Color = _theme.PanelBackgroundLight,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawRect(0, 0, width, TitleBarHeight, panelPaint);

        _titlePaint.DrawText(canvas, "Speech Coach", Padding, TitleBarHeight / 2 + 5);

        // Close button
        float btnSize = 22f;
        float btnX = width - Padding - btnSize;
        float btnY = (TitleBarHeight - btnSize) / 2;
        CloseButtonRect = new SKRect(btnX, btnY, btnX + btnSize, btnY + btnSize);

        using var closePaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            StrokeCap = SKStrokeCap.Round
        };

        float cx = CloseButtonRect.MidX;
        float cy = CloseButtonRect.MidY;
        float s = 5f;
        canvas.DrawLine(cx - s, cy - s, cx + s, cy + s, closePaint);
        canvas.DrawLine(cx + s, cy - s, cx - s, cy + s, closePaint);
    }

    private void DrawMetricCard(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        string label,
        float value,
        string unit,
        float goodMin,
        float goodMax,
        bool higherIsBetter)
    {
        var rect = new SKRect(x, y, x + width, y + MetricCardHeight);
        var roundRect = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(roundRect, _cardPaint);
        canvas.DrawRoundRect(roundRect, _borderPaint);

        // Label
        _cardLabelPaint.DrawText(canvas, label, x + 10, y + 18);

        // Value
        string valueStr = float.IsNaN(value) || float.IsInfinity(value) ? "--" : $"{value:0.0}";
        _cardValuePaint.DrawText(canvas, valueStr, x + 10, y + 48);

        // Unit
        float valueWidth = _cardValuePaint.MeasureText(valueStr);
        _cardUnitPaint.DrawText(canvas, unit, x + 14 + valueWidth, y + 48);

        // Status indicator (meter bar)
        float meterY = y + MetricCardHeight - 14;
        float meterHeight = 6f;
        float meterWidth = width - 20;

        var meterRect = new SKRect(x + 10, meterY, x + 10 + meterWidth, meterY + meterHeight);
        var meterRoundRect = new SKRoundRect(meterRect, 3f);
        canvas.DrawRoundRect(meterRoundRect, _meterBackgroundPaint);

        // Determine fill color based on value
        bool isGood = higherIsBetter
            ? (value >= goodMin && value <= goodMax)
            : (value <= goodMax);

        var fillPaint = isGood ? _goodPaint : _warningPaint;

        // Calculate fill width (normalized 0-1)
        float normalizedValue = Math.Clamp(value / goodMax, 0f, 1f);
        float fillWidth = meterWidth * normalizedValue;

        var fillRect = new SKRect(x + 10, meterY, x + 10 + fillWidth, meterY + meterHeight);
        var fillRoundRect = new SKRoundRect(fillRect, 3f);
        canvas.DrawRoundRect(fillRoundRect, fillPaint);
    }

    private void DrawTimeline(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        float height,
        byte[] speakingStateTrack,
        byte[] syllableMarkers,
        int availableFrames)
    {
        // Label
        _cardLabelPaint.DrawText(canvas, "Speaking Timeline", x, y + 14);

        float timelineY = y + 20;
        float timelineHeight = height - 24;

        // Background
        var rect = new SKRect(x, timelineY, x + width, timelineY + timelineHeight);
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _meterBackgroundPaint);

        if (availableFrames <= 0 || speakingStateTrack.Length == 0) return;

        int frameCount = Math.Min(availableFrames, speakingStateTrack.Length);
        float stepX = width / frameCount;

        // Draw speaking state regions
        for (int i = 0; i < frameCount; i++)
        {
            float frameX = x + (i * stepX);
            float frameWidth = Math.Max(1f, stepX);

            byte state = speakingStateTrack[i];
            SKPaint? statePaint = state switch
            {
                0 => null, // Silence - no fill
                1 => _speakingPaint, // Speaking
                2 => _silentPausePaint, // Silent pause
                3 => _filledPausePaint, // Filled pause
                _ => null
            };

            if (statePaint != null)
            {
                canvas.DrawRect(frameX, timelineY, frameWidth, timelineHeight, statePaint);
            }
        }

        // Draw syllable markers
        for (int i = 0; i < Math.Min(frameCount, syllableMarkers.Length); i++)
        {
            if (syllableMarkers[i] > 0)
            {
                float markerX = x + (i * stepX);
                canvas.DrawLine(markerX, timelineY, markerX, timelineY + timelineHeight, _syllableMarkerPaint);
            }
        }

        // Border
        canvas.DrawRoundRect(roundRect, _borderPaint);
    }

    public void Dispose()
    {
        _backgroundPaint.Dispose();
        _cardPaint.Dispose();
        _borderPaint.Dispose();
        _meterBackgroundPaint.Dispose();
        _meterFillPaint.Dispose();
        _goodPaint.Dispose();
        _warningPaint.Dispose();
        _speakingPaint.Dispose();
        _silentPausePaint.Dispose();
        _filledPausePaint.Dispose();
        _syllableMarkerPaint.Dispose();
        _titlePaint.Dispose();
        _cardLabelPaint.Dispose();
        _cardValuePaint.Dispose();
        _cardUnitPaint.Dispose();
    }
}
