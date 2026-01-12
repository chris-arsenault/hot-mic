using HotMic.Core.Dsp;
using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// FFT-based harmonic analyzer showing harmonic structure and even/odd ratio.
/// For tube-style warmth: even harmonics (2nd, 4th, 6th) should dominate over odd (3rd, 5th, 7th).
/// Target ratio for spoken word: 1.5-3.0 (even/odd).
/// </summary>
public sealed class HarmonicAnalyzer : IDisposable
{
    private const int FftSize = 2048;
    private const int HarmonicCount = 7; // 2nd through 7th + fundamental
    private const float MinDbDisplay = -60f;
    private const float MaxDbDisplay = 0f;

    private readonly PluginComponentTheme _theme;
    private readonly FastFft _fft;

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _gridPaint;
    private readonly SKPaint _evenBarPaint;
    private readonly SKPaint _oddBarPaint;
    private readonly SKPaint _fundamentalPaint;
    private readonly SKPaint _labelPaint;
    private readonly SKPaint _ratioLabelPaint;
    private readonly SKPaint _ratioValuePaint;
    private readonly SKPaint _ratioGoodPaint;
    private readonly SKPaint _ratioBadPaint;

    // Pre-allocated FFT buffers
    private readonly float[] _fftInputBuffer = new float[FftSize];
    private readonly float[] _fftReal = new float[FftSize];
    private readonly float[] _fftImag = new float[FftSize];
    private readonly float[] _window = new float[FftSize];
    private readonly float[] _harmonicDb = new float[HarmonicCount + 1]; // Fund + 7 harmonics
    private readonly float[] _smoothedHarmonicDb = new float[HarmonicCount + 1]; // Smoothed for display

    private int _sampleRate = 48000;
    private float _fundamentalHz = 200f; // Estimated fundamental
    private float _evenOddRatio = 1f;
    private float _smoothedRatio = 1f;
    private float _signalPeakDb = -60f; // For low-signal warning

    // Smoothing constants (fast attack, slow release for visual stability)
    private const float AttackSmoothing = 0.4f;  // Fast rise
    private const float ReleaseSmoothing = 0.05f; // Slow decay
    private const float RatioSmoothing = 0.1f;    // Moderate smoothing for ratio

    public HarmonicAnalyzer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _fft = new FastFft(FftSize);

