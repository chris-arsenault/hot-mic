using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Visualizes the gate envelope shape (attack, hold, release).
/// Shows how the gate opens and closes over time.
/// </summary>
public sealed class EnvelopeCurveDisplay : IDisposable
{
    private readonly PluginComponentTheme _theme;
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _gridPaint;
    private readonly SKPaint _curvePaint;
    private readonly SKPaint _fillPaint;
    private readonly SkiaTextPaint _labelPaint;
    private readonly SKPaint _markerPaint;

    public EnvelopeCurveDisplay(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;

        _backgroundPaint = new SKPaint
        {
            Color = _theme.PanelBackgroundLight,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _gridPaint = new SKPaint
        {
            Color = _theme.EnvelopeGrid,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };

        _curvePaint = new SKPaint
        {
            Color = _theme.EnvelopeLine,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };

        _fillPaint = new SKPaint
        {
            Color = _theme.EnvelopeFill,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _labelPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Center);

        _markerPaint = new SKPaint
        {
            Color = _theme.EnvelopeLine,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
    }

    /// <summary>
    /// Render the envelope curve.
    /// </summary>
    /// <param name="canvas">Canvas to draw on</param>
    /// <param name="rect">Bounding rectangle</param>
    /// <param name="attackMs">Attack time in ms</param>
    /// <param name="holdMs">Hold time in ms</param>
    /// <param name="releaseMs">Release time in ms</param>
    /// <param name="isGateOpen">Current gate state for animation</param>
    public void Render(
        SKCanvas canvas,
        SKRect rect,
        float attackMs,
        float holdMs,
        float releaseMs,
        bool isGateOpen = false)
    {
        // Background
        var roundRect = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(roundRect, _backgroundPaint);

        // Grid
        float midY = rect.MidY;
        canvas.DrawLine(rect.Left, midY, rect.Right, midY, _gridPaint);

        // Calculate normalized positions
        float totalTime = attackMs + holdMs + releaseMs;
        if (totalTime < 1f) totalTime = 1f;

        float padding = 16f;
        float drawWidth = rect.Width - (padding * 2);
        float drawHeight = rect.Height - (padding * 2);
        float baseY = rect.Bottom - padding;
        float topY = rect.Top + padding;

        float attackEndX = rect.Left + padding + (drawWidth * (attackMs / totalTime));
        float holdEndX = attackEndX + (drawWidth * (holdMs / totalTime));
        float releaseEndX = rect.Right - padding;

        // Build the envelope path
        using var path = new SKPath();
        using var fillPath = new SKPath();

        // Start at bottom left (gate closed)
        float startX = rect.Left + padding;
        path.MoveTo(startX, baseY);
        fillPath.MoveTo(startX, baseY);

        // Attack: exponential rise
        int attackPoints = 20;
        for (int i = 1; i <= attackPoints; i++)
        {
            float t = i / (float)attackPoints;
            float x = startX + (attackEndX - startX) * t;
            // Exponential curve: fast rise, slowing at top
            float curve = 1f - MathF.Exp(-3f * t);
            float y = baseY - (drawHeight * curve);
            path.LineTo(x, y);
            fillPath.LineTo(x, y);
        }

        // Hold: flat at top
        path.LineTo(holdEndX, topY);
        fillPath.LineTo(holdEndX, topY);

        // Release: exponential decay
        int releasePoints = 20;
        for (int i = 1; i <= releasePoints; i++)
        {
            float t = i / (float)releasePoints;
            float x = holdEndX + (releaseEndX - holdEndX) * t;
            // Exponential decay
            float curve = MathF.Exp(-3f * t);
            float y = baseY - (drawHeight * curve);
            path.LineTo(x, y);
            fillPath.LineTo(x, y);
        }

        // Close fill path
        fillPath.LineTo(releaseEndX, baseY);
        fillPath.LineTo(startX, baseY);
        fillPath.Close();

        // Draw fill
        canvas.DrawPath(fillPath, _fillPaint);

        // Draw curve
        canvas.DrawPath(path, _curvePaint);

        // Draw phase markers
        DrawMarker(canvas, startX, baseY, "0");
        DrawMarker(canvas, attackEndX, topY, "A");
        DrawMarker(canvas, holdEndX, topY, "H");
        DrawMarker(canvas, releaseEndX, baseY, "R");

        // Draw labels
        if (attackMs >= 1f)
        {
            float labelX = (startX + attackEndX) / 2;
            canvas.DrawText($"{attackMs:0}ms", labelX, rect.Bottom - 4, _labelPaint);
        }

        if (holdMs >= 1f)
        {
            float labelX = (attackEndX + holdEndX) / 2;
            canvas.DrawText($"{holdMs:0}ms", labelX, rect.Top + 12, _labelPaint);
        }

        if (releaseMs >= 10f)
        {
            float labelX = (holdEndX + releaseEndX) / 2;
            canvas.DrawText($"{releaseMs:0}ms", labelX, rect.Bottom - 4, _labelPaint);
        }

        // Gate state indicator
        if (isGateOpen)
        {
            using var glowPaint = new SKPaint
            {
                Color = _theme.GateOpenGlow,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 4f,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4f)
            };
            canvas.DrawPath(path, glowPaint);
        }

        // Border
        using var borderPaint = new SKPaint
        {
            Color = _theme.PanelBorder,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };
        canvas.DrawRoundRect(roundRect, borderPaint);
    }

    private void DrawMarker(SKCanvas canvas, float x, float y, string label)
    {
        canvas.DrawCircle(x, y, 4f, _markerPaint);

        using var textPaint = new SkiaTextPaint(_theme.TextSecondary, 8f, SKFontStyle.Bold, SKTextAlign.Center);

        float labelY = y < (y + 10) ? y + 14 : y - 8;
        canvas.DrawText(label, x, labelY, textPaint);
    }

    public void Dispose()
    {
        _backgroundPaint.Dispose();
        _gridPaint.Dispose();
        _curvePaint.Dispose();
        _fillPaint.Dispose();
        _labelPaint.Dispose();
        _markerPaint.Dispose();
    }
}
