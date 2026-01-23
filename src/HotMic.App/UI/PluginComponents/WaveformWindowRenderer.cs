using System;
using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Renderer for the standalone Waveform window.
/// Displays rolling amplitude envelope from analysis data.
/// </summary>
public sealed class WaveformWindowRenderer : IDisposable
{
    private const float Padding = 12f;
    private const float TitleBarHeight = 40f;
    private const float ControlPanelHeight = 60f;
    private const float KnobRadius = 16f;
    private const float AxisWidth = 40f;

    private readonly PluginComponentTheme _theme;
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _waveformAreaPaint;
    private readonly SKPaint _gridPaint;
    private readonly SKPaint _waveformPaint;
    private readonly SKPaint _waveformFillPaint;
    private readonly SKPaint _borderPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _closeButtonPaint;
    private readonly SkiaTextPaint _dbLabelPaint;

    private readonly SKPath _waveformPath = new();
    private readonly SKPath _fillPath = new();

    // Knobs
    public KnobWidget MinDbKnob { get; }
    public KnobWidget MaxDbKnob { get; }
    public KnobWidget TimeKnob { get; }

    public IReadOnlyList<KnobWidget> AllKnobs { get; }

    public SKRect CloseButtonRect { get; private set; }

    public WaveformWindowRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;

        _backgroundPaint = new SKPaint
        {
            Color = _theme.PanelBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _waveformAreaPaint = new SKPaint
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

        _borderPaint = new SKPaint
        {
            Color = _theme.PanelBorder,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };

        _titlePaint = new SkiaTextPaint(_theme.TextPrimary, 14f, SKFontStyle.Bold, SKTextAlign.Left);
        _closeButtonPaint = new SkiaTextPaint(_theme.TextSecondary, 18f, SKFontStyle.Normal, SKTextAlign.Center);
        _dbLabelPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Right);

        // Create knobs
        MinDbKnob = new KnobWidget(KnobRadius, -80f, -20f, "Floor", "dB", theme: _theme)
        {
            Value = -60f,
            ValueFormat = "0"
        };

        MaxDbKnob = new KnobWidget(KnobRadius, -20f, 6f, "Ceiling", "dB", theme: _theme)
        {
            Value = 0f,
            ValueFormat = "0"
        };

        TimeKnob = new KnobWidget(KnobRadius, 1f, 60f, "Time", "s", theme: _theme)
        {
            Value = 5f,
            IsLogarithmic = true,
            ValueFormat = "0.0"
        };

