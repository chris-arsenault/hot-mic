using System.Windows;
using SkiaSharp;

namespace HotMic.App.Controls;

public class MeterControl : SkiaControl
{
    public static readonly DependencyProperty PeakLevelProperty =
        DependencyProperty.Register(nameof(PeakLevel), typeof(float), typeof(MeterControl),
            new FrameworkPropertyMetadata(0f, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RmsLevelProperty =
        DependencyProperty.Register(nameof(RmsLevel), typeof(float), typeof(MeterControl),
            new FrameworkPropertyMetadata(0f, FrameworkPropertyMetadataOptions.AffectsRender));

    public float PeakLevel
    {
        get => (float)GetValue(PeakLevelProperty);
        set => SetValue(PeakLevelProperty, value);
    }

    public float RmsLevel
    {
        get => (float)GetValue(RmsLevelProperty);
        set => SetValue(RmsLevelProperty, value);
    }

    private readonly SKPaint _backgroundPaint = new() { Color = new SKColor(0x24, 0x24, 0x24) };
    private readonly SKPaint _peakPaint = new() { Color = SKColors.White, StrokeWidth = 2f, IsAntialias = true };
    private readonly SKPaint _tickPaint = new() { Color = new SKColor(0x44, 0x44, 0x44), StrokeWidth = 1f, IsAntialias = true };
    private readonly SKPaint _tickLabelPaint = new() { Color = new SKColor(0x88, 0x88, 0x88), TextSize = 9f, IsAntialias = true };
    private readonly SKPaint _greenPaint = new() { Color = new SKColor(0x00, 0xFF, 0x00) };
    private readonly SKPaint _yellowPaint = new() { Color = new SKColor(0xFF, 0xFF, 0x00) };
    private readonly SKPaint _redPaint = new() { Color = new SKColor(0xFF, 0x00, 0x00) };

    protected override void Render(SKCanvas canvas, int width, int height)
    {
        canvas.DrawRect(0, 0, width, height, _backgroundPaint);

        float rmsLevel = Math.Clamp(RmsLevel, 0f, 1f);
        float peakLevel = Math.Clamp(PeakLevel, 0f, 1f);

        float rmsHeight = height * rmsLevel;
        var rmsRect = new SKRect(2, height - rmsHeight, width - 2, height);
        canvas.DrawRect(rmsRect, GetMeterPaint(rmsLevel));

        float peakY = height * (1f - peakLevel);
        var peakPaint = peakLevel >= 1f ? _redPaint : _peakPaint;
        canvas.DrawLine(0, peakY, width, peakY, peakPaint);

        DrawScaleTicks(canvas, width, height);
    }

    private SKPaint GetMeterPaint(float level)
    {
        if (level > 0.95f)
        {
            return _redPaint;
        }

        if (level > 0.7f)
        {
            return _yellowPaint;
        }

        return _greenPaint;
    }

    private void DrawScaleTicks(SKCanvas canvas, int width, int height)
    {
        float[] dbMarks = [0f, -6f, -12f, -24f, -48f];
        foreach (float db in dbMarks)
        {
            float level = MathF.Pow(10f, db / 20f);
            float y = height * (1f - level);
            canvas.DrawLine(0, y, width, y, _tickPaint);
            canvas.DrawText($"{db:0}dB", 4, y - 2, _tickLabelPaint);
        }
    }
}
