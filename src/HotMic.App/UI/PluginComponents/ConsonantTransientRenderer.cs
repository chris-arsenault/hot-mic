using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Consonant Transient plugin UI with dual envelope display and transient detector visualization.
/// </summary>
public sealed class ConsonantTransientRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 12f;
    private const float KnobRadius = 24f;
    private const float KnobSpacing = 72f;
    private const float CornerRadius = 10f;
    private const float EnvelopeHeight = 80f;

    private readonly PluginComponentTheme _theme;
    private readonly PluginPresetBar _presetBar;

    public KnobWidget AmountKnob { get; }
    public KnobWidget ThresholdKnob { get; }
    public KnobWidget HighCutKnob { get; }

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _envelopeBackgroundPaint;
    private readonly SKPaint _fastEnvPaint;
    private readonly SKPaint _slowEnvPaint;
    private readonly SKPaint _transientPaint;
    private readonly SKPaint _gateLedOnPaint;
    private readonly SKPaint _gateLedOffPaint;
    private readonly SKPaint _gateLedGlowPaint;
    private readonly SkiaTextPaint _labelPaint;
    private readonly SkiaTextPaint _latencyPaint;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;

    // Envelope history for visualization
    private readonly float[] _fastEnvHistory = new float[64];
    private readonly float[] _slowEnvHistory = new float[64];
    private int _historyIndex;

    public ConsonantTransientRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _presetBar = new PluginPresetBar(_theme);

        AmountKnob = new KnobWidget(KnobRadius, 0f, 1f, "AMOUNT", "", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0.00"
        };
        ThresholdKnob = new KnobWidget(KnobRadius, 0f, 0.5f, "THRESH", "", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0.00"
        };
        HighCutKnob = new KnobWidget(KnobRadius, 3000f, 9000f, "HIGH CUT", "Hz", KnobStyle.Standard, _theme)
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

        _envelopeBackgroundPaint = new SKPaint
        {
            Color = _theme.WaveformBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _fastEnvPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0xA0, 0x40), // Bright orange for fast
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f
        };

        _slowEnvPaint = new SKPaint
        {
            Color = new SKColor(0x80, 0x60, 0x40), // Dim brown for slow
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f
        };

        _transientPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0xFF, 0x40), // Bright yellow for transient
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _gateLedOnPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0x80, 0x40), // Orange for unvoiced
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
            Color = new SKColor(0xFF, 0x80, 0x40, 0x60),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6f)
        };

        _labelPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Normal, SKTextAlign.Center);
        _latencyPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Right);
    }

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, ConsonantTransientState state)
    {
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        canvas.Scale(dpiScale);
        size = new SKSize(size.Width / dpiScale, size.Height / dpiScale);

        // Update envelope history
        _fastEnvHistory[_historyIndex] = state.FastEnvelope;
        _slowEnvHistory[_historyIndex] = state.SlowEnvelope;
        _historyIndex = (_historyIndex + 1) % _fastEnvHistory.Length;

        var backgroundRect = new SKRect(0, 0, size.Width, size.Height);
        var roundRect = new SKRoundRect(backgroundRect, CornerRadius);
        canvas.DrawRoundRect(roundRect, _backgroundPaint);

        DrawTitleBar(canvas, size, state);

        float y = TitleBarHeight + Padding;

        // Unvoiced gate LED + transient indicator
        float gateX = Padding + 20f;
        float gateY = y + 20f;
        bool gateOpen = state.UnvoicedGate > 0.3f;

        if (gateOpen)
        {
            canvas.DrawCircle(gateX, gateY, 10f, _gateLedGlowPaint);
            canvas.DrawCircle(gateX, gateY, 6f, _gateLedOnPaint);
        }
        else
        {
            canvas.DrawCircle(gateX, gateY, 6f, _gateLedOffPaint);
        }
        canvas.DrawText("UNVOICED", gateX, gateY + 18f, _labelPaint);

        // Transient indicator
        float transX = gateX + 60f;
        if (state.TransientDetected > 0.5f)
        {
            canvas.DrawCircle(transX, gateY, 10f, _transientPaint);
        }
        else
        {
            using var dimPaint = new SKPaint
            {
                Color = new SKColor(0x40, 0x40, 0x20),
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            canvas.DrawCircle(transX, gateY, 6f, dimPaint);
        }
        canvas.DrawText("TRANS", transX, gateY + 18f, _labelPaint);

        // Envelope display
        float envX = transX + 50f;
        float envWidth = size.Width - envX - Padding;
        var envRect = new SKRect(envX, y, envX + envWidth, y + EnvelopeHeight);
        DrawEnvelopeDisplay(canvas, envRect, state.Threshold);

        y += EnvelopeHeight + Padding + 10f;

        // Knobs
        float knobsY = y + KnobRadius + 10;
        float knobsTotalWidth = 3 * KnobSpacing;
        float knobsStartX = (size.Width - knobsTotalWidth) / 2 + KnobSpacing / 2;

        AmountKnob.Center = new SKPoint(knobsStartX, knobsY);
        AmountKnob.Value = state.Amount;
        AmountKnob.Render(canvas);

        ThresholdKnob.Center = new SKPoint(knobsStartX + KnobSpacing, knobsY);
        ThresholdKnob.Value = state.Threshold;
        ThresholdKnob.Render(canvas);

        HighCutKnob.Center = new SKPoint(knobsStartX + KnobSpacing * 2, knobsY);
        HighCutKnob.Value = state.HighCutHz;
        HighCutKnob.Render(canvas);

        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.Restore();
    }

    private void DrawTitleBar(SKCanvas canvas, SKSize size, ConsonantTransientState state)
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

        canvas.DrawText("Consonant Transient", Padding, TitleBarHeight / 2f + 5, _titlePaint);

        float presetBarX = 140f;
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

    private void DrawEnvelopeDisplay(SKCanvas canvas, SKRect rect, float threshold)
    {
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _envelopeBackgroundPaint);

        float padding = 4f;
        float plotWidth = rect.Width - padding * 2;
        float plotHeight = rect.Height - padding * 2;
        int count = _fastEnvHistory.Length;

        // Draw slow envelope (background)
        using var slowPath = new SKPath();
        for (int i = 0; i < count; i++)
        {
            int idx = (_historyIndex + i) % count;
            float x = rect.Left + padding + (i / (float)(count - 1)) * plotWidth;
            float level = Math.Clamp(_slowEnvHistory[idx] * 10f, 0f, 1f);
            float y = rect.Bottom - padding - level * plotHeight;

            if (i == 0)
                slowPath.MoveTo(x, y);
            else
                slowPath.LineTo(x, y);
        }
        canvas.DrawPath(slowPath, _slowEnvPaint);

        // Draw fast envelope (foreground)
        using var fastPath = new SKPath();
        for (int i = 0; i < count; i++)
        {
            int idx = (_historyIndex + i) % count;
            float x = rect.Left + padding + (i / (float)(count - 1)) * plotWidth;
            float level = Math.Clamp(_fastEnvHistory[idx] * 10f, 0f, 1f);
            float y = rect.Bottom - padding - level * plotHeight;

            if (i == 0)
                fastPath.MoveTo(x, y);
            else
                fastPath.LineTo(x, y);
        }
        canvas.DrawPath(fastPath, _fastEnvPaint);

        // Threshold line
        float threshY = rect.Bottom - padding - threshold * 2f * plotHeight;
        using var threshPaint = new SKPaint
        {
            Color = _theme.ThresholdLine,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            PathEffect = SKPathEffect.CreateDash(new[] { 4f, 3f }, 0)
        };
        canvas.DrawLine(rect.Left + padding, threshY, rect.Right - padding, threshY, threshPaint);

        canvas.DrawRoundRect(roundRect, _borderPaint);
    }

    public ConsonantTransientHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new ConsonantTransientHitTest(ConsonantTransientHitArea.CloseButton, -1);

        if (_bypassButtonRect.Contains(x, y))
            return new ConsonantTransientHitTest(ConsonantTransientHitArea.BypassButton, -1);

        var presetHit = _presetBar.HitTest(x, y);
        if (presetHit == PresetBarHitArea.Dropdown)
            return new ConsonantTransientHitTest(ConsonantTransientHitArea.PresetDropdown, -1);
        if (presetHit == PresetBarHitArea.SaveButton)
            return new ConsonantTransientHitTest(ConsonantTransientHitArea.PresetSave, -1);

        if (AmountKnob.HitTest(x, y))
            return new ConsonantTransientHitTest(ConsonantTransientHitArea.Knob, 0);
        if (ThresholdKnob.HitTest(x, y))
            return new ConsonantTransientHitTest(ConsonantTransientHitArea.Knob, 1);
        if (HighCutKnob.HitTest(x, y))
            return new ConsonantTransientHitTest(ConsonantTransientHitArea.Knob, 2);

        if (_titleBarRect.Contains(x, y))
            return new ConsonantTransientHitTest(ConsonantTransientHitArea.TitleBar, -1);

        return new ConsonantTransientHitTest(ConsonantTransientHitArea.None, -1);
    }

    public SKRect GetPresetDropdownRect() => _presetBar.GetDropdownRect();

    public static SKSize GetPreferredSize() => new(360, 280);

    public void Dispose()
    {
        AmountKnob.Dispose();
        ThresholdKnob.Dispose();
        HighCutKnob.Dispose();
        _presetBar.Dispose();
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _envelopeBackgroundPaint.Dispose();
        _fastEnvPaint.Dispose();
        _slowEnvPaint.Dispose();
        _transientPaint.Dispose();
        _gateLedOnPaint.Dispose();
        _gateLedOffPaint.Dispose();
        _gateLedGlowPaint.Dispose();
        _labelPaint.Dispose();
        _latencyPaint.Dispose();
    }
}

public record struct ConsonantTransientState(
    float Amount,
    float Threshold,
    float HighCutHz,
    float UnvoicedGate,
    float FastEnvelope,
    float SlowEnvelope,
    float TransientDetected,
    float LatencyMs,
    bool IsBypassed,
    string PresetName = "Custom");

public enum ConsonantTransientHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    Knob,
    PresetDropdown,
    PresetSave
}

public record struct ConsonantTransientHitTest(ConsonantTransientHitArea Area, int KnobIndex);
