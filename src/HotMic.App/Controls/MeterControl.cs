using System.Windows;
using SkiaSharp;

namespace HotMic.App.Controls;

/// <summary>
/// Audio level meter control.
/// Shows RMS level as filled bar and peak level as line marker.
/// Ballistics (peak hold, RMS smoothing) are handled by MeterProcessor.
/// </summary>
public class MeterControl : SkiaControl
{
    public static readonly DependencyProperty LevelProperty =
        DependencyProperty.Register(nameof(Level), typeof(float), typeof(MeterControl),
            new FrameworkPropertyMetadata(0f, FrameworkPropertyMetadataOptions.AffectsRender));

    // Keep legacy properties for compatibility but they now feed into ballistics
    public static readonly DependencyProperty PeakLevelProperty =
        DependencyProperty.Register(nameof(PeakLevel), typeof(float), typeof(MeterControl),
            new FrameworkPropertyMetadata(0f, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RmsLevelProperty =
        DependencyProperty.Register(nameof(RmsLevel), typeof(float), typeof(MeterControl),
            new FrameworkPropertyMetadata(0f, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// The input level (linear 0-1). Ballistics are applied internally.
    /// </summary>
    public float Level
    {
        get => (float)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    /// <summary>
    /// Legacy peak level property. If Level is 0, this is used instead.
    /// </summary>
    public float PeakLevel
    {
        get => (float)GetValue(PeakLevelProperty);
        set => SetValue(PeakLevelProperty, value);
    }

    /// <summary>
    /// Legacy RMS level property. If Level is 0, this is used instead.
    /// </summary>
    public float RmsLevel
    {
        get => (float)GetValue(RmsLevelProperty);
        set => SetValue(RmsLevelProperty, value);
    }

    private readonly SKPaint _backgroundPaint = new() { Color = new SKColor(0x24, 0x24, 0x24) };
    private readonly SKPaint _peakPaint = new() { Color = SKColors.White, StrokeWidth = 2f, IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKPaint _tickPaint = new() { Color = new SKColor(0x44, 0x44, 0x44), StrokeWidth = 1f, IsAntialias = true };
    private readonly SKPaint _tickLabelPaint = new() { Color = new SKColor(0x88, 0x88, 0x88), TextSize = 9f, IsAntialias = true };
    private readonly SKPaint _greenPaint = new() { Color = new SKColor(0x40, 0xC0, 0x40) };
    private readonly SKPaint _yellowPaint = new() { Color = new SKColor(0xFF, 0xC0, 0x40) };
    private readonly SKPaint _redPaint = new() { Color = new SKColor(0xFF, 0x50, 0x50) };

    // dB thresholds for color changes (normalized values)
    private const float YellowThreshold = 0.5f;  // ~-6dB
    private const float RedThreshold = 0.9f;     // ~-1dB

    protected override void Render(SKCanvas canvas, int width, int height)
    {
        canvas.DrawRect(0, 0, width, height, _backgroundPaint);

        // MeterProcessor already applies ballistics (peak hold, RMS smoothing)
        // Just convert to normalized dB and display directly
        float peakLinear = Level > 0 ? Level : PeakLevel;
        float rmsLinear = RmsLevel;

        // Convert to normalized (using -60dB to 0dB range)
        float peak = LinearToNormalized(peakLinear);
        float current = LinearToNormalized(rmsLinear);

        // If no RMS, use peak for both (single-value mode)
        if (rmsLinear <= 0 && peakLinear > 0)
        {
            current = peak;
        }

        // Draw current level bar (from bottom up, segmented by color)
        float padding = 2f;
        float barLeft = padding;
        float barRight = width - padding;
        float barBottom = height;
        float barHeight = height * current;

        if (barHeight > 1)
        {
            float barTop = barBottom - barHeight;

            // Green section (bottom)
            float greenTop = barBottom - height * Math.Min(current, YellowThreshold);
            if (current > 0)
            {
                var greenRect = new SKRect(barLeft, Math.Max(greenTop, barTop), barRight, barBottom);
                if (greenRect.Height > 0)
                    canvas.DrawRect(greenRect, _greenPaint);
            }

            // Yellow section (middle)
            if (current > YellowThreshold)
            {
                float yellowTop = barBottom - height * Math.Min(current, RedThreshold);
                var yellowRect = new SKRect(barLeft, Math.Max(yellowTop, barTop), barRight, greenTop);
                if (yellowRect.Height > 0)
                    canvas.DrawRect(yellowRect, _yellowPaint);
            }

            // Red section (top)
            if (current > RedThreshold)
            {
                float redTop = barTop;
                float yellowTop = barBottom - height * RedThreshold;
                var redRect = new SKRect(barLeft, redTop, barRight, yellowTop);
                if (redRect.Height > 0)
                    canvas.DrawRect(redRect, _redPaint);
            }
        }

        // Draw peak marker (horizontal line) - only if ahead of current
        if (peak > current + 0.02f)
        {
            float peakY = height * (1f - peak);
            var peakPaint = peak >= 0.98f ? _redPaint : _peakPaint;
            canvas.DrawLine(0, peakY, width, peakY, peakPaint);
        }

        DrawScaleTicks(canvas, width, height);
    }

    private static float LinearToNormalized(float linear)
    {
        if (linear <= 0) return 0f;
        float db = 20f * MathF.Log10(linear);
        return Math.Clamp((db + 60f) / 60f, 0f, 1f);
    }

    private void DrawScaleTicks(SKCanvas canvas, int width, int height)
    {
        float[] dbMarks = [0f, -6f, -12f, -24f, -48f];
        foreach (float db in dbMarks)
        {
            float level = MathF.Pow(10f, db / 20f);
            // Convert to normalized (same as input conversion)
            float normalized = (db + 60f) / 60f;
            float y = height * (1f - normalized);
            canvas.DrawLine(0, y, width, y, _tickPaint);
            canvas.DrawText($"{db:0}dB", 4, y - 2, _tickLabelPaint);
        }
    }
}
