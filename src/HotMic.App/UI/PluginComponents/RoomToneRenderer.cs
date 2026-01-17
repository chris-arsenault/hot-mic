using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Room Tone plugin UI with noise spectrum visualization and speech ducking meter.
/// </summary>
public sealed class RoomToneRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 12f;
    private const float KnobRadius = 24f;
    private const float KnobSpacing = 72f;
    private const float CornerRadius = 10f;
    private const float SpectrumHeight = 60f;

    private readonly PluginComponentTheme _theme;
    private readonly PluginPresetBar _presetBar;

    public KnobWidget LevelKnob { get; }
    public KnobWidget DuckKnob { get; }
    public KnobWidget ToneKnob { get; }

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _spectrumBackgroundPaint;
    private readonly SKPaint _spectrumFillPaint;
    private readonly SKPaint _duckMeterBackgroundPaint;
    private readonly SKPaint _duckMeterFillPaint;
    private readonly SKPaint _levelMeterBackgroundPaint;
    private readonly SKPaint _levelMeterFillPaint;
    private readonly SkiaTextPaint _labelPaint;
    private readonly SkiaTextPaint _latencyPaint;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;

    public RoomToneRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _presetBar = new PluginPresetBar(_theme);

        LevelKnob = new KnobWidget(KnobRadius, -60f, -20f, "LEVEL", "dB", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0"
        };
        DuckKnob = new KnobWidget(KnobRadius, 0f, 1f, "DUCK", "", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0.00"
        };
        ToneKnob = new KnobWidget(KnobRadius, 3000f, 12000f, "TONE", "Hz", KnobStyle.Standard, _theme)
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

        _spectrumBackgroundPaint = new SKPaint
        {
            Color = _theme.WaveformBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _spectrumFillPaint = new SKPaint
        {
            Color = new SKColor(0x80, 0x80, 0xA0, 0x80), // Gray-blue noise color
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _duckMeterBackgroundPaint = new SKPaint
        {
            Color = _theme.WaveformBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _duckMeterFillPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0xA0, 0x40), // Orange for ducking
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _levelMeterBackgroundPaint = new SKPaint
        {
            Color = _theme.WaveformBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _levelMeterFillPaint = new SKPaint
        {
            Color = _theme.WaveformLine,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _labelPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Normal, SKTextAlign.Center);
        _latencyPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Right);
    }

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, RoomToneState state)
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

        // Noise level meter (left)
        float meterWidth = 24f;
        float meterHeight = SpectrumHeight + 20f;
        var levelRect = new SKRect(Padding, y, Padding + meterWidth, y + meterHeight);
        DrawLevelMeter(canvas, levelRect, state.NoiseLevel, state.LevelDb);

        // Duck meter (shows ducking amount)
        var duckRect = new SKRect(levelRect.Right + 8, y, levelRect.Right + 8 + meterWidth, y + meterHeight);
        DrawDuckMeter(canvas, duckRect, state.DuckAmount);

        // Noise spectrum visualization
        float spectrumX = duckRect.Right + 12f;
        float spectrumWidth = size.Width - spectrumX - Padding;
        var spectrumRect = new SKRect(spectrumX, y + 10f, spectrumX + spectrumWidth, y + 10f + SpectrumHeight);
        DrawNoiseSpectrum(canvas, spectrumRect, state.ToneHz);

        y += meterHeight + Padding + 10f;

        // Knobs
        float knobsY = y + KnobRadius + 10;
        float knobsTotalWidth = 3 * KnobSpacing;
        float knobsStartX = (size.Width - knobsTotalWidth) / 2 + KnobSpacing / 2;

        LevelKnob.Center = new SKPoint(knobsStartX, knobsY);
        LevelKnob.Value = state.LevelDb;
        LevelKnob.Render(canvas);

        DuckKnob.Center = new SKPoint(knobsStartX + KnobSpacing, knobsY);
        DuckKnob.Value = state.DuckStrength;
        DuckKnob.Render(canvas);

        ToneKnob.Center = new SKPoint(knobsStartX + KnobSpacing * 2, knobsY);
        ToneKnob.Value = state.ToneHz;
        ToneKnob.Render(canvas);

        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.Restore();
    }

    private void DrawTitleBar(SKCanvas canvas, SKSize size, RoomToneState state)
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

        canvas.DrawText("Room Tone", Padding, TitleBarHeight / 2f + 5, _titlePaint);

        float presetBarX = 90f;
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

    private void DrawLevelMeter(SKCanvas canvas, SKRect rect, float noiseLevel, float levelDb)
    {
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _levelMeterBackgroundPaint);

        // Normalize noise level for display
        float norm = Math.Clamp(noiseLevel * 1000f, 0f, 1f);
        float fillHeight = (rect.Height - 4) * norm;
        if (fillHeight > 1)
        {
            var fillRect = new SKRect(rect.Left + 2, rect.Bottom - 2 - fillHeight, rect.Right - 2, rect.Bottom - 2);
            canvas.DrawRect(fillRect, _levelMeterFillPaint);
        }

        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.DrawText("LVL", rect.MidX, rect.Bottom + 12, _labelPaint);
    }

    private void DrawDuckMeter(SKCanvas canvas, SKRect rect, float duckAmount)
    {
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _duckMeterBackgroundPaint);

        // Duck shows as "reduction" from top
        float fillHeight = (rect.Height - 4) * Math.Clamp(duckAmount, 0f, 1f);
        if (fillHeight > 1)
        {
            var fillRect = new SKRect(rect.Left + 2, rect.Top + 2, rect.Right - 2, rect.Top + 2 + fillHeight);
            canvas.DrawRect(fillRect, _duckMeterFillPaint);
        }

        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.DrawText("DUCK", rect.MidX, rect.Bottom + 12, _labelPaint);
    }

    private void DrawNoiseSpectrum(SKCanvas canvas, SKRect rect, float toneHz)
    {
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _spectrumBackgroundPaint);

        // Draw shaped noise spectrum (highpass at 80Hz, lowpass at toneHz)
        using var path = new SKPath();
        float padding = 4f;
        float plotWidth = rect.Width - padding * 2;
        float plotHeight = rect.Height - padding * 2;

        path.MoveTo(rect.Left + padding, rect.Bottom - padding);

        for (int i = 0; i <= 40; i++)
        {
            float t = i / 40f;
            float freq = 20f * MathF.Pow(1000f, t); // 20Hz to 20kHz

            float level = 0.5f;

            // Highpass rolloff at 80Hz
            if (freq < 80f)
            {
                level *= freq / 80f;
            }

            // Lowpass rolloff at toneHz
            if (freq > toneHz)
            {
                float rolloff = MathF.Max(0f, 1f - (freq - toneHz) / toneHz);
                level *= rolloff * rolloff;
            }

            float x = rect.Left + padding + t * plotWidth;
            float y = rect.Bottom - padding - level * plotHeight;
            path.LineTo(x, y);
        }

        path.LineTo(rect.Right - padding, rect.Bottom - padding);
        path.Close();

        canvas.DrawPath(path, _spectrumFillPaint);

        // Tone cutoff indicator
        float toneNorm = MathF.Log(toneHz / 20f) / MathF.Log(20000f / 20f);
        float toneX = rect.Left + padding + toneNorm * plotWidth;
        using var tonePaint = new SKPaint
        {
            Color = _theme.ThresholdLine,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            PathEffect = SKPathEffect.CreateDash(new[] { 3f, 3f }, 0)
        };
        canvas.DrawLine(toneX, rect.Top + padding, toneX, rect.Bottom - padding, tonePaint);

        canvas.DrawRoundRect(roundRect, _borderPaint);
    }

    public RoomToneHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new RoomToneHitTest(RoomToneHitArea.CloseButton, -1);

        if (_bypassButtonRect.Contains(x, y))
            return new RoomToneHitTest(RoomToneHitArea.BypassButton, -1);

        var presetHit = _presetBar.HitTest(x, y);
        if (presetHit == PresetBarHitArea.Dropdown)
            return new RoomToneHitTest(RoomToneHitArea.PresetDropdown, -1);
        if (presetHit == PresetBarHitArea.SaveButton)
            return new RoomToneHitTest(RoomToneHitArea.PresetSave, -1);

        if (LevelKnob.HitTest(x, y))
            return new RoomToneHitTest(RoomToneHitArea.Knob, 0);
        if (DuckKnob.HitTest(x, y))
            return new RoomToneHitTest(RoomToneHitArea.Knob, 1);
        if (ToneKnob.HitTest(x, y))
            return new RoomToneHitTest(RoomToneHitArea.Knob, 2);

        if (_titleBarRect.Contains(x, y))
            return new RoomToneHitTest(RoomToneHitArea.TitleBar, -1);

        return new RoomToneHitTest(RoomToneHitArea.None, -1);
    }

    public SKRect GetPresetDropdownRect() => _presetBar.GetDropdownRect();

    public static SKSize GetPreferredSize() => new(320, 280);

    public void Dispose()
    {
        LevelKnob.Dispose();
        DuckKnob.Dispose();
        ToneKnob.Dispose();
        _presetBar.Dispose();
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _spectrumBackgroundPaint.Dispose();
        _spectrumFillPaint.Dispose();
        _duckMeterBackgroundPaint.Dispose();
        _duckMeterFillPaint.Dispose();
        _levelMeterBackgroundPaint.Dispose();
        _levelMeterFillPaint.Dispose();
        _labelPaint.Dispose();
        _latencyPaint.Dispose();
    }
}

public record struct RoomToneState(
    float LevelDb,
    float DuckStrength,
    float ToneHz,
    float SpeechPresence,
    float DuckAmount,
    float NoiseLevel,
    float LatencyMs,
    bool IsBypassed,
    string PresetName = "Custom");

public enum RoomToneHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    Knob,
    PresetDropdown,
    PresetSave
}

public record struct RoomToneHitTest(RoomToneHitArea Area, int KnobIndex);