        // Hanning window
        for (int i = 0; i < FftSize; i++)
        {
            _window[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (FftSize - 1)));
        }

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

        // Even harmonics - warm teal (good for warmth)
        _evenBarPaint = new SKPaint
        {
            Color = new SKColor(0x00, 0xD4, 0xAA),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        // Odd harmonics - orange (adds edge, less warmth)
        _oddBarPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0x6B, 0x00),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        // Fundamental - white/gray
        _fundamentalPaint = new SKPaint
        {
            Color = _theme.TextSecondary,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _labelPaint = new SKPaint
        {
            Color = _theme.TextSecondary,
            IsAntialias = true,
            TextSize = 8f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };

        _ratioLabelPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            TextSize = 8f,
            TextAlign = SKTextAlign.Left,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };

        _ratioValuePaint = new SKPaint
        {
            IsAntialias = true,
            TextSize = 12f,
            TextAlign = SKTextAlign.Left,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };

        _ratioGoodPaint = new SKPaint
        {
            Color = new SKColor(0x00, 0xD4, 0xAA),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _ratioBadPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0x50, 0x50),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
    }

    public int SampleRate
    {
        get => _sampleRate;
        set => _sampleRate = Math.Max(1, value);
    }

    public float EvenOddRatio => _evenOddRatio;

    public void Analyze(Func<float[], int> getSamples)
    {
        int count = getSamples(_fftInputBuffer);
        if (count < FftSize) return;

        // Find peak level and apply window
        float peak = 0f;
        for (int i = 0; i < FftSize; i++)
        {
            float sample = _fftInputBuffer[i];
            peak = MathF.Max(peak, MathF.Abs(sample));
            _fftReal[i] = sample * _window[i];
            _fftImag[i] = 0f;
        }
        _signalPeakDb = 20f * MathF.Log10(peak + 1e-10f);

        _fft.Forward(_fftReal, _fftImag);

        // Find fundamental frequency (strongest peak in useful range)
        // Expanded range: 60 Hz (low voice) to 2000 Hz (test tones, high harmonics)
        float binResolution = (float)_sampleRate / FftSize;
        int minBin = (int)(60f / binResolution);
        int maxBin = (int)(2000f / binResolution);

        float maxMag = 0f;
        int fundamentalBin = minBin;
        for (int bin = minBin; bin <= maxBin && bin < FftSize / 2; bin++)
        {
            float mag = _fftReal[bin] * _fftReal[bin] + _fftImag[bin] * _fftImag[bin];
            if (mag > maxMag)
            {
                maxMag = mag;
                fundamentalBin = bin;
            }
        }

        _fundamentalHz = fundamentalBin * binResolution;

        // Extract harmonic magnitudes (relative to fundamental)
        float[] rawMagnitudes = new float[HarmonicCount + 1];
        float evenEnergy = 0f;
        float oddEnergy = 0f;

        for (int h = 0; h <= HarmonicCount; h++)
        {
            int harmonicBin = fundamentalBin * (h + 1);
            if (harmonicBin >= FftSize / 2)
            {
                rawMagnitudes[h] = 0f;
                continue;
            }

            // Get magnitude with some bin averaging for stability
            float mag = 0f;
            int avgRange = 2;
            for (int offset = -avgRange; offset <= avgRange; offset++)
            {
                int bin = Math.Clamp(harmonicBin + offset, 0, FftSize / 2 - 1);
                float r = _fftReal[bin];
                float im = _fftImag[bin];
                mag += MathF.Sqrt(r * r + im * im);
            }
            mag /= (avgRange * 2 + 1);
            rawMagnitudes[h] = mag;

            // Accumulate even/odd energy (skip fundamental at h=0)
            if (h >= 1)
            {
                int harmonicNumber = h + 1; // 2nd, 3rd, 4th, etc.
                float linearMag = mag * mag; // Energy
                if (harmonicNumber % 2 == 0)
                {
                    evenEnergy += linearMag;
                }
                else
                {
                    oddEnergy += linearMag;
                }
            }
        }

        // Normalize to fundamental and convert to dB (relative display)
        float fundamentalMag = rawMagnitudes[0];
        for (int h = 0; h <= HarmonicCount; h++)
        {
            float relativeDb;
            if (fundamentalMag > 1e-10f)
            {
                // dB relative to fundamental (fundamental = 0 dB)
                relativeDb = 20f * MathF.Log10((rawMagnitudes[h] + 1e-10f) / fundamentalMag);
            }
            else
            {
                relativeDb = MinDbDisplay;
            }
            _harmonicDb[h] = Math.Clamp(relativeDb, MinDbDisplay, MaxDbDisplay);
        }

        // Calculate even/odd ratio
        _evenOddRatio = oddEnergy > 1e-10f ? evenEnergy / oddEnergy : 1f;

        // Apply smoothing to all values for stable display
        for (int h = 0; h <= HarmonicCount; h++)
        {
            float target = _harmonicDb[h];
            float current = _smoothedHarmonicDb[h];
            float smoothing = target > current ? AttackSmoothing : ReleaseSmoothing;
            _smoothedHarmonicDb[h] = current + smoothing * (target - current);
        }

        // Smooth the ratio (use log domain for perceptually linear smoothing)
        float logTarget = MathF.Log(_evenOddRatio + 0.01f);
        float logCurrent = MathF.Log(_smoothedRatio + 0.01f);
        float logSmoothed = logCurrent + RatioSmoothing * (logTarget - logCurrent);
        _smoothedRatio = MathF.Exp(logSmoothed);
    }

    public void Render(SKCanvas canvas, SKRect rect)
    {
        // Background
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _backgroundPaint);

        float padding = 4f;
        float innerWidth = rect.Width - padding * 2;
        float innerHeight = rect.Height - padding * 2;

        // Reserve space for ratio display at top
        float ratioHeight = 18f;
        float barAreaTop = rect.Top + padding + ratioHeight;
        float barAreaHeight = innerHeight - ratioHeight - 12f; // 12 for labels at bottom

        // Draw ratio indicator
        DrawRatioIndicator(canvas, rect.Left + padding, rect.Top + padding, innerWidth);

        // Grid lines (-20dB, -40dB)
        float dbRange = MaxDbDisplay - MinDbDisplay;
        for (float db = -20f; db >= MinDbDisplay; db -= 20f)
        {
            float y = barAreaTop + barAreaHeight * (MaxDbDisplay - db) / dbRange;
            canvas.DrawLine(rect.Left + padding, y, rect.Right - padding, y, _gridPaint);
        }

        // Draw harmonic bars
        float barWidth = (innerWidth - padding * 2) / (HarmonicCount + 1);
        float barGap = 2f;

        for (int h = 0; h <= HarmonicCount; h++)
        {
            float x = rect.Left + padding + h * barWidth + barGap;
            float barW = barWidth - barGap * 2;

            float db = _smoothedHarmonicDb[h]; // Use smoothed values for display
            float normalizedHeight = (db - MinDbDisplay) / dbRange;
            float barHeight = Math.Max(2f, barAreaHeight * normalizedHeight);

            float barTop = barAreaTop + barAreaHeight - barHeight;

            // Choose color based on harmonic type
            SKPaint barPaint;
            if (h == 0)
            {
                barPaint = _fundamentalPaint;
            }
            else
            {
                int harmonicNumber = h + 1;
                barPaint = harmonicNumber % 2 == 0 ? _evenBarPaint : _oddBarPaint;
            }

            canvas.DrawRect(x, barTop, barW, barHeight, barPaint);

            // Label
            string label = h == 0 ? "F" : $"{h + 1}";
            canvas.DrawText(label, x + barW / 2, rect.Bottom - padding - 2, _labelPaint);
        }

        // Border
        canvas.DrawRoundRect(roundRect, _borderPaint);

        // Low signal warning (delta signal below -30dB means harmonics may not be visible)
        if (_signalPeakDb < -30f)
        {
            using var warnPaint = new SKPaint
            {
                Color = _theme.TextMuted.WithAlpha(180),
                IsAntialias = true,
                TextSize = 8f,
                TextAlign = SKTextAlign.Right,
                Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Italic)
            };
            canvas.DrawText($"Δ:{_signalPeakDb:0}dB (low)", rect.Right - padding - 2, rect.Top + padding + 10, warnPaint);
        }
    }

    private void DrawRatioIndicator(SKCanvas canvas, float x, float y, float width)
    {
        // Fundamental frequency indicator
        string fundText = _fundamentalHz >= 1000f
            ? $"F:{_fundamentalHz / 1000f:0.0}kHz"
            : $"F:{_fundamentalHz:0}Hz";
        canvas.DrawText(fundText, x, y + 10, _ratioLabelPaint);

        // E/O label and value
        canvas.DrawText("E/O:", x + 48, y + 10, _ratioLabelPaint);

        // Value with color coding (use smoothed ratio for display)
        // Good warmth: ratio > 1.0, ideal: 1.5-3.0
        bool isGood = _smoothedRatio >= 1.0f;
        _ratioValuePaint.Color = isGood ? _ratioGoodPaint.Color : _ratioBadPaint.Color;

        string ratioText = _smoothedRatio >= 10f ? $"{_smoothedRatio:0}" : $"{_smoothedRatio:0.0}";
        canvas.DrawText(ratioText, x + 70, y + 10, _ratioValuePaint);

        // Small indicator bar (positioned after E/O value)
        float barX = x + 94;
        float barWidth = width - 99;
        float barHeight = 4f;
        float barY = y + 6;

        // Background
        using var bgPaint = new SKPaint { Color = _theme.PanelBackgroundLight, Style = SKPaintStyle.Fill };
        canvas.DrawRect(barX, barY, barWidth, barHeight, bgPaint);

        // Ratio indicator (log scale, 0.5 to 4.0 range)
        float logRatio = MathF.Log2(Math.Clamp(_smoothedRatio, 0.25f, 8f));
        float normalizedPos = (logRatio + 2f) / 5f; // Map [-2, 3] to [0, 1]
        normalizedPos = Math.Clamp(normalizedPos, 0f, 1f);

        using var markerPaint = new SKPaint
        {
            Color = isGood ? _ratioGoodPaint.Color : _ratioBadPaint.Color,
            Style = SKPaintStyle.Fill
        };
        float markerX = barX + barWidth * normalizedPos;
        canvas.DrawCircle(markerX, barY + barHeight / 2, 3f, markerPaint);

        // Target zone marker (1.5 - 3.0)
        float targetStart = (MathF.Log2(1.5f) + 2f) / 5f;
        float targetEnd = (MathF.Log2(3f) + 2f) / 5f;
        using var zonePaint = new SKPaint
        {
            Color = _ratioGoodPaint.Color.WithAlpha(60),
            Style = SKPaintStyle.Fill
        };
        canvas.DrawRect(barX + barWidth * targetStart, barY, barWidth * (targetEnd - targetStart), barHeight, zonePaint);
    }

    public void Dispose()
    {
        _backgroundPaint.Dispose();
        _borderPaint.Dispose();
        _gridPaint.Dispose();
        _evenBarPaint.Dispose();
        _oddBarPaint.Dispose();
        _fundamentalPaint.Dispose();
        _labelPaint.Dispose();
        _ratioLabelPaint.Dispose();
        _ratioValuePaint.Dispose();
        _ratioGoodPaint.Dispose();
        _ratioBadPaint.Dispose();
    }
}
