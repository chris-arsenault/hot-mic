using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// FFT Noise Removal plugin UI with spectrum visualization and noise profile display.
/// </summary>
public sealed class FFTNoiseRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 14f;
    private const float CornerRadius = 10f;
    private const float SpectrumHeight = 200f;
    private const float ControlsHeight = 100f;
    private const float KnobRadius = 30f;

    private const float WindowWidth = 500f;
    private const float WindowHeight = 400f;

    private readonly PluginComponentTheme _theme;

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _titlePaint;
    private readonly SKPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _spectrumBackgroundPaint;
    private readonly SKPaint _inputBarPaint;
    private readonly SKPaint _outputBarPaint;
    private readonly SKPaint _noiseProfilePaint;
    private readonly SKPaint _noiseProfileFillPaint;
    private readonly SKPaint _gridPaint;
    private readonly SKPaint _knobBackgroundPaint;
    private readonly SKPaint _knobTrackPaint;
    private readonly SKPaint _knobArcPaint;
    private readonly SKPaint _knobPointerPaint;
    private readonly SKPaint _labelPaint;
    private readonly SKPaint _valuePaint;
    private readonly SKPaint _learnButtonPaint;
    private readonly SKPaint _learningPaint;
    private readonly SKPaint _progressBarPaint;
    private readonly SKPaint _progressFillPaint;
    private readonly SKPaint _latencyPaint;

    private readonly SKColor _inputColor = new(0x3D, 0xA5, 0xF4, 0x80);    // Blue, semi-transparent
    private readonly SKColor _outputColor = new(0x00, 0xD4, 0xAA, 0xA0);   // Teal, semi-transparent
    private readonly SKColor _noiseColor = new(0xFF, 0x6B, 0x00);          // Orange
    private readonly SKColor _learnActiveColor = new(0xFF, 0x50, 0x50);    // Red when learning

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;
    private SKRect _spectrumRect;
    private SKRect _learnButtonRect;
    private SKPoint _reductionKnob;

    public FFTNoiseRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;

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

        _inputBarPaint = new SKPaint
        {
            Color = _inputColor,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _outputBarPaint = new SKPaint
        {
            Color = _outputColor,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _noiseProfilePaint = new SKPaint
        {
            Color = _noiseColor,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };

        _noiseProfileFillPaint = new SKPaint
        {
            Color = _noiseColor.WithAlpha(40),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _gridPaint = new SKPaint
        {
            Color = _theme.EnvelopeGrid,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
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
            Color = _theme.WaveformLine,
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
            TextSize = 12f,
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

        _learnButtonPaint = new SKPaint
        {
            Color = _noiseColor,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _learningPaint = new SKPaint
        {
            Color = _learnActiveColor,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _progressBarPaint = new SKPaint
        {
            Color = _theme.WaveformBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _progressFillPaint = new SKPaint
        {
            Color = _noiseColor,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
    }

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, FFTNoiseState state)
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
        canvas.DrawText("FFT Noise Removal", Padding, TitleBarHeight / 2f + 5, _titlePaint);

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

        // Spectrum area
        _spectrumRect = new SKRect(Padding, contentTop, size.Width - Padding, contentTop + SpectrumHeight);
        DrawSpectrum(canvas, _spectrumRect, state);

        // Controls area
        float controlsTop = contentTop + SpectrumHeight + Padding;
        DrawControls(canvas, Padding, controlsTop, size.Width - Padding * 2, ControlsHeight, state);

        // Outer border
        canvas.DrawRoundRect(roundRect, _borderPaint);

        canvas.Restore();
    }

    private void DrawSpectrum(SKCanvas canvas, SKRect outerRect, FFTNoiseState state)
    {
        // Layout with margins for axis labels
        const float leftMargin = 32f;   // Space for dB labels
        const float bottomMargin = 18f; // Space for frequency labels
        const float topMargin = 4f;

        var graphRect = new SKRect(
            outerRect.Left + leftMargin,
            outerRect.Top + topMargin,
            outerRect.Right - 4f,
            outerRect.Bottom - bottomMargin);

        // Background for entire area
        var roundRect = new SKRoundRect(outerRect, 6f);
        canvas.DrawRoundRect(roundRect, _spectrumBackgroundPaint);

        // dB scale: -90 to +12
        const float minDb = -90f;
        const float maxDb = 12f;
        float dbRange = maxDb - minDb;

        // Frequency scale
        const float minFreq = 20f;
        float maxFreq = state.SampleRate > 0 ? state.SampleRate / 2f : 24000f;

        // Axis label paint
        using var axisLabelPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            TextSize = 8f,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };

        // Draw dB grid lines and labels
        int[] dbMarkers = { 0, -12, -24, -36, -48, -60, -72, -84 };
        foreach (int db in dbMarkers)
        {
            float y = graphRect.Bottom - ((db - minDb) / dbRange) * graphRect.Height;
            if (y >= graphRect.Top && y <= graphRect.Bottom)
            {
                canvas.DrawLine(graphRect.Left, y, graphRect.Right, y, _gridPaint);
                // Label on left
                axisLabelPaint.TextAlign = SKTextAlign.Right;
                canvas.DrawText($"{db}", graphRect.Left - 3, y + 3, axisLabelPaint);
            }
        }

        // Draw frequency grid lines and labels
        float[] freqMarkers = { 50f, 100f, 200f, 500f, 1000f, 2000f, 5000f, 10000f, 20000f };
        axisLabelPaint.TextAlign = SKTextAlign.Center;
        foreach (float freq in freqMarkers)
        {
            if (freq > maxFreq) continue;
            float t = MathF.Log(freq / minFreq) / MathF.Log(maxFreq / minFreq);
            float x = graphRect.Left + t * graphRect.Width;
            if (x >= graphRect.Left && x <= graphRect.Right)
            {
                canvas.DrawLine(x, graphRect.Top, x, graphRect.Bottom, _gridPaint);
                // Label at bottom - format nicely
                string label = freq >= 1000 ? $"{freq / 1000:0}k" : $"{freq:0}";
                canvas.DrawText(label, x, outerRect.Bottom - 4, axisLabelPaint);
            }
        }

        // Clip to graph area for spectrum drawing
        canvas.Save();
        canvas.ClipRect(graphRect);

        int numBins = state.InputSpectrum?.Length ?? 0;
        if (numBins == 0)
        {
            canvas.Restore();
            canvas.DrawRoundRect(roundRect, _borderPaint);
            return;
        }

        float barWidth = graphRect.Width / numBins;

        // Helper to convert magnitude to Y position
        float MagToY(float magnitude)
        {
            float db = 20f * MathF.Log10(magnitude + 1e-10f);
            db = Math.Clamp(db, minDb, maxDb);
            return graphRect.Bottom - ((db - minDb) / dbRange) * graphRect.Height;
        }

        // Draw noise profile first (as filled area at bottom)
        if (state.HasNoiseProfile && state.NoiseProfile != null)
        {
            using var profilePath = new SKPath();
            profilePath.MoveTo(graphRect.Left, graphRect.Bottom);

            for (int i = 0; i < numBins; i++)
            {
                float x = graphRect.Left + i * barWidth + barWidth / 2;
                float y = MagToY(state.NoiseProfile[i]);
                profilePath.LineTo(x, y);
            }

            profilePath.LineTo(graphRect.Right, graphRect.Bottom);
            profilePath.Close();
            canvas.DrawPath(profilePath, _noiseProfileFillPaint);

            // Draw noise profile line
            using var linePath = new SKPath();
            bool first = true;
            for (int i = 0; i < numBins; i++)
            {
                float x = graphRect.Left + i * barWidth + barWidth / 2;
                float y = MagToY(state.NoiseProfile[i]);

                if (first)
                {
                    linePath.MoveTo(x, y);
                    first = false;
                }
                else
                {
                    linePath.LineTo(x, y);
                }
            }
            canvas.DrawPath(linePath, _noiseProfilePaint);
        }

        // Draw input spectrum bars (before noise removal) - behind output
        if (state.InputSpectrum != null)
        {
            for (int i = 0; i < numBins; i++)
            {
                float x = graphRect.Left + i * barWidth;
                float y = MagToY(state.InputSpectrum[i]);
                float barHeight = graphRect.Bottom - y;

                if (barHeight > 1)
                {
                    var barRect = new SKRect(x + 1, y, x + barWidth / 2 - 0.5f, graphRect.Bottom);
                    canvas.DrawRect(barRect, _inputBarPaint);
                }
            }
        }

        // Draw output spectrum bars (after noise removal) - in front
        if (state.OutputSpectrum != null)
        {
            for (int i = 0; i < numBins; i++)
            {
                float x = graphRect.Left + i * barWidth + barWidth / 2;
                float y = MagToY(state.OutputSpectrum[i]);
                float barHeight = graphRect.Bottom - y;

                if (barHeight > 1)
                {
                    var barRect = new SKRect(x + 0.5f, y, x + barWidth / 2 - 1, graphRect.Bottom);
                    canvas.DrawRect(barRect, _outputBarPaint);
                }
            }
        }

        // Learning overlay
        if (state.IsLearning)
        {
            using var learningOverlay = new SKPaint
            {
                Color = new SKColor(0xFF, 0x50, 0x50, 0x20),
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            canvas.DrawRect(graphRect, learningOverlay);

            // Progress bar
            float progressWidth = graphRect.Width * 0.6f;
            float progressHeight = 8f;
            float progressX = graphRect.MidX - progressWidth / 2;
            float progressY = graphRect.MidY - progressHeight / 2;
            var progressBg = new SKRect(progressX, progressY, progressX + progressWidth, progressY + progressHeight);
            canvas.DrawRoundRect(new SKRoundRect(progressBg, 4f), _progressBarPaint);

            float fillWidth = progressWidth * (state.LearningProgress / (float)state.LearningTotal);
            var progressFill = new SKRect(progressX, progressY, progressX + fillWidth, progressY + progressHeight);
            canvas.DrawRoundRect(new SKRoundRect(progressFill, 4f), _progressFillPaint);

            // Learning text
            using var learningText = new SKPaint
            {
                Color = _theme.TextPrimary,
                IsAntialias = true,
                TextSize = 14f,
                TextAlign = SKTextAlign.Center,
                Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
            };
            canvas.DrawText($"Learning noise profile... {state.LearningProgress}/{state.LearningTotal}",
                graphRect.MidX, progressY - 12, learningText);
        }

        canvas.Restore();

        // Border around graph area
        canvas.DrawRect(graphRect, _borderPaint);

        // Legend (below everything)
        float legendY = outerRect.Bottom + 14;
        using var legendPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            TextSize = 9f,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };

        // Input legend
        using var inputSwatch = new SKPaint { Color = _inputColor, Style = SKPaintStyle.Fill };
        canvas.DrawRect(outerRect.Left, legendY - 6, 12, 8, inputSwatch);
        canvas.DrawText("Input", outerRect.Left + 16, legendY, legendPaint);

        // Output legend
        using var outputSwatch = new SKPaint { Color = _outputColor, Style = SKPaintStyle.Fill };
        canvas.DrawRect(outerRect.Left + 60, legendY - 6, 12, 8, outputSwatch);
        canvas.DrawText("Output", outerRect.Left + 76, legendY, legendPaint);

        // Noise profile legend
        using var noiseSwatch = new SKPaint { Color = _noiseColor, Style = SKPaintStyle.Fill };
        canvas.DrawRect(outerRect.Left + 130, legendY - 6, 12, 8, noiseSwatch);
        canvas.DrawText("Noise Profile", outerRect.Left + 146, legendY, legendPaint);
    }

    private void DrawControls(SKCanvas canvas, float x, float y, float width, float height, FFTNoiseState state)
    {
        float sectionWidth = width / 2f;

        // Learn button
        float learnWidth = 100f;
        float learnHeight = 36f;
        float learnX = x + sectionWidth / 2 - learnWidth / 2;
        float learnY = y + height / 2 - learnHeight / 2;
        _learnButtonRect = new SKRect(learnX, learnY, learnX + learnWidth, learnY + learnHeight);

        var learnPaint = state.IsLearning ? _learningPaint : _learnButtonPaint;
        canvas.DrawRoundRect(new SKRoundRect(_learnButtonRect, 6f), learnPaint);
        canvas.DrawRoundRect(new SKRoundRect(_learnButtonRect, 6f), _borderPaint);

        using var learnTextPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };
        string learnLabel = state.IsLearning ? "STOP" : (state.HasNoiseProfile ? "RE-LEARN" : "LEARN");
        canvas.DrawText(learnLabel, _learnButtonRect.MidX, _learnButtonRect.MidY + 4, learnTextPaint);

        // Status text below button
        string statusText = state.IsLearning ? "Learning..." : (state.HasNoiseProfile ? "Profile captured" : "No profile yet");
        canvas.DrawText(statusText, _learnButtonRect.MidX, _learnButtonRect.Bottom + 16, _labelPaint);

        // Reduction knob
        float reductionX = x + sectionWidth + sectionWidth / 2;
        float reductionY = y + height / 2;
        _reductionKnob = new SKPoint(reductionX, reductionY);
        DrawKnob(canvas, _reductionKnob, state.Reduction, 0f, 1f, "REDUCTION", $"{state.Reduction * 100:0}%",
            state.HoveredKnob == 0);
    }

    private void DrawKnob(SKCanvas canvas, SKPoint center, float value, float minValue, float maxValue,
        string label, string valueText, bool isHovered)
    {
        const float startAngle = 135f;
        const float sweepAngle = 270f;
        float arcRadius = KnobRadius * 0.8f;

        float normalized = (value - minValue) / (maxValue - minValue);
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

        // Background
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
                Color = _theme.WaveformLine.WithAlpha(50),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2f
            };
            canvas.DrawCircle(center, KnobRadius + 3, hoverPaint);
        }

        // Label above
        canvas.DrawText(label, center.X, center.Y - KnobRadius - 8, _labelPaint);

        // Value below
        canvas.DrawText(valueText, center.X, center.Y + KnobRadius + 16, _valuePaint);
    }

    public FFTNoiseHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new FFTNoiseHitTest(FFTNoiseHitArea.CloseButton, -1);

        if (_bypassButtonRect.Contains(x, y))
            return new FFTNoiseHitTest(FFTNoiseHitArea.BypassButton, -1);

        if (_learnButtonRect.Contains(x, y))
            return new FFTNoiseHitTest(FFTNoiseHitArea.LearnButton, -1);

        if (IsInKnob(x, y, _reductionKnob))
            return new FFTNoiseHitTest(FFTNoiseHitArea.Knob, 0);

        if (_titleBarRect.Contains(x, y))
            return new FFTNoiseHitTest(FFTNoiseHitArea.TitleBar, -1);

        return new FFTNoiseHitTest(FFTNoiseHitArea.None, -1);
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
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _spectrumBackgroundPaint.Dispose();
        _inputBarPaint.Dispose();
        _outputBarPaint.Dispose();
        _noiseProfilePaint.Dispose();
        _noiseProfileFillPaint.Dispose();
        _gridPaint.Dispose();
        _knobBackgroundPaint.Dispose();
        _knobTrackPaint.Dispose();
        _knobArcPaint.Dispose();
        _knobPointerPaint.Dispose();
        _labelPaint.Dispose();
        _valuePaint.Dispose();
        _latencyPaint.Dispose();
        _learnButtonPaint.Dispose();
        _learningPaint.Dispose();
        _progressBarPaint.Dispose();
        _progressFillPaint.Dispose();
    }
}

public record struct FFTNoiseState(
    float Reduction,
    bool IsLearning,
    int LearningProgress,
    int LearningTotal,
    bool HasNoiseProfile,
    bool IsBypassed,
    int SampleRate,
    float LatencyMs,
    float[]? InputSpectrum = null,
    float[]? InputPeaks = null,
    float[]? OutputSpectrum = null,
    float[]? OutputPeaks = null,
    float[]? NoiseProfile = null,
    int HoveredKnob = -1);

public enum FFTNoiseHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    LearnButton,
    Knob
}

public record struct FFTNoiseHitTest(FFTNoiseHitArea Area, int KnobIndex);
