using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Renders an animated AI processing indicator with pulse effect.
/// Shows processing status and activity level.
/// </summary>
public sealed class AiProcessingIndicator : IDisposable
{
    private readonly PluginComponentTheme _theme;
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _ringPaint;
    private readonly SKPaint _activePaint;
    private readonly SKPaint _inactivePaint;
    private readonly SKPaint _pulsePaint;
    private readonly SkiaTextPaint _labelPaint;

    private float _pulsePhase;
    private const float PulseSpeed = 0.08f;

    public AiProcessingIndicator(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;

        _backgroundPaint = new SKPaint
        {
            Color = _theme.MeterBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _ringPaint = new SKPaint
        {
            Color = _theme.PanelBorder,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3f
        };

        _activePaint = new SKPaint
        {
            Color = new SKColor(0x4A, 0xFF, 0x4A),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _inactivePaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _pulsePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f
        };

        _labelPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Bold, SKTextAlign.Center);
    }

    /// <summary>
    /// Render the processing indicator.
    /// </summary>
    /// <param name="canvas">The canvas to render to.</param>
    /// <param name="center">Center point of the indicator.</param>
    /// <param name="radius">Radius of the indicator.</param>
    /// <param name="isActive">Whether processing is active.</param>
    /// <param name="activityLevel">Activity level (0-1) for pulse intensity.</param>
    /// <param name="label">Label to display below.</param>
    public void Render(SKCanvas canvas, SKPoint center, float radius, bool isActive, float activityLevel, string label)
    {
        // Background circle
        canvas.DrawCircle(center, radius, _backgroundPaint);

        // Pulse animation when active
        if (isActive && activityLevel > 0.1f)
        {
            _pulsePhase += PulseSpeed;
            if (_pulsePhase > 1f) _pulsePhase = 0f;

            float pulseRadius = radius + _pulsePhase * radius * 0.5f;
            byte alpha = (byte)(255 * (1f - _pulsePhase) * activityLevel);
            _pulsePaint.Color = new SKColor(0x4A, 0xFF, 0x4A, alpha);
            canvas.DrawCircle(center, pulseRadius, _pulsePaint);
        }

        // Ring
        _ringPaint.Color = isActive ? new SKColor(0x4A, 0xFF, 0x4A) : _theme.PanelBorder;
        canvas.DrawCircle(center, radius, _ringPaint);

        // Inner fill
        float innerRadius = radius * 0.6f;
        canvas.DrawCircle(center, innerRadius, isActive ? _activePaint : _inactivePaint);

        // AI icon (simplified brain/circuit pattern)
        if (isActive)
        {
            using var iconPaint = new SKPaint
            {
                Color = _theme.PanelBackground,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2f,
                StrokeCap = SKStrokeCap.Round
            };

            float s = innerRadius * 0.5f;
            // Draw simple neural network pattern
            canvas.DrawLine(center.X - s, center.Y, center.X + s, center.Y, iconPaint);
            canvas.DrawLine(center.X, center.Y - s, center.X, center.Y + s, iconPaint);
            canvas.DrawCircle(center.X - s * 0.7f, center.Y - s * 0.5f, 2f, iconPaint);
            canvas.DrawCircle(center.X + s * 0.7f, center.Y + s * 0.5f, 2f, iconPaint);
        }

        // Label
        if (!string.IsNullOrEmpty(label))
        {
            _labelPaint.Color = isActive ? _theme.TextPrimary : _theme.TextMuted;
            canvas.DrawText(label, center.X, center.Y + radius + 16f, _labelPaint);
        }
    }

    public void Dispose()
    {
        _backgroundPaint.Dispose();
        _ringPaint.Dispose();
        _activePaint.Dispose();
        _inactivePaint.Dispose();
        _pulsePaint.Dispose();
        _labelPaint.Dispose();
    }
}
