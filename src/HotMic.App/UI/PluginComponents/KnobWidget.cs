using System;
using System.Windows;
using System.Windows.Input;
using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Self-contained knob widget that handles both rendering and user interaction.
/// Supports drag-to-change, hover tooltip, and right-click value editing.
/// </summary>
public sealed class KnobWidget : IDisposable
{
    private const float StartAngle = 135f;
    private const float SweepAngle = 270f;

    private readonly PluginComponentTheme _theme;
    private readonly KnobValueEditor _editor;
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _trackPaint;
    private readonly SKPaint _arcPaint;
    private readonly SKPaint _pointerPaint;
    private readonly SKPaint _shadowPaint;
    private readonly SkiaTextPaint _labelPaint;
    private readonly SkiaTextPaint _valuePaint;
    private readonly SkiaTextPaint _unitPaint;
    private readonly SKPaint _tooltipBgPaint;
    private readonly SkiaTextPaint _tooltipTextPaint;

    private bool _isDragging;
    private float _dragStartY;
    private float _dragStartValue;

    /// <summary>Center position of the knob.</summary>
    public SKPoint Center { get; set; }

    /// <summary>Radius of the knob.</summary>
    public float Radius { get; }

    /// <summary>Current value in DSP units.</summary>
    public float Value { get; set; }

    /// <summary>Minimum allowed value.</summary>
    public float MinValue { get; }

    /// <summary>Maximum allowed value.</summary>
    public float MaxValue { get; }

    /// <summary>Label displayed above the knob.</summary>
    public string Label { get; }

    /// <summary>Unit string (e.g., "dB", "Hz", "%").</summary>
    public string Unit { get; }

    /// <summary>Rendering style options.</summary>
    public KnobStyle Style { get; set; }

    /// <summary>Whether the knob is currently hovered.</summary>
    public bool IsHovered { get; private set; }

    /// <summary>Whether the knob is currently being dragged.</summary>
    public bool IsDragging => _isDragging;

    /// <summary>Drag sensitivity (default 0.004).</summary>
    public float DragSensitivity { get; set; } = 0.004f;

    /// <summary>Fine-tune sensitivity multiplier when Shift is held (default 0.1).</summary>
    public float FineTuneFactor { get; set; } = 0.1f;

    /// <summary>Default value for double-click reset. If null, uses MinValue.</summary>
    public float? DefaultValue { get; set; }

    /// <summary>Whether to use logarithmic scaling for value normalization.</summary>
    public bool IsLogarithmic { get; set; }

    /// <summary>Format string for displaying the value (default "0.0").</summary>
    public string ValueFormat { get; set; } = "0.0";

    /// <summary>Whether to show + prefix for positive values.</summary>
    public bool ShowPositiveSign { get; set; }

    /// <summary>Fired when the value changes due to user interaction.</summary>
    public event Action<float>? ValueChanged;

    public KnobWidget(
        float radius,
        float minValue,
        float maxValue,
        string label,
        string unit = "",
        KnobStyle style = default,
        PluginComponentTheme? theme = null)
    {
        Radius = radius;
        MinValue = minValue;
        MaxValue = maxValue;
        Label = label;
        Unit = unit;
        Style = style;
        Value = minValue;

        _theme = theme ?? PluginComponentTheme.Default;
        _editor = new KnobValueEditor();

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
    /// Render the knob to the canvas.
    /// </summary>
    public void Render(SKCanvas canvas)
    {
        float normalized = Normalize(Value);
        float arcRadius = Radius * 0.8f;

        // Calculate stroke widths based on radius
        float baseTrackWidth = Style.TrackWidth > 0 ? Style.TrackWidth : Math.Max(2f, Radius * 0.12f);
        float baseArcWidth = Style.ArcWidth > 0 ? Style.ArcWidth : baseTrackWidth;
        float basePointerWidth = Style.PointerWidth > 0 ? Style.PointerWidth : Math.Max(1.5f, Radius * 0.09f);

        // Shadow
        if (Style.ShowShadow)
        {
            canvas.DrawCircle(Center.X + 2, Center.Y + 2, Radius, _shadowPaint);
        }

        // Background
        canvas.DrawCircle(Center, Radius, _backgroundPaint);

        // Track
        _trackPaint.StrokeWidth = baseTrackWidth;
        using var trackPath = new SKPath();
        trackPath.AddArc(
            new SKRect(Center.X - arcRadius, Center.Y - arcRadius, Center.X + arcRadius, Center.Y + arcRadius),
            StartAngle, SweepAngle);
        canvas.DrawPath(trackPath, _trackPaint);

        // Value arc
        SKColor arcColor = Style.ArcColor ?? _theme.KnobArc;
        _arcPaint.StrokeWidth = baseArcWidth;

        if (Style.IsBipolar)
        {
            DrawBipolarArc(canvas, arcRadius, normalized, arcColor);
        }
        else
        {
            DrawStandardArc(canvas, arcRadius, normalized, arcColor);
        }

        // Inner circle
        float innerRadius = Radius * 0.6f;
        if (Style.ShowInnerCircle)
        {
            using var innerPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Shader = SKShader.CreateRadialGradient(
                    new SKPoint(Center.X - innerRadius * 0.3f, Center.Y - innerRadius * 0.3f),
                    innerRadius * 2,
                    new[] { _theme.KnobHighlight, _theme.KnobBackground },
                    null,
                    SKShaderTileMode.Clamp)
            };
            canvas.DrawCircle(Center, innerRadius, innerPaint);
        }

        // Pointer
        float pointerAngle = StartAngle + (SweepAngle * normalized);
        float pointerRad = pointerAngle * MathF.PI / 180f;
        float pointerStartRadius = Style.ShowInnerCircle ? innerRadius * 0.3f : 0f;
        float pointerEndRadius = Style.ShowInnerCircle ? innerRadius * 0.75f : arcRadius - baseTrackWidth;
        _pointerPaint.StrokeWidth = basePointerWidth;
        canvas.DrawLine(
            Center.X + pointerStartRadius * MathF.Cos(pointerRad),
            Center.Y + pointerStartRadius * MathF.Sin(pointerRad),
            Center.X + pointerEndRadius * MathF.Cos(pointerRad),
            Center.Y + pointerEndRadius * MathF.Sin(pointerRad),
            _pointerPaint);

        // Hover highlight
        if (IsHovered)
        {
            using var hoverPaint = new SKPaint
            {
                Color = arcColor.WithAlpha(60),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1.5f, baseTrackWidth * 0.5f)
            };
            canvas.DrawCircle(Center, Radius + 2, hoverPaint);

            // Only show tooltip when labels are hidden (compact mode)
            // When labels are shown, value is already visible below the knob
            if (!Style.ShowLabels)
            {
                DrawTooltip(canvas);
            }
        }

