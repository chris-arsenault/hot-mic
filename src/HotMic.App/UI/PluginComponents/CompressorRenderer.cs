using HotMic.Core.Plugins.BuiltIn;
using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Complete compressor plugin UI renderer.
/// Features transfer curve display, gain reduction meter, and parameter knobs.
/// </summary>
public sealed class CompressorRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 12f;
    private const float TransferCurveSize = 120f;
    private const float GrMeterWidth = 40f;
    private const float KnobRadius = 24f;
    private const float KnobSpacing = 64f;
    private const float CornerRadius = 10f;
    private const int KnobCount = 6;

    private readonly PluginComponentTheme _theme;
    private readonly PluginPresetBar _presetBar;

    // Knob widgets
    public KnobWidget ThresholdKnob { get; }
    public KnobWidget RatioKnob { get; }
    public KnobWidget AttackKnob { get; }
    public KnobWidget ReleaseKnob { get; }
    public KnobWidget KneeKnob { get; }
    public KnobWidget MakeupKnob { get; }

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SkiaTextPaint _sectionLabelPaint;
    private readonly SKPaint _gridPaint;
    private readonly SKPaint _curvePaint;
    private readonly SKPaint _thresholdPaint;
    private readonly SKPaint _inputMarkerPaint;
    private readonly SKPaint _grBarPaint;
    private readonly SKPaint _grBackgroundPaint;
    private readonly SkiaTextPaint _grTextPaint;
    private readonly SkiaTextPaint _latencyPaint;
    private readonly SKPaint _togglePaint;
    private readonly SKPaint _toggleActivePaint;

    // Gain reduction meter with PPM-style ballistics
    private readonly GainReductionMeter _grMeter;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;
    private SKRect _detectorToggleRect;
    private SKRect _sidechainToggleRect;

    public CompressorRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _presetBar = new PluginPresetBar(_theme);

        // Initialize knob widgets
        ThresholdKnob = new KnobWidget(KnobRadius, -60f, 0f, "THRESH", "dB", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0.0",
            DragSensitivity = 0.004f
        };
        RatioKnob = new KnobWidget(KnobRadius, 1f, 20f, "RATIO", ":1", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0.0",
            DragSensitivity = 0.004f
        };
        AttackKnob = new KnobWidget(KnobRadius, 0.1f, 100f, "ATTACK", "ms", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0.0",
            DragSensitivity = 0.004f
        };
        ReleaseKnob = new KnobWidget(KnobRadius, 10f, 1000f, "RELEASE", "ms", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0",
            DragSensitivity = 0.004f
        };
        KneeKnob = new KnobWidget(KnobRadius, 0f, 12f, "KNEE", "dB", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0.0",
            DragSensitivity = 0.004f
        };
        MakeupKnob = new KnobWidget(KnobRadius, 0f, 24f, "MAKEUP", "dB", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0.0",
            DragSensitivity = 0.004f
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

        _gridPaint = new SKPaint
        {
            Color = _theme.EnvelopeGrid,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };

        _curvePaint = new SKPaint
        {
            Color = _theme.KnobArc,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f,
            StrokeCap = SKStrokeCap.Round
        };

        _thresholdPaint = new SKPaint
        {
            Color = _theme.ThresholdLine,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            PathEffect = SKPathEffect.CreateDash(new[] { 4f, 3f }, 0)
        };

        _inputMarkerPaint = new SKPaint
        {
            Color = _theme.WaveformLine,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _grBarPaint = new SKPaint
        {
            Color = _theme.KnobArc,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _grBackgroundPaint = new SKPaint
        {
            Color = _theme.WaveformBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _grTextPaint = new SkiaTextPaint(_theme.TextPrimary, 20f, SKFontStyle.Bold, SKTextAlign.Center);

        _latencyPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Right);

        _togglePaint = new SKPaint
        {
            Color = _theme.PanelBackgroundLight,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _toggleActivePaint = new SKPaint
        {
            Color = new SKColor(0x40, 0xC0, 0x40),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        // Initialize GR meter with PPM-style ballistics
        _grMeter = new GainReductionMeter(_theme);
    }

    public void Render(
        SKCanvas canvas,
        SKSize size,
        float dpiScale,
        CompressorState state)
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
        canvas.DrawText("Compressor", Padding, TitleBarHeight / 2f + 5, _titlePaint);

        // Preset bar (after title, before bypass)
        float presetBarX = 100f;
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

        // Transfer curve and GR meter side by side
        float displayWidth = size.Width - Padding * 3 - GrMeterWidth;
        var transferRect = new SKRect(Padding, y, Padding + displayWidth, y + TransferCurveSize);
        var grMeterRect = new SKRect(size.Width - Padding - GrMeterWidth, y, size.Width - Padding, y + TransferCurveSize);

        // Section labels
        canvas.DrawText("TRANSFER CURVE", Padding, y - 2, _sectionLabelPaint);

        // Draw transfer curve
        DrawTransferCurve(canvas, transferRect, state);

        // Draw GR meter with PPM-style ballistics
        var grLabelRect = new SKRect(grMeterRect.Left, y + 16f, grMeterRect.Right, grMeterRect.Bottom - 16f);
        _grMeter.Render(canvas, grLabelRect, state.GainReductionDb, "GR", maxDb: 24f);

        y += TransferCurveSize + Padding + 8;

        // Knobs section
        float knobsY = y + KnobRadius + 16;
        float knobsTotalWidth = KnobCount * KnobSpacing;
        float knobsStartX = (size.Width - knobsTotalWidth) / 2 + KnobSpacing / 2;

        // Threshold knob
        ThresholdKnob.Center = new SKPoint(knobsStartX, knobsY);
        ThresholdKnob.Value = state.ThresholdDb;
        ThresholdKnob.Render(canvas);

        // Ratio knob
        RatioKnob.Center = new SKPoint(knobsStartX + KnobSpacing, knobsY);
        RatioKnob.Value = state.Ratio;
        RatioKnob.Render(canvas);

        // Attack knob
        AttackKnob.Center = new SKPoint(knobsStartX + KnobSpacing * 2, knobsY);
        AttackKnob.Value = state.AttackMs;
        AttackKnob.Render(canvas);

        // Release knob
        ReleaseKnob.Center = new SKPoint(knobsStartX + KnobSpacing * 3, knobsY);
        ReleaseKnob.Value = state.ReleaseMs;
        ReleaseKnob.Render(canvas);

        // Knee knob
        KneeKnob.Center = new SKPoint(knobsStartX + KnobSpacing * 4, knobsY);
        KneeKnob.Value = state.KneeDb;
        KneeKnob.Render(canvas);

        // Makeup knob
        MakeupKnob.Center = new SKPoint(knobsStartX + KnobSpacing * 5, knobsY);
        MakeupKnob.Value = state.MakeupDb;
        MakeupKnob.Render(canvas);

        float toggleY = knobsY + KnobRadius + 34f;
        float toggleHeight = 22f;
        float toggleWidth = 120f;
        float toggleGap = 10f;
        float togglesTotalWidth = toggleWidth * 2f + toggleGap;
        float toggleStartX = (size.Width - togglesTotalWidth) / 2f;

        _detectorToggleRect = new SKRect(toggleStartX, toggleY, toggleStartX + toggleWidth, toggleY + toggleHeight);
        _sidechainToggleRect = new SKRect(_detectorToggleRect.Right + toggleGap, toggleY, _detectorToggleRect.Right + toggleGap + toggleWidth, toggleY + toggleHeight);

        DrawToggle(canvas, _detectorToggleRect, $"DET: {DetectorLabel(state.DetectorMode)}", true);
        DrawToggle(canvas, _sidechainToggleRect, $"SC HPF: {(state.SidechainHpfEnabled ? "ON" : "OFF")}", state.SidechainHpfEnabled);

        // Outer border
        canvas.DrawRoundRect(roundRect, _borderPaint);

        canvas.Restore();
    }

    private void DrawTransferCurve(SKCanvas canvas, SKRect rect, CompressorState state)
    {
        // Background
        var roundRect = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(roundRect, _grBackgroundPaint);

        float padding = 8f;
        float plotLeft = rect.Left + padding;
        float plotRight = rect.Right - padding;
        float plotTop = rect.Top + padding;
        float plotBottom = rect.Bottom - padding;
        float plotSize = MathF.Min(plotRight - plotLeft, plotBottom - plotTop);

        // Center the plot
        float plotCenterX = (plotLeft + plotRight) / 2;
        float plotCenterY = (plotTop + plotBottom) / 2;
        plotLeft = plotCenterX - plotSize / 2;
        plotRight = plotCenterX + plotSize / 2;
        plotTop = plotCenterY - plotSize / 2;
        plotBottom = plotCenterY + plotSize / 2;

        // Grid lines at -48, -36, -24, -12, 0 dB
        float dbRange = 60f; // -60 to 0 dB
        foreach (float db in new[] { -48f, -36f, -24f, -12f })
        {
            float norm = (db + 60f) / dbRange;
            float x = plotLeft + plotSize * norm;
            float y = plotBottom - plotSize * norm;
            canvas.DrawLine(x, plotBottom, x, plotTop, _gridPaint);
            canvas.DrawLine(plotLeft, y, plotRight, y, _gridPaint);
        }

        // 1:1 reference line (diagonal)
        using var refPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };
        canvas.DrawLine(plotLeft, plotBottom, plotRight, plotTop, refPaint);

        // Draw compression curve
        float threshNorm = (state.ThresholdDb + 60f) / dbRange;
        float threshX = plotLeft + plotSize * threshNorm;
        float threshY = plotBottom - plotSize * threshNorm;

        using var curvePath = new SKPath();
        curvePath.MoveTo(plotLeft, plotBottom);
        curvePath.LineTo(threshX, threshY);

        // Above threshold: apply ratio with soft knee if enabled
        int steps = 20;
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            float inputDb = state.ThresholdDb + t * (0f - state.ThresholdDb);
            float outputDb = inputDb - ComputeGainReduction(inputDb, state);
            outputDb = MathF.Max(outputDb, -60f);

            float inputNorm = (inputDb + 60f) / dbRange;
            float outputNorm = (outputDb + 60f) / dbRange;
            float x = plotLeft + plotSize * inputNorm;
            float y = plotBottom - plotSize * outputNorm;
            curvePath.LineTo(x, y);
        }

        canvas.DrawPath(curvePath, _curvePaint);

        // Threshold marker lines
        canvas.DrawLine(threshX, plotBottom, threshX, plotTop, _thresholdPaint);
        canvas.DrawLine(plotLeft, threshY, plotRight, threshY, _thresholdPaint);

        // Input level marker
        float markerInputDb = 20f * MathF.Log10(state.InputLevel + 1e-10f);
        markerInputDb = MathF.Max(markerInputDb, -60f);
        float markerInputNorm = (markerInputDb + 60f) / dbRange;
        float inputX = plotLeft + plotSize * markerInputNorm;

        // Calculate output based on compression
        float markerOutputDb = markerInputDb;
        markerOutputDb = markerInputDb - ComputeGainReduction(markerInputDb, state);
        float markerOutputNorm = (markerOutputDb + 60f) / dbRange;
        float outputY = plotBottom - plotSize * markerOutputNorm;

        // Draw input marker dot
        canvas.DrawCircle(inputX, outputY, 5f, _inputMarkerPaint);

        // Border
        canvas.DrawRoundRect(roundRect, _borderPaint);

        // Labels
        using var labelPaint = new SkiaTextPaint(_theme.TextMuted, 8f, SKFontStyle.Normal);
        labelPaint.TextAlign = SKTextAlign.Center;
        canvas.DrawText("IN", rect.MidX, rect.Bottom - 2, labelPaint);
        labelPaint.TextAlign = SKTextAlign.Left;
        canvas.DrawText("OUT", rect.Left + 2, rect.MidY, labelPaint);
    }

    private static float ComputeGainReduction(float inputDb, CompressorState state)
    {
        float delta = inputDb - state.ThresholdDb;
        if (state.KneeDb <= 0.01f)
        {
            return delta > 0f ? delta * (1f - 1f / state.Ratio) : 0f;
        }

        float halfKnee = state.KneeDb * 0.5f;
        if (delta <= -halfKnee)
        {
            return 0f;
        }

        if (delta >= halfKnee)
        {
            return delta * (1f - 1f / state.Ratio);
        }

        float x = delta + halfKnee;
        return (1f - 1f / state.Ratio) * x * x / (2f * state.KneeDb);
    }

    private void DrawToggle(SKCanvas canvas, SKRect rect, string label, bool active)
    {
        var round = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(round, active ? _toggleActivePaint : _togglePaint);
        canvas.DrawRoundRect(round, _borderPaint);

        using var textPaint = new SkiaTextPaint(active ? _theme.TextPrimary : _theme.TextSecondary, 9f, SKFontStyle.Bold, SKTextAlign.Center);
        canvas.DrawText(label, rect.MidX, rect.MidY + 3f, textPaint);
    }

    private static string DetectorLabel(CompressorDetectorMode mode)
    {
        return mode switch
        {
            CompressorDetectorMode.Peak => "PEAK",
            CompressorDetectorMode.Rms => "RMS",
            _ => "BLEND"
        };
    }

    private void DrawGainReductionMeter(SKCanvas canvas, SKRect rect, float grDb)
    {
        // Background
        var roundRect = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(roundRect, _grBackgroundPaint);

        float meterPadding = 6f;
        float meterLeft = rect.Left + meterPadding;
        float meterRight = rect.Right - meterPadding;
        float meterTop = rect.Top + meterPadding + 20f; // Leave room for value
        float meterBottom = rect.Bottom - meterPadding;
        float meterHeight = meterBottom - meterTop;

        // GR is typically 0-24 dB range
        float maxGr = 24f;
        float grNorm = MathF.Min(grDb / maxGr, 1f);
        float barHeight = meterHeight * grNorm;

        // Draw meter bar (from top down for GR)
        var barRect = new SKRect(meterLeft, meterTop, meterRight, meterTop + barHeight);
        var barRound = new SKRoundRect(barRect, 3f);
        canvas.DrawRoundRect(barRound, _grBarPaint);

        // Draw tick marks
        using var tickPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };
        using var tickLabelPaint = new SkiaTextPaint(_theme.TextMuted, 7f, SKFontStyle.Normal, SKTextAlign.Right);

        foreach (float db in new[] { 6f, 12f, 18f })
        {
            float tickNorm = db / maxGr;
            float tickY = meterTop + meterHeight * tickNorm;
            canvas.DrawLine(meterLeft - 2, tickY, meterLeft, tickY, tickPaint);
        }

        // GR value display at top
        string grText = grDb > 0.1f ? $"-{grDb:0.0}" : "0.0";
        _grTextPaint.Color = grDb > 0.1f ? _theme.KnobArc : _theme.TextSecondary;
        canvas.DrawText(grText, rect.MidX, rect.Top + 18, _grTextPaint);

        // dB label
        using var dbLabelPaint = new SkiaTextPaint(_theme.TextMuted, 8f, SKFontStyle.Normal, SKTextAlign.Center);
        canvas.DrawText("dB", rect.MidX, rect.Top + 28, dbLabelPaint);

        // Border
        canvas.DrawRoundRect(roundRect, _borderPaint);
    }

    public CompressorHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new CompressorHitTest(CompressorHitArea.CloseButton, -1);

        if (_bypassButtonRect.Contains(x, y))
            return new CompressorHitTest(CompressorHitArea.BypassButton, -1);

        // Check preset bar hits
        var presetHit = _presetBar.HitTest(x, y);
        if (presetHit == PresetBarHitArea.Dropdown)
            return new CompressorHitTest(CompressorHitArea.PresetDropdown, -1);
        if (presetHit == PresetBarHitArea.SaveButton)
            return new CompressorHitTest(CompressorHitArea.PresetSave, -1);

        if (_detectorToggleRect.Contains(x, y))
            return new CompressorHitTest(CompressorHitArea.DetectorToggle, -1);

        if (_sidechainToggleRect.Contains(x, y))
            return new CompressorHitTest(CompressorHitArea.SidechainToggle, -1);

        if (ThresholdKnob.HitTest(x, y))
            return new CompressorHitTest(CompressorHitArea.Knob, 0);
        if (RatioKnob.HitTest(x, y))
            return new CompressorHitTest(CompressorHitArea.Knob, 1);
        if (AttackKnob.HitTest(x, y))
            return new CompressorHitTest(CompressorHitArea.Knob, 2);
        if (ReleaseKnob.HitTest(x, y))
            return new CompressorHitTest(CompressorHitArea.Knob, 3);
        if (KneeKnob.HitTest(x, y))
            return new CompressorHitTest(CompressorHitArea.Knob, 4);
        if (MakeupKnob.HitTest(x, y))
            return new CompressorHitTest(CompressorHitArea.Knob, 5);

        if (_titleBarRect.Contains(x, y))
            return new CompressorHitTest(CompressorHitArea.TitleBar, -1);

        return new CompressorHitTest(CompressorHitArea.None, -1);
    }

    public SkiaSharp.SKRect GetPresetDropdownRect() => _presetBar.GetDropdownRect();

    public static SKSize GetPreferredSize() => new(420, 320);

    public void Dispose()
    {
        _grMeter.Dispose();
        ThresholdKnob.Dispose();
        RatioKnob.Dispose();
        AttackKnob.Dispose();
        ReleaseKnob.Dispose();
        KneeKnob.Dispose();
        MakeupKnob.Dispose();
        _presetBar.Dispose();
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _sectionLabelPaint.Dispose();
        _gridPaint.Dispose();
        _curvePaint.Dispose();
        _thresholdPaint.Dispose();
        _inputMarkerPaint.Dispose();
        _grBarPaint.Dispose();
        _grBackgroundPaint.Dispose();
        _grTextPaint.Dispose();
        _latencyPaint.Dispose();
        _togglePaint.Dispose();
        _toggleActivePaint.Dispose();
    }
}

/// <summary>
/// State data for rendering the compressor UI.
/// </summary>
public record struct CompressorState(
    float ThresholdDb,
    float Ratio,
    float AttackMs,
    float ReleaseMs,
    float KneeDb,
    float MakeupDb,
    float GainReductionDb,
    float InputLevel,
    CompressorDetectorMode DetectorMode,
    bool SidechainHpfEnabled,
    float LatencyMs,
    bool IsBypassed,
    string PresetName = "Custom");

public enum CompressorHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    Knob,
    DetectorToggle,
    SidechainToggle,
    PresetDropdown,
    PresetSave
}

public record struct CompressorHitTest(CompressorHitArea Area, int KnobIndex);
