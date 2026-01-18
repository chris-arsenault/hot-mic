using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Renderer for the Input Source plugin window.
/// Shows device selection list, channel mode, gain knob, and input meter.
/// </summary>
public sealed class InputSourceRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 12f;
    private const float CornerRadius = 10f;
    private const float DeviceListItemHeight = 28f;
    private const float MeterWidth = 20f;
    private const float KnobRadius = 32f;

    private readonly PluginComponentTheme _theme;

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _listBackgroundPaint;
    private readonly SKPaint _listItemHoverPaint;
    private readonly SKPaint _listItemSelectedPaint;
    private readonly SKPaint _modeButtonPaint;
    private readonly SKPaint _modeButtonActivePaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _closeButtonPaint;
    private readonly SkiaTextPaint _labelPaint;
    private readonly SkiaTextPaint _deviceNamePaint;
    private readonly SkiaTextPaint _deviceNameSelectedPaint;
    private readonly SkiaTextPaint _modeButtonTextPaint;
    private readonly SkiaTextPaint _modeButtonActiveTextPaint;

    private SKRect _titleBarRect;
    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _deviceListRect;
    private SKRect _modeLeftRect;
    private SKRect _modeRightRect;
    private SKRect _modeSumRect;
    private readonly List<SKRect> _deviceItemRects = new();

    private readonly LevelMeter _inputMeter;

    public KnobWidget GainKnob { get; }

    public InputSourceRenderer(PluginComponentTheme? theme = null)
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

        _listBackgroundPaint = new SKPaint
        {
            Color = _theme.WaveformBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _listItemHoverPaint = new SKPaint
        {
            Color = _theme.PanelBackgroundLight,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _listItemSelectedPaint = new SKPaint
        {
            Color = _theme.KnobArc.WithAlpha(80),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
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

        _titlePaint = new SkiaTextPaint(_theme.TextPrimary, 14f, SKFontStyle.Bold);
        _closeButtonPaint = new SkiaTextPaint(_theme.TextSecondary, 18f, SKFontStyle.Normal, SKTextAlign.Center);
        _labelPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Normal);
        _deviceNamePaint = new SkiaTextPaint(_theme.TextSecondary, 11f);
        _deviceNameSelectedPaint = new SkiaTextPaint(_theme.TextPrimary, 11f, SKFontStyle.Bold);
        _modeButtonTextPaint = new SkiaTextPaint(_theme.TextSecondary, 11f, SKFontStyle.Bold, SKTextAlign.Center);
        _modeButtonActiveTextPaint = new SkiaTextPaint(_theme.PanelBackground, 11f, SKFontStyle.Bold, SKTextAlign.Center);

        GainKnob = new KnobWidget(
            KnobRadius, -60f, 12f, "GAIN", "dB",
            KnobStyle.Bipolar with { TrackWidth = 6f, ArcWidth = 6f, PointerWidth = 3f },
            _theme)
        {
            ShowPositiveSign = true,
            ValueFormat = "0.0"
        };

        _inputMeter = new LevelMeter();
    }

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, InputSourceState state)
    {
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        canvas.Scale(dpiScale);
        size = new SKSize(size.Width / dpiScale, size.Height / dpiScale);

        _deviceItemRects.Clear();

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
        canvas.DrawText("Input Source", Padding, TitleBarHeight / 2f + 5, _titlePaint);

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

        // Device list label
        canvas.DrawText("INPUT DEVICE", Padding, contentTop + 10, _labelPaint);

        // Device list
        float listTop = contentTop + 18;
        float listHeight = Math.Min(state.Devices.Count * DeviceListItemHeight + 4, 120f);
        _deviceListRect = new SKRect(Padding, listTop, size.Width - Padding - MeterWidth - KnobRadius * 2 - 24, listTop + listHeight);
        var listRound = new SKRoundRect(_deviceListRect, 4f);
        canvas.DrawRoundRect(listRound, _listBackgroundPaint);
        canvas.DrawRoundRect(listRound, _borderPaint);

        // Clip to list area for device items
        canvas.Save();
        canvas.ClipRect(_deviceListRect);

        float itemY = _deviceListRect.Top + 2;
        for (int i = 0; i < state.Devices.Count && itemY < _deviceListRect.Bottom; i++)
        {
            var device = state.Devices[i];
            var itemRect = new SKRect(_deviceListRect.Left + 2, itemY, _deviceListRect.Right - 2, itemY + DeviceListItemHeight);
            _deviceItemRects.Add(itemRect);

            bool isSelected = device.Id == state.SelectedDeviceId;
            if (isSelected)
            {
                canvas.DrawRoundRect(new SKRoundRect(itemRect, 3f), _listItemSelectedPaint);
            }

            var textPaint = isSelected ? _deviceNameSelectedPaint : _deviceNamePaint;
            string displayName = TruncateText(device.Name, itemRect.Width - 8, textPaint);
            canvas.DrawText(displayName, itemRect.Left + 6, itemRect.MidY + 4, textPaint);

            itemY += DeviceListItemHeight;
        }

        canvas.Restore();

        // Channel mode section
        float modeTop = _deviceListRect.Bottom + Padding;
        canvas.DrawText("CHANNEL", Padding, modeTop + 10, _labelPaint);

        float modeButtonWidth = 44f;
        float modeButtonHeight = 26f;
        float modeY = modeTop + 16;
        float modeSpacing = 4f;

        _modeLeftRect = new SKRect(Padding, modeY, Padding + modeButtonWidth, modeY + modeButtonHeight);
        _modeRightRect = new SKRect(_modeLeftRect.Right + modeSpacing, modeY, _modeLeftRect.Right + modeSpacing + modeButtonWidth, modeY + modeButtonHeight);
        _modeSumRect = new SKRect(_modeRightRect.Right + modeSpacing, modeY, _modeRightRect.Right + modeSpacing + modeButtonWidth, modeY + modeButtonHeight);

        DrawModeButton(canvas, _modeLeftRect, "L", state.ChannelMode == InputChannelModeValue.Left);
        DrawModeButton(canvas, _modeRightRect, "R", state.ChannelMode == InputChannelModeValue.Right);
        DrawModeButton(canvas, _modeSumRect, "L+R", state.ChannelMode == InputChannelModeValue.Sum);

        // Gain knob (right side)
        float knobX = size.Width - Padding - KnobRadius - MeterWidth - 8;
        float knobY = contentTop + 60;
        GainKnob.Center = new SKPoint(knobX, knobY);
        GainKnob.Value = state.GainDb;
        GainKnob.Render(canvas);

        // Input meter (far right)
        float meterX = size.Width - Padding - MeterWidth;
        float meterHeight = size.Height - TitleBarHeight - Padding * 2 - 20;
        var meterRect = new SKRect(meterX, contentTop, meterX + MeterWidth, contentTop + meterHeight);
        _inputMeter.Update(state.InputLevel);
        _inputMeter.Render(canvas, meterRect, MeterOrientation.Vertical);

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

    public InputSourceHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new InputSourceHitTest(InputSourceHitArea.CloseButton);

        if (_bypassButtonRect.Contains(x, y))
            return new InputSourceHitTest(InputSourceHitArea.BypassButton);

        if (GainKnob.HitTest(x, y))
            return new InputSourceHitTest(InputSourceHitArea.GainKnob);

        if (_modeLeftRect.Contains(x, y))
            return new InputSourceHitTest(InputSourceHitArea.ModeLeft);

        if (_modeRightRect.Contains(x, y))
            return new InputSourceHitTest(InputSourceHitArea.ModeRight);

        if (_modeSumRect.Contains(x, y))
            return new InputSourceHitTest(InputSourceHitArea.ModeSum);

        for (int i = 0; i < _deviceItemRects.Count; i++)
        {
            if (_deviceItemRects[i].Contains(x, y))
                return new InputSourceHitTest(InputSourceHitArea.DeviceItem, i);
        }

        if (_titleBarRect.Contains(x, y))
            return new InputSourceHitTest(InputSourceHitArea.TitleBar);

        return new InputSourceHitTest(InputSourceHitArea.None);
    }

    public static SKSize GetPreferredSize() => new(300, 220);

    public void Dispose()
    {
        _inputMeter.Dispose();
        GainKnob.Dispose();
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _listBackgroundPaint.Dispose();
        _listItemHoverPaint.Dispose();
        _listItemSelectedPaint.Dispose();
        _modeButtonPaint.Dispose();
        _modeButtonActivePaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _labelPaint.Dispose();
        _deviceNamePaint.Dispose();
        _deviceNameSelectedPaint.Dispose();
        _modeButtonTextPaint.Dispose();
        _modeButtonActiveTextPaint.Dispose();
    }
}

public record struct InputSourceDevice(string Id, string Name);

public enum InputChannelModeValue
{
    Left,
    Right,
    Sum
}

public record struct InputSourceState(
    IReadOnlyList<InputSourceDevice> Devices,
    string SelectedDeviceId,
    InputChannelModeValue ChannelMode,
    float GainDb,
    float InputLevel,
    bool IsBypassed);

public enum InputSourceHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    GainKnob,
    ModeLeft,
    ModeRight,
    ModeSum,
    DeviceItem
}

public record struct InputSourceHitTest(InputSourceHitArea Area, int DeviceIndex = -1);
