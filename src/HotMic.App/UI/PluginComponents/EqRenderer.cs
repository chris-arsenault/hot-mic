using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// 5-Band EQ plugin UI with frequency response curve, spectrum analyzer, and per-band controls.
/// </summary>
public sealed class EqRenderer : IDisposable
{
    // Layout constants - calculated for proper spacing
    private const float TitleBarHeight = 40f;
    private const float Padding = 14f;
    private const float CornerRadius = 10f;

    // Spectrum/Curve area
    private const float SpectrumHeight = 160f;
    private const float SpectrumLabelMargin = 16f; // Space for frequency labels below

    // Knob section layout
    private const float KnobRadius = 24f;
    private const float BandLabelHeight = 16f;
    private const float KnobValueHeight = 14f;
    private const float KnobGap = 10f;

    // Calculated knob section height:
    // BandLabel(16) + Gap(6) + GainKnob(52) + ValueGap(4) + Value(14) + Gap(10) + FreqKnob(52) + ValueGap(4) + Value(14) + Bottom(8) = 180
    private const float KnobSectionHeight = 180f;

    // Total window: Title(40) + Pad(14) + Spectrum(160) + LabelMargin(16) + Gap(10) + KnobSection(180) + Pad(10) = 430
    private const float WindowWidth = 620f;
    private const float WindowHeight = 430f;

    private const float MeterWidth = 18f;

    private readonly PluginComponentTheme _theme;

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _titlePaint;
    private readonly SKPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _spectrumBackgroundPaint;
    private readonly SKPaint _spectrumBarPaint;
    private readonly SKPaint _spectrumPeakPaint;
    private readonly SKPaint _gridPaint;
    private readonly SKPaint _zeroLinePaint;
    private readonly SKPaint _curvePaint;
    private readonly SKPaint _curveFillPaint;
    private readonly SKPaint _bandMarkerPaint;
    private readonly SKPaint _knobBackgroundPaint;
    private readonly SKPaint _knobTrackPaint;
    private readonly SKPaint _knobArcPaint;
    private readonly SKPaint _knobPointerPaint;
    private readonly SKPaint _labelPaint;
    private readonly SKPaint _valuePaint;
    private readonly SKPaint _latencyPaint;
    private readonly SKPaint _bandLabelPaint;
    private readonly SKPaint _meterBackgroundPaint;

    private readonly LevelMeter _inputMeter;
    private readonly LevelMeter _outputMeter;

    // Band colors
    private readonly SKColor _hpfColor = new(0x88, 0x88, 0x88);  // Neutral gray
    private readonly SKColor _lowColor = new(0x3D, 0xA5, 0xF4);  // Blue
    private readonly SKColor _mid1Color = new(0x00, 0xD4, 0xAA); // Teal
    private readonly SKColor _mid2Color = new(0x5C, 0xD4, 0x6A); // Green
    private readonly SKColor _highColor = new(0xFF, 0x6B, 0x00); // Orange
    private readonly SKColor _spectrumColor = new(0x00, 0xD4, 0xAA, 0x60);
    private readonly SKColor _spectrumPeakColor = new(0x00, 0xD4, 0xAA);

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;
    private SKRect _spectrumRect;

    // Knob positions (center points)
    private SKPoint _hpfFreqKnob;
    private SKPoint _lowShelfGainKnob;
    private SKPoint _lowShelfFreqKnob;
    private SKPoint _mid1GainKnob;
    private SKPoint _mid1FreqKnob;
    private SKPoint _mid2GainKnob;
    private SKPoint _mid2FreqKnob;
    private SKPoint _highShelfGainKnob;
    private SKPoint _highShelfFreqKnob;

    public EqRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;

        _inputMeter = new LevelMeter();
        _outputMeter = new LevelMeter();

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

