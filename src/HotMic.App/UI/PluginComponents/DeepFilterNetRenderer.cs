using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// DeepFilterNet plugin UI renderer with deep learning visualization.
/// Shows reduction level, attenuation, and processing status.
/// </summary>
public sealed class DeepFilterNetRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 16f;
    private const float KnobRadius = 26f;
    private const float KnobSpacing = 100f;
    private const float CornerRadius = 10f;
    private const int KnobCount = 2;

    private readonly PluginComponentTheme _theme;
    private readonly GainReductionMeter _grMeter;
    private readonly AiProcessingIndicator _processingIndicator;
    private readonly RotaryKnob _knob;
    private readonly PluginPresetBar _presetBar;

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _titlePaint;
    private readonly SKPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _sectionLabelPaint;
    private readonly SKPaint _statusPaint;
    private readonly SKPaint _descriptionPaint;
    private readonly SKPaint _togglePaint;
    private readonly SKPaint _toggleActivePaint;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;
    private SKRect _postFilterToggleRect;
    private readonly SKRect[] _knobRects = new SKRect[KnobCount];
    private readonly SKPoint[] _knobCenters = new SKPoint[KnobCount];

    public DeepFilterNetRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _grMeter = new GainReductionMeter(_theme);
        _processingIndicator = new AiProcessingIndicator(_theme);
        _knob = new RotaryKnob(_theme);
        _presetBar = new PluginPresetBar(_theme);

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

        _sectionLabelPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            TextSize = 9f,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };

        _statusPaint = new SKPaint
        {
            Color = _theme.TextSecondary,
            IsAntialias = true,
            TextSize = 10f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };

        _descriptionPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            TextSize = 10f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };

        _togglePaint = new SKPaint
        {
            Color = _theme.PanelBackgroundLight,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _toggleActivePaint = new SKPaint
        {
            Color = new SKColor(0x40, 0xC0, 0x40),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
    }

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, DeepFilterNetState state)
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
        canvas.DrawText("DeepFilterNet", Padding, TitleBarHeight / 2f + 5, _titlePaint);

        // Preset bar
        float presetBarX = Padding + 105;
        float presetBarY = (TitleBarHeight - PluginPresetBar.TotalHeight) / 2f;
        _presetBar.Render(canvas, presetBarX, presetBarY, state.PresetName);

        // AI badge (purple for deep learning)
        float badgeX = presetBarX + PluginPresetBar.TotalWidth + 6f;
        var badgeRect = new SKRect(badgeX, (TitleBarHeight - 16) / 2, badgeX + 25, (TitleBarHeight + 16) / 2);
        using var badgePaint = new SKPaint { Color = new SKColor(0xA0, 0x40, 0xFF), IsAntialias = true };
        canvas.DrawRoundRect(new SKRoundRect(badgeRect, 3f), badgePaint);
        using var badgeTextPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            TextSize = 8f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };
        canvas.DrawText("DL", badgeRect.MidX, badgeRect.MidY + 3, badgeTextPaint);

        // Latency
        if (state.LatencyMs > 0)
        {
            using var latencyPaint = new SKPaint
            {
                Color = _theme.TextMuted,
                IsAntialias = true,
                TextSize = 9f,
                TextAlign = SKTextAlign.Right,
                Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
            };
            canvas.DrawText($"LAT {state.LatencyMs:0.0}ms", size.Width - Padding - 100, TitleBarHeight / 2f + 4, latencyPaint);
        }

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

        // Close button
        _closeButtonRect = new SKRect(size.Width - Padding - 24, (TitleBarHeight - 24) / 2,
            size.Width - Padding, (TitleBarHeight + 24) / 2);
        canvas.DrawText("\u00D7", _closeButtonRect.MidX, _closeButtonRect.MidY + 6, _closeButtonPaint);

        float y = TitleBarHeight + Padding;

        // Status message if any
        if (!string.IsNullOrEmpty(state.StatusMessage))
        {
            _statusPaint.Color = new SKColor(0xFF, 0xA0, 0x40);
            canvas.DrawText(state.StatusMessage, size.Width / 2, y + 10, _statusPaint);
            y += 24;
        }

        // Description
        canvas.DrawText("Deep Learning Noise Reduction (DeepFilterNet-3)", size.Width / 2, y + 10, _descriptionPaint);
        y += 24;

        // Processing indicator (centered)
        var indicatorCenter = new SKPoint(size.Width / 2, y + 45);
        _processingIndicator.Render(canvas, indicatorCenter, 36f, !state.IsBypassed, state.ReductionPercent / 100f, "DEEP FILTER");
        y += 100;

        // Gain reduction meter (actual dB reduction)
        var grRect = new SKRect(Padding, y, size.Width - Padding, y + 24);
        _grMeter.Render(canvas, grRect, state.GainReductionDb, "Gain Reduction");
        y += 55;

        // Knobs section
        float knobsY = y + KnobRadius + 8;
        float knobsTotalWidth = KnobCount * KnobSpacing;
        float knobsStartX = (size.Width - knobsTotalWidth) / 2 + KnobSpacing / 2;

        // Reduction knob
        _knobCenters[0] = new SKPoint(knobsStartX, knobsY);
        float reductionNorm = state.ReductionPercent / 100f;
        _knob.Render(canvas, _knobCenters[0], KnobRadius, reductionNorm,
            "REDUCTION", $"{state.ReductionPercent:0}", "%", state.HoveredKnob == 0);
        _knobRects[0] = _knob.GetHitRect(_knobCenters[0], KnobRadius);

        // Attenuation limit knob
        _knobCenters[1] = new SKPoint(knobsStartX + KnobSpacing, knobsY);
        float attenNorm = (state.AttenuationLimitDb - 6f) / (60f - 6f);
        _knob.Render(canvas, _knobCenters[1], KnobRadius, attenNorm,
            "ATTEN LIM", $"{state.AttenuationLimitDb:0}", "dB", state.HoveredKnob == 1);
        _knobRects[1] = _knob.GetHitRect(_knobCenters[1], KnobRadius);

        // Post-filter toggle
        y = knobsY + KnobRadius + 30;
        float toggleWidth = 120f;
        _postFilterToggleRect = new SKRect(
            (size.Width - toggleWidth) / 2,
            y,
            (size.Width + toggleWidth) / 2,
            y + 28);

        var toggleRound = new SKRoundRect(_postFilterToggleRect, 4f);
        canvas.DrawRoundRect(toggleRound, state.PostFilterEnabled ? _toggleActivePaint : _togglePaint);
        canvas.DrawRoundRect(toggleRound, _borderPaint);

        using var toggleTextPaint = new SKPaint
        {
            Color = state.PostFilterEnabled ? new SKColor(0x12, 0x12, 0x14) : _theme.TextSecondary,
            IsAntialias = true,
            TextSize = 10f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };
        canvas.DrawText("POST-FILTER", _postFilterToggleRect.MidX, _postFilterToggleRect.MidY + 4, toggleTextPaint);

        // Status bar at bottom
        float barHeight = 24f;
        float barY = size.Height - Padding - barHeight;
        var statusBarRect = new SKRect(Padding, barY, size.Width - Padding, barY + barHeight);

        string statusText = state.IsBypassed ? "BYPASSED" : "DEEP LEARNING ACTIVE";
        SKColor barColor = state.IsBypassed ? new SKColor(0x80, 0x80, 0x80) : new SKColor(0xA0, 0x40, 0xFF);

        using var barPaint = new SKPaint { Color = barColor.WithAlpha(100), IsAntialias = true };
        canvas.DrawRoundRect(new SKRoundRect(statusBarRect, 4f), barPaint);
        canvas.DrawRoundRect(new SKRoundRect(statusBarRect, 4f), _borderPaint);

        using var statusTextPaint = new SKPaint
        {
            Color = _theme.TextPrimary,
            IsAntialias = true,
            TextSize = 10f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };
        canvas.DrawText(statusText, statusBarRect.MidX, statusBarRect.MidY + 4, statusTextPaint);

        // Outer border
        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.Restore();
    }

    public DeepFilterNetHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new DeepFilterNetHitTest(DeepFilterNetHitArea.CloseButton, -1);

        if (_bypassButtonRect.Contains(x, y))
            return new DeepFilterNetHitTest(DeepFilterNetHitArea.BypassButton, -1);

        var presetHit = _presetBar.HitTest(x, y);
        if (presetHit == PresetBarHitArea.Dropdown)
            return new DeepFilterNetHitTest(DeepFilterNetHitArea.PresetDropdown, -1);
        if (presetHit == PresetBarHitArea.SaveButton)
            return new DeepFilterNetHitTest(DeepFilterNetHitArea.PresetSave, -1);

        if (_postFilterToggleRect.Contains(x, y))
            return new DeepFilterNetHitTest(DeepFilterNetHitArea.PostFilterToggle, -1);

        for (int i = 0; i < KnobCount; i++)
        {
            float dx = x - _knobCenters[i].X;
            float dy = y - _knobCenters[i].Y;
            if (dx * dx + dy * dy <= KnobRadius * KnobRadius * 1.5f)
            {
                return new DeepFilterNetHitTest(DeepFilterNetHitArea.Knob, i);
            }
        }

        if (_titleBarRect.Contains(x, y))
            return new DeepFilterNetHitTest(DeepFilterNetHitArea.TitleBar, -1);

        return new DeepFilterNetHitTest(DeepFilterNetHitArea.None, -1);
    }

    public SKRect GetPresetDropdownRect() => _presetBar.GetDropdownRect();

    public static SKSize GetPreferredSize() => new(380, 420);

    public void Dispose()
    {
        _grMeter.Dispose();
        _processingIndicator.Dispose();
        _knob.Dispose();
        _presetBar.Dispose();
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _sectionLabelPaint.Dispose();
        _statusPaint.Dispose();
        _descriptionPaint.Dispose();
        _togglePaint.Dispose();
        _toggleActivePaint.Dispose();
    }
}

public record struct DeepFilterNetState(
    float ReductionPercent,
    float AttenuationLimitDb,
    bool PostFilterEnabled,
    float GainReductionDb,
    float LatencyMs,
    bool IsBypassed,
    string StatusMessage = "",
    int HoveredKnob = -1,
    string PresetName = "Custom");

public enum DeepFilterNetHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    PostFilterToggle,
    Knob,
    PresetDropdown,
    PresetSave
}

public record struct DeepFilterNetHitTest(DeepFilterNetHitArea Area, int KnobIndex);
