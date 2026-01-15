using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Options for customizing knob rendering style.
/// </summary>
public record struct KnobStyle
{
    /// <summary>Standard knob with shadow, inner circle, and labels.</summary>
    public static KnobStyle Standard => new() { ShowShadow = true, ShowInnerCircle = true, ShowLabels = true };

    /// <summary>Compact knob without shadow, inner circle, or labels.</summary>
    public static KnobStyle Compact => new() { ShowShadow = false, ShowInnerCircle = false, ShowLabels = false };

    /// <summary>Bipolar knob that draws arc from center (for +/- values like gain).</summary>
    public static KnobStyle Bipolar => new() { ShowShadow = true, ShowInnerCircle = true, ShowLabels = true, IsBipolar = true };

    /// <summary>Whether to render drop shadow.</summary>
    public bool ShowShadow { get; init; }

    /// <summary>Whether to render inner gradient circle.</summary>
    public bool ShowInnerCircle { get; init; }

    /// <summary>Whether to render label above and value/unit below.</summary>
    public bool ShowLabels { get; init; }

    /// <summary>Whether arc draws from center (0.5) rather than start.</summary>
    public bool IsBipolar { get; init; }

    /// <summary>Custom arc color override (null uses theme default).</summary>
    public SKColor? ArcColor { get; init; }

    /// <summary>Custom track stroke width (0 uses default based on radius).</summary>
    public float TrackWidth { get; init; }

    /// <summary>Custom arc stroke width (0 uses default based on radius).</summary>
    public float ArcWidth { get; init; }

