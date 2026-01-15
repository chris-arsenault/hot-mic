using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Visual indicator showing gate open/closed state with glow effect.
/// </summary>
public sealed class GateIndicator : IDisposable
{
    private readonly PluginComponentTheme _theme;
    private readonly SKPaint _openPaint;
    private readonly SKPaint _closedPaint;
    private readonly SKPaint _glowPaint;
    private readonly SKPaint _rimPaint;
    private readonly SkiaTextPaint _labelPaint;

    public GateIndicator(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;

        _openPaint = new SKPaint
        {
            Color = _theme.GateOpen,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _closedPaint = new SKPaint
        {
            Color = _theme.GateClosed,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _glowPaint = new SKPaint
        {
            Color = _theme.GateOpenGlow,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8f)
        };

        _rimPaint = new SKPaint
        {
            Color = _theme.PanelBorder,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f
        };

        _labelPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Bold, SKTextAlign.Center);
    }

    /// <summary>
    /// Render the gate indicator.
    /// </summary>
    /// <param name="canvas">Canvas to draw on</param>
    /// <param name="center">Center point</param>
    /// <param name="radius">Indicator radius</param>
    /// <param name="isOpen">Whether gate is open</param>
    /// <param name="showLabel">Whether to show OPEN/CLOSED label</param>
    public void Render(
        SKCanvas canvas,
        SKPoint center,
        float radius,
        bool isOpen,
        bool showLabel = true)
    {
        // Glow when open
        if (isOpen)
        {
            canvas.DrawCircle(center, radius * 1.5f, _glowPaint);
        }

        // Main indicator
        var mainPaint = isOpen ? _openPaint : _closedPaint;
        canvas.DrawCircle(center, radius, mainPaint);

        // Inner highlight
        if (isOpen)
        {
            using var highlightPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Shader = SKShader.CreateRadialGradient(
                    new SKPoint(center.X - radius * 0.3f, center.Y - radius * 0.3f),
                    radius * 1.5f,
                    new[] { new SKColor(255, 255, 255, 80), SKColors.Transparent },
                    null,
                    SKShaderTileMode.Clamp)
            };
            canvas.DrawCircle(center, radius * 0.8f, highlightPaint);
        }

        // Rim
        canvas.DrawCircle(center, radius, _rimPaint);

        // Label
        if (showLabel)
        {
            string label = isOpen ? "OPEN" : "CLOSED";
            _labelPaint.Color = isOpen ? _theme.GateOpen : _theme.TextMuted;
            canvas.DrawText(label, center.X, center.Y + radius + 16, _labelPaint);
        }
    }

    /// <summary>
    /// Render a compact horizontal gate indicator bar.
    /// </summary>
    public void RenderBar(
        SKCanvas canvas,
        SKRect rect,
        bool isOpen,
        string? label = null)
    {
        var roundRect = new SKRoundRect(rect, 4f);

        // Background
        using var bgPaint = new SKPaint
        {
            Color = _theme.GateClosedDim,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawRoundRect(roundRect, bgPaint);

        // Fill when open
        if (isOpen)
        {
            canvas.DrawRoundRect(roundRect, _openPaint);

            // Glow effect
            using var barGlowPaint = new SKPaint
            {
                Color = _theme.GateOpenGlow,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 4f,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4f)
            };
            canvas.DrawRoundRect(roundRect, barGlowPaint);
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

        // Label
        if (!string.IsNullOrEmpty(label))
        {
            using var textPaint = new SkiaTextPaint(isOpen ? _theme.TextPrimary : _theme.TextMuted, 10f, SKFontStyle.Bold, SKTextAlign.Center);
            canvas.DrawText(label, rect.MidX, rect.MidY + 4, textPaint);
        }
    }

    public void Dispose()
    {
        _openPaint.Dispose();
        _closedPaint.Dispose();
        _glowPaint.Dispose();
        _rimPaint.Dispose();
        _labelPaint.Dispose();
    }
}
