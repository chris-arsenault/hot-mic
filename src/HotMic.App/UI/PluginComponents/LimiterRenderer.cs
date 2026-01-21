using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Renders a limiter plugin UI with gain reduction meter, ceiling indicator, and level meters.
/// </summary>
public sealed class LimiterRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 16f;
    private const float KnobRadius = 36f;
    private const float MeterWidth = 24f;
    private const float MeterHeight = 140f;
    private const float GrMeterWidth = 40f;
    private const float CornerRadius = 10f;

    private readonly PluginComponentTheme _theme;
    private readonly PluginPresetBar _presetBar;
    private readonly LevelMeter _inputMeter;
    private readonly LevelMeter _outputMeter;

    // Knob widgets
    public KnobWidget CeilingKnob { get; }
    public KnobWidget ReleaseKnob { get; }

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _meterBackgroundPaint;
    private readonly SKPaint _meterFillPaint;
    private readonly SKPaint _ceilingLinePaint;
    private readonly SKPaint _grMeterFillPaint;
    private readonly SkiaTextPaint _labelPaint;
    private readonly SkiaTextPaint _grValuePaint;
    private readonly SkiaTextPaint _latencyPaint;
    private readonly SKPaint _limitingIndicatorPaint;
    private readonly SKPaint _limitingGlowPaint;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;

    public LimiterRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _presetBar = new PluginPresetBar(_theme);
        _inputMeter = new LevelMeter();
        _outputMeter = new LevelMeter();

        // Initialize knob widgets
        CeilingKnob = new KnobWidget(KnobRadius, -3f, 0f, "CEILING", "dB", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0.0",
            DragSensitivity = 0.004f
        };
        ReleaseKnob = new KnobWidget(KnobRadius, 10f, 200f, "RELEASE", "ms", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0",
            DragSensitivity = 0.004f
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
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _ceilingLinePaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0x50, 0x50),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f
        };

        _grMeterFillPaint = new SKPaint
        {
            Color = _theme.KnobArc,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _labelPaint = new SkiaTextPaint(_theme.TextSecondary, 11f, SKFontStyle.Normal, SKTextAlign.Center);
        _grValuePaint = new SkiaTextPaint(_theme.TextPrimary, 16f, SKFontStyle.Bold, SKTextAlign.Center);
        _latencyPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Right);

        _limitingIndicatorPaint = new SKPaint
        {
            Color = _theme.KnobArc,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _limitingGlowPaint = new SKPaint
        {
            Color = _theme.KnobArc.WithAlpha(80),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8f)
        };
    }

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, LimiterState state)
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
        canvas.DrawText("Limiter", Padding, TitleBarHeight / 2f + 5, _titlePaint);

        // Preset bar
        float presetBarX = Padding + 60;
        float presetBarY = (TitleBarHeight - PluginPresetBar.TotalHeight) / 2f;
        _presetBar.Render(canvas, presetBarX, presetBarY, state.PresetName);

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

        using var bypassTextPaint = new SkiaTextPaint(state.IsBypassed ? _theme.TextPrimary : _theme.TextSecondary, 10f, SKFontStyle.Bold, SKTextAlign.Center);
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
        float meterY = contentTop + 20;
        var inputMeterRect = new SKRect(Padding, meterY, Padding + MeterWidth, meterY + MeterHeight);
        _inputMeter.Update(state.InputLevel);
        _inputMeter.Render(canvas, inputMeterRect, MeterOrientation.Vertical);
        DrawCeilingLine(canvas, inputMeterRect, state.CeilingDb);
        canvas.DrawText("IN", inputMeterRect.MidX, inputMeterRect.Bottom + 16, _labelPaint);

        // Output meter (right side of input)
        var outputMeterRect = new SKRect(inputMeterRect.Right + 8, meterY, inputMeterRect.Right + 8 + MeterWidth, meterY + MeterHeight);
        _outputMeter.Update(state.OutputLevel);
        _outputMeter.Render(canvas, outputMeterRect, MeterOrientation.Vertical);
        DrawCeilingLine(canvas, outputMeterRect, state.CeilingDb);
        canvas.DrawText("OUT", outputMeterRect.MidX, outputMeterRect.Bottom + 16, _labelPaint);

        // Gain reduction meter (center-left)
        float grMeterX = outputMeterRect.Right + 20;
        var grMeterRect = new SKRect(grMeterX, meterY, grMeterX + GrMeterWidth, meterY + MeterHeight);
        DrawGainReductionMeter(canvas, grMeterRect, state.GainReductionDb);

        // Limiting indicator
        bool isLimiting = state.GainReductionDb < -0.5f;
        float indicatorY = contentTop + 8;
        float indicatorX = grMeterRect.MidX;
        if (isLimiting)
        {
            canvas.DrawCircle(indicatorX, indicatorY, 8f, _limitingGlowPaint);
            canvas.DrawCircle(indicatorX, indicatorY, 5f, _limitingIndicatorPaint);
        }
        else
        {
            using var dimPaint = new SKPaint
            {
                Color = _theme.GateClosed,
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            canvas.DrawCircle(indicatorX, indicatorY, 5f, dimPaint);
        }

        // Knobs (right side)
        float knobAreaX = grMeterRect.Right + 24;
        float knobY = meterY + 50;
        float knobSpacing = 90f;

        // Ceiling knob
        CeilingKnob.Center = new SKPoint(knobAreaX + KnobRadius + 10, knobY);
        CeilingKnob.Value = state.CeilingDb;
        CeilingKnob.Render(canvas);

        // Release knob
        ReleaseKnob.Center = new SKPoint(knobAreaX + KnobRadius + 10, knobY + knobSpacing);
        ReleaseKnob.Value = state.ReleaseMs;
        ReleaseKnob.Render(canvas);

        // Outer border
        canvas.DrawRoundRect(roundRect, _borderPaint);

        canvas.Restore();
    }

    private void DrawCeilingLine(SKCanvas canvas, SKRect rect, float ceilingDb)
    {
        // Draw ceiling reference line on top of the meter
        float meterPadding = 2f;
        float innerHeight = rect.Height - meterPadding * 2;
        float ceilingNorm = (ceilingDb + 60f) / 60f;
        float ceilingY = rect.Bottom - meterPadding - innerHeight * ceilingNorm;
        canvas.DrawLine(rect.Left, ceilingY, rect.Right, ceilingY, _ceilingLinePaint);
    }

    private void DrawGainReductionMeter(SKCanvas canvas, SKRect rect, float grDb)
    {
        // Background
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _meterBackgroundPaint);

        // GR meter shows 0 to -20 dB of reduction (from top down)
        float normalizedGr = MathF.Abs(MathF.Max(grDb, -20f)) / 20f;
        float meterPadding = 3f;
        float innerHeight = rect.Height - meterPadding * 2;
        float fillHeight = innerHeight * normalizedGr;

        if (fillHeight > 0)
        {
            var fillRect = new SKRect(
                rect.Left + meterPadding,
                rect.Top + meterPadding,
                rect.Right - meterPadding,
                rect.Top + meterPadding + fillHeight);

            canvas.DrawRect(fillRect, _grMeterFillPaint);
        }

        // Border
        canvas.DrawRoundRect(roundRect, _borderPaint);

        // Label
        canvas.DrawText("GR", rect.MidX, rect.Bottom + 16, _labelPaint);

        // Value below
        string grText = grDb < -0.1f ? $"{grDb:0.0}" : "0.0";
        canvas.DrawText(grText, rect.MidX, rect.Bottom + 32, _grValuePaint);

        using var unitPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Center);
        canvas.DrawText("dB", rect.MidX, rect.Bottom + 44, unitPaint);
    }

    public LimiterHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new LimiterHitTest(LimiterHitArea.CloseButton, LimiterKnob.None);

        if (_bypassButtonRect.Contains(x, y))
            return new LimiterHitTest(LimiterHitArea.BypassButton, LimiterKnob.None);

        var presetHit = _presetBar.HitTest(x, y);
        if (presetHit == PresetBarHitArea.Dropdown)
            return new LimiterHitTest(LimiterHitArea.PresetDropdown, LimiterKnob.None);
        if (presetHit == PresetBarHitArea.SaveButton)
            return new LimiterHitTest(LimiterHitArea.PresetSave, LimiterKnob.None);

        if (CeilingKnob.HitTest(x, y))
            return new LimiterHitTest(LimiterHitArea.Knob, LimiterKnob.Ceiling);
        if (ReleaseKnob.HitTest(x, y))
            return new LimiterHitTest(LimiterHitArea.Knob, LimiterKnob.Release);

        if (_titleBarRect.Contains(x, y))
            return new LimiterHitTest(LimiterHitArea.TitleBar, LimiterKnob.None);

        return new LimiterHitTest(LimiterHitArea.None, LimiterKnob.None);
    }

    public SKRect GetPresetDropdownRect() => _presetBar.GetDropdownRect();

    public static SKSize GetPreferredSize() => new(320, 280);

    public void Dispose()
    {
        _inputMeter.Dispose();
        _outputMeter.Dispose();
        CeilingKnob.Dispose();
        ReleaseKnob.Dispose();
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
        _ceilingLinePaint.Dispose();
        _grMeterFillPaint.Dispose();
        _labelPaint.Dispose();
        _grValuePaint.Dispose();
        _latencyPaint.Dispose();
        _limitingIndicatorPaint.Dispose();
        _limitingGlowPaint.Dispose();
    }
}

public record struct LimiterState(
    float CeilingDb,
    float ReleaseMs,
    float InputLevel,
    float OutputLevel,
    float GainReductionDb,
    float LatencyMs,
    bool IsBypassed,
    string PresetName = "Custom");

public enum LimiterHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    Knob,
    PresetDropdown,
    PresetSave
}

public enum LimiterKnob
{
    None,
    Ceiling,
    Release
}

public record struct LimiterHitTest(LimiterHitArea Area, LimiterKnob Knob);
