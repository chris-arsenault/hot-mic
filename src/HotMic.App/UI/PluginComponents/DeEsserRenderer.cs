using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Renders a de-esser plugin UI with frequency band visualization, sibilance detection, and gain reduction.
/// </summary>
public sealed class DeEsserRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 16f;
    private const float KnobRadius = 36f;
    private const float MeterWidth = 24f;
    private const float MeterHeight = 120f;
    private const float FreqDisplayHeight = 70f;
    private const float CornerRadius = 10f;

    private readonly PluginComponentTheme _theme;
    private readonly RotaryKnob _knob;

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _titlePaint;
    private readonly SKPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _meterBackgroundPaint;
    private readonly SKPaint _meterFillPaint;
    private readonly SKPaint _sibilanceFillPaint;
    private readonly SKPaint _grMeterFillPaint;
    private readonly SKPaint _freqDisplayBackgroundPaint;
    private readonly SKPaint _freqBandPaint;
    private readonly SKPaint _freqBandActivePaint;
    private readonly SKPaint _thresholdLinePaint;
    private readonly SKPaint _labelPaint;
    private readonly SKPaint _freqLabelPaint;
    private readonly SKPaint _latencyPaint;
    private readonly SKPaint _activeIndicatorPaint;
    private readonly SKPaint _activeGlowPaint;
    private readonly SKPaint _spectrumPaint;
    private readonly SKPaint _spectrumInBandPaint;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;
    private SKPoint[] _knobCenters = new SKPoint[5];

    // Level meters with PPM-style ballistics
    private readonly LevelMeter _inputMeter;
    private readonly LevelMeter _sibilanceMeter;

    public DeEsserRenderer(PluginComponentTheme? theme = null)
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

        _meterBackgroundPaint = new SKPaint
        {
            Color = _theme.WaveformBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _meterFillPaint = new SKPaint
        {
            Color = _theme.WaveformLine,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _sibilanceFillPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0xA0, 0x40),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _grMeterFillPaint = new SKPaint
        {
            Color = _theme.KnobArc,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _freqDisplayBackgroundPaint = new SKPaint
        {
            Color = _theme.WaveformBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _freqBandPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0xA0, 0x40, 0x40),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _freqBandActivePaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0xA0, 0x40, 0xA0),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _thresholdLinePaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0x50, 0x50),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            PathEffect = SKPathEffect.CreateDash(new[] { 4f, 4f }, 0)
        };

        _labelPaint = new SKPaint
        {
            Color = _theme.TextSecondary,
            IsAntialias = true,
            TextSize = 10f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };

        _freqLabelPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            TextSize = 9f,
            TextAlign = SKTextAlign.Center,
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

        _activeIndicatorPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0xA0, 0x40),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _activeGlowPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0xA0, 0x40, 0x80),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8f)
        };

        _spectrumPaint = new SKPaint
        {
            Color = new SKColor(0x60, 0x90, 0xC0, 0xA0),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _spectrumInBandPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0xA0, 0x40, 0xC0),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        // Initialize level meters with PPM-style ballistics
        _inputMeter = new LevelMeter();
        _sibilanceMeter = new LevelMeter();
    }

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, DeEsserState state)
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
        canvas.DrawText("De-Esser", Padding, TitleBarHeight / 2f + 5, _titlePaint);

        // Active indicator
        bool isActive = state.GainReductionDb < -0.5f;
        float indicatorX = Padding + 70;
        float indicatorY = TitleBarHeight / 2f;
        if (isActive)
        {
            canvas.DrawCircle(indicatorX, indicatorY, 6f, _activeGlowPaint);
            canvas.DrawCircle(indicatorX, indicatorY, 4f, _activeIndicatorPaint);
        }
        else
        {
            using var dimPaint = new SKPaint
            {
                Color = _theme.GateClosed,
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            canvas.DrawCircle(indicatorX, indicatorY, 4f, dimPaint);
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

        if (state.LatencyMs >= 0f)
        {
            string latencyLabel = $"LAT {state.LatencyMs:0.0}ms";
            canvas.DrawText(latencyLabel, _bypassButtonRect.Left - 6f, TitleBarHeight / 2f + 4, _latencyPaint);
        }

        // Close button
        _closeButtonRect = new SKRect(size.Width - Padding - 24, (TitleBarHeight - 24) / 2,
            size.Width - Padding, (TitleBarHeight + 24) / 2);
        canvas.DrawText("\u00D7", _closeButtonRect.MidX, _closeButtonRect.MidY + 6, _closeButtonPaint);

        float contentTop = TitleBarHeight + Padding;

        // Frequency band display
        var freqDisplayRect = new SKRect(Padding, contentTop, size.Width - Padding, contentTop + FreqDisplayHeight);
        DrawFrequencyBandDisplay(canvas, freqDisplayRect, state);

        // Meters section - positioned on the right side
        float meterY = freqDisplayRect.Bottom + 16;
        float meterSpacing = 10f;
        float metersRight = size.Width - Padding;

        // GR meter (rightmost)
        var grMeterRect = new SKRect(metersRight - MeterWidth, meterY, metersRight, meterY + MeterHeight);
        DrawGainReductionMeter(canvas, grMeterRect, state.GainReductionDb);

        // Sibilance meter with PPM-style ballistics
        var sibMeterRect = new SKRect(grMeterRect.Left - meterSpacing - MeterWidth, meterY, grMeterRect.Left - meterSpacing, meterY + MeterHeight);
        _sibilanceMeter.Update(state.SibilanceLevel);
        _sibilanceMeter.Render(canvas, sibMeterRect, MeterOrientation.Vertical);
        using var sibLabelPaint = new SKPaint { Color = _theme.TextMuted, IsAntialias = true, TextSize = 9f, TextAlign = SKTextAlign.Center };
        canvas.DrawText("SIB", sibMeterRect.MidX, sibMeterRect.Bottom + 12, sibLabelPaint);

        // Input meter with PPM-style ballistics
        var inputMeterRect = new SKRect(sibMeterRect.Left - meterSpacing - MeterWidth, meterY, sibMeterRect.Left - meterSpacing, meterY + MeterHeight);
        _inputMeter.Update(state.InputLevel);
        _inputMeter.Render(canvas, inputMeterRect, MeterOrientation.Vertical);
        canvas.DrawText("IN", inputMeterRect.MidX, inputMeterRect.Bottom + 12, sibLabelPaint);

        // Knobs section - positioned on the left, in two rows
        float knobAreaRight = inputMeterRect.Left - 20;
        float knobSpacingX = 90f;
        float knobSpacingY = 70f;
        float knobRowWidth = knobSpacingX * 2;
        float knobAreaLeft = (Padding + knobAreaRight - knobRowWidth) / 2;

        // Row 1: Center, Bandwidth, Threshold (centered across available width)
        float knobY1 = meterY + KnobRadius + 10;
        _knobCenters[0] = new SKPoint(knobAreaLeft, knobY1);
        _knobCenters[1] = new SKPoint(knobAreaLeft + knobSpacingX, knobY1);
        _knobCenters[2] = new SKPoint(knobAreaLeft + knobSpacingX * 2, knobY1);

        // Row 2: Reduction, Max Range (centered below row 1)
        float knobY2 = knobY1 + knobSpacingY;
        _knobCenters[3] = new SKPoint(knobAreaLeft + knobSpacingX * 0.5f, knobY2);
        _knobCenters[4] = new SKPoint(knobAreaLeft + knobSpacingX * 1.5f, knobY2);

        // Center Freq (log scale display)
        float centerNorm = (MathF.Log10(state.CenterHz) - MathF.Log10(4000f)) / (MathF.Log10(9000f) - MathF.Log10(4000f));
        _knob.Render(canvas, _knobCenters[0], KnobRadius, centerNorm, "CENTER", $"{state.CenterHz / 1000f:0.0}k", "Hz", state.HoveredKnob == DeEsserKnob.CenterFreq);

        // Bandwidth
        float bwNorm = (state.BandwidthHz - 1000f) / 3000f;
        _knob.Render(canvas, _knobCenters[1], KnobRadius, bwNorm, "WIDTH", $"{state.BandwidthHz / 1000f:0.0}k", "Hz", state.HoveredKnob == DeEsserKnob.Bandwidth);

        // Threshold
        float threshNorm = (state.ThresholdDb + 40f) / 40f;
        _knob.Render(canvas, _knobCenters[2], KnobRadius, threshNorm, "THRESH", $"{state.ThresholdDb:0}", "dB", state.HoveredKnob == DeEsserKnob.Threshold);

        // Reduction
        float redNorm = state.ReductionDb / 12f;
        _knob.Render(canvas, _knobCenters[3], KnobRadius, redNorm, "REDUCE", $"{state.ReductionDb:0.0}", "dB", state.HoveredKnob == DeEsserKnob.Reduction);

        // Max Range
        float rangeNorm = state.MaxRangeDb / 20f;
        _knob.Render(canvas, _knobCenters[4], KnobRadius, rangeNorm, "RANGE", $"{state.MaxRangeDb:0}", "dB", state.HoveredKnob == DeEsserKnob.MaxRange);

        // Outer border
        canvas.DrawRoundRect(roundRect, _borderPaint);

        canvas.Restore();
    }

    private void DrawFrequencyBandDisplay(SKCanvas canvas, SKRect rect, DeEsserState state)
    {
        // Background
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _freqDisplayBackgroundPaint);

        // Frequency range: 1kHz to 16kHz (log scale) - matches plugin spectrum analysis
        float minFreq = 1000f;
        float maxFreq = 16000f;
        float logMin = MathF.Log10(minFreq);
        float logMax = MathF.Log10(maxFreq);
        float innerHeight = rect.Height - 8;
        float innerWidth = rect.Width - 8;

        // Calculate band position
        float lowFreq = state.CenterHz - state.BandwidthHz / 2f;
        float highFreq = state.CenterHz + state.BandwidthHz / 2f;
        float lowX = rect.Left + 4 + innerWidth * (MathF.Log10(MathF.Max(lowFreq, minFreq)) - logMin) / (logMax - logMin);
        float highX = rect.Left + 4 + innerWidth * (MathF.Log10(MathF.Min(highFreq, maxFreq)) - logMin) / (logMax - logMin);
        float centerX = rect.Left + 4 + innerWidth * (MathF.Log10(state.CenterHz) - logMin) / (logMax - logMin);

        // Draw spectrum analyzer bars (behind the band)
        if (state.Spectrum is { Length: > 0 })
        {
            int bins = state.Spectrum.Length;
            for (int i = 0; i < bins; i++)
            {
                float t0 = i / (float)bins;
                float t1 = (i + 1) / (float)bins;
                float freq0 = minFreq * MathF.Pow(maxFreq / minFreq, t0);
                float freq1 = minFreq * MathF.Pow(maxFreq / minFreq, t1);

                float x0 = rect.Left + 4 + innerWidth * (MathF.Log10(freq0) - logMin) / (logMax - logMin);
                float x1 = rect.Left + 4 + innerWidth * (MathF.Log10(freq1) - logMin) / (logMax - logMin);

                float magnitude = state.Spectrum[i];
                if (magnitude < 0.001f) continue;

                // Convert to dB and normalize (-60 to 0 dB range)
                float db = 20f * MathF.Log10(magnitude + 1e-6f);
                db = MathF.Max(db, -60f);
                float barHeight = innerHeight * ((db + 60f) / 60f);
                barHeight = MathF.Max(barHeight, 0f);

                float barY = rect.Bottom - 4 - barHeight;

                // Check if this bin is within the detection band
                float binCenterFreq = (freq0 + freq1) / 2f;
                bool inBand = binCenterFreq >= lowFreq && binCenterFreq <= highFreq;

                canvas.DrawRect(x0, barY, x1 - x0, barHeight, inBand ? _spectrumInBandPaint : _spectrumPaint);
            }
        }

        // Draw band region (semi-transparent overlay)
        bool isActive = state.GainReductionDb < -0.5f;
        var bandRect = new SKRect(lowX, rect.Top + 4, highX, rect.Bottom - 4);
        canvas.DrawRect(bandRect, isActive ? _freqBandActivePaint : _freqBandPaint);

        // Center line
        using var centerPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0xA0, 0x40),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f
        };
        canvas.DrawLine(centerX, rect.Top + 4, centerX, rect.Bottom - 4, centerPaint);

        // Threshold line (horizontal) - using -60 to 0 dB scale to match spectrum
        float threshNorm = (state.ThresholdDb + 60f) / 60f;
        threshNorm = MathF.Max(0f, MathF.Min(1f, threshNorm));
        float threshY = rect.Bottom - 4 - innerHeight * threshNorm;
        canvas.DrawLine(rect.Left + 4, threshY, rect.Right - 4, threshY, _thresholdLinePaint);

        // Frequency labels
        float[] freqMarkers = { 1000f, 2000f, 4000f, 6000f, 8000f, 12000f, 16000f };
        foreach (float freq in freqMarkers)
        {
            float x = rect.Left + 4 + innerWidth * (MathF.Log10(freq) - logMin) / (logMax - logMin);
            string label = freq >= 1000 ? $"{freq / 1000:0}k" : $"{freq:0}";
            canvas.DrawText(label, x, rect.Bottom + 12, _freqLabelPaint);
        }

        // Border
        canvas.DrawRoundRect(roundRect, _borderPaint);
    }

    private void DrawMeter(SKCanvas canvas, SKRect rect, float level, string label, SKPaint fillPaint)
    {
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _meterBackgroundPaint);

        // Use -48 to 0 dB range for better visibility at typical voice levels
        float levelDb = 20f * MathF.Log10(level + 1e-10f);
        levelDb = MathF.Max(levelDb, -48f);
        float normalized = (levelDb + 48f) / 48f;

        float meterPadding = 2f;
        float innerHeight = rect.Height - meterPadding * 2;
        float fillHeight = innerHeight * normalized;

        if (fillHeight > 1f)
        {
            var fillRect = new SKRect(
                rect.Left + meterPadding,
                rect.Bottom - meterPadding - fillHeight,
                rect.Right - meterPadding,
                rect.Bottom - meterPadding);
            canvas.DrawRect(fillRect, fillPaint);
        }

        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.DrawText(label, rect.MidX, rect.Bottom + 14, _labelPaint);
    }

    private void DrawGainReductionMeter(SKCanvas canvas, SKRect rect, float grDb)
    {
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _meterBackgroundPaint);

        // Show GR from 0 to -12 dB (typical de-esser range)
        float normalized = MathF.Abs(MathF.Max(grDb, -12f)) / 12f;
        float meterPadding = 2f;
        float innerHeight = rect.Height - meterPadding * 2;
        float fillHeight = innerHeight * normalized;

        if (fillHeight > 1f)
        {
            var fillRect = new SKRect(
                rect.Left + meterPadding,
                rect.Top + meterPadding,
                rect.Right - meterPadding,
                rect.Top + meterPadding + fillHeight);
            canvas.DrawRect(fillRect, _grMeterFillPaint);
        }

        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.DrawText("GR", rect.MidX, rect.Bottom + 14, _labelPaint);
    }

    public DeEsserHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new DeEsserHitTest(DeEsserHitArea.CloseButton, DeEsserKnob.None);

        if (_bypassButtonRect.Contains(x, y))
            return new DeEsserHitTest(DeEsserHitArea.BypassButton, DeEsserKnob.None);

        for (int i = 0; i < _knobCenters.Length; i++)
        {
            float dx = x - _knobCenters[i].X;
            float dy = y - _knobCenters[i].Y;
            if (dx * dx + dy * dy <= KnobRadius * KnobRadius * 1.5f)
            {
                var knob = (DeEsserKnob)(i + 1);
                return new DeEsserHitTest(DeEsserHitArea.Knob, knob);
            }
        }

        if (_titleBarRect.Contains(x, y))
            return new DeEsserHitTest(DeEsserHitArea.TitleBar, DeEsserKnob.None);

        return new DeEsserHitTest(DeEsserHitArea.None, DeEsserKnob.None);
    }

    public static SKSize GetPreferredSize() => new(480, 340);

    public void Dispose()
    {
        _inputMeter.Dispose();
        _sibilanceMeter.Dispose();
        _knob.Dispose();
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _meterBackgroundPaint.Dispose();
        _meterFillPaint.Dispose();
        _sibilanceFillPaint.Dispose();
        _grMeterFillPaint.Dispose();
        _freqDisplayBackgroundPaint.Dispose();
        _freqBandPaint.Dispose();
        _freqBandActivePaint.Dispose();
        _thresholdLinePaint.Dispose();
        _labelPaint.Dispose();
        _freqLabelPaint.Dispose();
        _latencyPaint.Dispose();
        _activeIndicatorPaint.Dispose();
        _activeGlowPaint.Dispose();
        _spectrumPaint.Dispose();
        _spectrumInBandPaint.Dispose();
    }
}

public record struct DeEsserState(
    float CenterHz,
    float BandwidthHz,
    float ThresholdDb,
    float ReductionDb,
    float MaxRangeDb,
    float InputLevel,
    float SibilanceLevel,
    float GainReductionDb,
    float LatencyMs,
    bool IsBypassed,
    DeEsserKnob HoveredKnob = DeEsserKnob.None,
    float[]? Spectrum = null);

public enum DeEsserHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    Knob
}

public enum DeEsserKnob
{
    None,
    CenterFreq,
    Bandwidth,
    Threshold,
    Reduction,
    MaxRange
}

public record struct DeEsserHitTest(DeEsserHitArea Area, DeEsserKnob Knob);
