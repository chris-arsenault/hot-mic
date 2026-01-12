using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Orientation for level meter rendering.
/// </summary>
public enum MeterOrientation
{
    Vertical,    // Bottom-to-top (standard audio meter)
    Horizontal   // Left-to-right
}

/// <summary>
/// Reusable audio level meter with PPM-style ballistics.
/// Shows current level as filled bar and quasi-peak as line marker.
/// </summary>
public sealed class LevelMeter : IDisposable
{
    private readonly MeterBallistics _ballistics;
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _greenPaint;
    private readonly SKPaint _yellowPaint;
    private readonly SKPaint _redPaint;
    private readonly SKPaint _peakPaint;
    private readonly SKPaint _borderPaint;

    private readonly float _minDb;
    private readonly float _maxDb;
    private readonly float _yellowThresholdDb;
    private readonly float _redThresholdDb;

    /// <summary>
    /// Creates a level meter with standard -60dB to 0dB range.
    /// </summary>
    public LevelMeter() : this(-60f, 0f, -6f, -3f)
    {
    }

    /// <summary>
    /// Creates a level meter with custom dB range and color thresholds.
    /// </summary>
    /// <param name="minDb">Minimum dB value (bottom/left of meter).</param>
    /// <param name="maxDb">Maximum dB value (top/right of meter).</param>
    /// <param name="yellowThresholdDb">dB level where meter turns yellow.</param>
    /// <param name="redThresholdDb">dB level where meter turns red.</param>
    public LevelMeter(float minDb, float maxDb, float yellowThresholdDb, float redThresholdDb)
    {
        _minDb = minDb;
        _maxDb = maxDb;
        _yellowThresholdDb = yellowThresholdDb;
        _redThresholdDb = redThresholdDb;

        _ballistics = new MeterBallistics();

        _backgroundPaint = new SKPaint
        {
            Color = new SKColor(0x1A, 0x1A, 0x1A),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _greenPaint = new SKPaint
        {
            Color = new SKColor(0x40, 0xC0, 0x40),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _yellowPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0xC0, 0x40),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _redPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0x50, 0x50),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _peakPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f
        };

