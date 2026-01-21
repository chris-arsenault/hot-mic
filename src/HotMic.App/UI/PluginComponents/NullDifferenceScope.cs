using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Null-difference oscilloscope: displays delta = wet - reference.
/// Reference path has identical linear filters but no saturation nonlinearity.
/// At warmth=0, delta should be ~0. At warmth>0, shows ONLY what saturation added.
/// Asymmetry indicator (+/-) shows even-harmonic content (ratio > 1 = even harmonics).
/// </summary>
public sealed class NullDifferenceScope : IDisposable
{
    private const int MaxSamples = 512;
    private const float DefaultScale = 0.05f; // ±5% full scale default zoom (accounts for phase differences)

    private readonly PluginComponentTheme _theme;

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _gridPaint;
    private readonly SKPaint _centerLinePaint;
    private readonly SKPaint _waveformPaint;
    private readonly SKPaint _positivePaint;
    private readonly SKPaint _negativePaint;
    private readonly SkiaTextPaint _labelPaint;
    private readonly SkiaTextPaint _scalePaint;

    private readonly float[] _samples = new float[MaxSamples];
    private float _scale = DefaultScale;
    private float _smoothedPeak = DefaultScale;
    private bool _autoScale = true;

    // Peak asymmetry tracking (ratio of positive to negative peaks indicates even harmonics)
    private float _positivePeak;
    private float _negativePeak;
    private float _asymmetryRatio = 1f;