        _spectrumBackgroundPaint = new SKPaint
        {
            Color = _theme.WaveformBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _spectrumBarPaint = new SKPaint
        {
            Color = _spectrumColor,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _spectrumPeakPaint = new SKPaint
        {
            Color = _spectrumPeakColor,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f
        };

        _gridPaint = new SKPaint
        {
            Color = _theme.EnvelopeGrid,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };

        _zeroLinePaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            PathEffect = SKPathEffect.CreateDash(new[] { 4f, 4f }, 0)
        };

        _curvePaint = new SKPaint
        {
            Color = _theme.WaveformLine,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };

        _curveFillPaint = new SKPaint
        {
            Color = _theme.WaveformFill,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _bandMarkerPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f
        };

        _knobBackgroundPaint = new SKPaint
        {
            Color = _theme.KnobBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _knobTrackPaint = new SKPaint
        {
            Color = _theme.KnobTrack,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 4f,
            StrokeCap = SKStrokeCap.Round
        };

        _knobArcPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 4f,
            StrokeCap = SKStrokeCap.Round
        };

        _knobPointerPaint = new SKPaint
        {
            Color = _theme.KnobPointer,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f,
            StrokeCap = SKStrokeCap.Round
        };

        _labelPaint = new SKPaint
        {
            Color = _theme.TextSecondary,
            IsAntialias = true,
            TextSize = 10f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };

        _valuePaint = new SKPaint
        {
            Color = _theme.TextPrimary,
            IsAntialias = true,
            TextSize = 11f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };

        _latencyPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            TextSize = 9f,
            TextAlign = SKTextAlign.Right,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };

        _bandLabelPaint = new SKPaint
        {
            IsAntialias = true,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };

