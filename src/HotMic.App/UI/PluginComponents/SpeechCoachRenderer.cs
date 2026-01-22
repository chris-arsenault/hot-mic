using System;
using System.Globalization;
using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

public readonly record struct SpeechCoachSummary(
    float WordsPerMinute,
    float ArticulationWpm,
    float PauseRatio,
    float MeanPauseDurationMs,
    float ClarityRatio,
    float MonotoneScore,
    float IntelligibilityScore,
    float BandLowRatio,
    float BandMidRatio,
    float BandPresenceRatio,
    float BandHighRatio,
    float PauseMicroCount,
    float PauseShortCount,
    float PauseMediumCount,
    float PauseLongCount);

public readonly record struct SpeechCoachState(
    long LatestFrameId,
    int AvailableFrames,
    int FrameCapacity,
    float[] PitchTrack,
    float[] PitchConfidence,
    byte[] VoicingStates,
    float[] WaveformMin,
    float[] WaveformMax,
    byte[] SpeakingState,
    byte[] SyllableMarkers,
    byte[] EmphasisMarkers,
    float[] WordsPerMinute,
    float[] ArticulationWpm,
    float[] PauseRatio,
    float PitchMedianHz,
    SpeechCoachSummary Summary);

/// <summary>
/// Renderer for the standalone Speech Coach window.
/// Displays real-time speech metrics and visualization.
/// </summary>
public sealed class SpeechCoachRenderer : IDisposable
{
    private const float Padding = 12f;
    private const float TitleBarHeight = 36f;
    private const float MetricCardHeight = 64f;
    private const float MetricCardSpacing = 8f;

    private readonly PluginComponentTheme _theme;
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _cardPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _meterBackgroundPaint;
    private readonly SKPaint _goodPaint;
    private readonly SKPaint _warningPaint;
    private readonly SKPaint _speakingPaint;
    private readonly SKPaint _silentPausePaint;
    private readonly SKPaint _filledPausePaint;
    private readonly SKPaint _syllableMarkerPaint;
    private readonly SKPaint _emphasisMarkerPaint;
    private readonly SKPaint _pitchLinePaint;
    private readonly SKPaint _pitchGridPaint;
    private readonly SKPaint _energyFillPaint;
    private readonly SKPaint _energyLinePaint;
    private readonly SKPaint _wpmLinePaint;
    private readonly SKPaint _spectralLowPaint;
    private readonly SKPaint _spectralMidPaint;
    private readonly SKPaint _spectralPresencePaint;
    private readonly SKPaint _spectralHighPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _cardLabelPaint;
    private readonly SkiaTextPaint _cardValuePaint;
    private readonly SkiaTextPaint _cardUnitPaint;
    private readonly SkiaTextPaint _smallLabelPaint;
    private readonly SkiaTextPaint _smallValuePaint;

    public SKRect CloseButtonRect { get; private set; }