        // Labels (label above, value+unit below)
        if (Style.ShowLabels)
        {
            canvas.DrawText(Label, Center.X, Center.Y - Radius - 10, _labelPaint);
            canvas.DrawText(FormatValue(), Center.X, Center.Y + Radius + 18, _valuePaint);
            if (!string.IsNullOrEmpty(Unit))
            {
                canvas.DrawText(Unit, Center.X, Center.Y + Radius + 30, _unitPaint);
            }
        }
    }

    private void DrawStandardArc(SKCanvas canvas, float arcRadius, float normalized, SKColor arcColor)
    {
        if (normalized > 0.001f)
        {
            _arcPaint.Color = arcColor;
            float valueAngle = SweepAngle * normalized;
            using var arcPath = new SKPath();
            arcPath.AddArc(
                new SKRect(Center.X - arcRadius, Center.Y - arcRadius, Center.X + arcRadius, Center.Y + arcRadius),
                StartAngle, valueAngle);
            canvas.DrawPath(arcPath, _arcPaint);
        }
    }

    private void DrawBipolarArc(SKCanvas canvas, float arcRadius, float normalized, SKColor arcColor)
    {
        float centerAngle = StartAngle + SweepAngle * 0.5f;
        float deviation = normalized - 0.5f;

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
                _arcPaint.Color = Style.ArcColor ?? _theme.WaveformLine;
            }

            using var arcPath = new SKPath();
            arcPath.AddArc(
                new SKRect(Center.X - arcRadius, Center.Y - arcRadius, Center.X + arcRadius, Center.Y + arcRadius),
                arcStart, arcSweep);
            canvas.DrawPath(arcPath, _arcPaint);
        }

        // Center marker
        float baseTrackWidth = Style.TrackWidth > 0 ? Style.TrackWidth : Math.Max(2f, Radius * 0.12f);
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
            Center.X + markerInner * MathF.Cos(centerRad),
            Center.Y + markerInner * MathF.Sin(centerRad),
            Center.X + markerOuter * MathF.Cos(centerRad),
            Center.Y + markerOuter * MathF.Sin(centerRad),
            centerMarkerPaint);
    }

    private void DrawTooltip(SKCanvas canvas)
    {
        string tooltipText = string.IsNullOrEmpty(Unit) ? FormatValue() : $"{FormatValue()} {Unit}";
        float textWidth = _tooltipTextPaint.MeasureText(tooltipText);
        float padding = 6f;
        float tooltipWidth = textWidth + padding * 2;
        float tooltipHeight = 20f;
        float tooltipY = Center.Y - Radius - 32f;

        var tooltipRect = new SKRect(
            Center.X - tooltipWidth / 2,
            tooltipY - tooltipHeight / 2,
            Center.X + tooltipWidth / 2,
            tooltipY + tooltipHeight / 2);

        canvas.DrawRoundRect(tooltipRect, 4f, 4f, _tooltipBgPaint);
        canvas.DrawText(tooltipText, Center.X, tooltipY + 4f, _tooltipTextPaint);
    }

    /// <summary>
    /// Test if a point is within the knob's hit area.
    /// </summary>
    public bool HitTest(float x, float y)
    {
        float dx = x - Center.X;
        float dy = y - Center.Y;
        float hitRadius = Radius + 4; // Slight padding for easier clicking
        return dx * dx + dy * dy <= hitRadius * hitRadius;
    }

    /// <summary>
    /// Handle mouse down event. Returns true if the event was handled.
    /// </summary>
    /// <param name="x">Mouse X position</param>
    /// <param name="y">Mouse Y position</param>
    /// <param name="button">Mouse button</param>
    /// <param name="target">UIElement for popup placement (needed for right-click edit)</param>
    public bool HandleMouseDown(float x, float y, MouseButton button, UIElement? target = null)
    {
        if (!HitTest(x, y))
            return false;

        if (button == MouseButton.Right && target != null)
        {
            // Right-click: show value editor
            _editor.Show(
                target,
                new SKPoint(x, y),
                Value,
                MinValue,
                MaxValue,
                Unit,
                newValue =>
                {
                    Value = newValue;
                    ValueChanged?.Invoke(Value);
                },
                IsLogarithmic);
            return true;
        }

        if (button == MouseButton.Left)
        {
            // Left-click: start drag
            _isDragging = true;
            _dragStartY = y;
            _dragStartValue = Normalize(Value);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Handle mouse move event. Returns true if the knob state changed.
    /// </summary>
    /// <param name="x">Mouse X position</param>
    /// <param name="y">Mouse Y position</param>
    /// <param name="isLeftButtonDown">Whether left mouse button is down</param>
    /// <param name="isShiftHeld">Whether Shift key is held for fine-tune mode</param>
    public bool HandleMouseMove(float x, float y, bool isLeftButtonDown, bool isShiftHeld = false)
    {
        bool stateChanged = false;

        if (_isDragging && isLeftButtonDown)
        {
            float sensitivity = isShiftHeld ? DragSensitivity * FineTuneFactor : DragSensitivity;
            float deltaY = _dragStartY - y;
            float newNormalized = Math.Clamp(_dragStartValue + deltaY * sensitivity, 0f, 1f);
            float newValue = Denormalize(newNormalized);

            if (MathF.Abs(newValue - Value) > 0.0001f)
            {
                Value = newValue;
                ValueChanged?.Invoke(Value);
                stateChanged = true;
            }
        }
        else
        {
            bool wasHovered = IsHovered;
            IsHovered = HitTest(x, y);
            stateChanged = wasHovered != IsHovered;
        }

        return stateChanged;
    }

    /// <summary>
    /// Handle mouse up event.
    /// </summary>
    public void HandleMouseUp(MouseButton button)
    {
        if (button == MouseButton.Left)
        {
            _isDragging = false;
        }
    }

    /// <summary>
    /// Handle double-click event to reset to default value. Returns true if handled.
    /// </summary>
    public bool HandleDoubleClick(float x, float y)
    {
        if (!HitTest(x, y))
            return false;

        float resetValue = DefaultValue ?? MinValue;
        if (MathF.Abs(Value - resetValue) > 0.0001f)
        {
            Value = resetValue;
            ValueChanged?.Invoke(Value);
        }

        return true;
    }

    /// <summary>
    /// Update hover state based on mouse position.
    /// </summary>
    public bool UpdateHover(float x, float y)
    {
        bool wasHovered = IsHovered;
        IsHovered = HitTest(x, y);
        return wasHovered != IsHovered;
    }

    private float Normalize(float value)
    {
        if (IsLogarithmic && MinValue > 0)
        {
            return MathF.Log(value / MinValue) / MathF.Log(MaxValue / MinValue);
        }
        return (value - MinValue) / (MaxValue - MinValue);
    }

    private float Denormalize(float normalized)
    {
        if (IsLogarithmic && MinValue > 0)
        {
            return MinValue * MathF.Pow(MaxValue / MinValue, normalized);
        }
        return MinValue + normalized * (MaxValue - MinValue);
    }

    private string FormatValue()
    {
        string formatted;
        if (Unit.Equals("Hz", StringComparison.OrdinalIgnoreCase) && Value >= 1000)
        {
            formatted = $"{Value / 1000f:0.0}k";
        }
        else
        {
            formatted = Value.ToString(ValueFormat);
        }

        if (ShowPositiveSign && Value > 0.05f)
        {
            formatted = "+" + formatted;
        }

        return formatted;
    }

    public void Dispose()
    {
        _editor.Dispose();
        _backgroundPaint.Dispose();
        _trackPaint.Dispose();
        _arcPaint.Dispose();
        _pointerPaint.Dispose();
        _shadowPaint.Dispose();
        _labelPaint.Dispose();
        _valuePaint.Dispose();
        _unitPaint.Dispose();
        _tooltipBgPaint.Dispose();
        _tooltipTextPaint.Dispose();
    }
}
