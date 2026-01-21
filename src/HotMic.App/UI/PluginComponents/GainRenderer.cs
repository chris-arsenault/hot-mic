using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Simple, clean gain plugin UI with large knob and input/output meters.
/// </summary>
public sealed class GainRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 16f;
    private const float KnobRadius = 50f;
    private const float MeterWidth = 24f;
    private const float MeterHeight = 140f;
    private const float CornerRadius = 10f;
    private static readonly float[] MeterGradientStops = [0f, 0.6f, 0.85f, 1f];

    private readonly PluginComponentTheme _theme;
    private readonly PluginPresetBar _presetBar;
    private readonly SKColor[] _meterGradientColors;

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _meterBackgroundPaint;
    private readonly SKPaint _meterFillPaint;
    private readonly SKPaint _meterPeakPaint;
    private readonly SkiaTextPaint _labelPaint;
    private readonly SkiaTextPaint _valuePaint;
    private readonly SkiaTextPaint _unitPaint;
    private readonly SKPaint _phaseButtonPaint;
    private readonly SKPaint _phaseActivePaint;
    private readonly SkiaTextPaint _latencyPaint;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;
    private SKPoint _knobCenter;
    private SKRect _phaseButtonRect;

    // Level meters with PPM-style ballistics
    private readonly LevelMeter _inputMeter;
    private readonly LevelMeter _outputMeter;

    /// <summary>The gain knob widget. Handles its own rendering and interaction.</summary>
    public KnobWidget GainKnob { get; }

    public GainRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _presetBar = new PluginPresetBar(_theme);
        _meterGradientColors =
        [
            _theme.WaveformLine,
            _theme.WaveformLine,
            new SKColor(0xFF, 0xD7, 0x00),
            new SKColor(0xFF, 0x50, 0x50)
        ];

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

        GainKnob = new KnobWidget(
            KnobRadius, -24f, 24f, "GAIN", "dB",
            KnobStyle.Bipolar with { TrackWidth = 8f, ArcWidth = 8f, PointerWidth = 4f },
            _theme)
        {
            ShowPositiveSign = true,
            ValueFormat = "0.0"
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

        _meterPeakPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0x50, 0x50),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _labelPaint = new SkiaTextPaint(_theme.TextSecondary, 11f, SKFontStyle.Normal, SKTextAlign.Center);
        _valuePaint = new SkiaTextPaint(_theme.TextPrimary, 24f, SKFontStyle.Bold, SKTextAlign.Center);
        _unitPaint = new SkiaTextPaint(_theme.TextMuted, 12f, SKFontStyle.Normal, SKTextAlign.Center);

        _phaseButtonPaint = new SKPaint
        {
            Color = _theme.PanelBackgroundLight,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _phaseActivePaint = new SKPaint
        {
            Color = _theme.KnobArc,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _latencyPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Right);

        // Initialize level meters with PPM-style ballistics
        _inputMeter = new LevelMeter();
        _outputMeter = new LevelMeter();
    }

    public void Render(
        SKCanvas canvas,
        SKSize size,
        float dpiScale,
        GainState state)
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
        canvas.DrawText("Gain", Padding, TitleBarHeight / 2f + 5, _titlePaint);

        // Preset bar
        float presetBarX = 60f;
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
        float contentHeight = size.Height - TitleBarHeight - Padding * 2;

        // Input meter (left side) with PPM-style ballistics
        float meterY = contentTop + (contentHeight - MeterHeight) / 2 - 10;
        var inputMeterRect = new SKRect(Padding, meterY, Padding + MeterWidth, meterY + MeterHeight);
        _inputMeter.Update(state.InputLevel);
        _inputMeter.Render(canvas, inputMeterRect, MeterOrientation.Vertical);
        canvas.DrawText("IN", inputMeterRect.MidX, inputMeterRect.Bottom + 16, _labelPaint);

        // Output meter (right side) with PPM-style ballistics
        var outputMeterRect = new SKRect(size.Width - Padding - MeterWidth, meterY,
            size.Width - Padding, meterY + MeterHeight);
        _outputMeter.Update(state.OutputLevel);
        _outputMeter.Render(canvas, outputMeterRect, MeterOrientation.Vertical);
        canvas.DrawText("OUT", outputMeterRect.MidX, outputMeterRect.Bottom + 16, _labelPaint);

        // Large center knob
        _knobCenter = new SKPoint(size.Width / 2, contentTop + contentHeight / 2 - 20);
        GainKnob.Center = _knobCenter;
        GainKnob.Value = state.GainDb;
        GainKnob.Render(canvas);

        // Phase invert button below knob
        float phaseY = _knobCenter.Y + KnobRadius + 50;
        _phaseButtonRect = new SKRect(
            size.Width / 2 - 40,
            phaseY,
            size.Width / 2 + 40,
            phaseY + 28);
        var phaseRound = new SKRoundRect(_phaseButtonRect, 4f);
        canvas.DrawRoundRect(phaseRound, state.IsPhaseInverted ? _phaseActivePaint : _phaseButtonPaint);
        canvas.DrawRoundRect(phaseRound, _borderPaint);

        using var phaseTextPaint = new SkiaTextPaint(state.IsPhaseInverted ? _theme.PanelBackground : _theme.TextSecondary, 11f, SKFontStyle.Bold, SKTextAlign.Center);
        canvas.DrawText("\u00D8 PHASE", _phaseButtonRect.MidX, _phaseButtonRect.MidY + 4, phaseTextPaint);

        // Outer border
        canvas.DrawRoundRect(roundRect, _borderPaint);

        canvas.Restore();
    }

    private void DrawMeter(SKCanvas canvas, SKRect rect, float level, string label)
    {
        // Background
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _meterBackgroundPaint);

        // Convert to dB for display
        float levelDb = 20f * MathF.Log10(level + 1e-10f);
        levelDb = MathF.Max(levelDb, -60f);

        // Normalize to 0-1 (range -60 to 0 dB)
        float normalizedLevel = (levelDb + 60f) / 60f;
        normalizedLevel = MathF.Min(normalizedLevel, 1f);

        float meterPadding = 3f;
        float innerHeight = rect.Height - meterPadding * 2;
        float fillHeight = innerHeight * normalizedLevel;

        // Meter fill (from bottom up)
        if (fillHeight > 0)
        {
            var fillRect = new SKRect(
                rect.Left + meterPadding,
                rect.Bottom - meterPadding - fillHeight,
                rect.Right - meterPadding,
                rect.Bottom - meterPadding);

            // Gradient from green to yellow to red
            using var gradientPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, rect.Bottom),
                    new SKPoint(0, rect.Top),
                    _meterGradientColors,
                    MeterGradientStops,
                    SKShaderTileMode.Clamp)
            };
            canvas.DrawRect(fillRect, gradientPaint);
        }

        // Border
        canvas.DrawRoundRect(roundRect, _borderPaint);

        // Label
        canvas.DrawText(label, rect.MidX, rect.Bottom + 16, _labelPaint);

        // Level value
        string dbText = levelDb > -59f ? $"{levelDb:0}" : "-\u221E";
        using var dbPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Center);
        canvas.DrawText(dbText, rect.MidX, rect.Top - 4, dbPaint);
    }

    public GainHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new GainHitTest(GainHitArea.CloseButton);

        if (_bypassButtonRect.Contains(x, y))
            return new GainHitTest(GainHitArea.BypassButton);

        var presetHit = _presetBar.HitTest(x, y);
        if (presetHit == PresetBarHitArea.Dropdown)
            return new GainHitTest(GainHitArea.PresetDropdown);
        if (presetHit == PresetBarHitArea.SaveButton)
            return new GainHitTest(GainHitArea.PresetSave);

        if (_phaseButtonRect.Contains(x, y))
            return new GainHitTest(GainHitArea.PhaseButton);

        if (GainKnob.HitTest(x, y))
            return new GainHitTest(GainHitArea.Knob);

        if (_titleBarRect.Contains(x, y))
            return new GainHitTest(GainHitArea.TitleBar);

        return new GainHitTest(GainHitArea.None);
    }

    public SKRect GetPresetDropdownRect() => _presetBar.GetDropdownRect();

    public static SKSize GetPreferredSize() => new(280, 320);

    public void Dispose()
    {
        _inputMeter.Dispose();
        _outputMeter.Dispose();
        _presetBar.Dispose();
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        GainKnob.Dispose();
        _meterBackgroundPaint.Dispose();
        _meterFillPaint.Dispose();
        _meterPeakPaint.Dispose();
        _labelPaint.Dispose();
        _valuePaint.Dispose();
        _unitPaint.Dispose();
        _phaseButtonPaint.Dispose();
        _phaseActivePaint.Dispose();
        _latencyPaint.Dispose();
    }
}

/// <summary>
/// State data for rendering the gain UI.
/// </summary>
public record struct GainState(
    float GainDb,
    float InputLevel,
    float OutputLevel,
    bool IsPhaseInverted,
    float LatencyMs,
    bool IsBypassed,
    string PresetName = "Custom");

public enum GainHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    PhaseButton,
    Knob,
    PresetDropdown,
    PresetSave
}

public record struct GainHitTest(GainHitArea Area);