    /// <summary>Custom pointer stroke width (0 uses default based on radius).</summary>
    public float PointerWidth { get; init; }
}

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
    private readonly SkiaTextPaint _labelPaint;
    private readonly SkiaTextPaint _valuePaint;
    private readonly SkiaTextPaint _unitPaint;
    private readonly SKPaint _shadowPaint;
    private readonly SKPaint _tooltipBgPaint;
    private readonly SkiaTextPaint _tooltipTextPaint;

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

        _labelPaint = new SkiaTextPaint(_theme.TextSecondary, 11f, SKFontStyle.Normal, SKTextAlign.Center);
        _valuePaint = new SkiaTextPaint(_theme.TextPrimary, 13f, SKFontStyle.Bold, SKTextAlign.Center);
        _unitPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Center);

        _tooltipBgPaint = new SKPaint
        {
            Color = new SKColor(30, 30, 30, 230),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _tooltipTextPaint = new SkiaTextPaint(SKColors.White, 12f, SKFontStyle.Normal, SKTextAlign.Center);
    }

    /// <summary>
    /// Render a rotary knob with default standard style.
    /// </summary>
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
        Render(canvas, center, radius, normalizedValue, label, displayValue, unit, isHovered, KnobStyle.Standard);
    }

    /// <summary>
    /// Render a rotary knob with custom style.
    /// </summary>
    /// <param name="canvas">Canvas to draw on</param>
    /// <param name="center">Center point of the knob</param>
    /// <param name="radius">Radius of the knob</param>
    /// <param name="normalizedValue">Value from 0 to 1</param>
    /// <param name="label">Parameter name</param>
    /// <param name="displayValue">Formatted value string</param>
    /// <param name="unit">Optional unit string</param>
    /// <param name="isHovered">Whether the knob is hovered</param>
    /// <param name="style">Knob rendering style options</param>
    public void Render(
        SKCanvas canvas,
        SKPoint center,
        float radius,
        float normalizedValue,
        string label,
        string displayValue,
        string? unit = null,
        bool isHovered = false,
        KnobStyle style = default)
    {
        normalizedValue = Math.Clamp(normalizedValue, 0f, 1f);
        float arcRadius = radius * 0.8f;

        // Calculate stroke widths based on radius (scale down for smaller knobs)
        float baseTrackWidth = style.TrackWidth > 0 ? style.TrackWidth : Math.Max(2f, radius * 0.12f);
        float baseArcWidth = style.ArcWidth > 0 ? style.ArcWidth : baseTrackWidth;
        float basePointerWidth = style.PointerWidth > 0 ? style.PointerWidth : Math.Max(1.5f, radius * 0.09f);

        // Shadow
        if (style.ShowShadow)
        {
            canvas.DrawCircle(center.X + 2, center.Y + 2, radius, _shadowPaint);
        }

        // Knob background
        canvas.DrawCircle(center, radius, _backgroundPaint);

        // Track (background arc)
        _trackPaint.StrokeWidth = baseTrackWidth;
        using var trackPath = new SKPath();
        trackPath.AddArc(
            new SKRect(center.X - arcRadius, center.Y - arcRadius, center.X + arcRadius, center.Y + arcRadius),
            StartAngle,
            SweepAngle);
        canvas.DrawPath(trackPath, _trackPaint);

        // Value arc
        SKColor arcColor = style.ArcColor ?? _theme.KnobArc;
        _arcPaint.StrokeWidth = baseArcWidth;

        if (style.IsBipolar)
        {
            // Bipolar: draw from center (0.5) outward
            float centerAngle = StartAngle + SweepAngle * 0.5f;
            float deviation = normalizedValue - 0.5f;

            if (MathF.Abs(deviation) > 0.001f)
            {
                float arcStart, arcSweep;
                if (deviation > 0)
                {
                    arcStart = centerAngle;
                    arcSweep = SweepAngle * deviation;
                    _arcPaint.Color = arcColor;
                }
                else
                {
                    arcStart = centerAngle + SweepAngle * deviation;
                    arcSweep = -SweepAngle * deviation;
                    _arcPaint.Color = style.ArcColor ?? _theme.WaveformLine; // Use different color for negative
                }

                using var arcPath = new SKPath();
                arcPath.AddArc(
                    new SKRect(center.X - arcRadius, center.Y - arcRadius, center.X + arcRadius, center.Y + arcRadius),
                    arcStart,
                    arcSweep);
                canvas.DrawPath(arcPath, _arcPaint);
            }

            // Draw center marker for bipolar
            float centerRad = centerAngle * MathF.PI / 180f;
            float markerInner = arcRadius - baseTrackWidth * 2f;
            float markerOuter = arcRadius + baseTrackWidth * 0.5f;
            using var centerMarkerPaint = new SKPaint
            {
                Color = _theme.TextMuted,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1.5f, baseTrackWidth * 0.5f)
            };
            canvas.DrawLine(
                center.X + markerInner * MathF.Cos(centerRad),
                center.Y + markerInner * MathF.Sin(centerRad),
                center.X + markerOuter * MathF.Cos(centerRad),
                center.Y + markerOuter * MathF.Sin(centerRad),
                centerMarkerPaint);
        }
        else
        {
            // Standard: draw from start
            if (normalizedValue > 0.001f)
            {
                _arcPaint.Color = arcColor;
                float valueAngle = SweepAngle * normalizedValue;
                using var arcPath = new SKPath();
                arcPath.AddArc(
                    new SKRect(center.X - arcRadius, center.Y - arcRadius, center.X + arcRadius, center.Y + arcRadius),
                    StartAngle,
                    valueAngle);
                canvas.DrawPath(arcPath, _arcPaint);
            }
        }

        // Inner circle
        float innerRadius = radius * 0.6f;
        if (style.ShowInnerCircle)
        {
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
        }

        // Pointer
        float pointerAngle = StartAngle + (SweepAngle * normalizedValue);
        float pointerRad = pointerAngle * MathF.PI / 180f;
        float pointerStartRadius = style.ShowInnerCircle ? innerRadius * 0.3f : 0f;
        float pointerEndRadius = style.ShowInnerCircle ? innerRadius * PointerLength : arcRadius - baseTrackWidth;
        _pointerPaint.StrokeWidth = basePointerWidth;
        canvas.DrawLine(
            center.X + pointerStartRadius * MathF.Cos(pointerRad),
            center.Y + pointerStartRadius * MathF.Sin(pointerRad),
            center.X + pointerEndRadius * MathF.Cos(pointerRad),
            center.Y + pointerEndRadius * MathF.Sin(pointerRad),
            _pointerPaint);

        // Highlight ring when hovered
        if (isHovered)
        {
            using var hoverPaint = new SKPaint
            {
                Color = arcColor.WithAlpha(60),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1.5f, baseTrackWidth * 0.5f)
            };
            canvas.DrawCircle(center, radius + 2, hoverPaint);

            // Hover tooltip with DSP value
            DrawTooltip(canvas, center, radius, displayValue, unit);
        }

        // Labels (if enabled)
        if (style.ShowLabels)
        {
            canvas.DrawText(label, center.X, center.Y - radius - 8, _labelPaint);
            canvas.DrawText(displayValue, center.X, center.Y + radius + 16, _valuePaint);
            if (!string.IsNullOrEmpty(unit))
            {
                canvas.DrawText(unit, center.X, center.Y + radius + 28, _unitPaint);
            }
        }
    }

    /// <summary>
    /// Draw a tooltip showing the DSP value above the knob.
    /// </summary>
    private void DrawTooltip(SKCanvas canvas, SKPoint center, float radius, string value, string? unit)
    {
        string tooltipText = string.IsNullOrEmpty(unit) ? value : $"{value} {unit}";
        float textWidth = _tooltipTextPaint.MeasureText(tooltipText);
        float padding = 6f;
        float tooltipWidth = textWidth + padding * 2;
        float tooltipHeight = 20f;
        float tooltipY = center.Y - radius - 32f;

        var tooltipRect = new SKRect(
            center.X - tooltipWidth / 2,
            tooltipY - tooltipHeight / 2,
            center.X + tooltipWidth / 2,
            tooltipY + tooltipHeight / 2);

        canvas.DrawRoundRect(tooltipRect, 4f, 4f, _tooltipBgPaint);
        canvas.DrawText(tooltipText, center.X, tooltipY + 4f, _tooltipTextPaint);
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
        _tooltipBgPaint.Dispose();
        _tooltipTextPaint.Dispose();
    }
}