        _meterBackgroundPaint = new SKPaint
        {
            Color = _theme.WaveformBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
    }

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, EqState state)
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
        canvas.DrawText("5-Band EQ", Padding, TitleBarHeight / 2f + 5, _titlePaint);

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

        // Spectrum/Curve area (with margins for meters and labels)
        float spectrumLeft = Padding + MeterWidth + 20; // Room for dB labels
        float spectrumRight = size.Width - Padding - MeterWidth - 8;
        _spectrumRect = new SKRect(spectrumLeft, contentTop, spectrumRight, contentTop + SpectrumHeight);

        DrawSpectrumAndCurve(canvas, _spectrumRect, state);

        // Input meter (left of spectrum)
        var inputMeterRect = new SKRect(
            Padding,
            contentTop + 15,
            Padding + MeterWidth,
            contentTop + SpectrumHeight - 15);
        _inputMeter.Update(state.InputLevel);
        _inputMeter.Render(canvas, inputMeterRect, MeterOrientation.Vertical);
        canvas.DrawText("IN", inputMeterRect.MidX, inputMeterRect.Bottom + 12, _labelPaint);

        // Output meter (right of spectrum)
        var outputMeterRect = new SKRect(
            size.Width - Padding - MeterWidth,
            contentTop + 15,
            size.Width - Padding,
            contentTop + SpectrumHeight - 15);
        _outputMeter.Update(state.OutputLevel);
        _outputMeter.Render(canvas, outputMeterRect, MeterOrientation.Vertical);
        canvas.DrawText("OUT", outputMeterRect.MidX, outputMeterRect.Bottom + 12, _labelPaint);

        // Knob section
        float knobSectionTop = contentTop + SpectrumHeight + SpectrumLabelMargin + 10;
        float bandWidth = (size.Width - Padding * 2) / 5f;

        // Calculate vertical positions within knob section
        float bandLabelY = knobSectionTop + 12;
        float gainKnobCenterY = bandLabelY + 6 + KnobRadius + 4;
        float gainValueY = gainKnobCenterY + KnobRadius + 12;
        float freqKnobCenterY = gainValueY + KnobGap + KnobRadius + 4;
        float freqValueY = freqKnobCenterY + KnobRadius + 12;

        // HPF
        float hpfCenterX = Padding + bandWidth * 0.5f;
        DrawSingleKnob(canvas, hpfCenterX, bandLabelY, gainKnobCenterY, gainValueY,
            "HPF", _hpfColor, state.HpfFreq, 40f, 200f, state.HoveredKnob, 0);
        _hpfFreqKnob = new SKPoint(hpfCenterX, gainKnobCenterY);

        // Low shelf
        float lowCenterX = Padding + bandWidth * 1.5f;
        DrawBandControls(canvas, lowCenterX, bandLabelY, gainKnobCenterY, gainValueY, freqKnobCenterY, freqValueY,
            "LOW", _lowColor, state.LowShelfGainDb, state.LowShelfFreq, 60f, 300f, state.HoveredKnob, 1);
        _lowShelfGainKnob = new SKPoint(lowCenterX, gainKnobCenterY);
        _lowShelfFreqKnob = new SKPoint(lowCenterX, freqKnobCenterY);

        // Low-mid
        float mid1CenterX = Padding + bandWidth * 2.5f;
        DrawBandControls(canvas, mid1CenterX, bandLabelY, gainKnobCenterY, gainValueY, freqKnobCenterY, freqValueY,
            "L-MID", _mid1Color, state.Mid1GainDb, state.Mid1Freq, 150f, 800f, state.HoveredKnob, 3);
        _mid1GainKnob = new SKPoint(mid1CenterX, gainKnobCenterY);
        _mid1FreqKnob = new SKPoint(mid1CenterX, freqKnobCenterY);

        // High-mid
        float mid2CenterX = Padding + bandWidth * 3.5f;
        DrawBandControls(canvas, mid2CenterX, bandLabelY, gainKnobCenterY, gainValueY, freqKnobCenterY, freqValueY,
            "H-MID", _mid2Color, state.Mid2GainDb, state.Mid2Freq, 1000f, 6000f, state.HoveredKnob, 5);
        _mid2GainKnob = new SKPoint(mid2CenterX, gainKnobCenterY);
        _mid2FreqKnob = new SKPoint(mid2CenterX, freqKnobCenterY);

        // High shelf
        float highCenterX = Padding + bandWidth * 4.5f;
        DrawBandControls(canvas, highCenterX, bandLabelY, gainKnobCenterY, gainValueY, freqKnobCenterY, freqValueY,
            "HIGH", _highColor, state.HighShelfGainDb, state.HighShelfFreq, 6000f, 16000f, state.HoveredKnob, 7);
        _highShelfGainKnob = new SKPoint(highCenterX, gainKnobCenterY);
        _highShelfFreqKnob = new SKPoint(highCenterX, freqKnobCenterY);

        // Outer border
        canvas.DrawRoundRect(roundRect, _borderPaint);

        canvas.Restore();
    }

    private void DrawSpectrumAndCurve(SKCanvas canvas, SKRect rect, EqState state)
    {
        // Background
        var roundRect = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(roundRect, _spectrumBackgroundPaint);

        canvas.Save();
        canvas.ClipRoundRect(roundRect);

        float zeroY = rect.MidY;
        float dbRange = 24f;
        float dbPixelsPerDb = (rect.Height / 2f) / dbRange;

        // Draw spectrum bars first (behind everything)
        if (state.SpectrumLevels != null && state.SpectrumPeaks != null)
        {
            DrawSpectrum(canvas, rect, state.SpectrumLevels, state.SpectrumPeaks, state.SampleRate);
        }

        // Grid lines (dB)
        float db12Y = zeroY - 12 * dbPixelsPerDb;
        float dbMinus12Y = zeroY + 12 * dbPixelsPerDb;
        canvas.DrawLine(rect.Left, db12Y, rect.Right, db12Y, _gridPaint);
        canvas.DrawLine(rect.Left, dbMinus12Y, rect.Right, dbMinus12Y, _gridPaint);

        // 0 dB center line
        canvas.DrawLine(rect.Left, zeroY, rect.Right, zeroY, _zeroLinePaint);

        // Frequency grid lines (logarithmic: 100, 1k, 10k)
        float[] freqMarkers = { 100f, 1000f, 10000f };
        foreach (float freq in freqMarkers)
        {
            float x = FreqToX(freq, rect, state.SampleRate);
            canvas.DrawLine(x, rect.Top, x, rect.Bottom, _gridPaint);
        }

        // Draw frequency response curve
        DrawResponseCurve(canvas, rect, state, zeroY, dbPixelsPerDb);

        // Band frequency markers
        DrawBandMarker(canvas, rect, state.HpfFreq, 0f, _hpfColor, state.SampleRate, zeroY, dbPixelsPerDb);
        DrawBandMarker(canvas, rect, state.LowShelfFreq, state.LowShelfGainDb, _lowColor, state.SampleRate, zeroY, dbPixelsPerDb);
        DrawBandMarker(canvas, rect, state.Mid1Freq, state.Mid1GainDb, _mid1Color, state.SampleRate, zeroY, dbPixelsPerDb);
        DrawBandMarker(canvas, rect, state.Mid2Freq, state.Mid2GainDb, _mid2Color, state.SampleRate, zeroY, dbPixelsPerDb);
        DrawBandMarker(canvas, rect, state.HighShelfFreq, state.HighShelfGainDb, _highColor, state.SampleRate, zeroY, dbPixelsPerDb);

        canvas.Restore();

        // Border
        canvas.DrawRoundRect(roundRect, _borderPaint);

        // Frequency labels below
        using var freqLabelPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            TextSize = 9f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };
        canvas.DrawText("100", FreqToX(100f, rect, state.SampleRate), rect.Bottom + 12, freqLabelPaint);
        canvas.DrawText("1k", FreqToX(1000f, rect, state.SampleRate), rect.Bottom + 12, freqLabelPaint);
        canvas.DrawText("10k", FreqToX(10000f, rect, state.SampleRate), rect.Bottom + 12, freqLabelPaint);

        // dB labels to the left
        canvas.DrawText("+12", rect.Left - 16, db12Y + 3, freqLabelPaint);
        canvas.DrawText("0", rect.Left - 10, zeroY + 3, freqLabelPaint);
        canvas.DrawText("-12", rect.Left - 16, dbMinus12Y + 3, freqLabelPaint);
    }

    private void DrawSpectrum(SKCanvas canvas, SKRect rect, float[] levels, float[] peaks, int sampleRate)
    {
        int numBins = levels.Length;
        if (numBins == 0) return;

        float minFreq = 20f;
        float maxFreq = sampleRate > 0 ? sampleRate / 2f : 20000f;

        // Draw bars
        for (int i = 0; i < numBins; i++)
        {
            float t = i / (float)(numBins - 1);
            float freq = minFreq * MathF.Pow(maxFreq / minFreq, t);
            float nextT = (i + 1) / (float)(numBins - 1);
            float nextFreq = minFreq * MathF.Pow(maxFreq / minFreq, nextT);

            float x1 = FreqToX(freq, rect, sampleRate);
            float x2 = i < numBins - 1 ? FreqToX(nextFreq, rect, sampleRate) : rect.Right;
            float barWidth = Math.Max(2f, x2 - x1 - 1f);

            // Convert level to dB and then to Y position
            float levelDb = 20f * MathF.Log10(levels[i] + 1e-10f);
            levelDb = Math.Clamp(levelDb, -60f, 0f);
            // Map -60 to 0 dB to bottom to top of spectrum area
            float normalizedLevel = (levelDb + 60f) / 60f;
            float barHeight = normalizedLevel * rect.Height;

            if (barHeight > 1f)
            {
                var barRect = new SKRect(x1, rect.Bottom - barHeight, x1 + barWidth, rect.Bottom);
                canvas.DrawRect(barRect, _spectrumBarPaint);
            }

            // Draw peak line
            float peakDb = 20f * MathF.Log10(peaks[i] + 1e-10f);
            peakDb = Math.Clamp(peakDb, -60f, 0f);
            float normalizedPeak = (peakDb + 60f) / 60f;
            float peakY = rect.Bottom - normalizedPeak * rect.Height;

            if (normalizedPeak > 0.01f)
            {
                canvas.DrawLine(x1, peakY, x1 + barWidth, peakY, _spectrumPeakPaint);
            }
        }
    }

    private void DrawResponseCurve(SKCanvas canvas, SKRect rect, EqState state, float zeroY, float dbPixelsPerDb)
    {
        int numPoints = (int)rect.Width;
        if (numPoints < 2) return;

        using var path = new SKPath();
        using var fillPath = new SKPath();

        bool first = true;
        float minFreq = 20f;
        float maxFreq = state.SampleRate > 0 ? state.SampleRate / 2f : 20000f;

        for (int i = 0; i < numPoints; i++)
        {
            float x = rect.Left + i;
            float t = i / (float)(numPoints - 1);

            // Log scale frequency
            float freq = minFreq * MathF.Pow(maxFreq / minFreq, t);

            // Calculate combined response of all bands
            float totalDb = 0f;
            totalDb += CalculateBiquadResponse(freq, state.HpfFreq, 0f, 0.707f, state.SampleRate, FilterType.HighPass);
            totalDb += CalculateBiquadResponse(freq, state.LowShelfFreq, state.LowShelfGainDb, 0.7f, state.SampleRate, FilterType.LowShelf);
            totalDb += CalculateBiquadResponse(freq, state.Mid1Freq, state.Mid1GainDb, state.Mid1Q, state.SampleRate, FilterType.Peaking);
            totalDb += CalculateBiquadResponse(freq, state.Mid2Freq, state.Mid2GainDb, state.Mid2Q, state.SampleRate, FilterType.Peaking);
            totalDb += CalculateBiquadResponse(freq, state.HighShelfFreq, state.HighShelfGainDb, 0.7f, state.SampleRate, FilterType.HighShelf);

            // Clamp to display range
            totalDb = Math.Clamp(totalDb, -24f, 24f);
            float y = zeroY - totalDb * dbPixelsPerDb;

            if (first)
            {
                path.MoveTo(x, y);
                fillPath.MoveTo(x, zeroY);
                fillPath.LineTo(x, y);
                first = false;
            }
            else
            {
                path.LineTo(x, y);
                fillPath.LineTo(x, y);
            }
        }

        // Complete fill path
        fillPath.LineTo(rect.Right, zeroY);
        fillPath.Close();

        // Draw fill and curve
        canvas.DrawPath(fillPath, _curveFillPaint);
        canvas.DrawPath(path, _curvePaint);
    }

    private void DrawBandMarker(SKCanvas canvas, SKRect rect, float freq, float gainDb, SKColor color,
        int sampleRate, float zeroY, float dbPixelsPerDb)
    {
        float x = FreqToX(freq, rect, sampleRate);
        float y = zeroY - gainDb * dbPixelsPerDb;
        y = Math.Clamp(y, rect.Top + 6, rect.Bottom - 6);

        // Draw marker dot
        using var markerFill = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        using var markerGlow = new SKPaint
        {
            Color = color.WithAlpha(80),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4f)
        };

        canvas.DrawCircle(x, y, 8f, markerGlow);
        canvas.DrawCircle(x, y, 5f, markerFill);

        // Vertical line
        _bandMarkerPaint.Color = color.WithAlpha(60);
        canvas.DrawLine(x, rect.Top, x, rect.Bottom, _bandMarkerPaint);
    }

    private void DrawSingleKnob(SKCanvas canvas, float centerX, float labelY, float knobY, float valueY,
        string label, SKColor color, float value, float minValue, float maxValue, int hoveredKnob, int knobIndex)
    {
        _bandLabelPaint.Color = color;
        canvas.DrawText(label, centerX, labelY, _bandLabelPaint);

        bool hovered = hoveredKnob == knobIndex;
        DrawSmallKnob(canvas, new SKPoint(centerX, knobY), value, minValue, maxValue, color, hovered, true);
        string freqStr = value >= 1000 ? $"{value / 1000f:0.0}k" : $"{value:0}";
        canvas.DrawText($"{freqStr} Hz", centerX, valueY, _valuePaint);
    }

    private void DrawBandControls(SKCanvas canvas, float centerX, float labelY, float gainKnobY, float gainValueY,
        float freqKnobY, float freqValueY, string bandName, SKColor bandColor,
        float gainDb, float freq, float minFreq, float maxFreq, int hoveredKnob, int baseKnobIndex)
    {
        // Band label
        _bandLabelPaint.Color = bandColor;
        canvas.DrawText(bandName, centerX, labelY, _bandLabelPaint);

        // Gain knob
        bool gainHovered = hoveredKnob == baseKnobIndex;
        DrawSmallKnob(canvas, new SKPoint(centerX, gainKnobY), gainDb, -24f, 24f, bandColor, gainHovered);
        string gainSign = gainDb > 0.05f ? "+" : "";
        canvas.DrawText($"{gainSign}{gainDb:0.0} dB", centerX, gainValueY, _valuePaint);

        // Freq knob
        bool freqHovered = hoveredKnob == baseKnobIndex + 1;
        DrawSmallKnob(canvas, new SKPoint(centerX, freqKnobY), freq, minFreq, maxFreq, bandColor, freqHovered, true);
        string freqStr = freq >= 1000 ? $"{freq / 1000f:0.0}k" : $"{freq:0}";
        canvas.DrawText($"{freqStr} Hz", centerX, freqValueY, _valuePaint);
    }

    private void DrawSmallKnob(SKCanvas canvas, SKPoint center, float value, float minValue, float maxValue,
        SKColor arcColor, bool isHovered, bool isLogScale = false)
    {
        const float startAngle = 135f;
        const float sweepAngle = 270f;
        float arcRadius = KnobRadius * 0.8f;

        // Normalize value
        float normalized;
        if (isLogScale)
        {
            normalized = MathF.Log(value / minValue) / MathF.Log(maxValue / minValue);
        }
        else
        {
            normalized = (value - minValue) / (maxValue - minValue);
        }
        normalized = Math.Clamp(normalized, 0f, 1f);

        // Shadow
        using var shadowPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 40),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3f)
        };
        canvas.DrawCircle(center.X + 2, center.Y + 2, KnobRadius, shadowPaint);

        // Knob background
        canvas.DrawCircle(center, KnobRadius, _knobBackgroundPaint);

        // Track
        using var trackPath = new SKPath();
        trackPath.AddArc(
            new SKRect(center.X - arcRadius, center.Y - arcRadius, center.X + arcRadius, center.Y + arcRadius),
            startAngle, sweepAngle);
        canvas.DrawPath(trackPath, _knobTrackPaint);

        // Value arc
        if (normalized > 0.001f)
        {
            float valueAngle = sweepAngle * normalized;
            _knobArcPaint.Color = arcColor;
            using var arcPath = new SKPath();
            arcPath.AddArc(
                new SKRect(center.X - arcRadius, center.Y - arcRadius, center.X + arcRadius, center.Y + arcRadius),
                startAngle, valueAngle);
            canvas.DrawPath(arcPath, _knobArcPaint);
        }

        // Inner circle
        float innerRadius = KnobRadius * 0.6f;
        using var innerPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(center.X - innerRadius * 0.2f, center.Y - innerRadius * 0.2f),
                innerRadius * 2,
                new[] { _theme.KnobHighlight, _theme.KnobBackground },
                null,
                SKShaderTileMode.Clamp)
        };
        canvas.DrawCircle(center, innerRadius, innerPaint);

        // Pointer
        float pointerAngle = startAngle + sweepAngle * normalized;
        float pointerRad = pointerAngle * MathF.PI / 180f;
        float pointerStartRadius = innerRadius * 0.3f;
        float pointerEndRadius = innerRadius * 0.8f;
        canvas.DrawLine(
            center.X + pointerStartRadius * MathF.Cos(pointerRad),
            center.Y + pointerStartRadius * MathF.Sin(pointerRad),
            center.X + pointerEndRadius * MathF.Cos(pointerRad),
            center.Y + pointerEndRadius * MathF.Sin(pointerRad),
            _knobPointerPaint);

        // Hover ring
        if (isHovered)
        {
            using var hoverPaint = new SKPaint
            {
                Color = arcColor.WithAlpha(50),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2f
            };
            canvas.DrawCircle(center, KnobRadius + 3, hoverPaint);
        }
    }

    private float FreqToX(float freq, SKRect rect, int sampleRate)
    {
        float minFreq = 20f;
        float maxFreq = sampleRate > 0 ? sampleRate / 2f : 20000f;
        float t = MathF.Log(freq / minFreq) / MathF.Log(maxFreq / minFreq);
        t = Math.Clamp(t, 0f, 1f);
        return rect.Left + t * rect.Width;
    }

    private enum FilterType { HighPass, LowShelf, Peaking, HighShelf }

    private float CalculateBiquadResponse(float freq, float filterFreq, float gainDb, float q, int sampleRate, FilterType type)
    {
        if (sampleRate <= 0)
            return 0f;
        if (type != FilterType.HighPass && MathF.Abs(gainDb) < 0.01f)
            return 0f;

        float ratio = freq / filterFreq;

        switch (type)
        {
            case FilterType.HighPass:
                if (ratio >= 1f)
                    return 0f;
                float octaves = -MathF.Log2(MathF.Max(0.001f, ratio));
                return Math.Clamp(-12f * octaves, -24f, 0f);

            case FilterType.LowShelf:
                if (ratio < 0.5f)
                    return gainDb;
                else if (ratio > 2f)
                    return 0f;
                else
                    return gainDb * (1f - (ratio - 0.5f) / 1.5f);

            case FilterType.HighShelf:
                if (ratio > 2f)
                    return gainDb;
                else if (ratio < 0.5f)
                    return 0f;
                else
                    return gainDb * ((ratio - 0.5f) / 1.5f);

            case FilterType.Peaking:
                float qFactor = 1f / (2f * q);
                float logRatio = MathF.Log2(ratio);
                float bellShape = MathF.Exp(-logRatio * logRatio / (2f * qFactor * qFactor));
                return gainDb * bellShape;

            default:
                return 0f;
        }
    }

    public EqHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new EqHitTest(EqHitArea.CloseButton, -1);

        if (_bypassButtonRect.Contains(x, y))
            return new EqHitTest(EqHitArea.BypassButton, -1);

        // Check knobs
        if (IsInKnob(x, y, _hpfFreqKnob)) return new EqHitTest(EqHitArea.Knob, 0);
        if (IsInKnob(x, y, _lowShelfGainKnob)) return new EqHitTest(EqHitArea.Knob, 1);
        if (IsInKnob(x, y, _lowShelfFreqKnob)) return new EqHitTest(EqHitArea.Knob, 2);
        if (IsInKnob(x, y, _mid1GainKnob)) return new EqHitTest(EqHitArea.Knob, 3);
        if (IsInKnob(x, y, _mid1FreqKnob)) return new EqHitTest(EqHitArea.Knob, 4);
        if (IsInKnob(x, y, _mid2GainKnob)) return new EqHitTest(EqHitArea.Knob, 5);
        if (IsInKnob(x, y, _mid2FreqKnob)) return new EqHitTest(EqHitArea.Knob, 6);
        if (IsInKnob(x, y, _highShelfGainKnob)) return new EqHitTest(EqHitArea.Knob, 7);
        if (IsInKnob(x, y, _highShelfFreqKnob)) return new EqHitTest(EqHitArea.Knob, 8);

        if (_titleBarRect.Contains(x, y))
            return new EqHitTest(EqHitArea.TitleBar, -1);

        return new EqHitTest(EqHitArea.None, -1);
    }

    private bool IsInKnob(float x, float y, SKPoint center)
    {
        float dx = x - center.X;
        float dy = y - center.Y;
        return dx * dx + dy * dy <= KnobRadius * KnobRadius * 1.3f;
    }

    public static SKSize GetPreferredSize() => new(WindowWidth, WindowHeight);

    public void Dispose()
    {
        _inputMeter.Dispose();
        _outputMeter.Dispose();
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _spectrumBackgroundPaint.Dispose();
        _spectrumBarPaint.Dispose();
        _spectrumPeakPaint.Dispose();
        _gridPaint.Dispose();
        _zeroLinePaint.Dispose();
        _curvePaint.Dispose();
        _curveFillPaint.Dispose();
        _bandMarkerPaint.Dispose();
        _knobBackgroundPaint.Dispose();
        _knobTrackPaint.Dispose();
        _knobArcPaint.Dispose();
        _knobPointerPaint.Dispose();
        _labelPaint.Dispose();
        _valuePaint.Dispose();
        _latencyPaint.Dispose();
        _bandLabelPaint.Dispose();
        _meterBackgroundPaint.Dispose();
    }
}

/// <summary>
/// State data for rendering the EQ UI.
/// </summary>
public record struct EqState(
    float HpfFreq,
    float LowShelfGainDb,
    float LowShelfFreq,
    float Mid1GainDb,
    float Mid1Freq,
    float Mid1Q,
    float Mid2GainDb,
    float Mid2Freq,
    float Mid2Q,
    float HighShelfGainDb,
    float HighShelfFreq,
    float InputLevel,
    float OutputLevel,
    int SampleRate,
    float LatencyMs,
    bool IsBypassed,
    float[]? SpectrumLevels = null,
    float[]? SpectrumPeaks = null,
    int HoveredKnob = -1);

public enum EqHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    Knob
}

public record struct EqHitTest(EqHitArea Area, int KnobIndex);
