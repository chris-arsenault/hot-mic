using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Renders a high-pass filter plugin UI with filter curve visualization and slope selector.
/// </summary>
public sealed class HighPassFilterRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 16f;
    private const float KnobRadius = 40f;
    private const float MeterWidth = 20f;
    private const float MeterHeight = 100f;
    private const float CurveHeight = 80f;
    private const float CornerRadius = 10f;
    private static readonly float[] CutoffDash = [4f, 4f];

    private readonly PluginComponentTheme _theme;
    private readonly PluginPresetBar _presetBar;
    private readonly LevelMeter _inputMeter;
    private readonly LevelMeter _outputMeter;

    public KnobWidget CutoffKnob { get; }

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _meterBackgroundPaint;
    private readonly SKPaint _meterFillPaint;
    private readonly SKPaint _curveBackgroundPaint;
    private readonly SKPaint _curvePaint;
    private readonly SKPaint _curveAreaPaint;
    private readonly SKPaint _cutoffLinePaint;
    private readonly SKPaint _gridPaint;
    private readonly SkiaTextPaint _labelPaint;
    private readonly SkiaTextPaint _freqLabelPaint;
    private readonly SKPaint _slopeButtonPaint;
    private readonly SKPaint _slopeButtonActivePaint;
    private readonly SkiaTextPaint _latencyPaint;
    private readonly SKPaint _spectrumPaint;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;
    private SKRect _slope12ButtonRect;
    private SKRect _slope18ButtonRect;

    public HighPassFilterRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _presetBar = new PluginPresetBar(_theme);
        _inputMeter = new LevelMeter();
        _outputMeter = new LevelMeter();

        var knobStyle = KnobStyle.Standard;
        CutoffKnob = new KnobWidget(KnobRadius, 40f, 200f, "CUTOFF", "Hz", knobStyle, _theme) { IsLogarithmic = true, ValueFormat = "0" };

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

        _titlePaint = new SkiaTextPaint(_theme.TextPrimary, 14f, SKFontStyle.Bold);
        _closeButtonPaint = new SkiaTextPaint(_theme.TextSecondary, 18f, SKFontStyle.Normal, SKTextAlign.Center);

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

        _meterBackgroundPaint = new SKPaint
        {
            Color = _theme.WaveformBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _meterFillPaint = new SKPaint
        {
            Color = _theme.WaveformLine,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _curveBackgroundPaint = new SKPaint
        {
            Color = _theme.WaveformBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _curvePaint = new SKPaint
        {
            Color = _theme.KnobArc,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f
        };

        _curveAreaPaint = new SKPaint
        {
            Color = _theme.KnobArc.WithAlpha(40),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _cutoffLinePaint = new SKPaint
        {
            Color = _theme.KnobArc,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            PathEffect = SKPathEffect.CreateDash(CutoffDash, 0)
        };

        _gridPaint = new SKPaint
        {
            Color = _theme.PanelBorder.WithAlpha(80),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 0.5f
        };

        _labelPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Normal, SKTextAlign.Center);
        _freqLabelPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Center);

        _slopeButtonPaint = new SKPaint
        {
            Color = _theme.PanelBackgroundLight,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _slopeButtonActivePaint = new SKPaint
        {
            Color = _theme.KnobArc,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _latencyPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Right);

        _spectrumPaint = new SKPaint
        {
            Color = new SKColor(0x60, 0xA0, 0xFF, 0x60),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
    }

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, HighPassFilterState state)
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
        canvas.DrawText("High-Pass Filter", Padding, TitleBarHeight / 2f + 5, _titlePaint);

        // Preset bar
        float presetBarX = Padding + 115;
        float presetBarY = (TitleBarHeight - PluginPresetBar.TotalHeight) / 2f;
        _presetBar.Render(canvas, presetBarX, presetBarY, state.PresetName);

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

        using var bypassTextPaint = new SkiaTextPaint(state.IsBypassed ? _theme.TextPrimary : _theme.TextSecondary, 10f, SKFontStyle.Bold, SKTextAlign.Center);
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

        // Filter curve display
        var curveRect = new SKRect(Padding, contentTop, size.Width - Padding, contentTop + CurveHeight);
        DrawFilterCurve(canvas, curveRect, state);

        // Meters
        float meterY = curveRect.Bottom + 16;
        var inputMeterRect = new SKRect(Padding, meterY, Padding + MeterWidth, meterY + MeterHeight);
        _inputMeter.Update(state.InputLevel);
        _inputMeter.Render(canvas, inputMeterRect, MeterOrientation.Vertical);
        canvas.DrawText("IN", inputMeterRect.MidX, inputMeterRect.Bottom + 12, _labelPaint);

        var outputMeterRect = new SKRect(inputMeterRect.Right + 8, meterY, inputMeterRect.Right + 8 + MeterWidth, meterY + MeterHeight);
        _outputMeter.Update(state.OutputLevel);
        _outputMeter.Render(canvas, outputMeterRect, MeterOrientation.Vertical);
        canvas.DrawText("OUT", outputMeterRect.MidX, outputMeterRect.Bottom + 12, _labelPaint);

        // Cutoff knob
        float knobAreaX = outputMeterRect.Right + 30;
        CutoffKnob.Center = new SKPoint(knobAreaX + KnobRadius, meterY + 50);
        CutoffKnob.Value = state.CutoffHz;
        CutoffKnob.Render(canvas);

        // Slope selector buttons
        float slopeButtonY = meterY + MeterHeight - 34;
        float slopeButtonWidth = 50f;
        float slopeButtonHeight = 28f;
        float slopeButtonX = CutoffKnob.Center.X + KnobRadius + 20;

        _slope12ButtonRect = new SKRect(slopeButtonX, slopeButtonY, slopeButtonX + slopeButtonWidth, slopeButtonY + slopeButtonHeight);
        _slope18ButtonRect = new SKRect(slopeButtonX, slopeButtonY - 36, slopeButtonX + slopeButtonWidth, slopeButtonY - 36 + slopeButtonHeight);

        bool is18dB = state.SlopeDbOct >= 18f;

        // 18 dB button
        var slope18Round = new SKRoundRect(_slope18ButtonRect, 4f);
        canvas.DrawRoundRect(slope18Round, is18dB ? _slopeButtonActivePaint : _slopeButtonPaint);
        canvas.DrawRoundRect(slope18Round, _borderPaint);
        using var slope18TextPaint = new SkiaTextPaint(is18dB ? _theme.PanelBackground : _theme.TextSecondary, 11f, SKFontStyle.Bold, SKTextAlign.Center);
        canvas.DrawText("18dB", _slope18ButtonRect.MidX, _slope18ButtonRect.MidY + 4, slope18TextPaint);

        // 12 dB button
        var slope12Round = new SKRoundRect(_slope12ButtonRect, 4f);
        canvas.DrawRoundRect(slope12Round, !is18dB ? _slopeButtonActivePaint : _slopeButtonPaint);
        canvas.DrawRoundRect(slope12Round, _borderPaint);
        using var slope12TextPaint = new SkiaTextPaint(!is18dB ? _theme.PanelBackground : _theme.TextSecondary, 11f, SKFontStyle.Bold, SKTextAlign.Center);
        canvas.DrawText("12dB", _slope12ButtonRect.MidX, _slope12ButtonRect.MidY + 4, slope12TextPaint);

        // Slope label
        canvas.DrawText("SLOPE", slopeButtonX + slopeButtonWidth / 2, slopeButtonY - 44, _labelPaint);

        // Outer border
        canvas.DrawRoundRect(roundRect, _borderPaint);

        canvas.Restore();
    }

    private void DrawFilterCurve(SKCanvas canvas, SKRect rect, HighPassFilterState state)
    {
        // Background
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _curveBackgroundPaint);

        // Frequency range: 20Hz to 500Hz (log scale)
        float minFreq = 20f;
        float maxFreq = 500f;
        float logMin = MathF.Log10(minFreq);
        float logMax = MathF.Log10(maxFreq);

        // Draw grid
        float[] freqMarkers = { 20f, 50f, 100f, 200f, 500f };
        foreach (float freq in freqMarkers)
        {
            float x = rect.Left + rect.Width * (MathF.Log10(freq) - logMin) / (logMax - logMin);
            canvas.DrawLine(x, rect.Top + 4, x, rect.Bottom - 4, _gridPaint);
            string label = freq >= 1000 ? $"{freq / 1000:0}k" : $"{freq:0}";
            canvas.DrawText(label, x, rect.Bottom + 12, _freqLabelPaint);
        }

        // dB grid lines at -24, -12, 0
        float[] dbMarkers = { 0f, -12f, -24f };
        foreach (float db in dbMarkers)
        {
            float y = rect.Top + 4 + (rect.Height - 8) * (-db / 24f);
            canvas.DrawLine(rect.Left + 4, y, rect.Right - 4, y, _gridPaint);
        }

        // Draw spectrum bars (behind the filter curve)
        if (state.Spectrum is { Length: > 0 })
        {
            int bins = state.Spectrum.Length;
            float innerWidth = rect.Width - 8;
            float innerHeight = rect.Height - 8;
            float barWidth = innerWidth / bins;

            for (int i = 0; i < bins; i++)
            {
                float magnitude = state.Spectrum[i];
                if (magnitude < 0.001f) continue;

                // Convert linear magnitude to dB (scale for display: 0 to -60dB range mapped to 0-24dB display)
                float db = 20f * MathF.Log10(magnitude + 1e-6f);
                db = MathF.Max(db, -60f);
                // Map -60..0 dB to 0..24 in display coordinates
                float displayDb = (db + 60f) / 60f * 24f;
                float barHeight = innerHeight * (displayDb / 24f);
                barHeight = MathF.Max(barHeight, 0f);

                float barX = rect.Left + 4 + i * barWidth;
                float barY = rect.Bottom - 4 - barHeight;

                canvas.DrawRect(barX, barY, barWidth - 1, barHeight, _spectrumPaint);
            }
        }

        // Draw filter response curve
        using var curvePath = new SKPath();
        using var areaPath = new SKPath();

        float slope = state.SlopeDbOct;
        float cutoff = state.CutoffHz;

        bool first = true;
        for (float x = rect.Left + 4; x <= rect.Right - 4; x += 2)
        {
            float normX = (x - rect.Left - 4) / (rect.Width - 8);
            float freq = MathF.Pow(10, logMin + normX * (logMax - logMin));

            // Simple HPF response approximation
            float ratio = freq / cutoff;
            float dbResponse;
            if (ratio >= 1f)
            {
                dbResponse = 0f;
            }
            else
            {
                // Roll-off below cutoff
                float octavesBelow = -MathF.Log2(ratio);
                dbResponse = -octavesBelow * slope;
            }

            dbResponse = MathF.Max(dbResponse, -24f);
            float y = rect.Top + 4 + (rect.Height - 8) * (-dbResponse / 24f);

            if (first)
            {
                curvePath.MoveTo(x, y);
                areaPath.MoveTo(x, rect.Bottom - 4);
                areaPath.LineTo(x, y);
                first = false;
            }
            else
            {
                curvePath.LineTo(x, y);
                areaPath.LineTo(x, y);
            }
        }

        // Complete area path
        areaPath.LineTo(rect.Right - 4, rect.Bottom - 4);
        areaPath.Close();

        canvas.DrawPath(areaPath, _curveAreaPaint);
        canvas.DrawPath(curvePath, _curvePaint);

        // Cutoff frequency line
        float cutoffX = rect.Left + rect.Width * (MathF.Log10(cutoff) - logMin) / (logMax - logMin);
        canvas.DrawLine(cutoffX, rect.Top + 4, cutoffX, rect.Bottom - 4, _cutoffLinePaint);

        // Border
        canvas.DrawRoundRect(roundRect, _borderPaint);
    }

    public HpfHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new HpfHitTest(HpfHitArea.CloseButton, HpfElement.None);

        if (_bypassButtonRect.Contains(x, y))
            return new HpfHitTest(HpfHitArea.BypassButton, HpfElement.None);

        var presetHit = _presetBar.HitTest(x, y);
        if (presetHit == PresetBarHitArea.Dropdown)
            return new HpfHitTest(HpfHitArea.PresetDropdown, HpfElement.None);
        if (presetHit == PresetBarHitArea.SaveButton)
            return new HpfHitTest(HpfHitArea.PresetSave, HpfElement.None);

        if (_slope12ButtonRect.Contains(x, y))
            return new HpfHitTest(HpfHitArea.SlopeButton, HpfElement.Slope12);

        if (_slope18ButtonRect.Contains(x, y))
            return new HpfHitTest(HpfHitArea.SlopeButton, HpfElement.Slope18);

        if (CutoffKnob.HitTest(x, y))
            return new HpfHitTest(HpfHitArea.Knob, HpfElement.CutoffKnob);

        if (_titleBarRect.Contains(x, y))
            return new HpfHitTest(HpfHitArea.TitleBar, HpfElement.None);

        return new HpfHitTest(HpfHitArea.None, HpfElement.None);
    }

    public SKRect GetPresetDropdownRect() => _presetBar.GetDropdownRect();

    public static SKSize GetPreferredSize() => new(340, 300);

    public void Dispose()
    {
        _inputMeter.Dispose();
        _outputMeter.Dispose();
        _presetBar.Dispose();
        CutoffKnob.Dispose();
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _meterBackgroundPaint.Dispose();
        _meterFillPaint.Dispose();
        _curveBackgroundPaint.Dispose();
        _curvePaint.Dispose();
        _curveAreaPaint.Dispose();
        _cutoffLinePaint.Dispose();
        _gridPaint.Dispose();
        _labelPaint.Dispose();
        _freqLabelPaint.Dispose();
        _slopeButtonPaint.Dispose();
        _slopeButtonActivePaint.Dispose();
        _latencyPaint.Dispose();
        _spectrumPaint.Dispose();
    }
}

public record struct HighPassFilterState(
    float CutoffHz,
    float SlopeDbOct,
    float InputLevel,
    float OutputLevel,
    float LatencyMs,
    bool IsBypassed,
    float[]? Spectrum = null,
    string PresetName = "Custom");

public enum HpfHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    Knob,
    SlopeButton,
    PresetDropdown,
    PresetSave
}

public enum HpfElement
{
    None,
    CutoffKnob,
    Slope12,
    Slope18
}

public record struct HpfHitTest(HpfHitArea Area, HpfElement Element);
