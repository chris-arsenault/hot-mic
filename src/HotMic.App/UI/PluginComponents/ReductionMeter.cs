using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Renders a horizontal reduction amount meter for noise reduction plugins.
/// Shows the wet/dry mix level with visual feedback.
/// </summary>
public sealed class ReductionMeter : IDisposable
{
    private readonly PluginComponentTheme _theme;
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _fillPaint;
    private readonly SKPaint _labelPaint;
    private readonly SKPaint _valuePaint;
    private readonly SKPaint _tickPaint;

    public ReductionMeter(PluginComponentTheme? theme = null)
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

        _fillPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _labelPaint = new SKPaint
        {
            Color = _theme.TextSecondary,
            IsAntialias = true,
            TextSize = 10f,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };

        _valuePaint = new SKPaint
        {
            Color = _theme.TextPrimary,
            IsAntialias = true,
            TextSize = 12f,
            TextAlign = SKTextAlign.Right,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };

        _tickPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };
    }

    /// <summary>
    /// Render the reduction meter.
    /// </summary>
    /// <param name="canvas">The canvas to render to.</param>
    /// <param name="rect">The bounding rectangle for the meter.</param>
    /// <param name="reductionPercent">Reduction amount (0-100).</param>
    /// <param name="label">Label to display.</param>
    public void Render(SKCanvas canvas, SKRect rect, float reductionPercent, string label = "Reduction")
    {
        float normalized = Math.Clamp(reductionPercent / 100f, 0f, 1f);

        // Label
        canvas.DrawText(label, rect.Left, rect.Top - 4f, _labelPaint);

        // Value
        canvas.DrawText($"{reductionPercent:0}%", rect.Right, rect.Top - 4f, _valuePaint);

        // Background
        var barRect = new SKRect(rect.Left, rect.Top + 4f, rect.Right, rect.Bottom);
        var roundRect = new SKRoundRect(barRect, 4f);
        canvas.DrawRoundRect(roundRect, _backgroundPaint);

        // Fill
        float fillWidth = barRect.Width * normalized;
        if (fillWidth > 0)
        {
            var fillRect = new SKRect(barRect.Left + 2, barRect.Top + 2, barRect.Left + fillWidth - 2, barRect.Bottom - 2);

            // Gradient: blue to cyan
            _fillPaint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(fillRect.Left, fillRect.MidY),
                new SKPoint(fillRect.Right, fillRect.MidY),
                [new SKColor(0x40, 0x80, 0xFF), new SKColor(0x40, 0xE0, 0xFF)],
                SKShaderTileMode.Clamp);

            canvas.DrawRoundRect(new SKRoundRect(fillRect, 2f), _fillPaint);
        }

        // Tick marks at 25%, 50%, 75%
        for (int i = 1; i <= 3; i++)
        {
            float tickX = barRect.Left + barRect.Width * i / 4f;
            canvas.DrawLine(tickX, barRect.Top + 2f, tickX, barRect.Bottom - 2f, _tickPaint);
        }

        // Border
        canvas.DrawRoundRect(roundRect, _borderPaint);
    }

    public void Dispose()
    {
        _backgroundPaint.Dispose();
        _borderPaint.Dispose();
        _fillPaint.Dispose();
        _labelPaint.Dispose();
        _valuePaint.Dispose();
        _tickPaint.Dispose();
    }
}
