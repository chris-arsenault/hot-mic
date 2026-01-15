using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Renders a saturation plugin UI with transfer curve visualization, input/output meters,
/// and diagnostic visualizations (dynamic transfer curve, null-difference scope, harmonic analyzer).
/// </summary>
public sealed class SaturationRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 16f;
    private const float KnobRadius = 36f;
    private const float MeterWidth = 20f;
    private const float MeterHeight = 100f;
    private const float CurveSize = 120f;
    private const float DiagnosticHeight = 100f;
    private const float CornerRadius = 10f;
    private const float WarmthPivotPct = 50f;
    private const float WarmthOverdriveMax = 2f;

    private readonly PluginComponentTheme _theme;
    private readonly PluginPresetBar _presetBar;
    private readonly LevelMeter _inputMeter;
    private readonly LevelMeter _outputMeter;
    private readonly DynamicTransferCurveDisplay _transferCurve;
    private readonly NullDifferenceScope _nullScope;
    private readonly HarmonicAnalyzer _harmonicAnalyzer;

    public KnobWidget WarmthKnob { get; }
    public KnobWidget BlendKnob { get; }

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _titlePaint;
    private readonly SKPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _meterBackgroundPaint;
    private readonly SKPaint _meterFillPaint;
    private readonly SKPaint _curveBackgroundPaint;
    private readonly SKPaint _curvePaint;
    private readonly SKPaint _curveLinearPaint;
    private readonly SKPaint _gridPaint;
    private readonly SKPaint _labelPaint;
    private readonly SKPaint _latencyPaint;
    private readonly SKPaint _sectionLabelPaint;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;

    public SaturationRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _presetBar = new PluginPresetBar(_theme);
        _inputMeter = new LevelMeter();
        _outputMeter = new LevelMeter();
        _transferCurve = new DynamicTransferCurveDisplay(_theme);
        _nullScope = new NullDifferenceScope(_theme);
        _harmonicAnalyzer = new HarmonicAnalyzer(_theme);

        var knobStyle = KnobStyle.Standard;
        WarmthKnob = new KnobWidget(KnobRadius, 0f, 100f, "WARMTH", "%", knobStyle, _theme) { ValueFormat = "0" };
        BlendKnob = new KnobWidget(KnobRadius, 0f, 100f, "BLEND", "%", knobStyle, _theme) { ValueFormat = "0" };

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

        _curveLinearPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            PathEffect = SKPathEffect.CreateDash(new[] { 4f, 4f }, 0)
        };

        _gridPaint = new SKPaint
        {
            Color = _theme.PanelBorder.WithAlpha(60),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 0.5f
        };

        _labelPaint = new SKPaint
        {
            Color = _theme.TextSecondary,
            IsAntialias = true,
            TextSize = 10f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };

        _latencyPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            TextSize = 9f,
            TextAlign = SKTextAlign.Right,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };

        _sectionLabelPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            TextSize = 9f,
            TextAlign = SKTextAlign.Left,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };
    }

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, SaturationState state)
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
        canvas.DrawText("Saturation", Padding, TitleBarHeight / 2f + 5, _titlePaint);

        // Preset bar
        float presetBarX = Padding + 80;
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

        // === ROW 1: Meters + Knobs + Dynamic Transfer Curve ===
        float row1Height = MeterHeight + 20;

        // Input meter
        float meterY = contentTop;
        var inputMeterRect = new SKRect(Padding, meterY, Padding + MeterWidth, meterY + MeterHeight);
        _inputMeter.Update(state.InputLevel);
        _inputMeter.Render(canvas, inputMeterRect, MeterOrientation.Vertical);
        canvas.DrawText("IN", inputMeterRect.MidX, inputMeterRect.Bottom + 12, _labelPaint);

        // Output meter
        var outputMeterRect = new SKRect(inputMeterRect.Right + 6, meterY, inputMeterRect.Right + 6 + MeterWidth, meterY + MeterHeight);
        _outputMeter.Update(state.OutputLevel);
        _outputMeter.Render(canvas, outputMeterRect, MeterOrientation.Vertical);
        canvas.DrawText("OUT", outputMeterRect.MidX, outputMeterRect.Bottom + 12, _labelPaint);

        // Knobs (stacked vertically)
        float knobAreaX = outputMeterRect.Right + 16;
        float knobY = meterY + 20;

        // Warmth knob
        WarmthKnob.Center = new SKPoint(knobAreaX + KnobRadius, knobY);
        WarmthKnob.Value = state.WarmthPct;
        WarmthKnob.Render(canvas);

        // Blend knob
        BlendKnob.Center = new SKPoint(knobAreaX + KnobRadius, knobY + 70);
        BlendKnob.Value = state.BlendPct;
        BlendKnob.Render(canvas);

        // Dynamic Transfer curve (larger, shows real samples)
        float curveX = knobAreaX + KnobRadius * 2 + 24;
        float curveY = contentTop;
        var curveRect = new SKRect(curveX, curveY, curveX + CurveSize, curveY + CurveSize);

        if (state.GetTransferCurveSamples != null)
        {
            _transferCurve.Render(canvas, curveRect, state.WarmthPct, state.GetTransferCurveSamples);
        }
        else
        {
            DrawTransferCurve(canvas, curveRect, state.WarmthPct);
        }

        // === ROW 2: Diagnostic Visualizations ===
        float row2Top = contentTop + row1Height + 8;
        float diagWidth = (size.Width - Padding * 3) / 2;

        // Section label
        canvas.DrawText("DIAGNOSTICS", Padding, row2Top - 2, _sectionLabelPaint);

        // Null-Difference Scope
        var scopeRect = new SKRect(Padding, row2Top + 8, Padding + diagWidth, row2Top + 8 + DiagnosticHeight);
        if (state.GetScopeSamples != null)
        {
            _nullScope.Render(canvas, scopeRect, state.GetScopeSamples);
        }

        // Harmonic Analyzer
        var harmonicRect = new SKRect(scopeRect.Right + Padding, row2Top + 8, size.Width - Padding, row2Top + 8 + DiagnosticHeight);
        if (state.GetFftSamples != null && state.SampleRate > 0)
        {
            _harmonicAnalyzer.SampleRate = state.SampleRate;
            _harmonicAnalyzer.Analyze(state.GetFftSamples);
            _harmonicAnalyzer.Render(canvas, harmonicRect);
        }

        // Outer border
        canvas.DrawRoundRect(roundRect, _borderPaint);

        canvas.Restore();
    }

    private void DrawTransferCurve(SKCanvas canvas, SKRect rect, float warmthPct)
    {
        // Background
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _curveBackgroundPaint);

        // Grid
        canvas.DrawLine(rect.Left + rect.Width / 2, rect.Top + 4, rect.Left + rect.Width / 2, rect.Bottom - 4, _gridPaint);
        canvas.DrawLine(rect.Left + 4, rect.Top + rect.Height / 2, rect.Right - 4, rect.Top + rect.Height / 2, _gridPaint);

        // Linear reference (diagonal)
        canvas.DrawLine(rect.Left + 4, rect.Bottom - 4, rect.Right - 4, rect.Top + 4, _curveLinearPaint);

        float warmth = MapWarmthPreview(warmthPct);
        float env = 0.6f; // Nominal envelope for preview

        // Asymmetric tanh coefficients (must match SaturationPlugin)
        const float K0 = 1.0f, K1 = 1.0f;
        const float A0 = 0.15f, A1 = 0.20f;

        float k = (K0 + K1 * env) * warmth;
        float asym = (A0 + A1 * env) * warmth;
        float kPos = k * (1f + asym);
        float kNeg = k * (1f - asym);

        // Draw asymmetric tanh transfer curve
        using var curvePath = new SKPath();
        bool first = true;

        for (float i = 0; i <= 1f; i += 0.02f)
        {
            float input = i * 2f - 1f; // -1 to 1

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

            float xPos = rect.Left + 4 + (rect.Width - 8) * i;
            float yPos = rect.Bottom - 4 - (rect.Height - 8) * ((output + 1f) / 2f);

            if (first)
            {
                curvePath.MoveTo(xPos, yPos);
                first = false;
            }
            else
            {
                curvePath.LineTo(xPos, yPos);
            }
        }

        canvas.DrawPath(curvePath, _curvePaint);

        // Border
        canvas.DrawRoundRect(roundRect, _borderPaint);

        // Label
        canvas.DrawText("CURVE", rect.MidX, rect.Bottom + 14, _labelPaint);
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

    public SaturationHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new SaturationHitTest(SaturationHitArea.CloseButton, SaturationKnob.None);

        if (_bypassButtonRect.Contains(x, y))
            return new SaturationHitTest(SaturationHitArea.BypassButton, SaturationKnob.None);

        var presetHit = _presetBar.HitTest(x, y);
        if (presetHit == PresetBarHitArea.Dropdown)
            return new SaturationHitTest(SaturationHitArea.PresetDropdown, SaturationKnob.None);
        if (presetHit == PresetBarHitArea.SaveButton)
            return new SaturationHitTest(SaturationHitArea.PresetSave, SaturationKnob.None);

        if (WarmthKnob.HitTest(x, y))
            return new SaturationHitTest(SaturationHitArea.Knob, SaturationKnob.Warmth);

        if (BlendKnob.HitTest(x, y))
            return new SaturationHitTest(SaturationHitArea.Knob, SaturationKnob.Blend);

        if (_titleBarRect.Contains(x, y))
            return new SaturationHitTest(SaturationHitArea.TitleBar, SaturationKnob.None);

        return new SaturationHitTest(SaturationHitArea.None, SaturationKnob.None);
    }

    public SKRect GetPresetDropdownRect() => _presetBar.GetDropdownRect();

    public static SKSize GetPreferredSize() => new(420, 380);

    public void Dispose()
    {
        _inputMeter.Dispose();
        _outputMeter.Dispose();
        _presetBar.Dispose();
        WarmthKnob.Dispose();
        BlendKnob.Dispose();
        _transferCurve.Dispose();
        _nullScope.Dispose();
        _harmonicAnalyzer.Dispose();
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
        _curveLinearPaint.Dispose();
        _gridPaint.Dispose();
        _labelPaint.Dispose();
        _latencyPaint.Dispose();
        _sectionLabelPaint.Dispose();
    }
}

public record struct SaturationState(
    float WarmthPct,
    float BlendPct,
    float InputLevel,
    float OutputLevel,
    float LatencyMs,
    bool IsBypassed,
    string PresetName = "Custom",
    int SampleRate = 0,
    Func<float[], float[], float[], int>? GetTransferCurveSamples = null,
    Func<float[], int>? GetScopeSamples = null,
    Func<float[], int>? GetFftSamples = null);

public enum SaturationHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    Knob,
    PresetDropdown,
    PresetSave
}

public enum SaturationKnob
{
    None,
    Warmth,
    Blend
}

public record struct SaturationHitTest(SaturationHitArea Area, SaturationKnob Knob);
