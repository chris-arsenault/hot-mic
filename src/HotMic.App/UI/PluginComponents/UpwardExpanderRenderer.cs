using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Upward Expander plugin UI with tri-band meters showing input level and gain.
/// </summary>
public sealed class UpwardExpanderRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 12f;
    private const float KnobRadius = 20f;
    private const float KnobSpacing = 52f;
    private const float CornerRadius = 10f;
    private const float BandMeterHeight = 80f;

    private readonly PluginComponentTheme _theme;
    private readonly PluginPresetBar _presetBar;

    public KnobWidget AmountKnob { get; }
    public KnobWidget ThresholdKnob { get; }
    public KnobWidget LowSplitKnob { get; }
    public KnobWidget HighSplitKnob { get; }
    public KnobWidget AttackKnob { get; }
    public KnobWidget ReleaseKnob { get; }
    public KnobWidget GateKnob { get; }

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _meterBackgroundPaint;
    private readonly SKPaint _lowBandPaint;
    private readonly SKPaint _midBandPaint;
    private readonly SKPaint _highBandPaint;
    private readonly SKPaint _gainPaint;
    private readonly SKPaint _thresholdPaint;
    private readonly SKPaint _speechLedOnPaint;
    private readonly SKPaint _speechLedOffPaint;
    private readonly SKPaint _speechLedGlowPaint;
    private readonly SkiaTextPaint _labelPaint;
    private readonly SkiaTextPaint _gainValuePaint;
    private readonly SkiaTextPaint _latencyPaint;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;

    public UpwardExpanderRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _presetBar = new PluginPresetBar(_theme);

        AmountKnob = new KnobWidget(KnobRadius, 0f, 100f, "AMOUNT", "%", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0"
        };
        ThresholdKnob = new KnobWidget(KnobRadius, -60f, -10f, "THRESH", "dB", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0"
        };
        LowSplitKnob = new KnobWidget(KnobRadius, 80f, 400f, "LOW", "Hz", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0"
        };
        HighSplitKnob = new KnobWidget(KnobRadius, 1500f, 8000f, "HIGH", "Hz", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0",
            IsLogarithmic = true
        };
        AttackKnob = new KnobWidget(KnobRadius, 2f, 50f, "ATK", "ms", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0"
        };
        ReleaseKnob = new KnobWidget(KnobRadius, 30f, 300f, "REL", "ms", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0"
        };
        GateKnob = new KnobWidget(KnobRadius, 0f, 1f, "GATE", "", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0.00"
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

        _lowBandPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0x80, 0x40), // Warm orange for bass
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _midBandPaint = new SKPaint
        {
            Color = _theme.WaveformLine,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _highBandPaint = new SKPaint
        {
            Color = new SKColor(0x80, 0xC0, 0xFF), // Light blue for highs
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _gainPaint = new SKPaint
        {
            Color = _theme.KnobArc,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _thresholdPaint = new SKPaint
        {
            Color = _theme.ThresholdLine,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            PathEffect = SKPathEffect.CreateDash(new[] { 4f, 3f }, 0)
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

        _labelPaint = new SkiaTextPaint(_theme.TextSecondary, 9f, SKFontStyle.Normal, SKTextAlign.Center);
        _gainValuePaint = new SkiaTextPaint(_theme.TextPrimary, 10f, SKFontStyle.Bold, SKTextAlign.Center);
        _latencyPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Right);
    }

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, UpwardExpanderState state)
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

        // Speech presence LED
        float ledX = Padding + 12f;
        float ledY = y + 12f;
        bool speechActive = state.SpeechPresence > 0.3f;

        if (speechActive)
        {
            canvas.DrawCircle(ledX, ledY, 8f, _speechLedGlowPaint);
            canvas.DrawCircle(ledX, ledY, 5f, _speechLedOnPaint);
        }
        else
        {
            canvas.DrawCircle(ledX, ledY, 5f, _speechLedOffPaint);
        }

        // Tri-band meters
        float meterX = ledX + 30f;
        float meterWidth = (size.Width - meterX - Padding - 20f) / 3f;
        float meterSpacing = 8f;

        // Low band
        var lowRect = new SKRect(meterX, y, meterX + meterWidth - meterSpacing, y + BandMeterHeight);
        DrawBandMeter(canvas, lowRect, state.LowLevel, state.LowGainDb, state.ThresholdDb, "LOW", _lowBandPaint);

        // Mid band
        var midRect = new SKRect(lowRect.Right + meterSpacing, y, lowRect.Right + meterSpacing + meterWidth - meterSpacing, y + BandMeterHeight);
        DrawBandMeter(canvas, midRect, state.MidLevel, state.MidGainDb, state.ThresholdDb, "MID", _midBandPaint);

        // High band
        var highRect = new SKRect(midRect.Right + meterSpacing, y, midRect.Right + meterSpacing + meterWidth, y + BandMeterHeight);
        DrawBandMeter(canvas, highRect, state.HighLevel, state.HighGainDb, state.ThresholdDb, "HIGH", _highBandPaint);

        y += BandMeterHeight + Padding + 8f;

        // Knobs (7 knobs in two rows)
        float knobsY1 = y + KnobRadius + 8;
        float knobsTotalWidth = 4 * KnobSpacing;
        float knobsStartX = (size.Width - knobsTotalWidth) / 2 + KnobSpacing / 2;

        // First row: Amount, Threshold, Attack, Release
        AmountKnob.Center = new SKPoint(knobsStartX, knobsY1);
        AmountKnob.Value = state.AmountPct;
        AmountKnob.Render(canvas);

        ThresholdKnob.Center = new SKPoint(knobsStartX + KnobSpacing, knobsY1);
        ThresholdKnob.Value = state.ThresholdDb;
        ThresholdKnob.Render(canvas);

        AttackKnob.Center = new SKPoint(knobsStartX + KnobSpacing * 2, knobsY1);
        AttackKnob.Value = state.AttackMs;
        AttackKnob.Render(canvas);

        ReleaseKnob.Center = new SKPoint(knobsStartX + KnobSpacing * 3, knobsY1);
        ReleaseKnob.Value = state.ReleaseMs;
        ReleaseKnob.Render(canvas);

        // Second row: Low Split, High Split, Gate
        float knobsY2 = knobsY1 + KnobRadius * 2 + 30;
        float row2StartX = (size.Width - 3 * KnobSpacing) / 2 + KnobSpacing / 2;

        LowSplitKnob.Center = new SKPoint(row2StartX, knobsY2);
        LowSplitKnob.Value = state.LowSplitHz;
        LowSplitKnob.Render(canvas);

        HighSplitKnob.Center = new SKPoint(row2StartX + KnobSpacing, knobsY2);
        HighSplitKnob.Value = state.HighSplitHz;
        HighSplitKnob.Render(canvas);

        GateKnob.Center = new SKPoint(row2StartX + KnobSpacing * 2, knobsY2);
        GateKnob.Value = state.GateStrength;
        GateKnob.Render(canvas);

        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.Restore();
    }

    private void DrawTitleBar(SKCanvas canvas, SKSize size, UpwardExpanderState state)
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

        canvas.DrawText("Upward Expander", Padding, TitleBarHeight / 2f + 5, _titlePaint);

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

    private void DrawBandMeter(SKCanvas canvas, SKRect rect, float level, float gainDb, float thresholdDb, string label, SKPaint bandPaint)
    {
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _meterBackgroundPaint);

        float padding = 3f;
        float innerHeight = rect.Height - padding * 2;

        // Convert level to dB and normalize
        float levelDb = 20f * MathF.Log10(level + 1e-6f);
        float levelNorm = Math.Clamp((levelDb + 60f) / 60f, 0f, 1f);
        float levelHeight = innerHeight * levelNorm;

        // Draw level bar (background)
        if (levelHeight > 1)
        {
            var levelRect = new SKRect(rect.Left + padding, rect.Bottom - padding - levelHeight,
                rect.Right - padding, rect.Bottom - padding);
            using var dimPaint = new SKPaint
            {
                Color = bandPaint.Color.WithAlpha(100),
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            canvas.DrawRect(levelRect, dimPaint);
        }

        // Draw gain bar (overlay showing boost)
        float gainNorm = Math.Clamp(gainDb / 6f, 0f, 1f); // Max 6dB boost
        float gainHeight = innerHeight * gainNorm * levelNorm;
        if (gainHeight > 1 && gainDb > 0.1f)
        {
            var gainRect = new SKRect(rect.Left + padding, rect.Bottom - padding - levelHeight - gainHeight,
                rect.Right - padding, rect.Bottom - padding - levelHeight);
            canvas.DrawRect(gainRect, _gainPaint);
        }

        // Threshold line
        float threshNorm = Math.Clamp((thresholdDb + 60f) / 60f, 0f, 1f);
        float threshY = rect.Bottom - padding - innerHeight * threshNorm;
        canvas.DrawLine(rect.Left + padding, threshY, rect.Right - padding, threshY, _thresholdPaint);

        canvas.DrawRoundRect(roundRect, _borderPaint);

        // Label
        canvas.DrawText(label, rect.MidX, rect.Bottom + 12, _labelPaint);

        // Gain value
        string gainText = gainDb > 0.1f ? $"+{gainDb:0.0}" : "0.0";
        canvas.DrawText(gainText, rect.MidX, rect.Top - 4, _gainValuePaint);
    }

    public UpwardExpanderHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new UpwardExpanderHitTest(UpwardExpanderHitArea.CloseButton, -1);

        if (_bypassButtonRect.Contains(x, y))
            return new UpwardExpanderHitTest(UpwardExpanderHitArea.BypassButton, -1);

        var presetHit = _presetBar.HitTest(x, y);
        if (presetHit == PresetBarHitArea.Dropdown)
            return new UpwardExpanderHitTest(UpwardExpanderHitArea.PresetDropdown, -1);
        if (presetHit == PresetBarHitArea.SaveButton)
            return new UpwardExpanderHitTest(UpwardExpanderHitArea.PresetSave, -1);

        if (AmountKnob.HitTest(x, y))
            return new UpwardExpanderHitTest(UpwardExpanderHitArea.Knob, 0);
        if (ThresholdKnob.HitTest(x, y))
            return new UpwardExpanderHitTest(UpwardExpanderHitArea.Knob, 1);
        if (LowSplitKnob.HitTest(x, y))
            return new UpwardExpanderHitTest(UpwardExpanderHitArea.Knob, 2);
        if (HighSplitKnob.HitTest(x, y))
            return new UpwardExpanderHitTest(UpwardExpanderHitArea.Knob, 3);
        if (AttackKnob.HitTest(x, y))
            return new UpwardExpanderHitTest(UpwardExpanderHitArea.Knob, 4);
        if (ReleaseKnob.HitTest(x, y))
            return new UpwardExpanderHitTest(UpwardExpanderHitArea.Knob, 5);
        if (GateKnob.HitTest(x, y))
            return new UpwardExpanderHitTest(UpwardExpanderHitArea.Knob, 6);

        if (_titleBarRect.Contains(x, y))
            return new UpwardExpanderHitTest(UpwardExpanderHitArea.TitleBar, -1);

        return new UpwardExpanderHitTest(UpwardExpanderHitArea.None, -1);
    }

    public SKRect GetPresetDropdownRect() => _presetBar.GetDropdownRect();

    public static SKSize GetPreferredSize() => new(320, 340);

    public void Dispose()
    {
        AmountKnob.Dispose();
        ThresholdKnob.Dispose();
        LowSplitKnob.Dispose();
        HighSplitKnob.Dispose();
        AttackKnob.Dispose();
        ReleaseKnob.Dispose();
        GateKnob.Dispose();
        _presetBar.Dispose();
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _meterBackgroundPaint.Dispose();
        _lowBandPaint.Dispose();
        _midBandPaint.Dispose();
        _highBandPaint.Dispose();
        _gainPaint.Dispose();
        _thresholdPaint.Dispose();
        _speechLedOnPaint.Dispose();
        _speechLedOffPaint.Dispose();
        _speechLedGlowPaint.Dispose();
        _labelPaint.Dispose();
        _gainValuePaint.Dispose();
        _latencyPaint.Dispose();
    }
}

public record struct UpwardExpanderState(
    float AmountPct,
    float ThresholdDb,
    float LowSplitHz,
    float HighSplitHz,
    float AttackMs,
    float ReleaseMs,
    float GateStrength,
    float LowLevel,
    float MidLevel,
    float HighLevel,
    float LowGainDb,
    float MidGainDb,
    float HighGainDb,
    float SpeechPresence,
    float LatencyMs,
    bool IsBypassed,
    string PresetName = "Custom");

public enum UpwardExpanderHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    Knob,
    PresetDropdown,
    PresetSave
}

public record struct UpwardExpanderHitTest(UpwardExpanderHitArea Area, int KnobIndex);
