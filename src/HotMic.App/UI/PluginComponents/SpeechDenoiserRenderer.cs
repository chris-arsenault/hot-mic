using System;
using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// SpeechDenoiser plugin UI renderer.
/// Focuses on streaming status and dry/wet control.
/// </summary>
public sealed class SpeechDenoiserRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 16f;
    private const float KnobRadius = 26f;
    private const float KnobSpacing = 120f;
    private const float CornerRadius = 10f;
    private const int KnobCount = 2;

    private readonly PluginComponentTheme _theme;
    private readonly AiProcessingIndicator _processingIndicator;
    private readonly PluginPresetBar _presetBar;

    /// <summary>Dry/Wet mix knob (0-100%).</summary>
    public KnobWidget MixKnob { get; }

    /// <summary>Attenuation limit knob (0-100 dB).</summary>
    public KnobWidget AttenLimitKnob { get; }

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SkiaTextPaint _statusPaint;
    private readonly SkiaTextPaint _descriptionPaint;
    private readonly SKPaint _togglePaint;
    private readonly SKPaint _toggleActivePaint;
    private readonly SkiaTextPaint _toggleLabelPaint;
    private readonly SKPaint _toggleCheckPaint;
    private readonly SKPaint _learnPaint;
    private readonly SKPaint _learnActivePaint;
    private readonly SkiaTextPaint _learnTextPaint;
    private readonly SKPaint _warningPaint;
    private readonly SKPaint _warningStrokePaint;
    private readonly SKPaint _warningDotPaint;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;
    private SKRect _attenToggleRect;
    private SKRect _learnButtonRect;

    public SpeechDenoiserRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _processingIndicator = new AiProcessingIndicator(_theme);
        _presetBar = new PluginPresetBar(_theme);

        var knobStyle = KnobStyle.Standard;
        MixKnob = new KnobWidget(KnobRadius, 0f, 100f, "DRY / WET", "%", knobStyle, _theme)
        {
            ValueFormat = "0"
        };
        AttenLimitKnob = new KnobWidget(KnobRadius, 0f, 100f, "ATTEN LIMIT", "dB", knobStyle, _theme)
        {
            ValueFormat = "0"
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

        _statusPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Normal, SKTextAlign.Center);
        _descriptionPaint = new SkiaTextPaint(_theme.TextMuted, 10f, SKFontStyle.Normal, SKTextAlign.Center);

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

        _toggleLabelPaint = new SkiaTextPaint(_theme.TextSecondary, 9f, SKFontStyle.Bold, SKTextAlign.Left);

        _toggleCheckPaint = new SKPaint
        {
            Color = _theme.TextPrimary,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };

        _learnPaint = new SKPaint
        {
            Color = _theme.PanelBackgroundLight,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _learnActivePaint = new SKPaint
        {
            Color = new SKColor(0x40, 0x80, 0xFF),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _learnTextPaint = new SkiaTextPaint(_theme.TextPrimary, 9f, SKFontStyle.Bold, SKTextAlign.Center);

        _warningPaint = new SKPaint
        {
            Color = new SKColor(0xF5, 0xA5, 0x24),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _warningStrokePaint = new SKPaint
        {
            Color = new SKColor(0x24, 0x18, 0x00),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.4f,
            StrokeCap = SKStrokeCap.Round
        };

        _warningDotPaint = new SKPaint
        {
            Color = new SKColor(0x24, 0x18, 0x00),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
    }

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, SpeechDenoiserState state)
    {
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        canvas.Scale(dpiScale);
        size = new SKSize(size.Width / dpiScale, size.Height / dpiScale);

        var backgroundRect = new SKRect(0, 0, size.Width, size.Height);
        var roundRect = new SKRoundRect(backgroundRect, CornerRadius);
        canvas.DrawRoundRect(roundRect, _backgroundPaint);

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

        canvas.DrawText("Speech Denoiser", Padding, TitleBarHeight / 2f + 5, _titlePaint);

        float presetBarX = Padding + 120;
        float presetBarY = (TitleBarHeight - PluginPresetBar.TotalHeight) / 2f;
        _presetBar.Render(canvas, presetBarX, presetBarY, state.PresetName);

        float badgeX = presetBarX + PluginPresetBar.TotalWidth + 6f;
        var badgeRect = new SKRect(badgeX, (TitleBarHeight - 16) / 2, badgeX + 25, (TitleBarHeight + 16) / 2);
        using var badgePaint = new SKPaint { Color = new SKColor(0x40, 0x80, 0xFF), IsAntialias = true };
        canvas.DrawRoundRect(new SKRoundRect(badgeRect, 3f), badgePaint);
        using var badgeTextPaint = new SkiaTextPaint(SKColors.White, 8f, SKFontStyle.Bold, SKTextAlign.Center);
        canvas.DrawText("AI", badgeRect.MidX, badgeRect.MidY + 3, badgeTextPaint);

        if (state.LatencyMs > 0)
        {
            using var latencyPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Right);
            string latencyText = state.LatencySamples > 0
                ? $"LAT {state.LatencyMs:0.0}ms ({state.LatencySamples})"
                : $"LAT {state.LatencyMs:0.0}ms";
            canvas.DrawText(latencyText, size.Width - Padding - 100, TitleBarHeight / 2f + 4, latencyPaint);
        }

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

        _closeButtonRect = new SKRect(size.Width - Padding - 24, (TitleBarHeight - 24) / 2,
            size.Width - Padding, (TitleBarHeight + 24) / 2);
        canvas.DrawText("\u00D7", _closeButtonRect.MidX, _closeButtonRect.MidY + 6, _closeButtonPaint);

        float y = TitleBarHeight + Padding;

        if (!string.IsNullOrEmpty(state.StatusMessage))
        {
            _statusPaint.Color = new SKColor(0xFF, 0xA0, 0x40);
            canvas.DrawText(state.StatusMessage, size.Width / 2, y + 10, _statusPaint);
            y += 24;
        }

        canvas.DrawText("Streaming speech denoising (SpeechDenoiser)", size.Width / 2, y + 10, _descriptionPaint);
        y += 24;

        if (!string.IsNullOrEmpty(state.LatencyReport))
        {
            _statusPaint.Color = _theme.TextMuted;
            canvas.DrawText(state.LatencyReport, size.Width / 2, y + 10, _statusPaint);
            y += 20;
        }

        float learnWidth = 150f;
        float learnHeight = 22f;
        float learnX = (size.Width - learnWidth) / 2f;
        float learnY = y + 4f;
        _learnButtonRect = new SKRect(learnX, learnY, learnX + learnWidth, learnY + learnHeight);
        var learnRound = new SKRoundRect(_learnButtonRect, 4f);
        canvas.DrawRoundRect(learnRound, state.IsLatencyLearning ? _learnActivePaint : _learnPaint);
        canvas.DrawRoundRect(learnRound, _borderPaint);
        string learnLabel = state.IsLatencyLearning ? "LEARNING..." : "LEARN LATENCY";
        canvas.DrawText(learnLabel, _learnButtonRect.MidX, _learnButtonRect.MidY + 4, _learnTextPaint);
        y = learnY + learnHeight + 18f;

        var indicatorCenter = new SKPoint(size.Width / 2, y + 45);
        _processingIndicator.Render(canvas, indicatorCenter, 36f, !state.IsBypassed, state.MixPercent / 100f, "STREAMING");
        y += 100;

        float knobsY = y + KnobRadius + 8;
        float knobsStartX = size.Width / 2 - KnobSpacing / 2f;

        MixKnob.Center = new SKPoint(knobsStartX, knobsY);
        MixKnob.Value = state.MixPercent;
        MixKnob.Render(canvas);

        AttenLimitKnob.Center = new SKPoint(knobsStartX + KnobSpacing, knobsY);
        AttenLimitKnob.Value = state.AttenLimitDb;
        AttenLimitKnob.Render(canvas);

        float toggleHeight = 22f;
        float toggleWidth = 170f;
        float toggleY = knobsY + KnobRadius + 30f;
        float toggleX = (size.Width - toggleWidth) / 2f;
        _attenToggleRect = new SKRect(toggleX, toggleY, toggleX + toggleWidth, toggleY + toggleHeight);
        DrawAttenToggle(canvas, _attenToggleRect, state.AttenEnabled);

        float barHeight = 24f;
        float barY = size.Height - Padding - barHeight;
        var statusBarRect = new SKRect(Padding, barY, size.Width - Padding, barY + barHeight);

        string statusText = state.IsBypassed ? "BYPASSED" : "SPEECH DENOISING ACTIVE";
        SKColor barColor = state.IsBypassed ? new SKColor(0x80, 0x80, 0x80) : new SKColor(0x40, 0x80, 0xFF);

        using var barPaint = new SKPaint { Color = barColor.WithAlpha(100), IsAntialias = true };
        canvas.DrawRoundRect(new SKRoundRect(statusBarRect, 4f), barPaint);
        canvas.DrawRoundRect(new SKRoundRect(statusBarRect, 4f), _borderPaint);

        using var statusTextPaint = new SkiaTextPaint(_theme.TextPrimary, 10f, SKFontStyle.Bold, SKTextAlign.Center);
        canvas.DrawText(statusText, statusBarRect.MidX, statusBarRect.MidY + 4, statusTextPaint);

        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.Restore();
    }

    public SpeechDenoiserHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new SpeechDenoiserHitTest(SpeechDenoiserHitArea.CloseButton, -1);

        if (_bypassButtonRect.Contains(x, y))
            return new SpeechDenoiserHitTest(SpeechDenoiserHitArea.BypassButton, -1);

        var presetHit = _presetBar.HitTest(x, y);
        if (presetHit == PresetBarHitArea.Dropdown)
            return new SpeechDenoiserHitTest(SpeechDenoiserHitArea.PresetDropdown, -1);
        if (presetHit == PresetBarHitArea.SaveButton)
            return new SpeechDenoiserHitTest(SpeechDenoiserHitArea.PresetSave, -1);

        if (MixKnob.HitTest(x, y))
            return new SpeechDenoiserHitTest(SpeechDenoiserHitArea.Knob, 0);

        if (AttenLimitKnob.HitTest(x, y))
            return new SpeechDenoiserHitTest(SpeechDenoiserHitArea.Knob, 1);

        if (_attenToggleRect.Contains(x, y))
            return new SpeechDenoiserHitTest(SpeechDenoiserHitArea.AttenLimitToggle, -1);

        if (_learnButtonRect.Contains(x, y))
            return new SpeechDenoiserHitTest(SpeechDenoiserHitArea.LearnLatency, -1);

        if (_titleBarRect.Contains(x, y))
            return new SpeechDenoiserHitTest(SpeechDenoiserHitArea.TitleBar, -1);

        return new SpeechDenoiserHitTest(SpeechDenoiserHitArea.None, -1);
    }

    public SKRect GetPresetDropdownRect() => _presetBar.GetDropdownRect();

    public static SKSize GetPreferredSize() => new(380, 420);

    public void Dispose()
    {
        _processingIndicator.Dispose();
        MixKnob.Dispose();
        AttenLimitKnob.Dispose();
        _presetBar.Dispose();
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _statusPaint.Dispose();
        _descriptionPaint.Dispose();
        _togglePaint.Dispose();
        _toggleActivePaint.Dispose();
        _toggleLabelPaint.Dispose();
        _toggleCheckPaint.Dispose();
        _learnPaint.Dispose();
        _learnActivePaint.Dispose();
        _learnTextPaint.Dispose();
        _warningPaint.Dispose();
        _warningStrokePaint.Dispose();
        _warningDotPaint.Dispose();
    }

    private void DrawAttenToggle(SKCanvas canvas, SKRect rect, bool enabled)
    {
        var round = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(round, enabled ? _toggleActivePaint : _togglePaint);
        canvas.DrawRoundRect(round, _borderPaint);

        float boxSize = 12f;
        float boxX = rect.Left + 10f;
        float boxY = rect.MidY - boxSize / 2f;
        var boxRect = new SKRect(boxX, boxY, boxX + boxSize, boxY + boxSize);
        canvas.DrawRect(boxRect, _borderPaint);

        if (enabled)
        {
            float checkLeft = boxRect.Left + 2f;
            float checkMid = boxRect.MidY + 1f;
            float checkRight = boxRect.Right - 2f;
            canvas.DrawLine(checkLeft, checkMid, boxRect.MidX - 1f, boxRect.Bottom - 3f, _toggleCheckPaint);
            canvas.DrawLine(boxRect.MidX - 1f, boxRect.Bottom - 3f, checkRight, boxRect.Top + 3f, _toggleCheckPaint);
        }

        _toggleLabelPaint.Color = enabled ? _theme.TextPrimary : _theme.TextSecondary;
        float labelX = boxRect.Right + 8f;
        canvas.DrawText("ATTEN LIMIT", labelX, rect.MidY + 3f, _toggleLabelPaint);

        float warnSize = 10f;
        float warnX = rect.Right - 12f;
        var warnCenter = new SKPoint(warnX, rect.MidY);
        DrawWarningIcon(canvas, warnCenter, warnSize);
    }

    private void DrawWarningIcon(SKCanvas canvas, SKPoint center, float size)
    {
        float half = size / 2f;
        using var path = new SKPath();
        path.MoveTo(center.X, center.Y - half);
        path.LineTo(center.X + half, center.Y + half);
        path.LineTo(center.X - half, center.Y + half);
        path.Close();
        canvas.DrawPath(path, _warningPaint);

        float lineTop = center.Y - half * 0.2f;
        float lineBottom = center.Y + half * 0.25f;
        canvas.DrawLine(center.X, lineTop, center.X, lineBottom, _warningStrokePaint);
        canvas.DrawCircle(center.X, center.Y + half * 0.45f, 1.2f, _warningDotPaint);
    }
}

public record struct SpeechDenoiserState(
    float MixPercent,
    float AttenLimitDb,
    bool AttenEnabled,
    float LatencyMs,
    int LatencySamples,
    bool IsBypassed,
    string StatusMessage = "",
    string PresetName = "Custom",
    string LatencyReport = "",
    bool IsLatencyLearning = false);

public enum SpeechDenoiserHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    AttenLimitToggle,
    Knob,
    PresetDropdown,
    PresetSave,
    LearnLatency
}

public record struct SpeechDenoiserHitTest(SpeechDenoiserHitArea Area, int KnobIndex);
