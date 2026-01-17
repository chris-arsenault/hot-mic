using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Air Exciter plugin UI with shimmer meter, sidechain gate indicator, and saturation curve display.
/// </summary>
public sealed class AirExciterRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 12f;
    private const float KnobRadius = 24f;
    private const float KnobSpacing = 72f;
    private const float CornerRadius = 10f;
    private const float MeterHeight = 100f;
    private const float CurveSize = 60f;

    private readonly PluginComponentTheme _theme;
    private readonly PluginPresetBar _presetBar;

    public KnobWidget DriveKnob { get; }
    public KnobWidget MixKnob { get; }
    public KnobWidget CutoffKnob { get; }

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _meterBackgroundPaint;
    private readonly SKPaint _meterFillPaint;
    private readonly SKPaint _gateLedOnPaint;
    private readonly SKPaint _gateLedOffPaint;
    private readonly SKPaint _gateLedGlowPaint;
    private readonly SKPaint _curvePaint;
    private readonly SKPaint _curveBackgroundPaint;
    private readonly SkiaTextPaint _labelPaint;
    private readonly SkiaTextPaint _latencyPaint;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;

    public AirExciterRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _presetBar = new PluginPresetBar(_theme);

        DriveKnob = new KnobWidget(KnobRadius, 0f, 1f, "DRIVE", "", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0.00"
        };
        MixKnob = new KnobWidget(KnobRadius, 0f, 1f, "MIX", "", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0.00"
        };
        CutoffKnob = new KnobWidget(KnobRadius, 3000f, 10000f, "CUTOFF", "Hz", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0",
            IsLogarithmic = true
        };

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

        _titlePaint = new SkiaTextPaint(_theme.TextPrimary, 14f, SKFontStyle.Bold);
        _closeButtonPaint = new SkiaTextPaint(_theme.TextSecondary, 18f, SKFontStyle.Normal, SKTextAlign.Center);

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

        _meterBackgroundPaint = new SKPaint
        {
            Color = _theme.WaveformBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _meterFillPaint = new SKPaint
        {
            Color = new SKColor(0x80, 0xD4, 0xFF), // Light blue shimmer
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _gateLedOnPaint = new SKPaint
        {
            Color = _theme.GateOpen,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _gateLedOffPaint = new SKPaint
        {
            Color = _theme.GateClosed,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _gateLedGlowPaint = new SKPaint
        {
            Color = _theme.GateOpenGlow,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6f)
        };

        _curvePaint = new SKPaint
        {
            Color = _theme.KnobArc,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            StrokeCap = SKStrokeCap.Round
        };

        _curveBackgroundPaint = new SKPaint
        {
            Color = _theme.WaveformBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _labelPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Normal, SKTextAlign.Center);
        _latencyPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Right);
    }

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, AirExciterState state)
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
        DrawTitleBar(canvas, size, state);

        float y = TitleBarHeight + Padding;

        // Gate indicator LED and label
        float gateX = Padding + 20f;
        float gateY = y + 20f;
        bool gateOpen = state.GateLevel > 0.3f;

        if (gateOpen)
        {
            canvas.DrawCircle(gateX, gateY, 10f, _gateLedGlowPaint);
            canvas.DrawCircle(gateX, gateY, 6f, _gateLedOnPaint);
        }
        else
        {
            canvas.DrawCircle(gateX, gateY, 6f, _gateLedOffPaint);
        }
        canvas.DrawText("VOICED", gateX, gateY + 20f, _labelPaint);

        // Shimmer meter (HF energy)
        float meterX = gateX + 40f;
        float meterWidth = 24f;
        var meterRect = new SKRect(meterX, y, meterX + meterWidth, y + MeterHeight);
        DrawShimmerMeter(canvas, meterRect, state.HfEnergy, state.SaturationAmount);

        // Saturation curve display
        float curveX = meterX + meterWidth + 20f;
        var curveRect = new SKRect(curveX, y + 20f, curveX + CurveSize, y + 20f + CurveSize);
        DrawSaturationCurve(canvas, curveRect, state.Drive);

        y += MeterHeight + Padding;

        // Knobs
        float knobsY = y + KnobRadius + 10;
        float knobsTotalWidth = 3 * KnobSpacing;
        float knobsStartX = (size.Width - knobsTotalWidth) / 2 + KnobSpacing / 2;

        DriveKnob.Center = new SKPoint(knobsStartX, knobsY);
        DriveKnob.Value = state.Drive;
        DriveKnob.Render(canvas);

        MixKnob.Center = new SKPoint(knobsStartX + KnobSpacing, knobsY);
        MixKnob.Value = state.Mix;
        MixKnob.Render(canvas);

        CutoffKnob.Center = new SKPoint(knobsStartX + KnobSpacing * 2, knobsY);
        CutoffKnob.Value = state.CutoffHz;
        CutoffKnob.Render(canvas);

        // Outer border
        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.Restore();
    }

    private void DrawTitleBar(SKCanvas canvas, SKSize size, AirExciterState state)
    {
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

        canvas.DrawText("Air Exciter", Padding, TitleBarHeight / 2f + 5, _titlePaint);

        float presetBarX = 100f;
        float presetBarY = (TitleBarHeight - PluginPresetBar.TotalHeight) / 2f;
        _presetBar.Render(canvas, presetBarX, presetBarY, state.PresetName);

        float bypassWidth = 60f;
        _bypassButtonRect = new SKRect(
            size.Width - Padding - 30 - bypassWidth - 8,
            (TitleBarHeight - 24) / 2,
            size.Width - Padding - 30 - 8,
            (TitleBarHeight + 24) / 2);
        var bypassRound = new SKRoundRect(_bypassButtonRect, 4f);
        canvas.DrawRoundRect(bypassRound, state.IsBypassed ? _bypassActivePaint : _bypassPaint);
        canvas.DrawRoundRect(bypassRound, _borderPaint);

        using var bypassTextPaint = new SkiaTextPaint(state.IsBypassed ? _theme.TextPrimary : _theme.TextSecondary, 10f, SKFontStyle.Bold, SKTextAlign.Center);
        canvas.DrawText("BYPASS", _bypassButtonRect.MidX, _bypassButtonRect.MidY + 4, bypassTextPaint);

        if (state.LatencyMs >= 0f)
        {
            string latencyLabel = $"LAT {state.LatencyMs:0.0}ms";
            canvas.DrawText(latencyLabel, _bypassButtonRect.Left - 6f, TitleBarHeight / 2f + 4, _latencyPaint);
        }

        _closeButtonRect = new SKRect(size.Width - Padding - 24, (TitleBarHeight - 24) / 2,
            size.Width - Padding, (TitleBarHeight + 24) / 2);
        canvas.DrawText("\u00D7", _closeButtonRect.MidX, _closeButtonRect.MidY + 6, _closeButtonPaint);
    }

    private void DrawShimmerMeter(SKCanvas canvas, SKRect rect, float hfEnergy, float saturation)
    {
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _meterBackgroundPaint);

        // HF energy bar (from bottom up)
        float fillHeight = rect.Height * Math.Clamp(hfEnergy * 3f, 0f, 1f);
        if (fillHeight > 1)
        {
            var fillRect = new SKRect(rect.Left + 2, rect.Bottom - 2 - fillHeight, rect.Right - 2, rect.Bottom - 2);
            using var gradient = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, fillRect.Bottom),
                    new SKPoint(0, fillRect.Top),
                    new[] { new SKColor(0x40, 0x80, 0xC0), new SKColor(0xA0, 0xE0, 0xFF) },
                    null,
                    SKShaderTileMode.Clamp)
            };
            canvas.DrawRect(fillRect, gradient);
        }

        // Saturation indicator overlay (brighter when saturating)
        if (saturation > 0.01f)
        {
            byte alpha = (byte)(Math.Clamp(saturation * 2f, 0f, 1f) * 60);
            using var satPaint = new SKPaint
            {
                Color = new SKColor(0xFF, 0xFF, 0xFF, alpha),
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            canvas.DrawRoundRect(roundRect, satPaint);
        }

        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.DrawText("HF", rect.MidX, rect.Bottom + 14, _labelPaint);
    }

    private void DrawSaturationCurve(SKCanvas canvas, SKRect rect, float drive)
    {
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _curveBackgroundPaint);

        // Draw tanh saturation curve
        using var path = new SKPath();
        float padding = 4f;
        float plotWidth = rect.Width - padding * 2;
        float plotHeight = rect.Height - padding * 2;
        float driveAmount = 1.5f + drive * 4f;

        bool first = true;
        for (int i = 0; i <= 20; i++)
        {
            float t = i / 20f;
            float inputX = t * 2f - 1f; // -1 to 1
            float output = MathF.Tanh(inputX * driveAmount);
            float x = rect.Left + padding + t * plotWidth;
            float y = rect.Bottom - padding - (output + 1f) * 0.5f * plotHeight;

            if (first)
            {
                path.MoveTo(x, y);
                first = false;
            }
            else
            {
                path.LineTo(x, y);
            }
        }

        canvas.DrawPath(path, _curvePaint);
        canvas.DrawRoundRect(roundRect, _borderPaint);
    }

    public AirExciterHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new AirExciterHitTest(AirExciterHitArea.CloseButton, -1);

        if (_bypassButtonRect.Contains(x, y))
            return new AirExciterHitTest(AirExciterHitArea.BypassButton, -1);

        var presetHit = _presetBar.HitTest(x, y);
        if (presetHit == PresetBarHitArea.Dropdown)
            return new AirExciterHitTest(AirExciterHitArea.PresetDropdown, -1);
        if (presetHit == PresetBarHitArea.SaveButton)
            return new AirExciterHitTest(AirExciterHitArea.PresetSave, -1);

        if (DriveKnob.HitTest(x, y))
            return new AirExciterHitTest(AirExciterHitArea.Knob, 0);
        if (MixKnob.HitTest(x, y))
            return new AirExciterHitTest(AirExciterHitArea.Knob, 1);
        if (CutoffKnob.HitTest(x, y))
            return new AirExciterHitTest(AirExciterHitArea.Knob, 2);

        if (_titleBarRect.Contains(x, y))
            return new AirExciterHitTest(AirExciterHitArea.TitleBar, -1);

        return new AirExciterHitTest(AirExciterHitArea.None, -1);
    }

    public SKRect GetPresetDropdownRect() => _presetBar.GetDropdownRect();

    public static SKSize GetPreferredSize() => new(300, 280);

    public void Dispose()
    {
        DriveKnob.Dispose();
        MixKnob.Dispose();
        CutoffKnob.Dispose();
        _presetBar.Dispose();
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _meterBackgroundPaint.Dispose();
        _meterFillPaint.Dispose();
        _gateLedOnPaint.Dispose();
        _gateLedOffPaint.Dispose();
        _gateLedGlowPaint.Dispose();
        _curvePaint.Dispose();
        _curveBackgroundPaint.Dispose();
        _labelPaint.Dispose();
        _latencyPaint.Dispose();
    }
}

public record struct AirExciterState(
    float Drive,
    float Mix,
    float CutoffHz,
    float GateLevel,
    float HfEnergy,
    float SaturationAmount,
    float LatencyMs,
    bool IsBypassed,
    string PresetName = "Custom");

public enum AirExciterHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    Knob,
    PresetDropdown,
    PresetSave
}

public record struct AirExciterHitTest(AirExciterHitArea Area, int KnobIndex);
