using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Formant Enhancer plugin UI with F1/F2/F3 frequency tracking bars and speech presence meter.
/// </summary>
public sealed class FormantEnhancerRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 12f;
    private const float KnobRadius = 24f;
    private const float KnobSpacing = 72f;
    private const float CornerRadius = 10f;
    private const float FormantDisplayHeight = 90f;

    private readonly PluginComponentTheme _theme;
    private readonly PluginPresetBar _presetBar;

    public KnobWidget AmountKnob { get; }
    public KnobWidget BoostKnob { get; }
    public KnobWidget SmoothingKnob { get; }

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _formantBackgroundPaint;
    private readonly SKPaint _f1Paint;
    private readonly SKPaint _f2Paint;
    private readonly SKPaint _f3Paint;
    private readonly SKPaint _speechLedOnPaint;
    private readonly SKPaint _speechLedOffPaint;
    private readonly SKPaint _speechLedGlowPaint;
    private readonly SkiaTextPaint _labelPaint;
    private readonly SkiaTextPaint _freqValuePaint;
    private readonly SkiaTextPaint _latencyPaint;
    private readonly SkiaTextPaint _statusPaint;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;

    public FormantEnhancerRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _presetBar = new PluginPresetBar(_theme);

        AmountKnob = new KnobWidget(KnobRadius, 0f, 1f, "AMOUNT", "", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0.00"
        };
        BoostKnob = new KnobWidget(KnobRadius, 0f, 4f, "BOOST", "dB", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0.0"
        };
        SmoothingKnob = new KnobWidget(KnobRadius, 40f, 200f, "SMOOTH", "ms", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0"
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

        _formantBackgroundPaint = new SKPaint
        {
            Color = _theme.WaveformBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _f1Paint = new SKPaint
        {
            Color = new SKColor(0xFF, 0x60, 0x60), // Red for F1
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _f2Paint = new SKPaint
        {
            Color = new SKColor(0x60, 0xFF, 0x60), // Green for F2
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _f3Paint = new SKPaint
        {
            Color = new SKColor(0x60, 0x80, 0xFF), // Blue for F3
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _speechLedOnPaint = new SKPaint
        {
            Color = _theme.GateOpen,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _speechLedOffPaint = new SKPaint
        {
            Color = _theme.GateClosed,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _speechLedGlowPaint = new SKPaint
        {
            Color = _theme.GateOpenGlow,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6f)
        };

        _labelPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Normal, SKTextAlign.Center);
        _freqValuePaint = new SkiaTextPaint(_theme.TextPrimary, 11f, SKFontStyle.Bold, SKTextAlign.Right);
        _latencyPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Right);
        _statusPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Normal, SKTextAlign.Center);
    }

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, FormantEnhancerState state)
    {
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        canvas.Scale(dpiScale);
        size = new SKSize(size.Width / dpiScale, size.Height / dpiScale);

        var backgroundRect = new SKRect(0, 0, size.Width, size.Height);
        var roundRect = new SKRoundRect(backgroundRect, CornerRadius);
        canvas.DrawRoundRect(roundRect, _backgroundPaint);

        DrawTitleBar(canvas, size, state);

        float y = TitleBarHeight + Padding;

        // Status message if any
        if (!string.IsNullOrEmpty(state.StatusMessage))
        {
            _statusPaint.Color = new SKColor(0xFF, 0xA0, 0x40);
            canvas.DrawText(state.StatusMessage, size.Width / 2, y + 10, _statusPaint);
            y += 24;
        }

        // Speech presence LED
        float ledX = Padding + 15f;
        float ledY = y + 15f;
        bool speechActive = state.SpeechPresence > 0.3f;

        if (speechActive)
        {
            canvas.DrawCircle(ledX, ledY, 10f, _speechLedGlowPaint);
            canvas.DrawCircle(ledX, ledY, 6f, _speechLedOnPaint);
        }
        else
        {
            canvas.DrawCircle(ledX, ledY, 6f, _speechLedOffPaint);
        }
        canvas.DrawText("SPEECH", ledX, ledY + 18f, _labelPaint);

        // Formant frequency display
        float formantX = ledX + 50f;
        float formantWidth = size.Width - formantX - Padding;
        var formantRect = new SKRect(formantX, y, formantX + formantWidth, y + FormantDisplayHeight);
        DrawFormantBars(canvas, formantRect, state.F1Hz, state.F2Hz, state.F3Hz);

        y += FormantDisplayHeight + Padding + 10f;

        // Knobs
        float knobsY = y + KnobRadius + 10;
        float knobsTotalWidth = 3 * KnobSpacing;
        float knobsStartX = (size.Width - knobsTotalWidth) / 2 + KnobSpacing / 2;

        AmountKnob.Center = new SKPoint(knobsStartX, knobsY);
        AmountKnob.Value = state.Amount;
        AmountKnob.Render(canvas);

        BoostKnob.Center = new SKPoint(knobsStartX + KnobSpacing, knobsY);
        BoostKnob.Value = state.BoostDb;
        BoostKnob.Render(canvas);

        SmoothingKnob.Center = new SKPoint(knobsStartX + KnobSpacing * 2, knobsY);
        SmoothingKnob.Value = state.SmoothingMs;
        SmoothingKnob.Render(canvas);

        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.Restore();
    }

    private void DrawTitleBar(SKCanvas canvas, SKSize size, FormantEnhancerState state)
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

        canvas.DrawText("Formant Enhancer", Padding, TitleBarHeight / 2f + 5, _titlePaint);

        float presetBarX = 130f;
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

    private void DrawFormantBars(SKCanvas canvas, SKRect rect, float f1Hz, float f2Hz, float f3Hz)
    {
        var roundRect = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(roundRect, _formantBackgroundPaint);

        float padding = 8f;
        float barHeight = 20f;
        float barSpacing = 6f;
        float labelWidth = 30f;
        float valueWidth = 50f;
        float barMaxWidth = rect.Width - padding * 2 - labelWidth - valueWidth - 8f;

        // F1 bar (300-900 Hz range, typically vowel height)
        float f1Y = rect.Top + padding;
        DrawFormantBar(canvas, rect.Left + padding + labelWidth, f1Y, barMaxWidth, barHeight,
            f1Hz, 300f, 900f, "F1", _f1Paint);

        // F2 bar (800-2400 Hz range, typically vowel frontness)
        float f2Y = f1Y + barHeight + barSpacing;
        DrawFormantBar(canvas, rect.Left + padding + labelWidth, f2Y, barMaxWidth, barHeight,
            f2Hz, 800f, 2400f, "F2", _f2Paint);

        // F3 bar (2400-3400 Hz range)
        float f3Y = f2Y + barHeight + barSpacing;
        DrawFormantBar(canvas, rect.Left + padding + labelWidth, f3Y, barMaxWidth, barHeight,
            f3Hz, 2400f, 3400f, "F3", _f3Paint);

        canvas.DrawRoundRect(roundRect, _borderPaint);
    }

    private void DrawFormantBar(SKCanvas canvas, float x, float y, float maxWidth, float height,
        float freqHz, float minHz, float maxHz, string label, SKPaint paint)
    {
        // Background bar
        using var bgPaint = new SKPaint
        {
            Color = _theme.KnobTrack,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        var bgRect = new SKRect(x, y, x + maxWidth, y + height);
        canvas.DrawRoundRect(new SKRoundRect(bgRect, 3f), bgPaint);

        // Frequency position indicator
        float norm = Math.Clamp((freqHz - minHz) / (maxHz - minHz), 0f, 1f);
        float indicatorWidth = 8f;
        float indicatorX = x + norm * (maxWidth - indicatorWidth);
        var indicatorRect = new SKRect(indicatorX, y, indicatorX + indicatorWidth, y + height);
        canvas.DrawRoundRect(new SKRoundRect(indicatorRect, 2f), paint);

        // Label
        using var labelPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Bold, SKTextAlign.Right);
        canvas.DrawText(label, x - 4f, y + height / 2 + 4f, labelPaint);

        // Frequency value
        canvas.DrawText($"{freqHz:0} Hz", x + maxWidth + 50f, y + height / 2 + 4f, _freqValuePaint);
    }

    public FormantEnhancerHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new FormantEnhancerHitTest(FormantEnhancerHitArea.CloseButton, -1);

        if (_bypassButtonRect.Contains(x, y))
            return new FormantEnhancerHitTest(FormantEnhancerHitArea.BypassButton, -1);

        var presetHit = _presetBar.HitTest(x, y);
        if (presetHit == PresetBarHitArea.Dropdown)
            return new FormantEnhancerHitTest(FormantEnhancerHitArea.PresetDropdown, -1);
        if (presetHit == PresetBarHitArea.SaveButton)
            return new FormantEnhancerHitTest(FormantEnhancerHitArea.PresetSave, -1);

        if (AmountKnob.HitTest(x, y))
            return new FormantEnhancerHitTest(FormantEnhancerHitArea.Knob, 0);
        if (BoostKnob.HitTest(x, y))
            return new FormantEnhancerHitTest(FormantEnhancerHitArea.Knob, 1);
        if (SmoothingKnob.HitTest(x, y))
            return new FormantEnhancerHitTest(FormantEnhancerHitArea.Knob, 2);

        if (_titleBarRect.Contains(x, y))
            return new FormantEnhancerHitTest(FormantEnhancerHitArea.TitleBar, -1);

        return new FormantEnhancerHitTest(FormantEnhancerHitArea.None, -1);
    }

    public SKRect GetPresetDropdownRect() => _presetBar.GetDropdownRect();

    public static SKSize GetPreferredSize() => new(360, 290);

    public void Dispose()
    {
        AmountKnob.Dispose();
        BoostKnob.Dispose();
        SmoothingKnob.Dispose();
        _presetBar.Dispose();
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _formantBackgroundPaint.Dispose();
        _f1Paint.Dispose();
        _f2Paint.Dispose();
        _f3Paint.Dispose();
        _speechLedOnPaint.Dispose();
        _speechLedOffPaint.Dispose();
        _speechLedGlowPaint.Dispose();
        _labelPaint.Dispose();
        _freqValuePaint.Dispose();
        _latencyPaint.Dispose();
        _statusPaint.Dispose();
    }
}

public record struct FormantEnhancerState(
    float Amount,
    float BoostDb,
    float SmoothingMs,
    float F1Hz,
    float F2Hz,
    float F3Hz,
    float SpeechPresence,
    float LatencyMs,
    bool IsBypassed,
    string StatusMessage = "",
    string PresetName = "Custom");

public enum FormantEnhancerHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    Knob,
    PresetDropdown,
    PresetSave
}

public record struct FormantEnhancerHitTest(FormantEnhancerHitArea Area, int KnobIndex);
