using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Dynamic EQ plugin UI with 3-band EQ curve visualization and voice presence meter.
/// </summary>
public sealed class DynamicEqRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 12f;
    private const float KnobRadius = 24f;
    private const float KnobSpacing = 72f;
    private const float CornerRadius = 10f;
    private const float EqDisplayHeight = 100f;

    private readonly PluginComponentTheme _theme;
    private readonly PluginPresetBar _presetBar;

    public KnobWidget LowBoostKnob { get; }
    public KnobWidget HighBoostKnob { get; }
    public KnobWidget SmoothingKnob { get; }

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _eqBackgroundPaint;
    private readonly SKPaint _eqCurvePaint;
    private readonly SKPaint _eqGridPaint;
    private readonly SKPaint _voicedMeterPaint;
    private readonly SKPaint _unvoicedMeterPaint;
    private readonly SKPaint _meterBackgroundPaint;
    private readonly SkiaTextPaint _labelPaint;
    private readonly SkiaTextPaint _freqLabelPaint;
    private readonly SkiaTextPaint _latencyPaint;
    private readonly SkiaTextPaint _statusPaint;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;

    public DynamicEqRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _presetBar = new PluginPresetBar(_theme);

        LowBoostKnob = new KnobWidget(KnobRadius, -6f, 6f, "LOW", "dB", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0.0"
        };
        HighBoostKnob = new KnobWidget(KnobRadius, -6f, 6f, "HIGH", "dB", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0.0"
        };
        SmoothingKnob = new KnobWidget(KnobRadius, 20f, 200f, "SMOOTH", "ms", KnobStyle.Standard, _theme)
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

        _eqBackgroundPaint = new SKPaint
        {
            Color = _theme.WaveformBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _eqCurvePaint = new SKPaint
        {
            Color = _theme.KnobArc,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f
        };

        _eqGridPaint = new SKPaint
        {
            Color = _theme.EnvelopeGrid,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };

        _voicedMeterPaint = new SKPaint
        {
            Color = _theme.GateOpen,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _unvoicedMeterPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0x80, 0x40),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _meterBackgroundPaint = new SKPaint
        {
            Color = _theme.WaveformBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _labelPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Normal, SKTextAlign.Center);
        _freqLabelPaint = new SkiaTextPaint(_theme.TextMuted, 8f, SKFontStyle.Normal, SKTextAlign.Center);
        _latencyPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Right);
        _statusPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Normal, SKTextAlign.Center);
    }

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, DynamicEqState state)
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

        // Voice presence meters (left side)
        float meterWidth = 24f;
        float meterHeight = EqDisplayHeight;
        var voicedRect = new SKRect(Padding, y, Padding + meterWidth, y + meterHeight);
        DrawVoiceMeter(canvas, voicedRect, state.VoicingLevel, "V", _voicedMeterPaint);

        var unvoicedRect = new SKRect(voicedRect.Right + 6, y, voicedRect.Right + 6 + meterWidth, y + meterHeight);
        DrawVoiceMeter(canvas, unvoicedRect, state.FricativeLevel, "FR", _unvoicedMeterPaint);

        // EQ curve display
        float eqX = unvoicedRect.Right + 12f;
        float eqWidth = size.Width - eqX - Padding;
        var eqRect = new SKRect(eqX, y, eqX + eqWidth, y + EqDisplayHeight);
        DrawEqCurve(canvas, eqRect, state.LowGainDb, state.EdgeGainDb, state.AirGainDb);

        y += EqDisplayHeight + Padding + 10f;

        // Knobs
        float knobsY = y + KnobRadius + 10;
        float knobsTotalWidth = 3 * KnobSpacing;
        float knobsStartX = (size.Width - knobsTotalWidth) / 2 + KnobSpacing / 2;

        LowBoostKnob.Center = new SKPoint(knobsStartX, knobsY);
        LowBoostKnob.Value = state.LowBoostDb;
        LowBoostKnob.Render(canvas);

        HighBoostKnob.Center = new SKPoint(knobsStartX + KnobSpacing, knobsY);
        HighBoostKnob.Value = state.HighBoostDb;
        HighBoostKnob.Render(canvas);

        SmoothingKnob.Center = new SKPoint(knobsStartX + KnobSpacing * 2, knobsY);
        SmoothingKnob.Value = state.SmoothingMs;
        SmoothingKnob.Render(canvas);

        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.Restore();
    }

    private void DrawTitleBar(SKCanvas canvas, SKSize size, DynamicEqState state)
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

        canvas.DrawText("Dynamic EQ", Padding, TitleBarHeight / 2f + 5, _titlePaint);

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

    private void DrawVoiceMeter(SKCanvas canvas, SKRect rect, float level, string label, SKPaint fillPaint)
    {
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _meterBackgroundPaint);

        float fillHeight = (rect.Height - 4) * Math.Clamp(level, 0f, 1f);
        if (fillHeight > 1)
        {
            var fillRect = new SKRect(rect.Left + 2, rect.Bottom - 2 - fillHeight, rect.Right - 2, rect.Bottom - 2);
            canvas.DrawRect(fillRect, fillPaint);
        }

        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.DrawText(label, rect.MidX, rect.Bottom + 12, _labelPaint);
    }

    private void DrawEqCurve(SKCanvas canvas, SKRect rect, float lowGainDb, float edgeGainDb, float airGainDb)
    {
        var roundRect = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(roundRect, _eqBackgroundPaint);

        float padding = 8f;
        float plotWidth = rect.Width - padding * 2;
        float plotHeight = rect.Height - padding * 2;
        float centerY = rect.Top + padding + plotHeight / 2;

        // Grid lines
        canvas.DrawLine(rect.Left + padding, centerY, rect.Right - padding, centerY, _eqGridPaint);

        // Draw 3-band EQ curve (low shelf + edge peak + air shelf)
        using var path = new SKPath();
        int steps = 40;

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float x = rect.Left + padding + t * plotWidth;

            // Simplified shelf response curve
            float freq = 20f * MathF.Pow(1000f, t); // 20Hz to 20kHz
            float lowShelfFreq = 220f;
            float edgeFreq = 3400f;
            float airShelfFreq = 9000f;

            float gain = 0f;

            // Low shelf contribution
            if (freq < lowShelfFreq * 2f)
            {
                float lowT = Math.Clamp((lowShelfFreq * 2f - freq) / lowShelfFreq, 0f, 1f);
                gain += lowGainDb * lowT;
            }

            // Edge peak contribution (approximate)
            float edgeDistance = MathF.Abs(MathF.Log2(freq / edgeFreq));
            float edgeWeight = Math.Clamp(1f - edgeDistance / 1.2f, 0f, 1f);
            gain += edgeGainDb * edgeWeight;

            // Air shelf contribution
            if (freq > airShelfFreq / 2f)
            {
                float highT = Math.Clamp((freq - airShelfFreq / 2f) / airShelfFreq, 0f, 1f);
                gain += airGainDb * highT;
            }

            // Scale to pixels (6dB = half height)
            float maxDb = 6f;
            float yOffset = (gain / maxDb) * (plotHeight / 2);
            float y = centerY - yOffset;

            if (i == 0)
                path.MoveTo(x, y);
            else
                path.LineTo(x, y);
        }

        canvas.DrawPath(path, _eqCurvePaint);

        // Frequency labels
        canvas.DrawText("100", rect.Left + padding + plotWidth * 0.2f, rect.Bottom - 2, _freqLabelPaint);
        canvas.DrawText("1k", rect.Left + padding + plotWidth * 0.5f, rect.Bottom - 2, _freqLabelPaint);
        canvas.DrawText("10k", rect.Left + padding + plotWidth * 0.8f, rect.Bottom - 2, _freqLabelPaint);

        // dB labels
        using var dbPaint = new SkiaTextPaint(_theme.TextMuted, 8f, SKFontStyle.Normal, SKTextAlign.Left);
        canvas.DrawText("+6", rect.Left + padding + 2, rect.Top + padding + 8, dbPaint);
        canvas.DrawText("-6", rect.Left + padding + 2, rect.Bottom - padding - 2, dbPaint);

        canvas.DrawRoundRect(roundRect, _borderPaint);
    }

    public DynamicEqHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new DynamicEqHitTest(DynamicEqHitArea.CloseButton, -1);

        if (_bypassButtonRect.Contains(x, y))
            return new DynamicEqHitTest(DynamicEqHitArea.BypassButton, -1);

        var presetHit = _presetBar.HitTest(x, y);
        if (presetHit == PresetBarHitArea.Dropdown)
            return new DynamicEqHitTest(DynamicEqHitArea.PresetDropdown, -1);
        if (presetHit == PresetBarHitArea.SaveButton)
            return new DynamicEqHitTest(DynamicEqHitArea.PresetSave, -1);

        if (LowBoostKnob.HitTest(x, y))
            return new DynamicEqHitTest(DynamicEqHitArea.Knob, 0);
        if (HighBoostKnob.HitTest(x, y))
            return new DynamicEqHitTest(DynamicEqHitArea.Knob, 1);
        if (SmoothingKnob.HitTest(x, y))
            return new DynamicEqHitTest(DynamicEqHitArea.Knob, 2);

        if (_titleBarRect.Contains(x, y))
            return new DynamicEqHitTest(DynamicEqHitArea.TitleBar, -1);

        return new DynamicEqHitTest(DynamicEqHitArea.None, -1);
    }

    public SKRect GetPresetDropdownRect() => _presetBar.GetDropdownRect();

    public static SKSize GetPreferredSize() => new(340, 290);

    public void Dispose()
    {
        LowBoostKnob.Dispose();
        HighBoostKnob.Dispose();
        SmoothingKnob.Dispose();
        _presetBar.Dispose();
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _eqBackgroundPaint.Dispose();
        _eqCurvePaint.Dispose();
        _eqGridPaint.Dispose();
        _voicedMeterPaint.Dispose();
        _unvoicedMeterPaint.Dispose();
        _meterBackgroundPaint.Dispose();
        _labelPaint.Dispose();
        _freqLabelPaint.Dispose();
        _latencyPaint.Dispose();
        _statusPaint.Dispose();
    }
}

public record struct DynamicEqState(
    float LowBoostDb,
    float HighBoostDb,
    float SmoothingMs,
    float VoicingLevel,
    float FricativeLevel,
    float LowGainDb,
    float EdgeGainDb,
    float AirGainDb,
    float LatencyMs,
    bool IsBypassed,
    string StatusMessage = "",
    string PresetName = "Custom");

public enum DynamicEqHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    Knob,
    PresetDropdown,
    PresetSave
}

public record struct DynamicEqHitTest(DynamicEqHitArea Area, int KnobIndex);
