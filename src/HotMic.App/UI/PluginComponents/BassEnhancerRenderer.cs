using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Bass Enhancer plugin UI with harmonic visualization and bass energy meter.
/// </summary>
public sealed class BassEnhancerRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 12f;
    private const float KnobRadius = 24f;
    private const float KnobSpacing = 64f;
    private const float CornerRadius = 10f;
    private const float MeterHeight = 80f;

    private readonly PluginComponentTheme _theme;
    private readonly PluginPresetBar _presetBar;

    public KnobWidget AmountKnob { get; }
    public KnobWidget DriveKnob { get; }
    public KnobWidget MixKnob { get; }
    public KnobWidget CenterKnob { get; }

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _meterBackgroundPaint;
    private readonly SKPaint _bassFillPaint;
    private readonly SKPaint _harmonicFillPaint;
    private readonly SKPaint _gateLedOnPaint;
    private readonly SKPaint _gateLedOffPaint;
    private readonly SKPaint _gateLedGlowPaint;
    private readonly SkiaTextPaint _labelPaint;
    private readonly SkiaTextPaint _latencyPaint;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;

    public BassEnhancerRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _presetBar = new PluginPresetBar(_theme);

        AmountKnob = new KnobWidget(KnobRadius, 0f, 1f, "AMOUNT", "", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0.00"
        };
        DriveKnob = new KnobWidget(KnobRadius, 0f, 1f, "DRIVE", "", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0.00"
        };
        MixKnob = new KnobWidget(KnobRadius, 0f, 1f, "MIX", "", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0.00"
        };
        CenterKnob = new KnobWidget(KnobRadius, 70f, 180f, "CENTER", "Hz", KnobStyle.Standard, _theme)
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

        _meterBackgroundPaint = new SKPaint
        {
            Color = _theme.WaveformBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _bassFillPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0x80, 0x40), // Warm orange for bass
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _harmonicFillPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0xC0, 0x60), // Lighter orange for harmonics
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

        _labelPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Normal, SKTextAlign.Center);
        _latencyPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Right);
    }

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, BassEnhancerState state)
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

        // Voiced gate LED
        float gateX = Padding + 15f;
        float gateY = y + 15f;
        bool gateOpen = state.VoicedGate > 0.3f;

        if (gateOpen)
        {
            canvas.DrawCircle(gateX, gateY, 10f, _gateLedGlowPaint);
            canvas.DrawCircle(gateX, gateY, 6f, _gateLedOnPaint);
        }
        else
        {
            canvas.DrawCircle(gateX, gateY, 6f, _gateLedOffPaint);
        }
        canvas.DrawText("VOICE", gateX, gateY + 18f, _labelPaint);

        // Bass energy meter with harmonic overlay
        float meterX = gateX + 40f;
        float meterWidth = size.Width - meterX - Padding - 40f;
        var meterRect = new SKRect(meterX, y, meterX + meterWidth, y + MeterHeight);
        DrawBassHarmonicMeter(canvas, meterRect, state.BassEnergy, state.HarmonicAmount, state.CenterHz);

        y += MeterHeight + Padding + 10f;

        // Knobs
        float knobsY = y + KnobRadius + 10;
        float knobsTotalWidth = 4 * KnobSpacing;
        float knobsStartX = (size.Width - knobsTotalWidth) / 2 + KnobSpacing / 2;

        AmountKnob.Center = new SKPoint(knobsStartX, knobsY);
        AmountKnob.Value = state.Amount;
        AmountKnob.Render(canvas);

        DriveKnob.Center = new SKPoint(knobsStartX + KnobSpacing, knobsY);
        DriveKnob.Value = state.Drive;
        DriveKnob.Render(canvas);

        MixKnob.Center = new SKPoint(knobsStartX + KnobSpacing * 2, knobsY);
        MixKnob.Value = state.Mix;
        MixKnob.Render(canvas);

        CenterKnob.Center = new SKPoint(knobsStartX + KnobSpacing * 3, knobsY);
        CenterKnob.Value = state.CenterHz;
        CenterKnob.Render(canvas);

        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.Restore();
    }

    private void DrawTitleBar(SKCanvas canvas, SKSize size, BassEnhancerState state)
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

        canvas.DrawText("Bass Enhancer", Padding, TitleBarHeight / 2f + 5, _titlePaint);

        float presetBarX = 110f;
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

    private void DrawBassHarmonicMeter(SKCanvas canvas, SKRect rect, float bassEnergy, float harmonicAmount, float centerHz)
    {
        var roundRect = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(roundRect, _meterBackgroundPaint);

        float padding = 4f;
        float barHeight = rect.Height - padding * 2;
        float barWidth = (rect.Width - padding * 3) / 2;

        // Bass energy bar (left)
        float bassHeight = barHeight * Math.Clamp(bassEnergy * 4f, 0f, 1f);
        if (bassHeight > 1)
        {
            var bassRect = new SKRect(
                rect.Left + padding,
                rect.Bottom - padding - bassHeight,
                rect.Left + padding + barWidth,
                rect.Bottom - padding);
            canvas.DrawRect(bassRect, _bassFillPaint);
        }

        // Harmonic generation bar (right)
        float harmHeight = barHeight * Math.Clamp(harmonicAmount * 6f, 0f, 1f);
        if (harmHeight > 1)
        {
            var harmRect = new SKRect(
                rect.Left + padding * 2 + barWidth,
                rect.Bottom - padding - harmHeight,
                rect.Right - padding,
                rect.Bottom - padding);
            canvas.DrawRect(harmRect, _harmonicFillPaint);
        }

        // Labels
        canvas.DrawText("BASS", rect.Left + padding + barWidth / 2, rect.Bottom + 14, _labelPaint);
        canvas.DrawText("HARM", rect.Left + padding * 2 + barWidth + barWidth / 2, rect.Bottom + 14, _labelPaint);

        // Center frequency display
        using var freqPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Right);
        canvas.DrawText($"{centerHz:0} Hz", rect.Right - 4, rect.Top + 12, freqPaint);

        canvas.DrawRoundRect(roundRect, _borderPaint);
    }

    public BassEnhancerHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new BassEnhancerHitTest(BassEnhancerHitArea.CloseButton, -1);

        if (_bypassButtonRect.Contains(x, y))
            return new BassEnhancerHitTest(BassEnhancerHitArea.BypassButton, -1);

        var presetHit = _presetBar.HitTest(x, y);
        if (presetHit == PresetBarHitArea.Dropdown)
            return new BassEnhancerHitTest(BassEnhancerHitArea.PresetDropdown, -1);
        if (presetHit == PresetBarHitArea.SaveButton)
            return new BassEnhancerHitTest(BassEnhancerHitArea.PresetSave, -1);

        if (AmountKnob.HitTest(x, y))
            return new BassEnhancerHitTest(BassEnhancerHitArea.Knob, 0);
        if (DriveKnob.HitTest(x, y))
            return new BassEnhancerHitTest(BassEnhancerHitArea.Knob, 1);
        if (MixKnob.HitTest(x, y))
            return new BassEnhancerHitTest(BassEnhancerHitArea.Knob, 2);
        if (CenterKnob.HitTest(x, y))
            return new BassEnhancerHitTest(BassEnhancerHitArea.Knob, 3);

        if (_titleBarRect.Contains(x, y))
            return new BassEnhancerHitTest(BassEnhancerHitArea.TitleBar, -1);

        return new BassEnhancerHitTest(BassEnhancerHitArea.None, -1);
    }

    public SKRect GetPresetDropdownRect() => _presetBar.GetDropdownRect();

    public static SKSize GetPreferredSize() => new(340, 280);

    public void Dispose()
    {
        AmountKnob.Dispose();
        DriveKnob.Dispose();
        MixKnob.Dispose();
        CenterKnob.Dispose();
        _presetBar.Dispose();
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _meterBackgroundPaint.Dispose();
        _bassFillPaint.Dispose();
        _harmonicFillPaint.Dispose();
        _gateLedOnPaint.Dispose();
        _gateLedOffPaint.Dispose();
        _gateLedGlowPaint.Dispose();
        _labelPaint.Dispose();
        _latencyPaint.Dispose();
    }
}

public record struct BassEnhancerState(
    float Amount,
    float Drive,
    float Mix,
    float CenterHz,
    float VoicedGate,
    float BassEnergy,
    float HarmonicAmount,
    float LatencyMs,
    bool IsBypassed,
    string PresetName = "Custom");

public enum BassEnhancerHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    Knob,
    PresetDropdown,
    PresetSave
}

public record struct BassEnhancerHitTest(BassEnhancerHitArea Area, int KnobIndex);
