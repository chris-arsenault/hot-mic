using HotMic.Core.Engine;
using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Renderer for the Output Send plugin window.
/// Shows send mode buttons (L/R/Both), output device info, and output meter.
/// </summary>
public sealed class OutputSendRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 12f;
    private const float CornerRadius = 10f;
    private const float MeterWidth = 20f;

    private readonly PluginComponentTheme _theme;

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _modeButtonPaint;
    private readonly SKPaint _modeButtonActivePaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _deviceBackgroundPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _closeButtonPaint;
    private readonly SkiaTextPaint _labelPaint;
    private readonly SkiaTextPaint _devicePaint;
    private readonly SkiaTextPaint _modeButtonTextPaint;
    private readonly SkiaTextPaint _modeButtonActiveTextPaint;

    private SKRect _titleBarRect;
    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _modeLeftRect;
    private SKRect _modeRightRect;
    private SKRect _modeBothRect;

    private readonly LevelMeter _outputMeter;

    public OutputSendRenderer(PluginComponentTheme? theme = null)
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

        _modeButtonPaint = new SKPaint
        {
            Color = _theme.PanelBackgroundLight,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _modeButtonActivePaint = new SKPaint
        {
            Color = _theme.KnobArc,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
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

        _deviceBackgroundPaint = new SKPaint
        {
            Color = _theme.WaveformBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _titlePaint = new SkiaTextPaint(_theme.TextPrimary, 14f, SKFontStyle.Bold);
        _closeButtonPaint = new SkiaTextPaint(_theme.TextSecondary, 18f, SKFontStyle.Normal, SKTextAlign.Center);
        _labelPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Normal);
        _devicePaint = new SkiaTextPaint(_theme.TextPrimary, 11f);
        _modeButtonTextPaint = new SkiaTextPaint(_theme.TextSecondary, 11f, SKFontStyle.Bold, SKTextAlign.Center);
        _modeButtonActiveTextPaint = new SkiaTextPaint(_theme.PanelBackground, 11f, SKFontStyle.Bold, SKTextAlign.Center);

        _outputMeter = new LevelMeter();
    }

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, OutputSendState state)
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
        canvas.DrawText("Output Send", Padding, TitleBarHeight / 2f + 5, _titlePaint);

        // Bypass button
        float bypassWidth = 50f;
        _bypassButtonRect = new SKRect(
            size.Width - Padding - 30 - bypassWidth - 8,
            (TitleBarHeight - 22) / 2,
            size.Width - Padding - 30 - 8,
            (TitleBarHeight + 22) / 2);
        var bypassRound = new SKRoundRect(_bypassButtonRect, 4f);
        canvas.DrawRoundRect(bypassRound, state.IsBypassed ? _bypassActivePaint : _bypassPaint);
        canvas.DrawRoundRect(bypassRound, _borderPaint);

        using var bypassTextPaint = new SkiaTextPaint(state.IsBypassed ? _theme.TextPrimary : _theme.TextSecondary, 9f, SKFontStyle.Bold, SKTextAlign.Center);
        canvas.DrawText("BYPASS", _bypassButtonRect.MidX, _bypassButtonRect.MidY + 3, bypassTextPaint);

        // Close button
        _closeButtonRect = new SKRect(size.Width - Padding - 24, (TitleBarHeight - 24) / 2,
            size.Width - Padding, (TitleBarHeight + 24) / 2);
        canvas.DrawText("\u00D7", _closeButtonRect.MidX, _closeButtonRect.MidY + 6, _closeButtonPaint);

        float contentTop = TitleBarHeight + Padding;

        // Output device label
        canvas.DrawText("OUTPUT DEVICE", Padding, contentTop + 10, _labelPaint);

        float deviceTop = contentTop + 18;
        var deviceRect = new SKRect(Padding, deviceTop, size.Width - Padding - MeterWidth - 8, deviceTop + 28);
        var deviceRound = new SKRoundRect(deviceRect, 4f);
        canvas.DrawRoundRect(deviceRound, _deviceBackgroundPaint);
        canvas.DrawRoundRect(deviceRound, _borderPaint);

        string deviceName = TruncateText(state.OutputDeviceName, deviceRect.Width - 12, _devicePaint);
        canvas.DrawText(deviceName, deviceRect.Left + 6, deviceRect.MidY + 4, _devicePaint);

        // Send mode section
        float modeTop = deviceRect.Bottom + Padding;
        canvas.DrawText("SEND MODE", Padding, modeTop + 10, _labelPaint);

        float modeButtonWidth = 50f;
        float modeButtonHeight = 26f;
        float modeY = modeTop + 16;
        float modeSpacing = 4f;

        _modeLeftRect = new SKRect(Padding, modeY, Padding + modeButtonWidth, modeY + modeButtonHeight);
        _modeRightRect = new SKRect(_modeLeftRect.Right + modeSpacing, modeY, _modeLeftRect.Right + modeSpacing + modeButtonWidth, modeY + modeButtonHeight);
        _modeBothRect = new SKRect(_modeRightRect.Right + modeSpacing, modeY, _modeRightRect.Right + modeSpacing + modeButtonWidth, modeY + modeButtonHeight);

        DrawModeButton(canvas, _modeLeftRect, "L", state.SendMode == OutputSendMode.Left);
        DrawModeButton(canvas, _modeRightRect, "R", state.SendMode == OutputSendMode.Right);
        DrawModeButton(canvas, _modeBothRect, "L+R", state.SendMode == OutputSendMode.Both);

        // Output meter (far right)
        float meterX = size.Width - Padding - MeterWidth;
        float meterHeight = size.Height - TitleBarHeight - Padding * 2 - 20;
        var meterRect = new SKRect(meterX, contentTop, meterX + MeterWidth, contentTop + meterHeight);
        _outputMeter.Update(state.OutputLevel);
        _outputMeter.Render(canvas, meterRect, MeterOrientation.Vertical);

        // Outer border
        canvas.DrawRoundRect(roundRect, _borderPaint);

        canvas.Restore();
    }

    private void DrawModeButton(SKCanvas canvas, SKRect rect, string label, bool isActive)
    {
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, isActive ? _modeButtonActivePaint : _modeButtonPaint);
        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.DrawText(label, rect.MidX, rect.MidY + 4, isActive ? _modeButtonActiveTextPaint : _modeButtonTextPaint);
    }

    private static string TruncateText(string text, float maxWidth, SkiaTextPaint paint)
    {
        if (paint.MeasureText(text) <= maxWidth)
            return text;

        int len = text.Length;
        while (len > 0 && paint.MeasureText(text[..len] + "..") > maxWidth)
            len--;

        return len > 0 ? text[..len] + ".." : "..";
    }

    public OutputSendHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new OutputSendHitTest(OutputSendHitArea.CloseButton);

        if (_bypassButtonRect.Contains(x, y))
            return new OutputSendHitTest(OutputSendHitArea.BypassButton);

        if (_modeLeftRect.Contains(x, y))
            return new OutputSendHitTest(OutputSendHitArea.ModeLeft);

        if (_modeRightRect.Contains(x, y))
            return new OutputSendHitTest(OutputSendHitArea.ModeRight);

        if (_modeBothRect.Contains(x, y))
            return new OutputSendHitTest(OutputSendHitArea.ModeBoth);

        if (_titleBarRect.Contains(x, y))
            return new OutputSendHitTest(OutputSendHitArea.TitleBar);

        return new OutputSendHitTest(OutputSendHitArea.None);
    }

    public static SKSize GetPreferredSize() => new(250, 170);

    public void Dispose()
    {
        _outputMeter.Dispose();
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _modeButtonPaint.Dispose();
        _modeButtonActivePaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _deviceBackgroundPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _labelPaint.Dispose();
        _devicePaint.Dispose();
        _modeButtonTextPaint.Dispose();
        _modeButtonActiveTextPaint.Dispose();
    }
}

public record struct OutputSendState(
    string OutputDeviceName,
    OutputSendMode SendMode,
    float OutputLevel,
    bool IsBypassed);

public enum OutputSendHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    ModeLeft,
    ModeRight,
    ModeBoth
}

public record struct OutputSendHitTest(OutputSendHitArea Area);