    public SpeechCoachRenderer(PluginComponentTheme theme)
    {
        _theme = theme;

        _backgroundPaint = new SKPaint
        {
            Color = theme.PanelBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _titleBarPaint = new SKPaint
        {
            Color = theme.PanelBackgroundLight,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _cardPaint = new SKPaint
        {
            Color = theme.PanelBackgroundLight,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _borderPaint = new SKPaint
        {
            Color = theme.PanelBorder,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };

        _meterBackgroundPaint = new SKPaint
        {
            Color = theme.MeterBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _goodPaint = new SKPaint
        {
            Color = new SKColor(0x4C, 0xC9, 0x4C),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _warningPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0xA5, 0x00),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _speakingPaint = new SKPaint
        {
            Color = theme.WaveformGateOpen,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _silentPausePaint = new SKPaint
        {
            Color = theme.WaveformGateClosed,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _filledPausePaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0xA5, 0x00, 0x40),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _syllableMarkerPaint = new SKPaint
        {
            Color = theme.AccentSecondary,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f
        };

        _emphasisMarkerPaint = new SKPaint
        {
            Color = theme.ThresholdLine,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f
        };

        _pitchLinePaint = new SKPaint
        {
            Color = theme.AccentSecondary,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f
        };

        _pitchGridPaint = new SKPaint
        {
            Color = theme.EnvelopeGrid,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };

        _energyFillPaint = new SKPaint
        {
            Color = theme.WaveformFill,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _energyLinePaint = new SKPaint
        {
            Color = theme.WaveformLine,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f
        };

        _wpmLinePaint = new SKPaint
        {
            Color = theme.ThresholdLine,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f
        };

        _spectralLowPaint = new SKPaint
        {
            Color = theme.KnobArc,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _spectralMidPaint = new SKPaint
        {
            Color = theme.AccentSecondary,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _spectralPresencePaint = new SKPaint
        {
            Color = theme.WaveformLine,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _spectralHighPaint = new SKPaint
        {
            Color = theme.TextMuted,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _titlePaint = new SkiaTextPaint(theme.TextPrimary, 14f, SKFontStyle.Bold, SKTextAlign.Left);
        _cardLabelPaint = new SkiaTextPaint(theme.TextSecondary, 10f, SKFontStyle.Normal, SKTextAlign.Left);
        _cardValuePaint = new SkiaTextPaint(theme.TextPrimary, 22f, SKFontStyle.Bold, SKTextAlign.Left);
        _cardUnitPaint = new SkiaTextPaint(theme.TextMuted, 10f, SKFontStyle.Normal, SKTextAlign.Left);
        _smallLabelPaint = new SkiaTextPaint(theme.TextSecondary, 9f, SKFontStyle.Normal, SKTextAlign.Left);
        _smallValuePaint = new SkiaTextPaint(theme.TextPrimary, 11f, SKFontStyle.Bold, SKTextAlign.Left);
    }

    public void Render(SKCanvas canvas, int width, int height, in SpeechCoachState state)
    {
        canvas.Clear(_theme.PanelBackground);

        DrawTitleBar(canvas, width);

        float y = TitleBarHeight + Padding;
        float cardWidth = (width - Padding * 3) / 2f;

        DrawMetricCard(canvas, Padding, y, cardWidth, "Speaking Rate", state.Summary.WordsPerMinute, "WPM", 120f, 180f, true);
        DrawMetricCard(canvas, Padding * 2 + cardWidth, y, cardWidth, "Articulation", state.Summary.ArticulationWpm, "WPM", 140f, 210f, true);

        y += MetricCardHeight + MetricCardSpacing;

        DrawMetricCard(canvas, Padding, y, cardWidth, "Pause Ratio", state.Summary.PauseRatio * 100f, "%", 0f, 35f, false);
        float variety = Math.Clamp((1f - state.Summary.MonotoneScore) * 100f, 0f, 100f);
        DrawMetricCard(canvas, Padding * 2 + cardWidth, y, cardWidth, "Pitch Variety", variety, "%", 40f, 100f, true);

        y += MetricCardHeight + MetricCardSpacing;

        float remainingHeight = height - y - Padding;
        float spectralHeight = Math.Clamp(remainingHeight * 0.28f, 70f, 120f);
        float prosodyHeight = MathF.Max(120f, remainingHeight - spectralHeight - Padding);

        DrawProsodyTimeline(canvas, Padding, y, width - Padding * 2, prosodyHeight, state);
        y += prosodyHeight + Padding;
        DrawSpectralBalance(canvas, Padding, y, width - Padding * 2, spectralHeight, state.Summary);
    }

    private void DrawTitleBar(SKCanvas canvas, int width)
    {
        canvas.DrawRect(0, 0, width, TitleBarHeight, _titleBarPaint);
        _titlePaint.DrawText(canvas, "Speech Coach", Padding, TitleBarHeight / 2 + 5);

        float btnSize = 22f;
        float btnX = width - Padding - btnSize;
        float btnY = (TitleBarHeight - btnSize) / 2f;
        CloseButtonRect = new SKRect(btnX, btnY, btnX + btnSize, btnY + btnSize);

        using var closePaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            StrokeCap = SKStrokeCap.Round
        };

        float cx = CloseButtonRect.MidX;
        float cy = CloseButtonRect.MidY;
        float s = 5f;
        canvas.DrawLine(cx - s, cy - s, cx + s, cy + s, closePaint);
        canvas.DrawLine(cx + s, cy - s, cx - s, cy + s, closePaint);
    }

    private void DrawMetricCard(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        string label,
        float value,
        string unit,
        float goodMin,
        float goodMax,
        bool higherIsBetter)
    {
        var rect = new SKRect(x, y, x + width, y + MetricCardHeight);
        var roundRect = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(roundRect, _cardPaint);
        canvas.DrawRoundRect(roundRect, _borderPaint);

        _cardLabelPaint.DrawText(canvas, label, x + 10f, y + 18f);

        string valueStr = float.IsNaN(value) || float.IsInfinity(value) ? "--" : $"{value:0.0}";
        _cardValuePaint.DrawText(canvas, valueStr, x + 10f, y + 44f);

        float valueWidth = _cardValuePaint.MeasureText(valueStr);
        _cardUnitPaint.DrawText(canvas, unit, x + 14f + valueWidth, y + 44f);

        float meterY = y + MetricCardHeight - 12f;
        float meterHeight = 6f;
        float meterWidth = width - 20f;

        var meterRect = new SKRect(x + 10f, meterY, x + 10f + meterWidth, meterY + meterHeight);
        var meterRoundRect = new SKRoundRect(meterRect, 3f);
        canvas.DrawRoundRect(meterRoundRect, _meterBackgroundPaint);

        bool isGood = higherIsBetter
            ? (value >= goodMin && value <= goodMax)
            : (value <= goodMax);

        var fillPaint = isGood ? _goodPaint : _warningPaint;

        float normalizedValue = Math.Clamp(value / Math.Max(1f, goodMax), 0f, 1f);
        float fillWidth = meterWidth * normalizedValue;

        var fillRect = new SKRect(x + 10f, meterY, x + 10f + fillWidth, meterY + meterHeight);
        var fillRoundRect = new SKRoundRect(fillRect, 3f);
        canvas.DrawRoundRect(fillRoundRect, fillPaint);
    }

    private void DrawProsodyTimeline(SKCanvas canvas, float x, float y, float width, float height, in SpeechCoachState state)
    {
        if (height <= 0f)
        {
            return;
        }

        _cardLabelPaint.DrawText(canvas, "Prosody Timeline", x, y + 12f);

        float contentTop = y + 18f;
        float contentHeight = height - 18f;
        float pitchHeight = contentHeight * 0.58f;
        float energyHeight = contentHeight - pitchHeight - 8f;

        var pitchRect = new SKRect(x, contentTop, x + width, contentTop + pitchHeight);
        var energyRect = new SKRect(x, pitchRect.Bottom + 8f, x + width, pitchRect.Bottom + 8f + energyHeight);

        DrawPitchContour(canvas, pitchRect, state);
        DrawEnergyAndPace(canvas, energyRect, state);
    }

    private void DrawPitchContour(SKCanvas canvas, SKRect rect, in SpeechCoachState state)
    {
        canvas.DrawRoundRect(new SKRoundRect(rect, 4f), _meterBackgroundPaint);

        float centerY = rect.MidY;
        canvas.DrawLine(rect.Left, centerY, rect.Right, centerY, _pitchGridPaint);
        float gridStep = rect.Height * 0.25f;
        canvas.DrawLine(rect.Left, centerY - gridStep, rect.Right, centerY - gridStep, _pitchGridPaint);
        canvas.DrawLine(rect.Left, centerY + gridStep, rect.Right, centerY + gridStep, _pitchGridPaint);

        _smallLabelPaint.DrawText(canvas, "Pitch", rect.Left + 6f, rect.Top + 12f);

        int available = state.AvailableFrames;
        if (available <= 1 || state.PitchTrack.Length == 0 || state.FrameCapacity <= 0 || state.LatestFrameId < 0)
        {
            canvas.DrawRoundRect(new SKRoundRect(rect, 4f), _borderPaint);
            return;
        }

        float medianHz = state.PitchMedianHz;
        if (medianHz <= 0f)
        {
            canvas.DrawRoundRect(new SKRoundRect(rect, 4f), _borderPaint);
            return;
        }

        float rangeSemitones = 12f;
        float stepX = rect.Width / Math.Max(1, available - 1);
        long oldestFrameId = state.LatestFrameId - available + 1;

        using var path = new SKPath();
        bool hasSegment = false;

        for (int i = 0; i < available; i++)
        {
            long frameId = oldestFrameId + i;
            if (!TryGetRingIndex(frameId, state.FrameCapacity, out int index))
            {
                continue;
            }

            if (index >= state.PitchTrack.Length || index >= state.VoicingStates.Length)
            {
                continue;
            }

            float pitchHz = state.PitchTrack[index];
            bool voiced = state.VoicingStates[index] == 2;
            if (!voiced || pitchHz <= 0f)
            {
                if (hasSegment)
                {
                    canvas.DrawPath(path, _pitchLinePaint);
                    path.Reset();
                    hasSegment = false;
                }
                continue;
            }

            float semitone = 12f * MathF.Log2(pitchHz / medianHz);
            semitone = Math.Clamp(semitone, -rangeSemitones, rangeSemitones);
            float norm = semitone / rangeSemitones;
            float y = rect.MidY - norm * (rect.Height * 0.45f);
            float xPos = rect.Left + i * stepX;

            if (!hasSegment)
            {
                path.MoveTo(xPos, y);
                hasSegment = true;
            }
            else
            {
                path.LineTo(xPos, y);
            }
        }

        if (hasSegment)
        {
            canvas.DrawPath(path, _pitchLinePaint);
        }

        canvas.DrawRoundRect(new SKRoundRect(rect, 4f), _borderPaint);
    }

    private void DrawEnergyAndPace(SKCanvas canvas, SKRect rect, in SpeechCoachState state)
    {
        canvas.DrawRoundRect(new SKRoundRect(rect, 4f), _meterBackgroundPaint);
        _smallLabelPaint.DrawText(canvas, "Energy + Pace", rect.Left + 6f, rect.Top + 12f);

        int available = state.AvailableFrames;
        if (available <= 1 || state.FrameCapacity <= 0 || state.LatestFrameId < 0)
        {
            canvas.DrawRoundRect(new SKRoundRect(rect, 4f), _borderPaint);
            return;
        }

        float stepX = rect.Width / Math.Max(1, available - 1);
        long oldestFrameId = state.LatestFrameId - available + 1;

        for (int i = 0; i < available; i++)
        {
            long frameId = oldestFrameId + i;
            if (!TryGetRingIndex(frameId, state.FrameCapacity, out int index))
            {
                continue;
            }

            if (index >= state.SpeakingState.Length)
            {
                continue;
            }

            byte speakingState = state.SpeakingState[index];
            SKPaint? fill = speakingState switch
            {
                0 => _speakingPaint,
                1 => _silentPausePaint,
                2 => _filledPausePaint,
                _ => null
            };

            if (fill != null)
            {
                float frameX = rect.Left + i * stepX;
                float frameWidth = Math.Max(1f, stepX);
                canvas.DrawRect(frameX, rect.Top, frameWidth, rect.Height, fill);
            }
        }

        using var energyPath = new SKPath();
        energyPath.MoveTo(rect.Left, rect.Bottom);
        for (int i = 0; i < available; i++)
        {
            long frameId = oldestFrameId + i;
            if (!TryGetRingIndex(frameId, state.FrameCapacity, out int index))
            {
                continue;
            }

            if (index >= state.WaveformMin.Length || index >= state.WaveformMax.Length)
            {
                continue;
            }

            float amplitude = MathF.Max(MathF.Abs(state.WaveformMin[index]), MathF.Abs(state.WaveformMax[index]));
            float energy = MathF.Sqrt(Math.Clamp(amplitude, 0f, 1f));
            float y = rect.Bottom - energy * rect.Height;
            float xPos = rect.Left + i * stepX;
            energyPath.LineTo(xPos, y);
        }
        energyPath.LineTo(rect.Right, rect.Bottom);
        energyPath.Close();
        canvas.DrawPath(energyPath, _energyFillPaint);
        canvas.DrawPath(energyPath, _energyLinePaint);

        using var pacePath = new SKPath();
        bool paceStarted = false;
        const float wpmMin = 90f;
        const float wpmMax = 200f;
        for (int i = 0; i < available; i++)
        {
            long frameId = oldestFrameId + i;
            if (!TryGetRingIndex(frameId, state.FrameCapacity, out int index))
            {
                continue;
            }

            if (index >= state.WordsPerMinute.Length)
            {
                continue;
            }

            float wpm = state.WordsPerMinute[index];
            float t = Math.Clamp((wpm - wpmMin) / (wpmMax - wpmMin), 0f, 1f);
            float y = rect.Bottom - t * rect.Height;
            float xPos = rect.Left + i * stepX;

            if (!paceStarted)
            {
                pacePath.MoveTo(xPos, y);
                paceStarted = true;
            }
            else
            {
                pacePath.LineTo(xPos, y);
            }
        }

        if (paceStarted)
        {
            canvas.DrawPath(pacePath, _wpmLinePaint);
        }

        for (int i = 0; i < available; i++)
        {
            long frameId = oldestFrameId + i;
            if (!TryGetRingIndex(frameId, state.FrameCapacity, out int index))
            {
                continue;
            }

            float xPos = rect.Left + i * stepX;
            if (index < state.SyllableMarkers.Length && state.SyllableMarkers[index] != 0)
            {
                canvas.DrawLine(xPos, rect.Top + 4f, xPos, rect.Bottom - 4f, _syllableMarkerPaint);
            }

            if (index < state.EmphasisMarkers.Length && state.EmphasisMarkers[index] != 0)
            {
                canvas.DrawLine(xPos, rect.Top + 2f, xPos, rect.Top + 8f, _emphasisMarkerPaint);
            }
        }

        string wpmText = float.IsNaN(state.Summary.WordsPerMinute) ? "--" : $"{state.Summary.WordsPerMinute:0} WPM";
        _smallValuePaint.DrawText(canvas, wpmText, rect.Right - 70f, rect.Top + 12f);
        string pauseText = float.IsNaN(state.Summary.MeanPauseDurationMs) ? "--" : $"{state.Summary.MeanPauseDurationMs:0} ms";
        _smallLabelPaint.DrawText(canvas, "Mean Pause", rect.Right - 70f, rect.Top + 26f);
        _smallValuePaint.DrawText(canvas, pauseText, rect.Right - 70f, rect.Top + 38f);

        canvas.DrawRoundRect(new SKRoundRect(rect, 4f), _borderPaint);
    }

    private void DrawSpectralBalance(SKCanvas canvas, float x, float y, float width, float height, in SpeechCoachSummary summary)
    {
        if (height <= 0f)
        {
            return;
        }

        var rect = new SKRect(x, y, x + width, y + height);
        canvas.DrawRoundRect(new SKRoundRect(rect, 6f), _cardPaint);
        canvas.DrawRoundRect(new SKRoundRect(rect, 6f), _borderPaint);

        _cardLabelPaint.DrawText(canvas, "Spectral Balance", rect.Left + 10f, rect.Top + 16f);

        string pauseSummary = $"m {summary.PauseMicroCount:0}  s {summary.PauseShortCount:0}  md {summary.PauseMediumCount:0}  l {summary.PauseLongCount:0}";
        _smallLabelPaint.DrawText(canvas, "Pause bins", rect.Left + 10f, rect.Top + 30f);
        _smallValuePaint.DrawText(canvas, pauseSummary, rect.Left + 78f, rect.Top + 30f);

        string clarityText = float.IsNaN(summary.ClarityRatio) ? "--" : summary.ClarityRatio.ToString("0.00", CultureInfo.InvariantCulture);
        _smallLabelPaint.DrawText(canvas, "Clarity", rect.Right - 110f, rect.Top + 16f);
        _smallValuePaint.DrawText(canvas, clarityText, rect.Right - 60f, rect.Top + 16f);

        string intelText = float.IsNaN(summary.IntelligibilityScore) ? "--" : summary.IntelligibilityScore.ToString("0", CultureInfo.InvariantCulture);
        _smallLabelPaint.DrawText(canvas, "Intel", rect.Right - 110f, rect.Top + 30f);
        _smallValuePaint.DrawText(canvas, intelText, rect.Right - 60f, rect.Top + 30f);

        float barTop = rect.Top + 44f;
        float barHeight = rect.Height - 56f;
        float barSpacing = 10f;
        float barWidth = (rect.Width - 20f - barSpacing * 3) / 4f;

        DrawSpectralBar(canvas, rect.Left + 10f, barTop, barWidth, barHeight, summary.BandLowRatio, _spectralLowPaint, "Low");
        DrawSpectralBar(canvas, rect.Left + 10f + (barWidth + barSpacing) * 1, barTop, barWidth, barHeight, summary.BandMidRatio, _spectralMidPaint, "Mid");
        DrawSpectralBar(canvas, rect.Left + 10f + (barWidth + barSpacing) * 2, barTop, barWidth, barHeight, summary.BandPresenceRatio, _spectralPresencePaint, "Pres");
        DrawSpectralBar(canvas, rect.Left + 10f + (barWidth + barSpacing) * 3, barTop, barWidth, barHeight, summary.BandHighRatio, _spectralHighPaint, "High");
    }

    private void DrawSpectralBar(SKCanvas canvas, float x, float y, float width, float height, float ratio, SKPaint paint, string label)
    {
        float clamped = Math.Clamp(ratio, 0f, 1f);
        float barHeight = height * clamped;
        var rect = new SKRect(x, y + height - barHeight, x + width, y + height);
        canvas.DrawRect(rect, paint);
        _smallLabelPaint.DrawText(canvas, label, x, y + height + 10f);
    }

    private static bool TryGetRingIndex(long frameId, int capacity, out int index)
    {
        if (capacity <= 0)
        {
            index = 0;
            return false;
        }

        index = (int)(frameId % capacity);
        if (index < 0)
        {
            index += capacity;
        }

        return true;
    }

    public void Dispose()
    {
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _cardPaint.Dispose();
        _borderPaint.Dispose();
        _meterBackgroundPaint.Dispose();
        _goodPaint.Dispose();
        _warningPaint.Dispose();
        _speakingPaint.Dispose();
        _silentPausePaint.Dispose();
        _filledPausePaint.Dispose();
        _syllableMarkerPaint.Dispose();
        _emphasisMarkerPaint.Dispose();
        _pitchLinePaint.Dispose();
        _pitchGridPaint.Dispose();
        _energyFillPaint.Dispose();
        _energyLinePaint.Dispose();
        _wpmLinePaint.Dispose();
        _spectralLowPaint.Dispose();
        _spectralMidPaint.Dispose();
        _spectralPresencePaint.Dispose();
        _spectralHighPaint.Dispose();
        _titlePaint.Dispose();
        _cardLabelPaint.Dispose();
        _cardValuePaint.Dispose();
        _cardUnitPaint.Dispose();
        _smallLabelPaint.Dispose();
        _smallValuePaint.Dispose();
    }
}
