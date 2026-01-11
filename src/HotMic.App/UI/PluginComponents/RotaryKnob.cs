using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Renders a styled rotary knob control for plugin parameters.
/// </summary>
public sealed class RotaryKnob : IDisposable
{
    private const float StartAngle = 135f;  // Start at bottom-left
    private const float SweepAngle = 270f;  // Sweep to bottom-right
    private const float PointerLength = 0.75f;

    private readonly PluginComponentTheme _theme;
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _trackPaint;
    private readonly SKPaint _arcPaint;
    private readonly SKPaint _pointerPaint;
    private readonly SKPaint _highlightPaint;
    private readonly SKPaint _labelPaint;
    private readonly SKPaint _valuePaint;
    private readonly SKPaint _unitPaint;
    private readonly SKPaint _shadowPaint;

    public RotaryKnob(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;

        _backgroundPaint = new SKPaint
        {
            Color = _theme.KnobBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _trackPaint = new SKPaint
        {
            Color = _theme.KnobTrack,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 4f,
            StrokeCap = SKStrokeCap.Round
        };

        _arcPaint = new SKPaint
        {
            Color = _theme.KnobArc,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 4f,
            StrokeCap = SKStrokeCap.Round
        };

        _pointerPaint = new SKPaint
        {
            Color = _theme.KnobPointer,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3f,
            StrokeCap = SKStrokeCap.Round
        };

        _highlightPaint = new SKPaint
        {
            Color = _theme.KnobHighlight,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };

        _shadowPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 60),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4f)
        };

        _labelPaint = new SKPaint
        {
            Color = _theme.TextSecondary,
            IsAntialias = true,
            TextSize = 11f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };

        _valuePaint = new SKPaint
        {
            Color = _theme.TextPrimary,
            IsAntialias = true,
            TextSize = 13f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };

        _unitPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            TextSize = 9f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };
    }

    /// <summary>
    /// Render a rotary knob.
    /// </summary>
    /// <param name="canvas">Canvas to draw on</param>
    /// <param name="center">Center point of the knob</param>
    /// <param name="radius">Radius of the knob</param>
    /// <param name="normalizedValue">Value from 0 to 1</param>
    /// <param name="label">Parameter name</param>
    /// <param name="displayValue">Formatted value string</param>
    /// <param name="unit">Optional unit string</param>
    /// <param name="isHovered">Whether the knob is hovered</param>
    public void Render(
        SKCanvas canvas,
        SKPoint center,
        float radius,
        float normalizedValue,
        string label,
        string displayValue,
        string? unit = null,
        bool isHovered = false)
    {
        normalizedValue = Math.Clamp(normalizedValue, 0f, 1f);
        float arcRadius = radius * 0.8f;

        // Shadow
        canvas.DrawCircle(center.X + 2, center.Y + 2, radius, _shadowPaint);

        // Knob background
        canvas.DrawCircle(center, radius, _backgroundPaint);

        // Track (background arc)
        using var trackPath = new SKPath();
        trackPath.AddArc(
            new SKRect(center.X - arcRadius, center.Y - arcRadius, center.X + arcRadius, center.Y + arcRadius),
            StartAngle,
            SweepAngle);
        canvas.DrawPath(trackPath, _trackPaint);

        // Value arc
        if (normalizedValue > 0.001f)
        {
            float valueAngle = SweepAngle * normalizedValue;
            using var arcPath = new SKPath();
            arcPath.AddArc(
                new SKRect(center.X - arcRadius, center.Y - arcRadius, center.X + arcRadius, center.Y + arcRadius),
                StartAngle,
                valueAngle);
            canvas.DrawPath(arcPath, _arcPaint);
        }

        // Inner circle
        float innerRadius = radius * 0.6f;
        using var innerPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(center.X - innerRadius * 0.3f, center.Y - innerRadius * 0.3f),
                innerRadius * 2,
                new[] { _theme.KnobHighlight, _theme.KnobBackground },
                null,
                SKShaderTileMode.Clamp)
        };
        canvas.DrawCircle(center, innerRadius, innerPaint);

        // Pointer
        float pointerAngle = StartAngle + (SweepAngle * normalizedValue);
        float pointerRad = pointerAngle * MathF.PI / 180f;
        float pointerStartRadius = innerRadius * 0.3f;
        float pointerEndRadius = innerRadius * PointerLength;
        SKPoint pointerStart = new(
            center.X + pointerStartRadius * MathF.Cos(pointerRad),
            center.Y + pointerStartRadius * MathF.Sin(pointerRad));
        SKPoint pointerEnd = new(
            center.X + pointerEndRadius * MathF.Cos(pointerRad),
            center.Y + pointerEndRadius * MathF.Sin(pointerRad));
        canvas.DrawLine(pointerStart, pointerEnd, _pointerPaint);

        // Highlight ring when hovered
        if (isHovered)
        {
            using var hoverPaint = new SKPaint
            {
                Color = _theme.KnobArc.WithAlpha(60),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2f
            };
            canvas.DrawCircle(center, radius + 2, hoverPaint);
        }

        // Label above
        canvas.DrawText(label, center.X, center.Y - radius - 8, _labelPaint);

        // Value below
        canvas.DrawText(displayValue, center.X, center.Y + radius + 16, _valuePaint);

        // Unit below value
        if (!string.IsNullOrEmpty(unit))
        {
            canvas.DrawText(unit, center.X, center.Y + radius + 28, _unitPaint);
        }
    }

    /// <summary>
    /// Get the hit rect for a knob at the given position.
    /// </summary>
    public SKRect GetHitRect(SKPoint center, float radius)
    {
        return new SKRect(
            center.X - radius,
            center.Y - radius - 16,  // Include label
            center.X + radius,
            center.Y + radius + 32); // Include value/unit
    }

    /// <summary>
    /// Calculate normalized value from drag delta.
    /// Vertical drag: up increases, down decreases.
    /// </summary>
    public static float CalculateValueFromDrag(float currentValue, float deltaY, float sensitivity = 0.005f)
    {
        return Math.Clamp(currentValue - (deltaY * sensitivity), 0f, 1f);
    }

    public void Dispose()
    {
        _backgroundPaint.Dispose();
        _trackPaint.Dispose();
        _arcPaint.Dispose();
        _pointerPaint.Dispose();
        _highlightPaint.Dispose();
        _shadowPaint.Dispose();
        _labelPaint.Dispose();
        _valuePaint.Dispose();
        _unitPaint.Dispose();
    }
}
