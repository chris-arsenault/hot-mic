using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Renders a horizontal gain reduction meter showing actual dB reduction.
/// Uses PPM-style ballistics via MeterBallistics class.
/// </summary>
public sealed class GainReductionMeter : IDisposable
{
    private const float DefaultMaxReductionDb = 40f;

    private readonly PluginComponentTheme _theme;
    private readonly MeterBallistics _ballistics;
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _currentFillPaint;
    private readonly SKPaint _peakPaint;
    private readonly SkiaTextPaint _labelPaint;
    private readonly SkiaTextPaint _valuePaint;
    private readonly SKPaint _tickPaint;
    private readonly SkiaTextPaint _tickLabelPaint;

    public GainReductionMeter(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _ballistics = new MeterBallistics();

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

        _currentFillPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _peakPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f
        };

        _labelPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Normal);
        _valuePaint = new SkiaTextPaint(_theme.TextPrimary, 12f, SKFontStyle.Bold, SKTextAlign.Right);

        _tickPaint = new SKPaint
        {
            Color = _theme.TextMuted.WithAlpha(100),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };

        _tickLabelPaint = new SkiaTextPaint(_theme.TextMuted, 8f, SKFontStyle.Normal, SKTextAlign.Center);
    }

    /// <summary>
    /// Render the gain reduction meter with PPM-style ballistics.
    /// </summary>
    public void Render(SKCanvas canvas, SKRect rect, float gainReductionDb, string label = "Gain Reduction", float maxDb = DefaultMaxReductionDb)
    {
        // Update ballistics
        _ballistics.UpdateDb(Math.Max(0f, gainReductionDb));

        float displayCurrent = Math.Clamp(_ballistics.Current, 0f, maxDb);
        float displayPeak = Math.Clamp(_ballistics.Peak, 0f, maxDb);
        float currentNorm = displayCurrent / maxDb;
        float peakNorm = displayPeak / maxDb;

        // Label
        canvas.DrawText(label, rect.Left, rect.Top - 4f, _labelPaint);

        // Value - show held peak dB
        string valueText = displayPeak < 0.1f ? "0 dB" : $"-{displayPeak:0.0} dB";
        canvas.DrawText(valueText, rect.Right, rect.Top - 4f, _valuePaint);

        // Background
        var barRect = new SKRect(rect.Left, rect.Top + 4f, rect.Right, rect.Bottom);
        var roundRect = new SKRoundRect(barRect, 4f);
        canvas.DrawRoundRect(roundRect, _backgroundPaint);

        // Current level fill (responsive bar)
        float currentWidth = (barRect.Width - 4) * currentNorm;
        if (currentWidth > 1)
        {
            var currentRect = new SKRect(barRect.Left + 2, barRect.Top + 2, barRect.Left + 2 + currentWidth, barRect.Bottom - 2);

            // Gradient: green (low GR) to orange (medium) to red (high GR)
            _currentFillPaint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(barRect.Left, currentRect.MidY),
                new SKPoint(barRect.Right, currentRect.MidY),
                [
                    new SKColor(0x40, 0xC0, 0x40), // Green at 0 dB
                    new SKColor(0xFF, 0xC0, 0x40), // Orange at mid
                    new SKColor(0xFF, 0x50, 0x50)  // Red at max
                ],
                [0f, 0.5f, 1f],
                SKShaderTileMode.Clamp);

            canvas.DrawRoundRect(new SKRoundRect(currentRect, 2f), _currentFillPaint);
            _currentFillPaint.Shader = null;
        }

        // Peak marker (vertical line) - only show if ahead of current
        if (peakNorm > currentNorm + 0.02f)
        {
            float peakX = barRect.Left + 2 + (barRect.Width - 4) * peakNorm;
            canvas.DrawLine(peakX, barRect.Top + 2, peakX, barRect.Bottom - 2, _peakPaint);
        }

        // Tick marks
        float[] ticks = maxDb switch
        {
            <= 12f => [0, 3, 6, 9, 12],
            <= 20f => [0, 5, 10, 15, 20],
            <= 30f => [0, 10, 20, 30],
            _ => [0, 10, 20, 30, 40]
        };

        foreach (float tickDb in ticks)
        {
            if (tickDb > maxDb) continue;
            float tickNorm = tickDb / maxDb;
            float tickX = barRect.Left + 2 + (barRect.Width - 4) * tickNorm;

            canvas.DrawLine(tickX, barRect.Bottom + 2f, tickX, barRect.Bottom + 6f, _tickPaint);

            string tickLabel = tickDb == 0 ? "0" : $"-{tickDb:0}";
            canvas.DrawText(tickLabel, tickX, barRect.Bottom + 15f, _tickLabelPaint);
        }

        // Border
        canvas.DrawRoundRect(roundRect, _borderPaint);
    }

    /// <summary>
    /// Reset the meter state.
    /// </summary>
    public void Reset()
    {
        _ballistics.Reset();
    }

    public void Dispose()
    {
        _backgroundPaint.Dispose();
        _borderPaint.Dispose();
        _currentFillPaint.Dispose();
        _peakPaint.Dispose();
        _labelPaint.Dispose();
        _valuePaint.Dispose();
        _tickPaint.Dispose();
        _tickLabelPaint.Dispose();
    }
}
