using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Renders a vertical VAD (Voice Activity Detection) probability meter.
/// Visualizes the confidence level of voice detection with smooth animation.
/// </summary>
public sealed class VadMeter : IDisposable
{
    private readonly PluginComponentTheme _theme;
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _meterPaint;
    private readonly SKPaint _thresholdPaint;
    private readonly SkiaTextPaint _labelPaint;
    private readonly SkiaTextPaint _valuePaint;
    private readonly SKPaint _glowPaint;

    private float _smoothedValue;
    private const float SmoothingFactor = 0.15f;

    public VadMeter(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;

        _backgroundPaint = new SKPaint
        {
            Color = _theme.MeterBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _borderPaint = new SKPaint
        {
            Color = _theme.PanelBorder,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };

        _meterPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _thresholdPaint = new SKPaint
        {
            Color = _theme.AccentSecondary,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            PathEffect = SKPathEffect.CreateDash([4f, 2f], 0)
        };

        _labelPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Center);
        _valuePaint = new SkiaTextPaint(_theme.TextPrimary, 12f, SKFontStyle.Bold, SKTextAlign.Center);

        _glowPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4f)
        };
    }

    /// <summary>
    /// Render the VAD meter.
    /// </summary>
    /// <param name="canvas">The canvas to render to.</param>
    /// <param name="rect">The bounding rectangle for the meter.</param>
    /// <param name="vadProbability">VAD probability (0-1).</param>
    /// <param name="threshold">Detection threshold (0-1).</param>
    /// <param name="isDetected">Whether voice is currently detected.</param>
    public void Render(SKCanvas canvas, SKRect rect, float vadProbability, float threshold, bool isDetected)
    {
        _smoothedValue += (vadProbability - _smoothedValue) * SmoothingFactor;
        float displayValue = Math.Clamp(_smoothedValue, 0f, 1f);

        // Background
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _backgroundPaint);

        // Meter fill
        float meterHeight = rect.Height * displayValue;
        var meterRect = new SKRect(rect.Left + 2, rect.Bottom - meterHeight - 2, rect.Right - 2, rect.Bottom - 2);

        if (meterRect.Height > 0)
        {
            // Gradient based on detection state
            SKColor topColor = isDetected ? new SKColor(0x4A, 0xFF, 0x4A) : new SKColor(0x4A, 0xA0, 0xFF);
            SKColor bottomColor = isDetected ? new SKColor(0x20, 0xA0, 0x20) : new SKColor(0x20, 0x60, 0xA0);

            _meterPaint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(meterRect.Left, meterRect.Top),
                new SKPoint(meterRect.Left, meterRect.Bottom),
                [topColor, bottomColor],
                SKShaderTileMode.Clamp);

            canvas.DrawRect(meterRect, _meterPaint);

            // Glow when detected
            if (isDetected && displayValue > threshold)
            {
                _glowPaint.Color = topColor.WithAlpha(60);
                canvas.DrawRect(meterRect, _glowPaint);
            }
        }

        // Threshold line
        float thresholdY = rect.Bottom - rect.Height * threshold;
        canvas.DrawLine(rect.Left, thresholdY, rect.Right, thresholdY, _thresholdPaint);

        // Border
        canvas.DrawRoundRect(roundRect, _borderPaint);

        // Label
        canvas.DrawText("VAD", rect.MidX, rect.Top - 4f, _labelPaint);

        // Value
        int percentage = (int)(displayValue * 100);
        canvas.DrawText($"{percentage}%", rect.MidX, rect.Bottom + 14f, _valuePaint);
    }

    public void Dispose()
    {
        _backgroundPaint.Dispose();
        _borderPaint.Dispose();
        _meterPaint.Dispose();
        _thresholdPaint.Dispose();
        _labelPaint.Dispose();
        _valuePaint.Dispose();
        _glowPaint.Dispose();
    }
}
