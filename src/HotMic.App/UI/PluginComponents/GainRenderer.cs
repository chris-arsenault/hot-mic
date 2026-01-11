using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Simple, clean gain plugin UI with large knob and input/output meters.
/// </summary>
public sealed class GainRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 16f;
    private const float KnobRadius = 50f;
    private const float MeterWidth = 24f;
    private const float MeterHeight = 140f;
    private const float CornerRadius = 10f;

    private readonly PluginComponentTheme _theme;

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _titlePaint;
    private readonly SKPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _knobBackgroundPaint;
    private readonly SKPaint _knobTrackPaint;
    private readonly SKPaint _knobArcPaint;
    private readonly SKPaint _knobPointerPaint;
    private readonly SKPaint _knobCenterPaint;
    private readonly SKPaint _meterBackgroundPaint;
    private readonly SKPaint _meterFillPaint;
    private readonly SKPaint _meterPeakPaint;
    private readonly SKPaint _labelPaint;
    private readonly SKPaint _valuePaint;
    private readonly SKPaint _unitPaint;
    private readonly SKPaint _phaseButtonPaint;
    private readonly SKPaint _phaseActivePaint;
    private readonly SKPaint _latencyPaint;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;
    private SKPoint _knobCenter;
    private SKRect _phaseButtonRect;

    public GainRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;

        _backgroundPaint = new SKPaint
        {
            Color = _theme.PanelBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _titleBarPaint = new SKPaint
        {
            Color = _theme.PanelBackgroundLight,
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

        _titlePaint = new SKPaint
        {
            Color = _theme.TextPrimary,
            IsAntialias = true,
            TextSize = 14f,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };

        _closeButtonPaint = new SKPaint
        {
            Color = _theme.TextSecondary,
            IsAntialias = true,
            TextSize = 18f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };

        _bypassPaint = new SKPaint
        {
            Color = _theme.PanelBackgroundLight,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _bypassActivePaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0x50, 0x50),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _knobBackgroundPaint = new SKPaint
        {
            Color = _theme.KnobBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _knobTrackPaint = new SKPaint
        {
            Color = _theme.KnobTrack,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 8f,
            StrokeCap = SKStrokeCap.Round
        };

        _knobArcPaint = new SKPaint
        {
            Color = _theme.KnobArc,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 8f,
            StrokeCap = SKStrokeCap.Round
        };

        _knobPointerPaint = new SKPaint
        {
            Color = _theme.KnobPointer,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 4f,
            StrokeCap = SKStrokeCap.Round
        };

        _knobCenterPaint = new SKPaint
        {
            Color = _theme.KnobHighlight,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _meterBackgroundPaint = new SKPaint
        {
            Color = _theme.WaveformBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _meterFillPaint = new SKPaint
        {
            Color = _theme.WaveformLine,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _meterPeakPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0x50, 0x50),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
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
            TextSize = 24f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };

        _unitPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };

        _phaseButtonPaint = new SKPaint
        {
            Color = _theme.PanelBackgroundLight,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _phaseActivePaint = new SKPaint
        {
            Color = _theme.KnobArc,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _latencyPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            TextSize = 9f,
            TextAlign = SKTextAlign.Right,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };
    }

    public void Render(
        SKCanvas canvas,
        SKSize size,
        float dpiScale,
        GainState state)
    {
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        canvas.Scale(dpiScale);
        size = new SKSize(size.Width / dpiScale, size.Height / dpiScale);

        // Main background
        var backgroundRect = new SKRect(0, 0, size.Width, size.Height);
        var roundRect = new SKRoundRect(backgroundRect, CornerRadius);
        canvas.DrawRoundRect(roundRect, _backgroundPaint);

        // Title bar
        _titleBarRect = new SKRect(0, 0, size.Width, TitleBarHeight);
        using (var titleClip = new SKPath())
        {
            titleClip.AddRoundRect(new SKRoundRect(_titleBarRect, CornerRadius, CornerRadius));
            titleClip.AddRect(new SKRect(0, CornerRadius, size.Width, TitleBarHeight));
            canvas.Save();
            canvas.ClipPath(titleClip);
            canvas.DrawRect(_titleBarRect, _titleBarPaint);
            canvas.Restore();
        }
        canvas.DrawLine(0, TitleBarHeight, size.Width, TitleBarHeight, _borderPaint);

        // Title
        canvas.DrawText("Gain", Padding, TitleBarHeight / 2f + 5, _titlePaint);

        // Bypass button
        float bypassWidth = 60f;
        _bypassButtonRect = new SKRect(
            size.Width - Padding - 30 - bypassWidth - 8,
            (TitleBarHeight - 24) / 2,
            size.Width - Padding - 30 - 8,
            (TitleBarHeight + 24) / 2);
        var bypassRound = new SKRoundRect(_bypassButtonRect, 4f);
        canvas.DrawRoundRect(bypassRound, state.IsBypassed ? _bypassActivePaint : _bypassPaint);
        canvas.DrawRoundRect(bypassRound, _borderPaint);

        using var bypassTextPaint = new SKPaint
        {
            Color = state.IsBypassed ? _theme.TextPrimary : _theme.TextSecondary,
            IsAntialias = true,
            TextSize = 10f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };
        canvas.DrawText("BYPASS", _bypassButtonRect.MidX, _bypassButtonRect.MidY + 4, bypassTextPaint);

        if (state.LatencyMs >= 0f)
        {
            string latencyLabel = $"LAT {state.LatencyMs:0.0}ms";
            canvas.DrawText(latencyLabel, _bypassButtonRect.Left - 6f, TitleBarHeight / 2f + 4, _latencyPaint);
        }

        // Close button
        _closeButtonRect = new SKRect(size.Width - Padding - 24, (TitleBarHeight - 24) / 2,
            size.Width - Padding, (TitleBarHeight + 24) / 2);
        canvas.DrawText("\u00D7", _closeButtonRect.MidX, _closeButtonRect.MidY + 6, _closeButtonPaint);

        float contentTop = TitleBarHeight + Padding;
        float contentHeight = size.Height - TitleBarHeight - Padding * 2;

        // Input meter (left side)
        float meterY = contentTop + (contentHeight - MeterHeight) / 2 - 10;
        var inputMeterRect = new SKRect(Padding, meterY, Padding + MeterWidth, meterY + MeterHeight);
        DrawMeter(canvas, inputMeterRect, state.InputLevel, "IN");

        // Output meter (right side)
        var outputMeterRect = new SKRect(size.Width - Padding - MeterWidth, meterY,
            size.Width - Padding, meterY + MeterHeight);
        DrawMeter(canvas, outputMeterRect, state.OutputLevel, "OUT");

        // Large center knob
        _knobCenter = new SKPoint(size.Width / 2, contentTop + contentHeight / 2 - 20);
        DrawLargeKnob(canvas, _knobCenter, state.GainDb, state.IsKnobHovered);

        // Phase invert button below knob
        float phaseY = _knobCenter.Y + KnobRadius + 50;
        _phaseButtonRect = new SKRect(
            size.Width / 2 - 40,
            phaseY,
            size.Width / 2 + 40,
            phaseY + 28);
        var phaseRound = new SKRoundRect(_phaseButtonRect, 4f);
        canvas.DrawRoundRect(phaseRound, state.IsPhaseInverted ? _phaseActivePaint : _phaseButtonPaint);
        canvas.DrawRoundRect(phaseRound, _borderPaint);

        using var phaseTextPaint = new SKPaint
        {
            Color = state.IsPhaseInverted ? _theme.PanelBackground : _theme.TextSecondary,
            IsAntialias = true,
            TextSize = 11f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };
        canvas.DrawText("\u00D8 PHASE", _phaseButtonRect.MidX, _phaseButtonRect.MidY + 4, phaseTextPaint);

        // Outer border
        canvas.DrawRoundRect(roundRect, _borderPaint);

        canvas.Restore();
    }

    private void DrawLargeKnob(SKCanvas canvas, SKPoint center, float gainDb, bool isHovered)
    {
        const float startAngle = 135f;
        const float sweepAngle = 270f;
        float arcRadius = KnobRadius * 0.85f;

        // Shadow
        using var shadowPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 40),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6f)
        };
        canvas.DrawCircle(center.X + 3, center.Y + 3, KnobRadius, shadowPaint);

        // Knob background
        canvas.DrawCircle(center, KnobRadius, _knobBackgroundPaint);

        // Track (background arc)
        using var trackPath = new SKPath();
        trackPath.AddArc(
            new SKRect(center.X - arcRadius, center.Y - arcRadius, center.X + arcRadius, center.Y + arcRadius),
            startAngle, sweepAngle);
        canvas.DrawPath(trackPath, _knobTrackPaint);

        // Value arc - from center (0dB) to current value
        // Normalized: -24 to +24 maps to 0 to 1, with 0.5 being center (0dB)
        float normalizedValue = (gainDb + 24f) / 48f;
        float centerAngle = startAngle + sweepAngle * 0.5f; // 0dB position

        if (MathF.Abs(gainDb) > 0.1f)
        {
            float valueAngle = startAngle + sweepAngle * normalizedValue;
            float arcStart = gainDb > 0 ? centerAngle : valueAngle;
            float arcSweep = gainDb > 0 ? valueAngle - centerAngle : centerAngle - valueAngle;

            // Color based on boost/cut
            _knobArcPaint.Color = gainDb > 0
                ? _theme.KnobArc
                : _theme.WaveformLine;

            using var arcPath = new SKPath();
            arcPath.AddArc(
                new SKRect(center.X - arcRadius, center.Y - arcRadius, center.X + arcRadius, center.Y + arcRadius),
                arcStart, arcSweep);
            canvas.DrawPath(arcPath, _knobArcPaint);
        }

        // Center marker line at 0dB
        float centerRad = centerAngle * MathF.PI / 180f;
        float markerInner = arcRadius - 12f;
        float markerOuter = arcRadius + 4f;
        using var centerMarkerPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f
        };
        canvas.DrawLine(
            center.X + markerInner * MathF.Cos(centerRad),
            center.Y + markerInner * MathF.Sin(centerRad),
            center.X + markerOuter * MathF.Cos(centerRad),
            center.Y + markerOuter * MathF.Sin(centerRad),
            centerMarkerPaint);

        // Inner circle with gradient
        float innerRadius = KnobRadius * 0.65f;
        using var innerPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(center.X - innerRadius * 0.2f, center.Y - innerRadius * 0.2f),
                innerRadius * 2,
                new[] { _theme.KnobHighlight, _theme.KnobBackground },
                null,
                SKShaderTileMode.Clamp)
        };
        canvas.DrawCircle(center, innerRadius, innerPaint);

        // Pointer
        float pointerAngle = startAngle + sweepAngle * normalizedValue;
        float pointerRad = pointerAngle * MathF.PI / 180f;
        float pointerStartRadius = innerRadius * 0.3f;
        float pointerEndRadius = innerRadius * 0.85f;
        canvas.DrawLine(
            center.X + pointerStartRadius * MathF.Cos(pointerRad),
            center.Y + pointerStartRadius * MathF.Sin(pointerRad),
            center.X + pointerEndRadius * MathF.Cos(pointerRad),
            center.Y + pointerEndRadius * MathF.Sin(pointerRad),
            _knobPointerPaint);

        // Hover highlight
        if (isHovered)
        {
            using var hoverPaint = new SKPaint
            {
                Color = _theme.KnobArc.WithAlpha(40),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3f
            };
            canvas.DrawCircle(center, KnobRadius + 4, hoverPaint);
        }

        // Value display below knob
        string sign = gainDb > 0.05f ? "+" : "";
        string valueText = $"{sign}{gainDb:0.0}";
        canvas.DrawText(valueText, center.X, center.Y + KnobRadius + 24, _valuePaint);
        canvas.DrawText("dB", center.X, center.Y + KnobRadius + 40, _unitPaint);

        // Label above knob
        canvas.DrawText("GAIN", center.X, center.Y - KnobRadius - 12, _labelPaint);
    }

    private void DrawMeter(SKCanvas canvas, SKRect rect, float level, string label)
    {
        // Background
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _meterBackgroundPaint);

        // Convert to dB for display
        float levelDb = 20f * MathF.Log10(level + 1e-10f);
        levelDb = MathF.Max(levelDb, -60f);

        // Normalize to 0-1 (range -60 to 0 dB)
        float normalizedLevel = (levelDb + 60f) / 60f;
        normalizedLevel = MathF.Min(normalizedLevel, 1f);

        float meterPadding = 3f;
        float innerHeight = rect.Height - meterPadding * 2;
        float fillHeight = innerHeight * normalizedLevel;

        // Meter fill (from bottom up)
        if (fillHeight > 0)
        {
            var fillRect = new SKRect(
                rect.Left + meterPadding,
                rect.Bottom - meterPadding - fillHeight,
                rect.Right - meterPadding,
                rect.Bottom - meterPadding);

            // Gradient from green to yellow to red
            using var gradientPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, rect.Bottom),
                    new SKPoint(0, rect.Top),
                    new[]
                    {
                        _theme.WaveformLine,           // Green at bottom
                        _theme.WaveformLine,           // Green
                        new SKColor(0xFF, 0xD7, 0x00), // Yellow
                        new SKColor(0xFF, 0x50, 0x50)  // Red at top
                    },
                    new[] { 0f, 0.6f, 0.85f, 1f },
                    SKShaderTileMode.Clamp)
            };
            canvas.DrawRect(fillRect, gradientPaint);
        }

        // Border
        canvas.DrawRoundRect(roundRect, _borderPaint);

        // Label
        canvas.DrawText(label, rect.MidX, rect.Bottom + 16, _labelPaint);

        // Level value
        string dbText = levelDb > -59f ? $"{levelDb:0}" : "-\u221E";
        using var dbPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            TextSize = 9f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };
        canvas.DrawText(dbText, rect.MidX, rect.Top - 4, dbPaint);
    }

    public GainHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new GainHitTest(GainHitArea.CloseButton);

        if (_bypassButtonRect.Contains(x, y))
            return new GainHitTest(GainHitArea.BypassButton);

        if (_phaseButtonRect.Contains(x, y))
            return new GainHitTest(GainHitArea.PhaseButton);

        float dx = x - _knobCenter.X;
        float dy = y - _knobCenter.Y;
        if (dx * dx + dy * dy <= KnobRadius * KnobRadius * 1.5f)
        {
            return new GainHitTest(GainHitArea.Knob);
        }

        if (_titleBarRect.Contains(x, y))
            return new GainHitTest(GainHitArea.TitleBar);

        return new GainHitTest(GainHitArea.None);
    }

    public static SKSize GetPreferredSize() => new(280, 320);

    public void Dispose()
    {
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _knobBackgroundPaint.Dispose();
        _knobTrackPaint.Dispose();
        _knobArcPaint.Dispose();
        _knobPointerPaint.Dispose();
        _knobCenterPaint.Dispose();
        _meterBackgroundPaint.Dispose();
        _meterFillPaint.Dispose();
        _meterPeakPaint.Dispose();
        _labelPaint.Dispose();
        _valuePaint.Dispose();
        _unitPaint.Dispose();
        _phaseButtonPaint.Dispose();
        _phaseActivePaint.Dispose();
        _latencyPaint.Dispose();
    }
}

/// <summary>
/// State data for rendering the gain UI.
/// </summary>
public record struct GainState(
    float GainDb,
    float InputLevel,
    float OutputLevel,
    bool IsPhaseInverted,
    float LatencyMs,
    bool IsBypassed,
    bool IsKnobHovered = false);

public enum GainHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    PhaseButton,
    Knob
}

public record struct GainHitTest(GainHitArea Area);
