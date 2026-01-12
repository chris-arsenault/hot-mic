using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// RNNoise plugin UI renderer with noise reduction visualization.
/// Shows VAD probability, reduction level, and processing status.
/// </summary>
public sealed class RNNoiseRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 16f;
    private const float VadMeterWidth = 50f;
    private const float KnobRadius = 28f;
    private const float KnobSpacing = 100f;
    private const float CornerRadius = 10f;
    private const int KnobCount = 2;

    private readonly PluginComponentTheme _theme;
    private readonly VadMeter _vadMeter;
    private readonly GainReductionMeter _grMeter;
    private readonly AiProcessingIndicator _processingIndicator;
    private readonly RotaryKnob _knob;
    private readonly PluginPresetBar _presetBar;

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _titlePaint;
    private readonly SKPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _sectionLabelPaint;
    private readonly SKPaint _statusPaint;
    private readonly SKPaint _descriptionPaint;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;
    private readonly SKRect[] _knobRects = new SKRect[KnobCount];
    private readonly SKPoint[] _knobCenters = new SKPoint[KnobCount];

    public RNNoiseRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _vadMeter = new VadMeter(_theme);
        _grMeter = new GainReductionMeter(_theme);
        _processingIndicator = new AiProcessingIndicator(_theme);
        _knob = new RotaryKnob(_theme);
        _presetBar = new PluginPresetBar(_theme);

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

        _sectionLabelPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            TextSize = 9f,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };

        _statusPaint = new SKPaint
        {
            Color = _theme.TextSecondary,
            IsAntialias = true,
            TextSize = 10f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };

        _descriptionPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            TextSize = 10f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };
    }

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, RNNoiseState state)
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
        canvas.DrawText("RNNoise", Padding, TitleBarHeight / 2f + 5, _titlePaint);

        // Preset bar (after title, before AI badge)
        float presetBarX = Padding + 70;
        float presetBarY = (TitleBarHeight - PluginPresetBar.TotalHeight) / 2f;
        _presetBar.Render(canvas, presetBarX, presetBarY, state.PresetName);

        // AI badge (positioned after preset bar)
        float badgeX = presetBarX + PluginPresetBar.TotalWidth + 8f;
        var badgeRect = new SKRect(badgeX, (TitleBarHeight - 16) / 2, badgeX + 20, (TitleBarHeight + 16) / 2);
        using var badgePaint = new SKPaint { Color = new SKColor(0x40, 0xA0, 0xFF), IsAntialias = true };
        canvas.DrawRoundRect(new SKRoundRect(badgeRect, 3f), badgePaint);
        using var badgeTextPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            TextSize = 8f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };
        canvas.DrawText("AI", badgeRect.MidX, badgeRect.MidY + 3, badgeTextPaint);

        // Latency
        if (state.LatencyMs > 0)
        {
            using var latencyPaint = new SKPaint
            {
                Color = _theme.TextMuted,
                IsAntialias = true,
                TextSize = 9f,
                TextAlign = SKTextAlign.Right,
                Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
            };
            canvas.DrawText($"LAT {state.LatencyMs:0.0}ms", size.Width - Padding - 100, TitleBarHeight / 2f + 4, latencyPaint);
        }

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

        // Close button
        _closeButtonRect = new SKRect(size.Width - Padding - 24, (TitleBarHeight - 24) / 2,
            size.Width - Padding, (TitleBarHeight + 24) / 2);
        canvas.DrawText("\u00D7", _closeButtonRect.MidX, _closeButtonRect.MidY + 6, _closeButtonPaint);

        float y = TitleBarHeight + Padding;

        // Status message if any
        if (!string.IsNullOrEmpty(state.StatusMessage))
        {
            _statusPaint.Color = new SKColor(0xFF, 0xA0, 0x40);
            canvas.DrawText(state.StatusMessage, size.Width / 2, y + 10, _statusPaint);
            y += 24;
        }

        // Description
        canvas.DrawText("Recurrent Neural Network Noise Suppression", size.Width / 2, y + 10, _descriptionPaint);
        y += 24;

        // Processing indicator (centered)
        var indicatorCenter = new SKPoint(size.Width / 2, y + 40);
        _processingIndicator.Render(canvas, indicatorCenter, 32f, !state.IsBypassed && state.VadProbability > 0.1f, state.VadProbability, "PROCESSING");
        y += 90;

        // VAD Meter on the right
        var vadRect = new SKRect(
            size.Width - Padding - VadMeterWidth,
            TitleBarHeight + Padding + 24,
            size.Width - Padding,
            TitleBarHeight + Padding + 24 + 120);
        _vadMeter.Render(canvas, vadRect, state.VadProbability, state.VadThreshold / 100f, state.VadProbability >= state.VadThreshold / 100f);

        // Gain reduction meter (actual dB reduction) - RNNoise typically < 12dB
        var grRect = new SKRect(Padding, y, size.Width - Padding, y + 24);
        _grMeter.Render(canvas, grRect, state.GainReductionDb, "Gain Reduction", maxDb: 12f);
        y += 55;

        // Knobs section
        float knobsY = y + KnobRadius + 16;
        float knobsTotalWidth = KnobCount * KnobSpacing;
        float knobsStartX = (size.Width - knobsTotalWidth) / 2 + KnobSpacing / 2;

        // Reduction knob
        _knobCenters[0] = new SKPoint(knobsStartX, knobsY);
        float reductionNorm = state.ReductionPercent / 100f;
        _knob.Render(canvas, _knobCenters[0], KnobRadius, reductionNorm,
            "REDUCTION", $"{state.ReductionPercent:0}", "%", state.HoveredKnob == 0);
        _knobRects[0] = _knob.GetHitRect(_knobCenters[0], KnobRadius);

        // VAD Threshold knob
        _knobCenters[1] = new SKPoint(knobsStartX + KnobSpacing, knobsY);
        float vadNorm = state.VadThreshold / 100f;
        _knob.Render(canvas, _knobCenters[1], KnobRadius, vadNorm,
            "VAD THRESH", $"{state.VadThreshold:0}", "%", state.HoveredKnob == 1);
        _knobRects[1] = _knob.GetHitRect(_knobCenters[1], KnobRadius);

        // Status bar at bottom
        float barHeight = 24f;
        float barY = size.Height - Padding - barHeight;
        var statusBarRect = new SKRect(Padding, barY, size.Width - Padding, barY + barHeight);

        string statusText = state.IsBypassed ? "BYPASSED" :
            state.VadProbability >= state.VadThreshold / 100f ? "VOICE DETECTED - PROCESSING" : "NOISE REDUCTION ACTIVE";
        SKColor barColor = state.IsBypassed ? new SKColor(0x80, 0x80, 0x80) :
            state.VadProbability >= state.VadThreshold / 100f ? new SKColor(0x40, 0xC0, 0x40) : new SKColor(0x40, 0x80, 0xFF);

        using var barPaint = new SKPaint { Color = barColor.WithAlpha(100), IsAntialias = true };
        canvas.DrawRoundRect(new SKRoundRect(statusBarRect, 4f), barPaint);
        canvas.DrawRoundRect(new SKRoundRect(statusBarRect, 4f), _borderPaint);

        using var statusTextPaint = new SKPaint
        {
            Color = _theme.TextPrimary,
            IsAntialias = true,
            TextSize = 10f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };
        canvas.DrawText(statusText, statusBarRect.MidX, statusBarRect.MidY + 4, statusTextPaint);

        // Outer border
        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.Restore();
    }

    public RNNoiseHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new RNNoiseHitTest(RNNoiseHitArea.CloseButton, -1);

        if (_bypassButtonRect.Contains(x, y))
            return new RNNoiseHitTest(RNNoiseHitArea.BypassButton, -1);

        // Check preset bar hits
        var presetHit = _presetBar.HitTest(x, y);
        if (presetHit == PresetBarHitArea.Dropdown)
            return new RNNoiseHitTest(RNNoiseHitArea.PresetDropdown, -1);
        if (presetHit == PresetBarHitArea.SaveButton)
            return new RNNoiseHitTest(RNNoiseHitArea.PresetSave, -1);

        for (int i = 0; i < KnobCount; i++)
        {
            float dx = x - _knobCenters[i].X;
            float dy = y - _knobCenters[i].Y;
            if (dx * dx + dy * dy <= KnobRadius * KnobRadius * 1.5f)
            {
                return new RNNoiseHitTest(RNNoiseHitArea.Knob, i);
            }
        }

        if (_titleBarRect.Contains(x, y))
            return new RNNoiseHitTest(RNNoiseHitArea.TitleBar, -1);

        return new RNNoiseHitTest(RNNoiseHitArea.None, -1);
    }

    public SKRect GetPresetDropdownRect() => _presetBar.GetDropdownRect();

    public static SKSize GetPreferredSize() => new(380, 400);

    public void Dispose()
    {
        _vadMeter.Dispose();
        _grMeter.Dispose();
        _processingIndicator.Dispose();
        _knob.Dispose();
        _presetBar.Dispose();
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _sectionLabelPaint.Dispose();
        _statusPaint.Dispose();
        _descriptionPaint.Dispose();
    }
}

public record struct RNNoiseState(
    float ReductionPercent,
    float VadThreshold,
    float VadProbability,
    float GainReductionDb,
    float LatencyMs,
    bool IsBypassed,
    string StatusMessage = "",
    string PresetName = "Custom",
    int HoveredKnob = -1);

public enum RNNoiseHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    Knob,
    PresetDropdown,
    PresetSave
}

public record struct RNNoiseHitTest(RNNoiseHitArea Area, int KnobIndex);
