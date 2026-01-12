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
    private readonly RotaryKnob _knob;
    private readonly GateIndicator _gateIndicator;

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _titlePaint;
    private readonly SKPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _sectionLabelPaint;
    private readonly SKPaint _latencyPaint;
    private readonly SKPaint _statusPaint;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;
    private readonly SKRect[] _knobRects = new SKRect[KnobCount];
    private readonly SKPoint[] _knobCenters = new SKPoint[KnobCount];

    public WaveformBuffer WaveformBuffer { get; } = new(256);

    public VoiceGateRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _vadMeter = new VadMeter(_theme);
        _waveformDisplay = new WaveformDisplay(_theme);
        _processingIndicator = new AiProcessingIndicator(_theme);
        _knob = new RotaryKnob(_theme);
        _gateIndicator = new GateIndicator(_theme);

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

        _latencyPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            TextSize = 9f,
            TextAlign = SKTextAlign.Right,
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
        using var badgeTextPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            TextSize = 8f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };
        canvas.DrawText("AI", badgeRect.MidX, badgeRect.MidY + 3, badgeTextPaint);

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
        _knobCenters[0] = new SKPoint(knobsStartX, knobsY);
        float thresholdNorm = (state.Threshold - 0.05f) / (0.95f - 0.05f);
        _knob.Render(canvas, _knobCenters[0], KnobRadius, thresholdNorm,
            "THRESH", $"{state.Threshold * 100:0}", "%", state.HoveredKnob == 0);
        _knobRects[0] = _knob.GetHitRect(_knobCenters[0], KnobRadius);

        // Attack knob
        _knobCenters[1] = new SKPoint(knobsStartX + KnobSpacing, knobsY);
        float attackNorm = (state.AttackMs - 1f) / (50f - 1f);
        _knob.Render(canvas, _knobCenters[1], KnobRadius, attackNorm,
            "ATTACK", $"{state.AttackMs:0}", "ms", state.HoveredKnob == 1);
        _knobRects[1] = _knob.GetHitRect(_knobCenters[1], KnobRadius);

        // Release knob
        _knobCenters[2] = new SKPoint(knobsStartX + KnobSpacing * 2, knobsY);
        float releaseNorm = (state.ReleaseMs - 20f) / (500f - 20f);
        _knob.Render(canvas, _knobCenters[2], KnobRadius, releaseNorm,
            "RELEASE", $"{state.ReleaseMs:0}", "ms", state.HoveredKnob == 2);
        _knobRects[2] = _knob.GetHitRect(_knobCenters[2], KnobRadius);

        // Hold knob
        _knobCenters[3] = new SKPoint(knobsStartX + KnobSpacing * 3, knobsY);
        float holdNorm = state.HoldMs / 300f;
        _knob.Render(canvas, _knobCenters[3], KnobRadius, holdNorm,
            "HOLD", $"{state.HoldMs:0}", "ms", state.HoveredKnob == 3);
        _knobRects[3] = _knob.GetHitRect(_knobCenters[3], KnobRadius);

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

        for (int i = 0; i < KnobCount; i++)
        {
            float dx = x - _knobCenters[i].X;
            float dy = y - _knobCenters[i].Y;
            if (dx * dx + dy * dy <= KnobRadius * KnobRadius * 1.5f)
            {
                return new VoiceGateHitTest(VoiceGateHitArea.Knob, i);
            }
        }

        if (_titleBarRect.Contains(x, y))
            return new VoiceGateHitTest(VoiceGateHitArea.TitleBar, -1);

        return new VoiceGateHitTest(VoiceGateHitArea.None, -1);
    }

    public static SKSize GetPreferredSize() => new(450, 380);

    public void Dispose()
    {
        _vadMeter.Dispose();
        _waveformDisplay.Dispose();
        _processingIndicator.Dispose();
        _knob.Dispose();
        _gateIndicator.Dispose();
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
    int HoveredKnob = -1);

public enum VoiceGateHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    Knob
}

public record struct VoiceGateHitTest(VoiceGateHitArea Area, int KnobIndex);
