using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Spectral Contrast plugin UI with spectrum display showing before/after enhancement.
/// </summary>
public sealed class SpectralContrastRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 12f;
    private const float KnobRadius = 24f;
    private const float KnobSpacing = 72f;
    private const float CornerRadius = 10f;
    private const float SpectrumHeight = 100f;

    private readonly PluginComponentTheme _theme;
    private readonly PluginPresetBar _presetBar;

    public KnobWidget StrengthKnob { get; }
    public KnobWidget MixKnob { get; }
    public KnobWidget GateStrengthKnob { get; }

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _spectrumBackgroundPaint;
    private readonly SKPaint _spectrumOriginalPaint;
    private readonly SKPaint _spectrumEnhancedPaint;
    private readonly SKPaint _spectrumGridPaint;
    private readonly SKPaint _speechLedOnPaint;
    private readonly SKPaint _speechLedOffPaint;
    private readonly SKPaint _speechLedGlowPaint;
    private readonly SkiaTextPaint _labelPaint;
    private readonly SkiaTextPaint _freqLabelPaint;
    private readonly SkiaTextPaint _latencyPaint;
    private readonly SkiaTextPaint _statusPaint;
    private readonly ScaleToggleGroup _scaleToggle;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;

    // Spectrum display buffers
    private readonly float[] _displayMagnitudes = new float[256];

    public SpectralContrastRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _presetBar = new PluginPresetBar(_theme);

        StrengthKnob = new KnobWidget(KnobRadius, 0f, 100f, "STRENGTH", "%", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0"
        };
        MixKnob = new KnobWidget(KnobRadius, 0f, 100f, "MIX", "%", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0"
        };
        GateStrengthKnob = new KnobWidget(KnobRadius, 0f, 1f, "GATE", "", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0.00"
        };

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

        _spectrumBackgroundPaint = new SKPaint
        {
            Color = _theme.WaveformBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _spectrumOriginalPaint = new SKPaint
        {
            Color = new SKColor(0x60, 0x60, 0x80, 0x80), // Dim gray
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f
        };

        _spectrumEnhancedPaint = new SKPaint
        {
            Color = _theme.KnobArc,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f
        };

        _spectrumGridPaint = new SKPaint
        {
            Color = _theme.EnvelopeGrid,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };

        _speechLedOnPaint = new SKPaint
        {
            Color = _theme.GateOpen,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _speechLedOffPaint = new SKPaint
        {
            Color = _theme.GateClosed,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _speechLedGlowPaint = new SKPaint
        {
            Color = _theme.GateOpenGlow,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6f)
        };

        _labelPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Normal, SKTextAlign.Center);
        _freqLabelPaint = new SkiaTextPaint(_theme.TextMuted, 8f, SKFontStyle.Normal, SKTextAlign.Center);
        _latencyPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Right);
        _statusPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Normal, SKTextAlign.Center);
        _scaleToggle = new ScaleToggleGroup(_theme);
    }

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, SpectralContrastState state)
    {
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        canvas.Scale(dpiScale);
        size = new SKSize(size.Width / dpiScale, size.Height / dpiScale);

        var backgroundRect = new SKRect(0, 0, size.Width, size.Height);
        var roundRect = new SKRoundRect(backgroundRect, CornerRadius);
        canvas.DrawRoundRect(roundRect, _backgroundPaint);

        DrawTitleBar(canvas, size, state);

        float y = TitleBarHeight + Padding;

        // Status message if any
        if (!string.IsNullOrEmpty(state.StatusMessage))
        {
            _statusPaint.Color = new SKColor(0xFF, 0xA0, 0x40);
            canvas.DrawText(state.StatusMessage, size.Width / 2, y + 10, _statusPaint);
            y += 24;
        }

        // Speech gate LED
        float ledX = Padding + 15f;
        float ledY = y + 15f;
        bool speechActive = state.SpeechGate > 0.3f;

        if (speechActive)
        {
            canvas.DrawCircle(ledX, ledY, 10f, _speechLedGlowPaint);
            canvas.DrawCircle(ledX, ledY, 6f, _speechLedOnPaint);
        }
        else
        {
            canvas.DrawCircle(ledX, ledY, 6f, _speechLedOffPaint);
        }
        canvas.DrawText("ACTIVE", ledX, ledY + 18f, _labelPaint);

        // Spectrum display
        float spectrumX = ledX + 50f;
        float spectrumWidth = size.Width - spectrumX - Padding;
        var spectrumRect = new SKRect(spectrumX, y, spectrumX + spectrumWidth, y + SpectrumHeight);
        DrawSpectrum(canvas, spectrumRect, state.Magnitudes.Span, state.ContrastStrength);

        y += SpectrumHeight + Padding;

        // Scale toggle row
        float scaleRowY = y;
        const string scaleLabel = "SCALE";
        float scaleLabelWidth = _labelPaint.MeasureText(scaleLabel);
        float scaleRowWidth = scaleLabelWidth + 6f + _scaleToggle.Width;
        float scaleRowX = (size.Width - scaleRowWidth) / 2f;
        float scaleLabelY = scaleRowY + _scaleToggle.Height / 2f + 3f;
        _labelPaint.DrawText(canvas, scaleLabel, scaleRowX, scaleLabelY, SKTextAlign.Left);
        _scaleToggle.Render(canvas, scaleRowX + scaleLabelWidth + 6f, scaleRowY, state.ScaleIndex);

        y += _scaleToggle.Height + 10f;

        // Knobs
        float knobsY = y + KnobRadius + 10;
        float knobsTotalWidth = 3 * KnobSpacing;
        float knobsStartX = (size.Width - knobsTotalWidth) / 2 + KnobSpacing / 2;

        StrengthKnob.Center = new SKPoint(knobsStartX, knobsY);
        StrengthKnob.Value = state.StrengthPct;
        StrengthKnob.Render(canvas);

        MixKnob.Center = new SKPoint(knobsStartX + KnobSpacing, knobsY);
        MixKnob.Value = state.MixPct;
        MixKnob.Render(canvas);

        GateStrengthKnob.Center = new SKPoint(knobsStartX + KnobSpacing * 2, knobsY);
        GateStrengthKnob.Value = state.GateStrength;
        GateStrengthKnob.Render(canvas);

        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.Restore();
    }

    private void DrawTitleBar(SKCanvas canvas, SKSize size, SpectralContrastState state)
    {
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

        canvas.DrawText("Spectral Contrast", Padding, TitleBarHeight / 2f + 5, _titlePaint);

        float presetBarX = 130f;
        float presetBarY = (TitleBarHeight - PluginPresetBar.TotalHeight) / 2f;
        _presetBar.Render(canvas, presetBarX, presetBarY, state.PresetName);

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

        _closeButtonRect = new SKRect(size.Width - Padding - 24, (TitleBarHeight - 24) / 2,
            size.Width - Padding, (TitleBarHeight + 24) / 2);
        canvas.DrawText("\u00D7", _closeButtonRect.MidX, _closeButtonRect.MidY + 6, _closeButtonPaint);
    }

    private void DrawSpectrum(SKCanvas canvas, SKRect rect, ReadOnlySpan<float> magnitudes, float strength)
    {
        var roundRect = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(roundRect, _spectrumBackgroundPaint);

        float padding = 6f;
        float plotWidth = rect.Width - padding * 2;
        float plotHeight = rect.Height - padding * 2;

        // Grid lines
        for (int i = 1; i < 4; i++)
        {
            float gridY = rect.Top + padding + (i / 4f) * plotHeight;
            canvas.DrawLine(rect.Left + padding, gridY, rect.Right - padding, gridY, _spectrumGridPaint);
        }

        // Draw spectrum curve
        if (magnitudes.Length > 0)
        {
            using var path = new SKPath();
            int displayBins = Math.Min(magnitudes.Length, (int)plotWidth);

            for (int i = 0; i < displayBins; i++)
            {
                int binIndex = i * magnitudes.Length / displayBins;
                float mag = magnitudes.Length > binIndex ? magnitudes[binIndex] : 0f;

                // Convert to dB and normalize
                float magDb = 20f * MathF.Log10(mag + 1e-6f);
                float norm = Math.Clamp((magDb + 60f) / 60f, 0f, 1f);

                float x = rect.Left + padding + (i / (float)(displayBins - 1)) * plotWidth;
                float y = rect.Bottom - padding - norm * plotHeight;

                if (i == 0)
                    path.MoveTo(x, y);
                else
                    path.LineTo(x, y);
            }

            // Draw enhanced version (brighter)
            canvas.DrawPath(path, _spectrumEnhancedPaint);
        }

        // Strength indicator bar at bottom
        using var strengthPaint = new SKPaint
        {
            Color = _theme.KnobArc.WithAlpha((byte)(strength * 255)),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        float strengthHeight = 4f;
        var strengthRect = new SKRect(rect.Left + padding, rect.Bottom - padding - strengthHeight,
            rect.Left + padding + plotWidth * strength, rect.Bottom - padding);
        canvas.DrawRect(strengthRect, strengthPaint);

        // Frequency labels
        canvas.DrawText("100", rect.Left + padding + plotWidth * 0.15f, rect.Bottom + 10, _freqLabelPaint);
        canvas.DrawText("1k", rect.Left + padding + plotWidth * 0.45f, rect.Bottom + 10, _freqLabelPaint);
        canvas.DrawText("10k", rect.Left + padding + plotWidth * 0.85f, rect.Bottom + 10, _freqLabelPaint);

        canvas.DrawRoundRect(roundRect, _borderPaint);
    }

    public SpectralContrastHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new SpectralContrastHitTest(SpectralContrastHitArea.CloseButton, -1);

        if (_bypassButtonRect.Contains(x, y))
            return new SpectralContrastHitTest(SpectralContrastHitArea.BypassButton, -1);

        var presetHit = _presetBar.HitTest(x, y);
        if (presetHit == PresetBarHitArea.Dropdown)
            return new SpectralContrastHitTest(SpectralContrastHitArea.PresetDropdown, -1);
        if (presetHit == PresetBarHitArea.SaveButton)
            return new SpectralContrastHitTest(SpectralContrastHitArea.PresetSave, -1);

        int scaleIndex = _scaleToggle.HitTest(x, y);
        if (scaleIndex >= 0)
            return new SpectralContrastHitTest(SpectralContrastHitArea.ScaleToggle, scaleIndex);

        if (StrengthKnob.HitTest(x, y))
            return new SpectralContrastHitTest(SpectralContrastHitArea.Knob, 0);
        if (MixKnob.HitTest(x, y))
            return new SpectralContrastHitTest(SpectralContrastHitArea.Knob, 1);
        if (GateStrengthKnob.HitTest(x, y))
            return new SpectralContrastHitTest(SpectralContrastHitArea.Knob, 2);

        if (_titleBarRect.Contains(x, y))
            return new SpectralContrastHitTest(SpectralContrastHitArea.TitleBar, -1);

        return new SpectralContrastHitTest(SpectralContrastHitArea.None, -1);
    }

    public SKRect GetPresetDropdownRect() => _presetBar.GetDropdownRect();

    public static SKSize GetPreferredSize() => new(360, 320);

    public void Dispose()
    {
        StrengthKnob.Dispose();
        MixKnob.Dispose();
        GateStrengthKnob.Dispose();
        _presetBar.Dispose();
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _spectrumBackgroundPaint.Dispose();
        _spectrumOriginalPaint.Dispose();
        _spectrumEnhancedPaint.Dispose();
        _spectrumGridPaint.Dispose();
        _speechLedOnPaint.Dispose();
        _speechLedOffPaint.Dispose();
        _speechLedGlowPaint.Dispose();
        _labelPaint.Dispose();
        _freqLabelPaint.Dispose();
        _latencyPaint.Dispose();
        _statusPaint.Dispose();
        _scaleToggle.Dispose();
    }
}

public record struct SpectralContrastState(
    float StrengthPct,
    float MixPct,
    float GateStrength,
    int ScaleIndex,
    float SpeechGate,
    float ContrastStrength,
    ReadOnlyMemory<float> Magnitudes,
    float LatencyMs,
    bool IsBypassed,
    string StatusMessage = "",
    string PresetName = "Custom");

public enum SpectralContrastHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    ScaleToggle,
    Knob,
    PresetDropdown,
    PresetSave
}

public record struct SpectralContrastHitTest(SpectralContrastHitArea Area, int KnobIndex);