        AllKnobs = new[] { MinDbKnob, MaxDbKnob, TimeKnob };
    }

    public void Render(
        SKCanvas canvas,
        int width,
        int height,
        float[] waveformMin,
        float[] waveformMax,
        int availableFrames,
        float minDb,
        float maxDb)
    {
        canvas.Clear(_theme.PanelBackground);

        // Title bar
        DrawTitleBar(canvas, width);

        // Control panel at bottom
        float controlY = height - ControlPanelHeight;
        DrawControlPanel(canvas, width, controlY);

        // Waveform area
        float waveformTop = TitleBarHeight + Padding;
        float waveformBottom = controlY - Padding;
        float waveformLeft = Padding + AxisWidth;
        float waveformRight = width - Padding;

        var waveformRect = new SKRect(waveformLeft, waveformTop, waveformRight, waveformBottom);
        var roundRect = new SKRoundRect(waveformRect, 6f);
        canvas.DrawRoundRect(roundRect, _waveformAreaPaint);
        canvas.DrawRoundRect(roundRect, _borderPaint);

        // Draw dB grid
        DrawDbGrid(canvas, waveformRect, minDb, maxDb);

        // Draw waveform
        if (availableFrames > 0 && waveformMin.Length > 0)
        {
            DrawWaveform(canvas, waveformRect, waveformMin, waveformMax, availableFrames, minDb, maxDb);
        }
    }

    private void DrawTitleBar(SKCanvas canvas, int width)
    {
        using var panelPaint = new SKPaint
        {
            Color = _theme.PanelBackgroundLight,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawRect(0, 0, width, TitleBarHeight, panelPaint);

        _titlePaint.DrawText(canvas, "Waveform", Padding, TitleBarHeight / 2 + 5);

        // Close button
        float btnSize = 24f;
        float btnX = width - Padding - btnSize;
        float btnY = (TitleBarHeight - btnSize) / 2;
        CloseButtonRect = new SKRect(btnX, btnY, btnX + btnSize, btnY + btnSize);
        canvas.DrawText("\u00D7", CloseButtonRect.MidX, CloseButtonRect.MidY + 6, _closeButtonPaint);
    }

    private void DrawControlPanel(SKCanvas canvas, int width, float y)
    {
        using var panelPaint = new SKPaint
        {
            Color = _theme.PanelBackgroundLight,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawRect(0, y, width, ControlPanelHeight, panelPaint);

        // Position knobs
        float knobY = y + ControlPanelHeight / 2;
        float knobSpacing = (width - 2 * Padding) / 3;

        MinDbKnob.Center = new SKPoint(Padding + knobSpacing * 0.5f, knobY);
        MaxDbKnob.Center = new SKPoint(Padding + knobSpacing * 1.5f, knobY);
        TimeKnob.Center = new SKPoint(Padding + knobSpacing * 2.5f, knobY);

        foreach (var knob in AllKnobs)
        {
            knob.Render(canvas);
        }
    }

    private void DrawDbGrid(SKCanvas canvas, SKRect rect, float minDb, float maxDb)
    {
        float dbRange = maxDb - minDb;

        // Draw grid lines at standard dB levels
        foreach (float db in new[] { 0f, -6f, -12f, -18f, -24f, -30f, -36f, -42f, -48f, -54f, -60f })
        {
            if (db < minDb || db > maxDb) continue;

            float normalizedDb = (db - minDb) / dbRange;
            float y = rect.Bottom - (rect.Height * normalizedDb);

            canvas.DrawLine(rect.Left, y, rect.Right, y, _gridPaint);

            // Label on the left axis
            float labelX = rect.Left - 4;
            _dbLabelPaint.DrawText(canvas, $"{db:0}", labelX, y + 3);
        }
    }

    private void DrawWaveform(
        SKCanvas canvas,
        SKRect rect,
        float[] waveformMin,
        float[] waveformMax,
        int availableFrames,
        float minDb,
        float maxDb)
    {
        if (availableFrames <= 1) return;

        float dbRange = maxDb - minDb;
        int frameCount = Math.Min(availableFrames, waveformMin.Length);
        float stepX = rect.Width / (frameCount - 1);

        _waveformPath.Reset();
        _fillPath.Reset();

        bool started = false;
        float lastY = rect.Bottom;

        for (int i = 0; i < frameCount; i++)
        {
            // Convert linear amplitude to dB
            float maxAmp = waveformMax[i];
            float db = maxAmp > 0 ? 20f * MathF.Log10(maxAmp) : minDb;
            float normalizedDb = Math.Clamp((db - minDb) / dbRange, 0f, 1f);

            float x = rect.Left + (i * stepX);
            float y = rect.Bottom - (rect.Height * normalizedDb);

            if (!started)
            {
                _waveformPath.MoveTo(x, y);
                _fillPath.MoveTo(x, rect.Bottom);
                _fillPath.LineTo(x, y);
                started = true;
            }
            else
            {
                _waveformPath.LineTo(x, y);
                _fillPath.LineTo(x, y);
            }

            lastY = y;
        }

        // Close fill path
        _fillPath.LineTo(rect.Right, rect.Bottom);
        _fillPath.Close();

        // Draw fill and stroke
        canvas.Save();
        canvas.ClipRect(rect);
        canvas.DrawPath(_fillPath, _waveformFillPaint);
        canvas.DrawPath(_waveformPath, _waveformPaint);
        canvas.Restore();
    }

    public void Dispose()
    {
        _backgroundPaint.Dispose();
        _waveformAreaPaint.Dispose();
        _gridPaint.Dispose();
        _waveformPaint.Dispose();
        _waveformFillPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _dbLabelPaint.Dispose();
        _waveformPath.Dispose();
        _fillPath.Dispose();

        foreach (var knob in AllKnobs)
        {
            knob.Dispose();
        }
    }
}
