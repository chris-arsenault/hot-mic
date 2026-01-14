using HotMic.Core.Dsp;
using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Renderer for the Frequency Analyzer plugin window.
/// </summary>
public sealed class FrequencyAnalyzerRenderer : IDisposable
{
    private const float CornerRadius = 10f;
    private const float Padding = 14f;
    private const float TitleBarHeight = 36f;
    private const float ControlBarHeight = 58f;
    private const float SpectrumHeight = 240f;
    private const float KnobRadius = 26f;

    private readonly PluginComponentTheme _theme;
    private readonly RotaryKnob _knob;

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _panelPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _titlePaint;
    private readonly SKPaint _textPaint;
    private readonly SKPaint _mutedTextPaint;
    private readonly SKPaint _gridPaint;
    private readonly SKPaint _spectrumPaint;
    private readonly SKPaint _spectrumFillPaint;
    private readonly SKPaint _buttonPaint;
    private readonly SKPaint _buttonActivePaint;
    private readonly SKPaint _buttonTextPaint;
    private readonly SKPath _spectrumPath = new();
    private readonly SKPath _spectrumFillPath = new();
    private float[] _spectrumXPositions = Array.Empty<float>();
    private float _lastSpectrumWidth;

    private readonly SKPoint[] _knobCenters = new SKPoint[4];
    private SKRect _titleBarRect;
    private SKRect _closeRect;
    private SKRect _bypassRect;
    private SKRect _fftRect;
    private SKRect _binsRect;
    private SKRect _scaleRect;

    public FrequencyAnalyzerRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _knob = new RotaryKnob(_theme);