    public NullDifferenceScope(PluginComponentTheme? theme = null)
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
            Color = _theme.PanelBorder.WithAlpha(40),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 0.5f
        };

        _centerLinePaint = new SKPaint
        {
            Color = _theme.TextMuted.WithAlpha(80),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };

        _waveformPaint = new SKPaint
        {
            Color = _theme.WaveformLine,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };

        // Even harmonics (positive asymmetry) - warm teal
        _positivePaint = new SKPaint
        {
            Color = new SKColor(0x00, 0xD4, 0xAA, 0xC0),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        // Odd harmonics (negative asymmetry) - orange warning
        _negativePaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0x6B, 0x00, 0xC0),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _labelPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Normal, SKTextAlign.Center);
        _scalePaint = new SkiaTextPaint(_theme.TextMuted, 8f, SKFontStyle.Normal, SKTextAlign.Left);
    }

    /// <summary>
    /// Current vertical scale (amplitude range displayed).
    /// </summary>
    public float Scale
    {
        get => _scale;
        set => _scale = Math.Clamp(value, 0.0001f, 0.1f);
    }

    public bool AutoScale
    {
        get => _autoScale;
        set => _autoScale = value;
    }

    public void Render(SKCanvas canvas, SKRect rect, Func<float[], int> getSamples)
    {
        // Background
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _backgroundPaint);

        float padding = 4f;
        float innerHeight = rect.Height - padding * 2;
        float centerY = rect.Top + rect.Height / 2;

        // Grid lines (±50% of scale)
        float quarterHeight = innerHeight / 4;
        canvas.DrawLine(rect.Left + padding, centerY - quarterHeight, rect.Right - padding, centerY - quarterHeight, _gridPaint);
        canvas.DrawLine(rect.Left + padding, centerY + quarterHeight, rect.Right - padding, centerY + quarterHeight, _gridPaint);

        // Center line (zero)
        canvas.DrawLine(rect.Left + padding, centerY, rect.Right - padding, centerY, _centerLinePaint);

        // Get raw delta samples and compute asymmetry
        int sampleCount = getSamples(_samples);
        if (sampleCount > 0)
        {
            // Track positive and negative peaks for asymmetry calculation
            float posPeak = 0f, negPeak = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                float s = _samples[i];
                if (s > posPeak) posPeak = s;
                if (s < negPeak) negPeak = s;
            }

            // Smooth the peaks and compute asymmetry ratio
            _positivePeak = _positivePeak * 0.9f + posPeak * 0.1f;
            _negativePeak = _negativePeak * 0.9f + negPeak * 0.1f;
            float absNeg = MathF.Abs(_negativePeak);
            _asymmetryRatio = absNeg > 0.0001f ? _positivePeak / absNeg : 1f;

            // Auto-scale based on peak amplitude
            if (_autoScale)
            {
                float peak = 0f;
                for (int i = 0; i < sampleCount; i++)
                {
                    peak = MathF.Max(peak, MathF.Abs(_samples[i]));
                }

                // Smooth the peak to avoid jitter (fast attack, slow release)
                if (peak > _smoothedPeak)
                {
                    _smoothedPeak = _smoothedPeak * 0.5f + peak * 0.5f; // Fast attack
                }
                else
                {
                    _smoothedPeak = _smoothedPeak * 0.98f + peak * 0.02f; // Slow release
                }

                // Set scale with headroom, minimum floor to avoid division issues
                _scale = MathF.Max(_smoothedPeak * 1.5f, 0.001f);
            }

            DrawWaveform(canvas, rect, padding, sampleCount, centerY, innerHeight);
        }

        // Scale indicator
        float scalePercent = _scale * 100f;
        string scaleText = scalePercent >= 1f ? $"±{scalePercent:0}%" : $"±{scalePercent:0.00}%";
        canvas.DrawText(scaleText, rect.Left + padding + 2, rect.Top + padding + 10, _scalePaint);

        // Border
        canvas.DrawRoundRect(roundRect, _borderPaint);

        // Asymmetry indicator (ratio != 1.0 indicates even harmonics from saturation)
        using var asymPaint = new SkiaTextPaint(_theme.TextMuted, 8f, SKFontStyle.Normal, SKTextAlign.Right);
        string asymText = $"+/-:{_asymmetryRatio:0.00}";
        canvas.DrawText(asymText, rect.Right - padding - 2, rect.Top + padding + 10, asymPaint);

        // Label
        canvas.DrawText("NULL DIFF", rect.MidX, rect.Bottom + 14, _labelPaint);
    }

    private void DrawWaveform(SKCanvas canvas, SKRect rect, float padding, int sampleCount, float centerY, float innerHeight)
    {
        float innerWidth = rect.Width - padding * 2;
        float halfHeight = innerHeight / 2;

        // Draw filled areas for positive/negative to show even/odd emphasis
        using var positivePath = new SKPath();
        using var negativePath = new SKPath();
        using var linePath = new SKPath();

        bool firstPoint = true;
        float lastX = 0, lastY = centerY;

        for (int i = 0; i < sampleCount; i++)
        {
            float x = rect.Left + padding + (innerWidth * i / (sampleCount - 1));
            float sample = _samples[i];

            // Normalize to scale and clamp
            float normalizedSample = Math.Clamp(sample / _scale, -1f, 1f);
            float y = centerY - normalizedSample * halfHeight;

            if (firstPoint)
            {
                linePath.MoveTo(x, y);
                positivePath.MoveTo(x, centerY);
                negativePath.MoveTo(x, centerY);
                firstPoint = false;
            }
            else
            {
                linePath.LineTo(x, y);
            }

            // Fill positive (above center) and negative (below center) separately
            if (sample > 0)
            {
                positivePath.LineTo(x, y);
                negativePath.LineTo(x, centerY);
            }
            else
            {
                positivePath.LineTo(x, centerY);
                negativePath.LineTo(x, y);
            }

            lastX = x;
            lastY = y;
        }

        // Close the filled paths
        positivePath.LineTo(lastX, centerY);
        positivePath.Close();
        negativePath.LineTo(lastX, centerY);
        negativePath.Close();

        // Draw fills
        canvas.DrawPath(positivePath, _positivePaint);
        canvas.DrawPath(negativePath, _negativePaint);

        // Draw line on top
        canvas.DrawPath(linePath, _waveformPaint);
    }

    public void Dispose()
    {
        _backgroundPaint.Dispose();
        _borderPaint.Dispose();
        _gridPaint.Dispose();
        _centerLinePaint.Dispose();
        _waveformPaint.Dispose();
        _positivePaint.Dispose();
        _negativePaint.Dispose();
        _labelPaint.Dispose();
        _scalePaint.Dispose();
    }
}
