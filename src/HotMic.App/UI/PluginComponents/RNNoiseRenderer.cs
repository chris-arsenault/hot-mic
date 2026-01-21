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
    private readonly PluginPresetBar _presetBar;

    /// <summary>Reduction percentage knob (0-100%).</summary>
    public KnobWidget ReductionKnob { get; }

    /// <summary>VAD threshold knob (0-100%).</summary>
    public KnobWidget VadThresholdKnob { get; }

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SkiaTextPaint _sectionLabelPaint;
    private readonly SkiaTextPaint _statusPaint;
    private readonly SkiaTextPaint _descriptionPaint;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;

    public RNNoiseRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _vadMeter = new VadMeter(_theme);
        _grMeter = new GainReductionMeter(_theme);
        _processingIndicator = new AiProcessingIndicator(_theme);
        _presetBar = new PluginPresetBar(_theme);

        var knobStyle = KnobStyle.Standard;
        ReductionKnob = new KnobWidget(KnobRadius, 0f, 100f, "REDUCTION", "%", knobStyle, _theme)
        {
            ValueFormat = "0"
        };
        VadThresholdKnob = new KnobWidget(KnobRadius, 0f, 100f, "VAD THRESH", "%", knobStyle, _theme)
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

        _sectionLabelPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal);
        _statusPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Normal, SKTextAlign.Center);
        _descriptionPaint = new SkiaTextPaint(_theme.TextMuted, 10f, SKFontStyle.Normal, SKTextAlign.Center);
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
        using var badgeTextPaint = new SkiaTextPaint(SKColors.White, 8f, SKFontStyle.Bold, SKTextAlign.Center);
        canvas.DrawText("AI", badgeRect.MidX, badgeRect.MidY + 3, badgeTextPaint);

        // Latency
        if (state.LatencyMs > 0)
        {
            using var latencyPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Right);
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

        using var bypassTextPaint = new SkiaTextPaint(state.IsBypassed ? _theme.TextPrimary : _theme.TextSecondary, 10f, SKFontStyle.Bold, SKTextAlign.Center);
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
        ReductionKnob.Center = new SKPoint(knobsStartX, knobsY);
        ReductionKnob.Value = state.ReductionPercent;
        ReductionKnob.Render(canvas);

        // VAD Threshold knob
        VadThresholdKnob.Center = new SKPoint(knobsStartX + KnobSpacing, knobsY);
        VadThresholdKnob.Value = state.VadThreshold;
        VadThresholdKnob.Render(canvas);

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

        using var statusTextPaint = new SkiaTextPaint(_theme.TextPrimary, 10f, SKFontStyle.Bold, SKTextAlign.Center);
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

        if (ReductionKnob.HitTest(x, y))
            return new RNNoiseHitTest(RNNoiseHitArea.Knob, 0);

        if (VadThresholdKnob.HitTest(x, y))
            return new RNNoiseHitTest(RNNoiseHitArea.Knob, 1);

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
        ReductionKnob.Dispose();
        VadThresholdKnob.Dispose();
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
    string PresetName = "Custom");

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
