using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Displays dynamic transfer curve with real sample scatter plot.
/// Shows actual (input, output) pairs colored by envelope level.
/// </summary>
public sealed class DynamicTransferCurveDisplay : IDisposable
{
    private const float WarmthPivotPct = 50f;
    private const float WarmthOverdriveMax = 2f;
    private const int MaxSamples = 256;

    private readonly PluginComponentTheme _theme;

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _gridPaint;
    private readonly SKPaint _linearPaint;
    private readonly SKPaint _curvePaint;
    private readonly SkiaTextPaint _labelPaint;

    // Pre-allocated sample buffers
    private readonly float[] _inputs = new float[MaxSamples];
    private readonly float[] _outputs = new float[MaxSamples];
    private readonly float[] _envelopes = new float[MaxSamples];

    public DynamicTransferCurveDisplay(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;

        _backgroundPaint = new SKPaint
        {
            Color = _theme.WaveformBackground,
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

        _gridPaint = new SKPaint
        {
            Color = _theme.PanelBorder.WithAlpha(60),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 0.5f
        };

        _linearPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            PathEffect = SKPathEffect.CreateDash([4f, 4f], 0)
        };

        _curvePaint = new SKPaint
        {
            Color = _theme.KnobArc.WithAlpha(100),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f
        };

        _labelPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Normal, SKTextAlign.Center);
        _rangePaint = new SkiaTextPaint(_theme.TextMuted, 8f, SKFontStyle.Normal, SKTextAlign.Left);
    }

    private readonly SkiaTextPaint _rangePaint;

    public void Render(
        SKCanvas canvas,
        SKRect rect,
        float warmthPct,
        Func<float[], float[], float[], int> getSamples)
    {
        // Background
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _backgroundPaint);

        float padding = 4f;

        // Get samples first to determine auto-scale range
        int sampleCount = getSamples(_inputs, _outputs, _envelopes);

        // Find actual signal range, but enforce minimum to show saturation curve shape
        // At high warmth (2x drive), the polynomial peaks around input ~0.4-0.5
        // Minimum range of 0.6 ensures we see the curve's nonlinear region
        const float MinDisplayRange = 0.6f;
        float range = MinDisplayRange;

        if (sampleCount > 0)
        {
            float minVal = float.MaxValue, maxVal = float.MinValue;
            for (int i = 0; i < sampleCount; i++)
            {
                float input = _inputs[i];
                float output = _outputs[i];
                if (MathF.Abs(input) < 0.0001f && MathF.Abs(output) < 0.0001f)
                    continue;
                minVal = MathF.Min(minVal, MathF.Min(input, output));
                maxVal = MathF.Max(maxVal, MathF.Max(input, output));
            }
            if (minVal < float.MaxValue)
            {
                float absMax = MathF.Max(MathF.Abs(minVal), MathF.Abs(maxVal));
                // Use larger of actual range or minimum display range
                range = MathF.Max(absMax * 1.2f, MinDisplayRange);
            }
        }

        // Grid crosshairs
        float centerX = rect.Left + rect.Width / 2;
        float centerY = rect.Top + rect.Height / 2;
        canvas.DrawLine(centerX, rect.Top + padding, centerX, rect.Bottom - padding, _gridPaint);
        canvas.DrawLine(rect.Left + padding, centerY, rect.Right - padding, centerY, _gridPaint);

        // Linear reference diagonal
        canvas.DrawLine(rect.Left + padding, rect.Bottom - padding, rect.Right - padding, rect.Top + padding, _linearPaint);

        // Clip drawing to the inner display area
        canvas.Save();
        canvas.ClipRect(new SKRect(rect.Left + padding, rect.Top + padding, rect.Right - padding, rect.Bottom - padding));

        // Draw theoretical curve (dimmed) - scaled to match sample range
        DrawTheoreticalCurve(canvas, rect, warmthPct, padding, range);

        // Draw real sample scatter
        DrawSampleScatter(canvas, rect, padding, sampleCount, range);

        canvas.Restore();

        // Border
        canvas.DrawRoundRect(roundRect, _borderPaint);

        // Range indicator (show in dB)
        float rangeDb = 20f * MathF.Log10(range + 1e-10f);
        string rangeText = $"±{rangeDb:0}dB";
        canvas.DrawText(rangeText, rect.Left + padding + 2, rect.Top + padding + 10, _rangePaint);

        // Label
        canvas.DrawText("TRANSFER", rect.MidX, rect.Bottom + 14, _labelPaint);
    }

    private void DrawTheoreticalCurve(SKCanvas canvas, SKRect rect, float warmthPct, float padding, float range)
    {
        float warmth = MapWarmthPreview(warmthPct);
        float env = 0.6f; // Nominal envelope for preview

        // Asymmetric tanh coefficients (must match SaturationPlugin)
        const float K0 = 1.0f, K1 = 1.0f;
        const float A0 = 0.15f, A1 = 0.20f;

        float k = (K0 + K1 * env) * warmth;
        float asym = (A0 + A1 * env) * warmth;
        float kPos = k * (1f + asym);
        float kNeg = k * (1f - asym);

        using var path = new SKPath();
        bool first = true;

        // Draw curve over the actual signal range [-range, range]
        for (float t = 0; t <= 1f; t += 0.02f)
        {
            float input = (t * 2f - 1f) * range;

            // Split curvature: different k for positive vs negative
            float shaped;
            if (input >= 0f)
            {
                shaped = MathF.Tanh(kPos * input);
            }
            else
            {
                shaped = MathF.Tanh(kNeg * input);
            }

            // Gain normalization
            if (k > 0.001f)
            {
                shaped /= k;
            }

            float output = shaped;

            // Normalize back to [0, 1] for display
            float normInput = (input + range) / (2f * range);
            float normOutput = (output + range) / (2f * range);

            float xPos = rect.Left + padding + (rect.Width - padding * 2) * normInput;
            float yPos = rect.Bottom - padding - (rect.Height - padding * 2) * normOutput;

            if (first)
            {
                path.MoveTo(xPos, yPos);
                first = false;
            }
            else
            {
                path.LineTo(xPos, yPos);
            }
        }

        canvas.DrawPath(path, _curvePaint);
    }

    private void DrawSampleScatter(SKCanvas canvas, SKRect rect, float padding, int sampleCount, float range)
    {
        if (sampleCount == 0) return;

        float innerWidth = rect.Width - padding * 2;
        float innerHeight = rect.Height - padding * 2;

        using var dotPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        for (int i = 0; i < sampleCount; i++)
        {
            float input = _inputs[i];
            float output = _outputs[i];
            float envelope = _envelopes[i];

            // Skip silent samples
            if (MathF.Abs(input) < 0.0001f && MathF.Abs(output) < 0.0001f)
                continue;

            // Map input/output from [-range, range] to pixel coords
            float normInput = (input + range) / (2f * range);
            float normOutput = (output + range) / (2f * range);

            float xPos = rect.Left + padding + innerWidth * normInput;
            float yPos = rect.Bottom - padding - innerHeight * normOutput;

            // Skip dots outside display bounds (don't clamp - that would be misleading)
            if (xPos < rect.Left || xPos > rect.Right || yPos < rect.Top || yPos > rect.Bottom)
                continue;

            // Color by envelope: low (blue) -> mid (cyan) -> high (orange)
            dotPaint.Color = GetEnvelopeColor(envelope);

            canvas.DrawCircle(xPos, yPos, 2f, dotPaint);
        }
    }

    private static SKColor GetEnvelopeColor(float envelope)
    {
        float t = Math.Clamp(envelope, 0f, 1f);

        if (t < 0.5f)
        {
            // Blue to cyan
            float s = t * 2f;
            byte r = (byte)(0x30 + (0x00 - 0x30) * s);
            byte g = (byte)(0x80 + (0xD4 - 0x80) * s);
            byte b = (byte)(0xFF + (0xAA - 0xFF) * s);
            return new SKColor(r, g, b, 200);
        }
        else
        {
            // Cyan to orange
            float s = (t - 0.5f) * 2f;
            byte r = (byte)(0x00 + (0xFF - 0x00) * s);
            byte g = (byte)(0xD4 + (0x6B - 0xD4) * s);
            byte b = (byte)(0xAA + (0x00 - 0xAA) * s);
            return new SKColor(r, g, b, 200);
        }
    }

    private static float MapWarmthPreview(float warmthPct)
    {
        float clamped = Math.Clamp(warmthPct, 0f, 100f);
        if (clamped <= WarmthPivotPct)
        {
            return clamped / WarmthPivotPct;
        }

        float t = (clamped - WarmthPivotPct) / (100f - WarmthPivotPct);
        return 1f + t * (WarmthOverdriveMax - 1f);
    }

    public void Dispose()
    {
        _backgroundPaint.Dispose();
        _borderPaint.Dispose();
        _gridPaint.Dispose();
        _linearPaint.Dispose();
        _curvePaint.Dispose();
        _labelPaint.Dispose();
        _rangePaint.Dispose();
    }
}