        _backgroundPaint = new SKPaint { Color = _theme.PanelBackground, IsAntialias = true, Style = SKPaintStyle.Fill };
        _panelPaint = new SKPaint { Color = _theme.PanelBackgroundLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        _borderPaint = new SKPaint { Color = _theme.PanelBorder, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        _titlePaint = new SKPaint
        {
            Color = _theme.TextPrimary,
            IsAntialias = true,
            TextSize = 14f,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };
        _textPaint = new SKPaint
        {
            Color = _theme.TextSecondary,
            IsAntialias = true,
            TextSize = 11f,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };
        _mutedTextPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            TextSize = 10f,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };
        _gridPaint = new SKPaint
        {
            Color = _theme.PanelBorder.WithAlpha(50),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };
        _spectrumPaint = new SKPaint
        {
            Color = _theme.AccentSecondary,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            StrokeJoin = SKStrokeJoin.Round,
            StrokeCap = SKStrokeCap.Round
        };
        _spectrumFillPaint = new SKPaint
        {
            Color = _theme.AccentSecondary.WithAlpha(40),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        _buttonPaint = new SKPaint { Color = _theme.PanelBackgroundLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        _buttonActivePaint = new SKPaint { Color = _theme.KnobArc.WithAlpha(120), IsAntialias = true, Style = SKPaintStyle.Fill };
        _buttonTextPaint = new SKPaint
        {
            Color = _theme.TextPrimary,
            IsAntialias = true,
            TextSize = 10f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };
    }

    public static SKSize GetPreferredSize() => new(760, 520);

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, FrequencyAnalyzerState state)
    {
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        canvas.Scale(dpiScale);
        size = new SKSize(size.Width / dpiScale, size.Height / dpiScale);

        var outerRect = new SKRect(0, 0, size.Width, size.Height);
        canvas.DrawRoundRect(new SKRoundRect(outerRect, CornerRadius), _backgroundPaint);
        canvas.DrawRoundRect(new SKRoundRect(outerRect, CornerRadius), _borderPaint);

        DrawTitleBar(canvas, size, state);
        DrawSpectrum(canvas, size, state);
        DrawControls(canvas, size, state);

        canvas.Restore();
    }

    public FrequencyAnalyzerHitTest HitTest(float x, float y)
    {
        if (_closeRect.Contains(x, y))
        {
            return new FrequencyAnalyzerHitTest(AnalyzerHitArea.CloseButton, -1);
        }

        if (_bypassRect.Contains(x, y))
        {
            return new FrequencyAnalyzerHitTest(AnalyzerHitArea.BypassButton, -1);
        }

        if (_titleBarRect.Contains(x, y))
        {
            return new FrequencyAnalyzerHitTest(AnalyzerHitArea.TitleBar, -1);
        }

        if (_fftRect.Contains(x, y))
        {
            return new FrequencyAnalyzerHitTest(AnalyzerHitArea.FftButton, -1);
        }

        if (_binsRect.Contains(x, y))
        {
            return new FrequencyAnalyzerHitTest(AnalyzerHitArea.BinsButton, -1);
        }

        if (_scaleRect.Contains(x, y))
        {
            return new FrequencyAnalyzerHitTest(AnalyzerHitArea.ScaleButton, -1);
        }

        for (int i = 0; i < _knobCenters.Length; i++)
        {
            if (Distance(_knobCenters[i], x, y) <= KnobRadius)
            {
                return new FrequencyAnalyzerHitTest(AnalyzerHitArea.Knob, i);
            }
        }

        return new FrequencyAnalyzerHitTest(AnalyzerHitArea.None, -1);
    }

    private void DrawTitleBar(SKCanvas canvas, SKSize size, FrequencyAnalyzerState state)
    {
        _titleBarRect = new SKRect(0, 0, size.Width, TitleBarHeight);
        canvas.DrawRect(_titleBarRect, _panelPaint);
        canvas.DrawLine(0, TitleBarHeight, size.Width, TitleBarHeight, _borderPaint);
        canvas.DrawText("Frequency Analyzer", Padding, TitleBarHeight / 2f + 5f, _titlePaint);

        float buttonSize = 18f;
        float right = size.Width - Padding;
        _closeRect = new SKRect(right - buttonSize, TitleBarHeight / 2f - buttonSize / 2f, right, TitleBarHeight / 2f + buttonSize / 2f);
        DrawIconButton(canvas, _closeRect, "X", _theme.TextPrimary);
        right -= buttonSize + 8f;

        _bypassRect = new SKRect(right - 48f, TitleBarHeight / 2f - 9f, right, TitleBarHeight / 2f + 9f);
        DrawPillButton(canvas, _bypassRect, state.IsBypassed ? "BYP" : "ON", state.IsBypassed);
    }

    private void DrawSpectrum(SKCanvas canvas, SKSize size, FrequencyAnalyzerState state)
    {
        float top = TitleBarHeight + Padding;
        var spectrumRect = new SKRect(Padding, top, size.Width - Padding, top + SpectrumHeight);
        canvas.DrawRoundRect(new SKRoundRect(spectrumRect, 6f), _panelPaint);

        DrawSpectrumGrid(canvas, spectrumRect, state);
        DrawSpectrumLine(canvas, spectrumRect, state);

        DrawFrequencyLabels(canvas, spectrumRect, state);
    }

    private void DrawControls(SKCanvas canvas, SKSize size, FrequencyAnalyzerState state)
    {
        float controlTop = TitleBarHeight + Padding + SpectrumHeight + Padding;
        var controlRect = new SKRect(Padding, controlTop, size.Width - Padding, controlTop + ControlBarHeight);
        canvas.DrawRoundRect(new SKRoundRect(controlRect, 6f), _panelPaint);

        float buttonWidth = 84f;
        float buttonHeight = 24f;
        float buttonY = controlRect.Top + 8f;

        _fftRect = new SKRect(controlRect.Left + 10f, buttonY, controlRect.Left + 10f + buttonWidth, buttonY + buttonHeight);
        DrawPillButton(canvas, _fftRect, $"FFT {state.FftSize}", false);

        _binsRect = new SKRect(_fftRect.Right + 10f, buttonY, _fftRect.Right + 10f + buttonWidth, buttonY + buttonHeight);
        DrawPillButton(canvas, _binsRect, $"Bins {state.DisplayBins}", false);

        _scaleRect = new SKRect(_binsRect.Right + 10f, buttonY, _binsRect.Right + 10f + buttonWidth, buttonY + buttonHeight);
        DrawPillButton(canvas, _scaleRect, state.Scale.ToString(), false);

        // Knobs
        float knobRowY = controlRect.Bottom + Padding + KnobRadius;
        float knobSpacing = (size.Width - Padding * 2f) / (_knobCenters.Length + 1);
        for (int i = 0; i < _knobCenters.Length; i++)
        {
            _knobCenters[i] = new SKPoint(Padding + knobSpacing * (i + 1), knobRowY);
        }

        float minFreqNorm = Normalize(state.MinFrequency, 20f, 2000f);
        _knob.Render(canvas, _knobCenters[0], KnobRadius, minFreqNorm, "MIN FREQ", FormatHz(state.MinFrequency), "Hz", state.HoveredKnob == 0);

        float maxFreqNorm = Normalize(state.MaxFrequency, 2000f, 12000f);
        _knob.Render(canvas, _knobCenters[1], KnobRadius, maxFreqNorm, "MAX FREQ", FormatHz(state.MaxFrequency), "Hz", state.HoveredKnob == 1);

        float minDbNorm = Normalize(state.MinDb, -120f, -20f);
        _knob.Render(canvas, _knobCenters[2], KnobRadius, minDbNorm, "MIN dB", $"{state.MinDb:0}", "dB", state.HoveredKnob == 2);

        float maxDbNorm = Normalize(state.MaxDb, -40f, 0f);
        _knob.Render(canvas, _knobCenters[3], KnobRadius, maxDbNorm, "MAX dB", $"{state.MaxDb:0}", "dB", state.HoveredKnob == 3);
    }

    private void DrawSpectrumGrid(SKCanvas canvas, SKRect rect, FrequencyAnalyzerState state)
    {
        int lines = 4;
        for (int i = 1; i < lines; i++)
        {
            float y = rect.Top + rect.Height * i / lines;
            canvas.DrawLine(rect.Left, y, rect.Right, y, _gridPaint);
        }
    }

    private void DrawSpectrumLine(SKCanvas canvas, SKRect rect, FrequencyAnalyzerState state)
    {
        if (state.Spectrum is null || state.Spectrum.Length == 0)
        {
            return;
        }

        int bins = state.Spectrum.Length;
        if (bins <= 1)
        {
            return;
        }

        EnsureSpectrumPositions(rect, bins);
        _spectrumPath.Rewind();
        _spectrumFillPath.Rewind();

        for (int i = 0; i < bins; i++)
        {
            float x = _spectrumXPositions[i];
            float y = rect.Bottom - rect.Height * state.Spectrum[i];
            if (i == 0)
            {
                _spectrumPath.MoveTo(x, y);
                _spectrumFillPath.MoveTo(x, y);
            }
            else
            {
                _spectrumPath.LineTo(x, y);
                _spectrumFillPath.LineTo(x, y);
            }
        }

        _spectrumFillPath.LineTo(rect.Right, rect.Bottom);
        _spectrumFillPath.LineTo(rect.Left, rect.Bottom);
        _spectrumFillPath.Close();
        canvas.DrawPath(_spectrumFillPath, _spectrumFillPaint);
        canvas.DrawPath(_spectrumPath, _spectrumPaint);
    }

    private void DrawFrequencyLabels(SKCanvas canvas, SKRect rect, FrequencyAnalyzerState state)
    {
        float[] labels = { 80f, 100f, 200f, 500f, 1000f, 2000f, 4000f, 8000f };
        foreach (float freq in labels)
        {
            if (freq < state.MinFrequency || freq > state.MaxFrequency)
            {
                continue;
            }

            float norm = FrequencyScaleUtils.Normalize(state.Scale, freq, state.MinFrequency, state.MaxFrequency);
            float x = rect.Left + norm * rect.Width;
            canvas.DrawLine(x, rect.Top, x, rect.Bottom, _gridPaint);
            canvas.DrawText(FormatHz(freq), x + 2f, rect.Bottom + 14f, _mutedTextPaint);
        }
    }

    private void DrawPillButton(SKCanvas canvas, SKRect rect, string label, bool active)
    {
        canvas.DrawRoundRect(new SKRoundRect(rect, rect.Height / 2f), active ? _buttonActivePaint : _buttonPaint);
        canvas.DrawRoundRect(new SKRoundRect(rect, rect.Height / 2f), _borderPaint);
        canvas.DrawText(label, rect.MidX, rect.MidY + 4f, _buttonTextPaint);
    }

    private void DrawIconButton(SKCanvas canvas, SKRect rect, string label, SKColor color)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true, TextSize = 12f, TextAlign = SKTextAlign.Center };
        canvas.DrawText(label, rect.MidX, rect.MidY + 4f, paint);
    }

    private static float Normalize(float value, float min, float max)
    {
        if (max <= min)
        {
            return 0f;
        }
        return Math.Clamp((value - min) / (max - min), 0f, 1f);
    }

    private static string FormatHz(float hz)
    {
        return hz >= 1000f ? $"{hz / 1000f:0.#}k" : $"{hz:0}";
    }

    private static float Distance(SKPoint center, float x, float y)
    {
        float dx = center.X - x;
        float dy = center.Y - y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private void EnsureSpectrumPositions(SKRect rect, int bins)
    {
        if (_spectrumXPositions.Length != bins || _lastSpectrumWidth != rect.Width)
        {
            _spectrumXPositions = new float[bins];
            float step = rect.Width / Math.Max(1, bins - 1);
            for (int i = 0; i < bins; i++)
            {
                _spectrumXPositions[i] = rect.Left + i * step;
            }

            _lastSpectrumWidth = rect.Width;
        }
    }

    public void Dispose()
    {
        _spectrumPath.Dispose();
        _spectrumFillPath.Dispose();
        _knob.Dispose();
        _backgroundPaint.Dispose();
        _panelPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _textPaint.Dispose();
        _mutedTextPaint.Dispose();
        _gridPaint.Dispose();
        _spectrumPaint.Dispose();
        _spectrumFillPaint.Dispose();
        _buttonPaint.Dispose();
        _buttonActivePaint.Dispose();
        _buttonTextPaint.Dispose();
    }
}

public enum AnalyzerHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    FftButton,
    BinsButton,
    ScaleButton,
    Knob
}

public record struct FrequencyAnalyzerHitTest(AnalyzerHitArea Area, int KnobIndex);

public record struct FrequencyAnalyzerState(
    int FftSize,
    int DisplayBins,
    FrequencyScale Scale,
    float MinFrequency,
    float MaxFrequency,
    float MinDb,
    float MaxDb,
    bool IsBypassed,
    int HoveredKnob,
    float[]? Spectrum);
