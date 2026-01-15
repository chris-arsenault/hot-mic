using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Complete voice gate plugin UI renderer with AI processing visualization.
/// Shows VAD probability, waveform, and gate status.
/// </summary>
public sealed class VoiceGateRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 16f;
    private const float VadMeterWidth = 50f;
    private const float WaveformHeight = 80f;
    private const float KnobRadius = 22f;
    private const float KnobSpacing = 80f;
    private const float CornerRadius = 10f;
    private const int KnobCount = 4;

    private readonly PluginComponentTheme _theme;
    private readonly VadMeter _vadMeter;
    private readonly WaveformDisplay _waveformDisplay;
    private readonly AiProcessingIndicator _processingIndicator;
    private readonly GateIndicator _gateIndicator;
    private readonly PluginPresetBar _presetBar;

    public KnobWidget ThresholdKnob { get; }
    public KnobWidget AttackKnob { get; }
    public KnobWidget ReleaseKnob { get; }
    public KnobWidget HoldKnob { get; }

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SkiaTextPaint _sectionLabelPaint;
    private readonly SkiaTextPaint _latencyPaint;
    private readonly SkiaTextPaint _statusPaint;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;

    public WaveformBuffer WaveformBuffer { get; } = new(256);

    public VoiceGateRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _vadMeter = new VadMeter(_theme);
        _waveformDisplay = new WaveformDisplay(_theme);
        _processingIndicator = new AiProcessingIndicator(_theme);
        _gateIndicator = new GateIndicator(_theme);
        _presetBar = new PluginPresetBar(_theme);

        var knobStyle = KnobStyle.Standard;
        ThresholdKnob = new KnobWidget(KnobRadius, 0.05f, 0.95f, "THRESH", "%", knobStyle, _theme) { ValueFormat = "0" };
        AttackKnob = new KnobWidget(KnobRadius, 1f, 50f, "ATTACK", "ms", knobStyle, _theme) { ValueFormat = "0" };
        ReleaseKnob = new KnobWidget(KnobRadius, 20f, 500f, "RELEASE", "ms", knobStyle, _theme) { ValueFormat = "0" };
        HoldKnob = new KnobWidget(KnobRadius, 0f, 300f, "HOLD", "ms", knobStyle, _theme) { ValueFormat = "0" };

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
        _latencyPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Right);
        _statusPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Normal, SKTextAlign.Center);
    }

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, VoiceGateState state)
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

        // Title with AI badge
        canvas.DrawText("Voice Gate", Padding, TitleBarHeight / 2f + 5, _titlePaint);

        // AI badge
        var badgeRect = new SKRect(Padding + 85, (TitleBarHeight - 16) / 2, Padding + 105, (TitleBarHeight + 16) / 2);
        using var badgePaint = new SKPaint { Color = new SKColor(0x80, 0x40, 0xFF), IsAntialias = true };
        canvas.DrawRoundRect(new SKRoundRect(badgeRect, 3f), badgePaint);
        using var badgeTextPaint = new SkiaTextPaint(SKColors.White, 8f, SKFontStyle.Bold, SKTextAlign.Center);
        canvas.DrawText("AI", badgeRect.MidX, badgeRect.MidY + 3, badgeTextPaint);

        // Preset bar
        float presetBarX = Padding + 115;
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

        // Main visualization area
        float vizLeft = Padding;
        float vizRight = size.Width - Padding - VadMeterWidth - Padding;

        // Processing indicator
        var indicatorCenter = new SKPoint(size.Width - Padding - VadMeterWidth / 2, y + 30);
        _processingIndicator.Render(canvas, indicatorCenter, 24f, !state.IsBypassed && state.VadProbability > 0.1f, state.VadProbability, "SILERO");

        // VAD Meter
        var vadRect = new SKRect(
            size.Width - Padding - VadMeterWidth,
            y + 70,
            size.Width - Padding,
            y + 70 + 100);
        _vadMeter.Render(canvas, vadRect, state.VadProbability, state.Threshold, state.IsGateOpen);

        // Waveform section
        canvas.DrawText("AUDIO LEVEL", vizLeft, y + 10, _sectionLabelPaint);
        y += 14;

        var waveformRect = new SKRect(vizLeft, y, vizRight, y + WaveformHeight);
        _waveformDisplay.Render(canvas, waveformRect, WaveformBuffer, -40f); // Use threshold as reference

        // Gate indicator overlay
        var gateCenter = new SKPoint(waveformRect.Right - 20, waveformRect.MidY);
        _gateIndicator.Render(canvas, gateCenter, 14f, state.IsGateOpen, showLabel: false);

        y += WaveformHeight + Padding + 10;

        // Knobs section
        float knobsY = y + KnobRadius + 8;
        float knobsTotalWidth = KnobCount * KnobSpacing;
        float knobsStartX = vizLeft + (vizRight - vizLeft - knobsTotalWidth) / 2 + KnobSpacing / 2;

        // Threshold knob
        ThresholdKnob.Center = new SKPoint(knobsStartX, knobsY);
        ThresholdKnob.Value = state.Threshold * 100f;
        ThresholdKnob.Render(canvas);

        // Attack knob
        AttackKnob.Center = new SKPoint(knobsStartX + KnobSpacing, knobsY);
        AttackKnob.Value = state.AttackMs;
        AttackKnob.Render(canvas);

        // Release knob
        ReleaseKnob.Center = new SKPoint(knobsStartX + KnobSpacing * 2, knobsY);
        ReleaseKnob.Value = state.ReleaseMs;
        ReleaseKnob.Render(canvas);

        // Hold knob
        HoldKnob.Center = new SKPoint(knobsStartX + KnobSpacing * 3, knobsY);
        HoldKnob.Value = state.HoldMs;
        HoldKnob.Render(canvas);

        // Gate status bar
        float barHeight = 24f;
        float barY = size.Height - Padding - barHeight;
        var gateBarRect = new SKRect(Padding, barY, size.Width - Padding, barY + barHeight);
        _gateIndicator.RenderBar(canvas, gateBarRect, state.IsGateOpen,
            state.IsGateOpen ? $"VOICE DETECTED ({state.VadProbability * 100:0}%)" : "WAITING FOR VOICE");

        // Outer border
        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.Restore();
    }

    public VoiceGateHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new VoiceGateHitTest(VoiceGateHitArea.CloseButton, -1);

        if (_bypassButtonRect.Contains(x, y))
            return new VoiceGateHitTest(VoiceGateHitArea.BypassButton, -1);

        var presetHit = _presetBar.HitTest(x, y);
        if (presetHit == PresetBarHitArea.Dropdown)
            return new VoiceGateHitTest(VoiceGateHitArea.PresetDropdown, -1);
        if (presetHit == PresetBarHitArea.SaveButton)
            return new VoiceGateHitTest(VoiceGateHitArea.PresetSave, -1);

        if (ThresholdKnob.HitTest(x, y))
            return new VoiceGateHitTest(VoiceGateHitArea.Knob, 0);
        if (AttackKnob.HitTest(x, y))
            return new VoiceGateHitTest(VoiceGateHitArea.Knob, 1);
        if (ReleaseKnob.HitTest(x, y))
            return new VoiceGateHitTest(VoiceGateHitArea.Knob, 2);
        if (HoldKnob.HitTest(x, y))
            return new VoiceGateHitTest(VoiceGateHitArea.Knob, 3);

        if (_titleBarRect.Contains(x, y))
            return new VoiceGateHitTest(VoiceGateHitArea.TitleBar, -1);

        return new VoiceGateHitTest(VoiceGateHitArea.None, -1);
    }

    public SKRect GetPresetDropdownRect() => _presetBar.GetDropdownRect();

    public static SKSize GetPreferredSize() => new(450, 380);

    public void Dispose()
    {
        _vadMeter.Dispose();
        _waveformDisplay.Dispose();
        _processingIndicator.Dispose();
        _gateIndicator.Dispose();
        _presetBar.Dispose();
        ThresholdKnob.Dispose();
        AttackKnob.Dispose();
        ReleaseKnob.Dispose();
        HoldKnob.Dispose();
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _sectionLabelPaint.Dispose();
        _latencyPaint.Dispose();
        _statusPaint.Dispose();
    }
}

public record struct VoiceGateState(
    float Threshold,
    float AttackMs,
    float ReleaseMs,
    float HoldMs,
    float VadProbability,
    bool IsGateOpen,
    bool IsBypassed,
    string StatusMessage = "",
    string PresetName = "Custom");

public enum VoiceGateHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    Knob,
    PresetDropdown,
    PresetSave
}

public record struct VoiceGateHitTest(VoiceGateHitArea Area, int KnobIndex);
