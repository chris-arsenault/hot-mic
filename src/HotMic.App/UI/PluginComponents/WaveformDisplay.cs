using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Renders a rolling waveform display with threshold line overlay.
/// Shows input level history with gate state visualization.
/// </summary>
public sealed class WaveformDisplay : IDisposable
{
    private readonly PluginComponentTheme _theme;
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _gridPaint;
    private readonly SKPaint _waveformPaint;
    private readonly SKPaint _waveformFillPaint;
    private readonly SKPaint _thresholdPaint;
    private readonly SKPaint _thresholdGlowPaint;
    private readonly SKPaint _gateOpenPaint;
    private readonly SKPaint _gateClosedPaint;
    private readonly SkiaTextPaint _labelPaint;
    private readonly SkiaTextPaint _dbLabelPaint;

    // Pre-allocated arrays for rendering (no allocation during draw)
    private float[] _levelBuffer;
    private bool[] _gateBuffer;
    private SKPoint[] _waveformPoints;

    public WaveformDisplay(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;

        _backgroundPaint = new SKPaint
        {
            Color = _theme.WaveformBackground,
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

        _waveformPaint = new SKPaint
        {
            Color = _theme.WaveformLine,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };

        _waveformFillPaint = new SKPaint
        {
            Color = _theme.WaveformFill,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _thresholdPaint = new SKPaint
        {
            Color = _theme.ThresholdLine,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            PathEffect = SKPathEffect.CreateDash(new[] { 6f, 4f }, 0)
        };

        _thresholdGlowPaint = new SKPaint
        {
            Color = _theme.ThresholdLineGlow,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 6f,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3f)
        };

        _gateOpenPaint = new SKPaint
        {
            Color = _theme.WaveformGateOpen,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _gateClosedPaint = new SKPaint
        {
            Color = _theme.WaveformGateClosed,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _labelPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Normal);
        _dbLabelPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Right);

        _levelBuffer = new float[256];
        _gateBuffer = new bool[256];
        _waveformPoints = new SKPoint[256];
    }

    /// <summary>
    /// Render the waveform display.
    /// </summary>
    /// <param name="canvas">Canvas to draw on</param>
    /// <param name="rect">Bounding rectangle</param>
    /// <param name="waveformBuffer">Source of level data</param>
    /// <param name="thresholdDb">Current threshold in dB (-80 to 0)</param>
    /// <param name="minDb">Minimum dB scale (default -60)</param>
    /// <param name="maxDb">Maximum dB scale (default 0)</param>
    public void Render(
        SKCanvas canvas,
        SKRect rect,
        WaveformBuffer waveformBuffer,
        float thresholdDb,
        float minDb = -60f,
        float maxDb = 0f)
    {
        EnsureBufferSize(waveformBuffer.Capacity);
        waveformBuffer.CopyTo(_levelBuffer, _gateBuffer);

        // Background
        var roundRect = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(roundRect, _backgroundPaint);

        // Draw grid lines at -12, -24, -36, -48 dB
        float dbRange = maxDb - minDb;
        foreach (float db in new[] { -12f, -24f, -36f, -48f })
        {
            if (db < minDb) continue;
            float normalizedDb = (db - minDb) / dbRange;
            float y = rect.Bottom - (rect.Height * normalizedDb);
            canvas.DrawLine(rect.Left, y, rect.Right, y, _gridPaint);
            canvas.DrawText($"{db:0}", rect.Left + 4, y - 2, _dbLabelPaint);
        }

        // Calculate waveform points
        float stepX = rect.Width / (_levelBuffer.Length - 1);
        for (int i = 0; i < _levelBuffer.Length; i++)
        {
            float levelDb = LevelToDb(_levelBuffer[i]);
            float normalizedLevel = Math.Clamp((levelDb - minDb) / dbRange, 0f, 1f);
            float x = rect.Left + (i * stepX);
            float y = rect.Bottom - (rect.Height * normalizedLevel);
            _waveformPoints[i] = new SKPoint(x, y);
        }

        // Draw gate state regions
        DrawGateRegions(canvas, rect, stepX);

        // Draw waveform fill (area under curve)
        using var fillPath = new SKPath();
        fillPath.MoveTo(rect.Left, rect.Bottom);
        for (int i = 0; i < _levelBuffer.Length; i++)
        {
            fillPath.LineTo(_waveformPoints[i]);
        }
        fillPath.LineTo(rect.Right, rect.Bottom);
        fillPath.Close();
        canvas.DrawPath(fillPath, _waveformFillPaint);

        // Draw waveform line
        using var linePath = new SKPath();
        linePath.MoveTo(_waveformPoints[0]);
        for (int i = 1; i < _levelBuffer.Length; i++)
        {
            linePath.LineTo(_waveformPoints[i]);
        }
        canvas.DrawPath(linePath, _waveformPaint);

        // Draw threshold line with glow
        float thresholdNormalized = Math.Clamp((thresholdDb - minDb) / dbRange, 0f, 1f);
        float thresholdY = rect.Bottom - (rect.Height * thresholdNormalized);
        canvas.DrawLine(rect.Left, thresholdY, rect.Right, thresholdY, _thresholdGlowPaint);
        canvas.DrawLine(rect.Left, thresholdY, rect.Right, thresholdY, _thresholdPaint);

        // Draw threshold label
        string thresholdText = $"{thresholdDb:0} dB";
        float labelX = rect.Right - 8;
        float labelY = thresholdY - 4;
        if (labelY < rect.Top + 14) labelY = thresholdY + 14;

        using var labelBgPaint = new SKPaint
        {
            Color = _theme.LabelBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        float textWidth = _labelPaint.MeasureText(thresholdText);
        var labelRect = new SKRect(labelX - textWidth - 6, labelY - 11, labelX, labelY + 3);
        canvas.DrawRoundRect(new SKRoundRect(labelRect, 3f), labelBgPaint);

        _labelPaint.TextAlign = SKTextAlign.Right;
        _labelPaint.Color = _theme.ThresholdLine;
        canvas.DrawText(thresholdText, labelX - 3, labelY, _labelPaint);
        _labelPaint.Color = _theme.TextSecondary;
        _labelPaint.TextAlign = SKTextAlign.Left;

        // Border
        using var borderPaint = new SKPaint
        {
            Color = _theme.PanelBorder,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };
        canvas.DrawRoundRect(roundRect, borderPaint);
    }

    private void DrawGateRegions(SKCanvas canvas, SKRect rect, float stepX)
    {
        // Draw vertical strips for gate open/closed regions
        int regionStart = 0;
        bool regionState = _gateBuffer[0];

        for (int i = 1; i <= _gateBuffer.Length; i++)
        {
            bool currentState = i < _gateBuffer.Length ? _gateBuffer[i] : !regionState;

            if (currentState != regionState || i == _gateBuffer.Length)
            {
                float x1 = rect.Left + (regionStart * stepX);
                float x2 = rect.Left + (i * stepX);
                var regionRect = new SKRect(x1, rect.Top, x2, rect.Bottom);
                canvas.DrawRect(regionRect, regionState ? _gateOpenPaint : _gateClosedPaint);

                regionStart = i;
                regionState = currentState;
            }
        }
    }

    private void EnsureBufferSize(int capacity)
    {
        if (_levelBuffer.Length != capacity)
        {
            _levelBuffer = new float[capacity];
            _gateBuffer = new bool[capacity];
            _waveformPoints = new SKPoint[capacity];
        }
    }

    private static float LevelToDb(float level)
    {
        if (level < 1e-10f) return -100f;
        return 20f * MathF.Log10(level);
    }

    public void Dispose()
    {
        _backgroundPaint.Dispose();
        _gridPaint.Dispose();
        _waveformPaint.Dispose();
        _waveformFillPaint.Dispose();
        _thresholdPaint.Dispose();
        _thresholdGlowPaint.Dispose();
        _gateOpenPaint.Dispose();
        _gateClosedPaint.Dispose();
        _labelPaint.Dispose();
        _dbLabelPaint.Dispose();
    }
}
