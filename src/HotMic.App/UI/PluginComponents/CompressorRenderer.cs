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
    private const float KnobSpacing = 72f;
    private const float CornerRadius = 10f;
    private const int KnobCount = 5;

    private readonly PluginComponentTheme _theme;
    private readonly RotaryKnob _knob;

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _titlePaint;
    private readonly SKPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _sectionLabelPaint;
    private readonly SKPaint _gridPaint;
    private readonly SKPaint _curvePaint;
    private readonly SKPaint _thresholdPaint;
    private readonly SKPaint _inputMarkerPaint;
    private readonly SKPaint _grBarPaint;
    private readonly SKPaint _grBackgroundPaint;
    private readonly SKPaint _grTextPaint;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;
    private readonly SKRect[] _knobRects = new SKRect[KnobCount];
    private readonly SKPoint[] _knobCenters = new SKPoint[KnobCount];

    public CompressorRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _knob = new RotaryKnob(_theme);

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

        _grTextPaint = new SKPaint
        {
            Color = _theme.TextPrimary,
            IsAntialias = true,
            TextSize = 20f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };
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

        // Transfer curve and GR meter side by side
        float displayWidth = size.Width - Padding * 3 - GrMeterWidth;
        var transferRect = new SKRect(Padding, y, Padding + displayWidth, y + TransferCurveSize);
        var grMeterRect = new SKRect(size.Width - Padding - GrMeterWidth, y, size.Width - Padding, y + TransferCurveSize);

        // Section labels
        canvas.DrawText("TRANSFER CURVE", Padding, y - 2, _sectionLabelPaint);
        canvas.DrawText("GR", grMeterRect.MidX - 6, y - 2, _sectionLabelPaint);

        // Draw transfer curve
        DrawTransferCurve(canvas, transferRect, state);

        // Draw GR meter
        DrawGainReductionMeter(canvas, grMeterRect, state.GainReductionDb);

        y += TransferCurveSize + Padding + 8;

        // Knobs section
        float knobsY = y + KnobRadius + 16;
        float knobsTotalWidth = KnobCount * KnobSpacing;
        float knobsStartX = (size.Width - knobsTotalWidth) / 2 + KnobSpacing / 2;

        // Threshold knob
        _knobCenters[0] = new SKPoint(knobsStartX, knobsY);
        float thresholdNorm = (state.ThresholdDb - (-60f)) / (0f - (-60f));
        _knob.Render(canvas, _knobCenters[0], KnobRadius, thresholdNorm,
            "THRESH", $"{state.ThresholdDb:0.0}", "dB", state.HoveredKnob == 0);
        _knobRects[0] = _knob.GetHitRect(_knobCenters[0], KnobRadius);

        // Ratio knob
        _knobCenters[1] = new SKPoint(knobsStartX + KnobSpacing, knobsY);
        float ratioNorm = (state.Ratio - 1f) / (20f - 1f);
        _knob.Render(canvas, _knobCenters[1], KnobRadius, ratioNorm,
            "RATIO", $"{state.Ratio:0.0}", ":1", state.HoveredKnob == 1);
        _knobRects[1] = _knob.GetHitRect(_knobCenters[1], KnobRadius);

        // Attack knob
        _knobCenters[2] = new SKPoint(knobsStartX + KnobSpacing * 2, knobsY);
        float attackNorm = (state.AttackMs - 0.1f) / (100f - 0.1f);
        _knob.Render(canvas, _knobCenters[2], KnobRadius, attackNorm,
            "ATTACK", $"{state.AttackMs:0.0}", "ms", state.HoveredKnob == 2);
        _knobRects[2] = _knob.GetHitRect(_knobCenters[2], KnobRadius);

        // Release knob
        _knobCenters[3] = new SKPoint(knobsStartX + KnobSpacing * 3, knobsY);
        float releaseNorm = (state.ReleaseMs - 10f) / (1000f - 10f);
        _knob.Render(canvas, _knobCenters[3], KnobRadius, releaseNorm,
            "RELEASE", $"{state.ReleaseMs:0}", "ms", state.HoveredKnob == 3);
        _knobRects[3] = _knob.GetHitRect(_knobCenters[3], KnobRadius);

        // Makeup knob
        _knobCenters[4] = new SKPoint(knobsStartX + KnobSpacing * 4, knobsY);
        float makeupNorm = state.MakeupDb / 24f;
        _knob.Render(canvas, _knobCenters[4], KnobRadius, makeupNorm,
            "MAKEUP", $"{state.MakeupDb:0.0}", "dB", state.HoveredKnob == 4);
        _knobRects[4] = _knob.GetHitRect(_knobCenters[4], KnobRadius);

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

        // Above threshold: apply ratio
        int steps = 20;
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            float inputDb = state.ThresholdDb + t * (0f - state.ThresholdDb);
            float overDb = inputDb - state.ThresholdDb;
            float outputDb = state.ThresholdDb + overDb / state.Ratio;
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
        if (markerInputDb > state.ThresholdDb)
        {
            float over = markerInputDb - state.ThresholdDb;
            markerOutputDb = state.ThresholdDb + over / state.Ratio;
        }
        float markerOutputNorm = (markerOutputDb + 60f) / dbRange;
        float outputY = plotBottom - plotSize * markerOutputNorm;

        // Draw input marker dot
        canvas.DrawCircle(inputX, outputY, 5f, _inputMarkerPaint);

        // Border
        canvas.DrawRoundRect(roundRect, _borderPaint);

        // Labels
        using var labelPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            TextSize = 8f,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };
        labelPaint.TextAlign = SKTextAlign.Center;
        canvas.DrawText("IN", rect.MidX, rect.Bottom - 2, labelPaint);
        labelPaint.TextAlign = SKTextAlign.Left;
        canvas.DrawText("OUT", rect.Left + 2, rect.MidY, labelPaint);
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
        using var tickLabelPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            TextSize = 7f,
            TextAlign = SKTextAlign.Right,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };

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
        using var dbLabelPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            TextSize = 8f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };
        canvas.DrawText("dB", rect.MidX, rect.Top + 28, dbLabelPaint);

        // Border
        canvas.DrawRoundRect(roundRect, _borderPaint);
    }

    public NoiseGateHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new NoiseGateHitTest(NoiseGateHitArea.CloseButton, -1);

        if (_bypassButtonRect.Contains(x, y))
            return new NoiseGateHitTest(NoiseGateHitArea.BypassButton, -1);

        for (int i = 0; i < KnobCount; i++)
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

    public static SKSize GetPreferredSize() => new(420, 320);

    public void Dispose()
    {
        _knob.Dispose();
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
    float MakeupDb,
    float GainReductionDb,
    float InputLevel,
    bool IsBypassed,
    int HoveredKnob = -1);
