using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Complete noise gate plugin UI renderer.
/// Composes waveform display, envelope curve, rotary knobs, and gate indicator.
/// </summary>
public sealed class NoiseGateRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 12f;
    private const float WaveformHeight = 100f;
    private const float EnvelopeHeight = 60f;
    private const float KnobRadius = 24f;
    private const float KnobSpacing = 72f;
    private const float CornerRadius = 10f;
    private const int KnobCount = 5;

    private readonly PluginComponentTheme _theme;
    private readonly WaveformDisplay _waveformDisplay;
    private readonly EnvelopeCurveDisplay _envelopeCurve;
    private readonly RotaryKnob _knob;
    private readonly GateIndicator _gateIndicator;
    private readonly PluginPresetBar _presetBar;

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _titlePaint;
    private readonly SKPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _sectionLabelPaint;
    private readonly SKPaint _latencyPaint;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;
    private readonly SKRect[] _knobRects = new SKRect[KnobCount];
    private readonly SKPoint[] _knobCenters = new SKPoint[KnobCount];
    private int _knobCount = KnobCount;

    public WaveformBuffer WaveformBuffer { get; } = new(256);

    public NoiseGateRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _waveformDisplay = new WaveformDisplay(_theme);
        _envelopeCurve = new EnvelopeCurveDisplay(_theme);
        _knob = new RotaryKnob(_theme);
        _gateIndicator = new GateIndicator(_theme);
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

        _latencyPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            TextSize = 9f,
            TextAlign = SKTextAlign.Right,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };
    }

    /// <summary>
    /// Render the complete noise gate UI.
    /// </summary>
    public void Render(
        SKCanvas canvas,
        SKSize size,
        float dpiScale,
        NoiseGateState state)
    {
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        canvas.Scale(dpiScale);
        size = new SKSize(size.Width / dpiScale, size.Height / dpiScale);

        // Main background with rounded corners
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
        canvas.DrawText("Noise Gate", Padding, TitleBarHeight / 2f + 5, _titlePaint);

        // Preset bar
        float presetBarX = Padding + 85;
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

        using var bypassTextPaint = new SKPaint
        {
            Color = state.IsBypassed ? _theme.TextPrimary : _theme.TextSecondary,
            IsAntialias = true,
            TextSize = 10f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };
        canvas.DrawText("BYPASS", _bypassButtonRect.MidX, _bypassButtonRect.MidY + 4, bypassTextPaint);

        if (state.LatencyMs >= 0f)
        {
            string latencyLabel = $"LAT {state.LatencyMs:0.0}ms";
            canvas.DrawText(latencyLabel, _bypassButtonRect.Left - 6f, TitleBarHeight / 2f + 4, _latencyPaint);
        }

        // Close button
        _closeButtonRect = new SKRect(size.Width - Padding - 24, (TitleBarHeight - 24) / 2,
            size.Width - Padding, (TitleBarHeight + 24) / 2);
        canvas.DrawText("\u00D7", _closeButtonRect.MidX, _closeButtonRect.MidY + 6, _closeButtonPaint);

        float y = TitleBarHeight + Padding;

        // Waveform section
        canvas.DrawText("INPUT LEVEL", Padding, y + 10, _sectionLabelPaint);
        y += 14;

        var waveformRect = new SKRect(Padding, y, size.Width - Padding, y + WaveformHeight);
        _waveformDisplay.Render(canvas, waveformRect, WaveformBuffer, state.ThresholdDb);
        y += WaveformHeight + Padding;

        // Gate indicator on the right side of waveform
        var indicatorCenter = new SKPoint(size.Width - Padding - 24, waveformRect.MidY);
        _gateIndicator.Render(canvas, indicatorCenter, 16f, state.IsGateOpen, showLabel: false);

        // Envelope curve section
        canvas.DrawText("ENVELOPE SHAPE", Padding, y + 10, _sectionLabelPaint);
        y += 14;

        var envelopeRect = new SKRect(Padding, y, size.Width - Padding, y + EnvelopeHeight);
        _envelopeCurve.Render(canvas, envelopeRect, state.AttackMs, state.HoldMs, state.ReleaseMs, state.IsGateOpen);
        y += EnvelopeHeight + Padding;

        // Knobs section
        float knobsY = y + KnobRadius + 16;
        float knobsTotalWidth = KnobCount * KnobSpacing;
        float knobsStartX = (size.Width - knobsTotalWidth) / 2 + KnobSpacing / 2;

        // Threshold knob
        _knobCenters[0] = new SKPoint(knobsStartX, knobsY);
        float thresholdNorm = (state.ThresholdDb - (-80f)) / (0f - (-80f));
        _knob.Render(canvas, _knobCenters[0], KnobRadius, thresholdNorm,
            "THRESH", $"{state.ThresholdDb:0.0}", "dB", state.HoveredKnob == 0);
        _knobRects[0] = _knob.GetHitRect(_knobCenters[0], KnobRadius);

        // Hysteresis knob
        _knobCenters[1] = new SKPoint(knobsStartX + KnobSpacing, knobsY);
        float hysteresisNorm = state.HysteresisDb / 12f;
        _knob.Render(canvas, _knobCenters[1], KnobRadius, hysteresisNorm,
            "HYSTER", $"{state.HysteresisDb:0.0}", "dB", state.HoveredKnob == 1);
        _knobRects[1] = _knob.GetHitRect(_knobCenters[1], KnobRadius);

        // Attack knob
        _knobCenters[2] = new SKPoint(knobsStartX + KnobSpacing * 2, knobsY);
        float attackNorm = (state.AttackMs - 0.1f) / (50f - 0.1f);
        _knob.Render(canvas, _knobCenters[2], KnobRadius, attackNorm,
            "ATTACK", $"{state.AttackMs:0.0}", "ms", state.HoveredKnob == 2);
        _knobRects[2] = _knob.GetHitRect(_knobCenters[2], KnobRadius);

        // Hold knob
        _knobCenters[3] = new SKPoint(knobsStartX + KnobSpacing * 3, knobsY);
        float holdNorm = state.HoldMs / 500f;
        _knob.Render(canvas, _knobCenters[3], KnobRadius, holdNorm,
            "HOLD", $"{state.HoldMs:0}", "ms", state.HoveredKnob == 3);
        _knobRects[3] = _knob.GetHitRect(_knobCenters[3], KnobRadius);

        // Release knob
        _knobCenters[4] = new SKPoint(knobsStartX + KnobSpacing * 4, knobsY);
        float releaseNorm = (state.ReleaseMs - 10f) / (500f - 10f);
        _knob.Render(canvas, _knobCenters[4], KnobRadius, releaseNorm,
            "RELEASE", $"{state.ReleaseMs:0}", "ms", state.HoveredKnob == 4);
        _knobRects[4] = _knob.GetHitRect(_knobCenters[4], KnobRadius);

        // Gate status bar at bottom
        float barHeight = 20f;
        float barY = size.Height - Padding - barHeight;
        var gateBarRect = new SKRect(Padding, barY, size.Width - Padding, barY + barHeight);
        _gateIndicator.RenderBar(canvas, gateBarRect, state.IsGateOpen, state.IsGateOpen ? "GATE OPEN" : "GATE CLOSED");

        // Outer border
        canvas.DrawRoundRect(roundRect, _borderPaint);

        canvas.Restore();
    }

    /// <summary>
    /// Test which UI element was hit.
    /// Mouse coordinates are in logical pixels (WPF DIPs), same as our stored rects.
    /// </summary>
    public NoiseGateHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new NoiseGateHitTest(NoiseGateHitArea.CloseButton, -1);

        if (_bypassButtonRect.Contains(x, y))
            return new NoiseGateHitTest(NoiseGateHitArea.BypassButton, -1);

        var presetHit = _presetBar.HitTest(x, y);
        if (presetHit == PresetBarHitArea.Dropdown)
            return new NoiseGateHitTest(NoiseGateHitArea.PresetDropdown, -1);
        if (presetHit == PresetBarHitArea.SaveButton)
            return new NoiseGateHitTest(NoiseGateHitArea.PresetSave, -1);

        for (int i = 0; i < _knobCount; i++)
        {
            float dx = x - _knobCenters[i].X;
            float dy = y - _knobCenters[i].Y;
            if (dx * dx + dy * dy <= KnobRadius * KnobRadius * 1.5f)
            {
                return new NoiseGateHitTest(NoiseGateHitArea.Knob, i);
            }
        }

        if (_titleBarRect.Contains(x, y))
            return new NoiseGateHitTest(NoiseGateHitArea.TitleBar, -1);

        return new NoiseGateHitTest(NoiseGateHitArea.None, -1);
    }

    public SKRect GetPresetDropdownRect() => _presetBar.GetDropdownRect();

    public static SKSize GetPreferredSize() => new(420, 400);

    public void Dispose()
    {
        _waveformDisplay.Dispose();
        _envelopeCurve.Dispose();
        _knob.Dispose();
        _gateIndicator.Dispose();
        _presetBar.Dispose();
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _sectionLabelPaint.Dispose();
        _latencyPaint.Dispose();
    }
}

/// <summary>
/// State data for rendering the noise gate UI.
/// </summary>
public record struct NoiseGateState(
    float ThresholdDb,
    float HysteresisDb,
    float AttackMs,
    float HoldMs,
    float ReleaseMs,
    float LatencyMs,
    bool IsGateOpen,
    bool IsBypassed,
    int HoveredKnob = -1,
    string PresetName = "Custom");

public enum NoiseGateHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    Knob,
    PresetDropdown,
    PresetSave
}

public record struct NoiseGateHitTest(NoiseGateHitArea Area, int KnobIndex);
