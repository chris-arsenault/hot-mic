using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Sidechain Tap plugin UI - debug/monitor panel showing all sidechain signals.
/// </summary>
public sealed class SidechainTapRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 12f;
    private const float CornerRadius = 10f;
    private const float MeterHeight = 20f;
    private const float MeterSpacing = 8f;
    private const float ToggleWidth = 40f;

    private readonly PluginComponentTheme _theme;

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _meterBackgroundPaint;
    private readonly SKPaint _speechMeterPaint;
    private readonly SKPaint _voicedMeterPaint;
    private readonly SKPaint _unvoicedMeterPaint;
    private readonly SKPaint _sibilanceMeterPaint;
    private readonly SKPaint _toggleOnPaint;
    private readonly SKPaint _toggleOffPaint;
    private readonly SkiaTextPaint _labelPaint;
    private readonly SkiaTextPaint _valuePaint;
    private readonly SkiaTextPaint _latencyPaint;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;
    private SKRect _speechToggleRect;
    private SKRect _voicedToggleRect;
    private SKRect _unvoicedToggleRect;
    private SKRect _sibilanceToggleRect;

    public SidechainTapRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;

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

        _speechMeterPaint = new SKPaint
        {
            Color = _theme.GateOpen,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _voicedMeterPaint = new SKPaint
        {
            Color = new SKColor(0x60, 0xC0, 0xFF), // Light blue
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _unvoicedMeterPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0xA0, 0x40), // Orange
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _sibilanceMeterPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0x60, 0x80), // Pink
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _toggleOnPaint = new SKPaint
        {
            Color = new SKColor(0x40, 0xC0, 0x40),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _toggleOffPaint = new SKPaint
        {
            Color = _theme.GateClosed,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _labelPaint = new SkiaTextPaint(_theme.TextSecondary, 11f, SKFontStyle.Normal, SKTextAlign.Left);
        _valuePaint = new SkiaTextPaint(_theme.TextPrimary, 11f, SKFontStyle.Bold, SKTextAlign.Right);
        _latencyPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Right);
    }

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, SidechainTapState state)
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
        float meterWidth = size.Width - Padding * 2 - ToggleWidth - 80f;
        float meterX = Padding + 80f;

        // Speech Presence
        DrawSignalRow(canvas, y, meterX, meterWidth, "Speech Presence",
            state.SpeechPresence, state.SpeechEnabled, _speechMeterPaint, out _speechToggleRect);
        y += MeterHeight + MeterSpacing;

        // Voiced Probability
        DrawSignalRow(canvas, y, meterX, meterWidth, "Voiced Prob.",
            state.VoicedProbability, state.VoicedEnabled, _voicedMeterPaint, out _voicedToggleRect);
        y += MeterHeight + MeterSpacing;

        // Unvoiced Energy
        DrawSignalRow(canvas, y, meterX, meterWidth, "Unvoiced Energy",
            state.UnvoicedEnergy, state.UnvoicedEnabled, _unvoicedMeterPaint, out _unvoicedToggleRect);
        y += MeterHeight + MeterSpacing;

        // Sibilance Energy
        DrawSignalRow(canvas, y, meterX, meterWidth, "Sibilance",
            state.SibilanceEnergy, state.SibilanceEnabled, _sibilanceMeterPaint, out _sibilanceToggleRect);

        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.Restore();
    }

    private void DrawTitleBar(SKCanvas canvas, SKSize size, SidechainTapState state)
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

        canvas.DrawText("Sidechain Tap", Padding, TitleBarHeight / 2f + 5, _titlePaint);

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

    private void DrawSignalRow(SKCanvas canvas, float y, float meterX, float meterWidth,
        string label, float value, bool enabled, SKPaint meterPaint, out SKRect toggleRect)
    {
        // Label
        canvas.DrawText(label, Padding, y + MeterHeight / 2 + 4, _labelPaint);

        // Meter
        var meterRect = new SKRect(meterX, y, meterX + meterWidth, y + MeterHeight);
        var meterRound = new SKRoundRect(meterRect, 4f);
        canvas.DrawRoundRect(meterRound, _meterBackgroundPaint);

        float fillWidth = (meterRect.Width - 4) * Math.Clamp(value, 0f, 1f);
        if (fillWidth > 1 && enabled)
        {
            var fillRect = new SKRect(meterRect.Left + 2, meterRect.Top + 2,
                meterRect.Left + 2 + fillWidth, meterRect.Bottom - 2);
            canvas.DrawRoundRect(new SKRoundRect(fillRect, 2f), meterPaint);
        }

        canvas.DrawRoundRect(meterRound, _borderPaint);

        // Value display
        canvas.DrawText($"{value:0.00}", meterRect.Right + 40f, y + MeterHeight / 2 + 4, _valuePaint);

        // Toggle button
        toggleRect = new SKRect(
            meterRect.Right + 50f,
            y + (MeterHeight - 18) / 2,
            meterRect.Right + 50f + ToggleWidth,
            y + (MeterHeight + 18) / 2);
        var toggleRound = new SKRoundRect(toggleRect, 4f);
        canvas.DrawRoundRect(toggleRound, enabled ? _toggleOnPaint : _toggleOffPaint);
        canvas.DrawRoundRect(toggleRound, _borderPaint);

        using var toggleTextPaint = new SkiaTextPaint(enabled ? _theme.PanelBackground : _theme.TextSecondary, 9f, SKFontStyle.Bold, SKTextAlign.Center);
        canvas.DrawText(enabled ? "ON" : "OFF", toggleRect.MidX, toggleRect.MidY + 3, toggleTextPaint);
    }

    public SidechainTapHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new SidechainTapHitTest(SidechainTapHitArea.CloseButton, -1);

        if (_bypassButtonRect.Contains(x, y))
            return new SidechainTapHitTest(SidechainTapHitArea.BypassButton, -1);

        if (_speechToggleRect.Contains(x, y))
            return new SidechainTapHitTest(SidechainTapHitArea.Toggle, 0);
        if (_voicedToggleRect.Contains(x, y))
            return new SidechainTapHitTest(SidechainTapHitArea.Toggle, 1);
        if (_unvoicedToggleRect.Contains(x, y))
            return new SidechainTapHitTest(SidechainTapHitArea.Toggle, 2);
        if (_sibilanceToggleRect.Contains(x, y))
            return new SidechainTapHitTest(SidechainTapHitArea.Toggle, 3);

        if (_titleBarRect.Contains(x, y))
            return new SidechainTapHitTest(SidechainTapHitArea.TitleBar, -1);

        return new SidechainTapHitTest(SidechainTapHitArea.None, -1);
    }

    public static SKSize GetPreferredSize() => new(380, 200);

    public void Dispose()
    {
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _meterBackgroundPaint.Dispose();
        _speechMeterPaint.Dispose();
        _voicedMeterPaint.Dispose();
        _unvoicedMeterPaint.Dispose();
        _sibilanceMeterPaint.Dispose();
        _toggleOnPaint.Dispose();
        _toggleOffPaint.Dispose();
        _labelPaint.Dispose();
        _valuePaint.Dispose();
        _latencyPaint.Dispose();
    }
}

public record struct SidechainTapState(
    float SpeechPresence,
    float VoicedProbability,
    float UnvoicedEnergy,
    float SibilanceEnergy,
    bool SpeechEnabled,
    bool VoicedEnabled,
    bool UnvoicedEnabled,
    bool SibilanceEnabled,
    float LatencyMs,
    bool IsBypassed);

public enum SidechainTapHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    Toggle
}

public record struct SidechainTapHitTest(SidechainTapHitArea Area, int ToggleIndex);