        _borderPaint = new SKPaint
        {
            Color = new SKColor(0x40, 0x40, 0x40),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };
    }

    /// <summary>
    /// Gets the current smoothed level (normalized 0-1).
    /// </summary>
    public float CurrentNormalized => _ballistics.Current;

    /// <summary>
    /// Gets the held peak level (normalized 0-1).
    /// </summary>
    public float PeakNormalized => _ballistics.Peak;

    /// <summary>
    /// Updates the meter with a linear level value (0-1 range).
    /// </summary>
    public void Update(float linearLevel)
    {
        linearLevel = Math.Clamp(linearLevel, 0f, 2f); // Allow some headroom
        float normalized = LinearToNormalized(linearLevel);
        _ballistics.Update(normalized);
    }

    /// <summary>
    /// Updates the meter with a dB level value.
    /// </summary>
    public void UpdateDb(float dbLevel)
    {
        float normalized = DbToNormalized(dbLevel);
        _ballistics.Update(normalized);
    }

    /// <summary>
    /// Renders the meter.
    /// </summary>
    public void Render(SKCanvas canvas, SKRect rect, MeterOrientation orientation = MeterOrientation.Vertical)
    {
        float current = _ballistics.Current;
        float peak = _ballistics.Peak;

        // Background
        canvas.DrawRect(rect, _backgroundPaint);

        if (orientation == MeterOrientation.Vertical)
        {
            RenderVertical(canvas, rect, current, peak);
        }
        else
        {
            RenderHorizontal(canvas, rect, current, peak);
        }

        // Border
        canvas.DrawRect(rect, _borderPaint);
    }

    private void RenderVertical(SKCanvas canvas, SKRect rect, float current, float peak)
    {
        float padding = 2f;
        float innerLeft = rect.Left + padding;
        float innerRight = rect.Right - padding;
        float innerTop = rect.Top + padding;
        float innerBottom = rect.Bottom - padding;
        float innerHeight = innerBottom - innerTop;

        // Current level bar (from bottom up)
        float barHeight = innerHeight * Math.Clamp(current, 0f, 1f);
        if (barHeight > 1)
        {
            float barTop = innerBottom - barHeight;

            // Draw segmented by color thresholds
            float yellowNorm = DbToNormalized(_yellowThresholdDb);
            float redNorm = DbToNormalized(_redThresholdDb);

            float greenTop = innerBottom - innerHeight * Math.Min(current, yellowNorm);
            float yellowTop = innerBottom - innerHeight * Math.Min(current, redNorm);
            float redTop = innerBottom - innerHeight * current;

            // Green section (always at bottom)
            if (current > 0)
            {
                var greenRect = new SKRect(innerLeft, Math.Max(greenTop, barTop), innerRight, innerBottom);
                if (greenRect.Height > 0)
                    canvas.DrawRect(greenRect, _greenPaint);
            }

            // Yellow section
            if (current > yellowNorm)
            {
                var yellowRect = new SKRect(innerLeft, Math.Max(yellowTop, barTop), innerRight, greenTop);
                if (yellowRect.Height > 0)
                    canvas.DrawRect(yellowRect, _yellowPaint);
            }

            // Red section
            if (current > redNorm)
            {
                var redRect = new SKRect(innerLeft, barTop, innerRight, yellowTop);
                if (redRect.Height > 0)
                    canvas.DrawRect(redRect, _redPaint);
            }
        }

        // Peak marker (horizontal line)
        if (peak > current + 0.02f)
        {
            float peakY = innerBottom - innerHeight * Math.Clamp(peak, 0f, 1f);
            canvas.DrawLine(innerLeft, peakY, innerRight, peakY, _peakPaint);
        }
    }

    private void RenderHorizontal(SKCanvas canvas, SKRect rect, float current, float peak)
    {
        float padding = 2f;
        float innerLeft = rect.Left + padding;
        float innerRight = rect.Right - padding;
        float innerTop = rect.Top + padding;
        float innerBottom = rect.Bottom - padding;
        float innerWidth = innerRight - innerLeft;

        // Current level bar (from left to right)
        float barWidth = innerWidth * Math.Clamp(current, 0f, 1f);
        if (barWidth > 1)
        {
            float yellowNorm = DbToNormalized(_yellowThresholdDb);
            float redNorm = DbToNormalized(_redThresholdDb);

            float greenRight = innerLeft + innerWidth * Math.Min(current, yellowNorm);
            float yellowRight = innerLeft + innerWidth * Math.Min(current, redNorm);
            float redRight = innerLeft + innerWidth * current;

            // Green section
            if (current > 0)
            {
                var greenRect = new SKRect(innerLeft, innerTop, greenRight, innerBottom);
                if (greenRect.Width > 0)
                    canvas.DrawRect(greenRect, _greenPaint);
            }

            // Yellow section
            if (current > yellowNorm)
            {
                var yellowRect = new SKRect(greenRight, innerTop, yellowRight, innerBottom);
                if (yellowRect.Width > 0)
                    canvas.DrawRect(yellowRect, _yellowPaint);
            }

            // Red section
            if (current > redNorm)
            {
                var redRect = new SKRect(yellowRight, innerTop, redRight, innerBottom);
                if (redRect.Width > 0)
                    canvas.DrawRect(redRect, _redPaint);
            }
        }

        // Peak marker (vertical line)
        if (peak > current + 0.02f)
        {
            float peakX = innerLeft + innerWidth * Math.Clamp(peak, 0f, 1f);
            canvas.DrawLine(peakX, innerTop, peakX, innerBottom, _peakPaint);
        }
    }

    private float LinearToNormalized(float linear)
    {
        if (linear <= 0) return 0;
        float db = 20f * MathF.Log10(linear);
        return DbToNormalized(db);
    }

    private float DbToNormalized(float db)
    {
        float range = _maxDb - _minDb;
        return Math.Clamp((db - _minDb) / range, 0f, 1f);
    }

    /// <summary>
    /// Resets the meter state.
    /// </summary>
    public void Reset()
    {
        _ballistics.Reset();
    }

    public void Dispose()
    {
        _backgroundPaint.Dispose();
        _greenPaint.Dispose();
        _yellowPaint.Dispose();
        _redPaint.Dispose();
        _peakPaint.Dispose();
        _borderPaint.Dispose();
    }
}
