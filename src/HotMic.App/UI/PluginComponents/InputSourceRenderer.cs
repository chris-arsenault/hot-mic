using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Renderer for the Input Source plugin window.
/// Shows device dropdown, channel mode buttons, gain knob, and input meter.
/// </summary>
public sealed class InputSourceRenderer : IDisposable
{
    private const float TitleBarHeight = 36f;
    private const float Padding = 10f;
    private const float CornerRadius = 8f;
    private const float DropdownHeight = 28f;
    private const float DropdownItemHeight = 26f;
    private const float MeterWidth = 16f;
    private const float KnobRadius = 28f;

    private readonly PluginComponentTheme _theme;

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _dropdownPaint;
    private readonly SKPaint _dropdownItemHoverPaint;
    private readonly SKPaint _modeButtonPaint;
    private readonly SKPaint _modeButtonActivePaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _closeButtonPaint;
    private readonly SkiaTextPaint _labelPaint;
    private readonly SkiaTextPaint _dropdownTextPaint;
    private readonly SkiaTextPaint _modeButtonTextPaint;
    private readonly SkiaTextPaint _modeButtonActiveTextPaint;

    private SKRect _titleBarRect;
    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _deviceDropdownRect;
    private SKRect _modeLeftRect;
    private SKRect _modeRightRect;
    private SKRect _modeSumRect;
    private readonly List<SKRect> _dropdownItemRects = new();
    private SKRect _dropdownListRect;

    private readonly LevelMeter _inputMeter;

    public KnobWidget GainKnob { get; }
    public bool IsDropdownExpanded { get; set; }

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

        _dropdownPaint = new SKPaint
        {
            Color = _theme.WaveformBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _dropdownItemHoverPaint = new SKPaint
        {
            Color = _theme.KnobArc.WithAlpha(60),
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

        _titlePaint = new SkiaTextPaint(_theme.TextPrimary, 13f, SKFontStyle.Bold);
        _closeButtonPaint = new SkiaTextPaint(_theme.TextSecondary, 16f, SKFontStyle.Normal, SKTextAlign.Center);
        _labelPaint = new SkiaTextPaint(_theme.TextSecondary, 9f, SKFontStyle.Normal);
        _dropdownTextPaint = new SkiaTextPaint(_theme.TextPrimary, 10f);
        _modeButtonTextPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Bold, SKTextAlign.Center);
        _modeButtonActiveTextPaint = new SkiaTextPaint(_theme.PanelBackground, 10f, SKFontStyle.Bold, SKTextAlign.Center);

        GainKnob = new KnobWidget(
            KnobRadius, -60f, 12f, "GAIN", "dB",
            KnobStyle.Bipolar with { TrackWidth = 5f, ArcWidth = 5f, PointerWidth = 2f },
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

        _dropdownItemRects.Clear();

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
        canvas.DrawText("Input", Padding, TitleBarHeight / 2f + 4, _titlePaint);

        // Bypass button
        float bypassWidth = 40f;
        _bypassButtonRect = new SKRect(
            size.Width - Padding - 22 - bypassWidth - 6,
            (TitleBarHeight - 20) / 2,
            size.Width - Padding - 22 - 6,
            (TitleBarHeight + 20) / 2);
        var bypassRound = new SKRoundRect(_bypassButtonRect, 3f);
        canvas.DrawRoundRect(bypassRound, state.IsBypassed ? _bypassActivePaint : _bypassPaint);
        canvas.DrawRoundRect(bypassRound, _borderPaint);

        using var bypassTextPaint = new SkiaTextPaint(state.IsBypassed ? _theme.TextPrimary : _theme.TextSecondary, 8f, SKFontStyle.Bold, SKTextAlign.Center);
        canvas.DrawText("BYP", _bypassButtonRect.MidX, _bypassButtonRect.MidY + 3, bypassTextPaint);

        // Close button
        _closeButtonRect = new SKRect(size.Width - Padding - 20, (TitleBarHeight - 20) / 2,
            size.Width - Padding, (TitleBarHeight + 20) / 2);
        canvas.DrawText("\u00D7", _closeButtonRect.MidX, _closeButtonRect.MidY + 5, _closeButtonPaint);

        float contentTop = TitleBarHeight + Padding;
        float rightColumnX = size.Width - Padding - MeterWidth - KnobRadius * 2 - 8;

        // Device dropdown
        canvas.DrawText("DEVICE", Padding, contentTop + 8, _labelPaint);
        float dropdownTop = contentTop + 12;
        _deviceDropdownRect = new SKRect(Padding, dropdownTop, rightColumnX - 8, dropdownTop + DropdownHeight);
        var dropdownRound = new SKRoundRect(_deviceDropdownRect, 4f);
        canvas.DrawRoundRect(dropdownRound, _dropdownPaint);
        canvas.DrawRoundRect(dropdownRound, _borderPaint);

        // Selected device text
        string selectedName = "No Device";
        foreach (var device in state.Devices)
        {
            if (device.Id == state.SelectedDeviceId)
            {
                selectedName = device.Name;
                break;
            }
        }
        string truncatedName = TruncateText(selectedName, _deviceDropdownRect.Width - 20, _dropdownTextPaint);
        canvas.DrawText(truncatedName, _deviceDropdownRect.Left + 6, _deviceDropdownRect.MidY + 4, _dropdownTextPaint);

        // Dropdown arrow
        float arrowX = _deviceDropdownRect.Right - 12;
        float arrowY = _deviceDropdownRect.MidY;
        using var arrowPaint = new SKPaint { Color = _theme.TextSecondary, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
        canvas.DrawLine(arrowX - 4, arrowY - 2, arrowX, arrowY + 2, arrowPaint);
        canvas.DrawLine(arrowX, arrowY + 2, arrowX + 4, arrowY - 2, arrowPaint);

        // Channel mode buttons
        float modeTop = _deviceDropdownRect.Bottom + Padding;
        canvas.DrawText("CHANNEL", Padding, modeTop + 8, _labelPaint);

        float modeButtonWidth = 36f;
        float modeButtonHeight = 22f;
        float modeY = modeTop + 12;
        float modeSpacing = 3f;

        _modeLeftRect = new SKRect(Padding, modeY, Padding + modeButtonWidth, modeY + modeButtonHeight);
        _modeRightRect = new SKRect(_modeLeftRect.Right + modeSpacing, modeY, _modeLeftRect.Right + modeSpacing + modeButtonWidth, modeY + modeButtonHeight);
        _modeSumRect = new SKRect(_modeRightRect.Right + modeSpacing, modeY, _modeRightRect.Right + modeSpacing + modeButtonWidth, modeY + modeButtonHeight);

        DrawModeButton(canvas, _modeLeftRect, "L", state.ChannelMode == InputChannelModeValue.Left);
        DrawModeButton(canvas, _modeRightRect, "R", state.ChannelMode == InputChannelModeValue.Right);
        DrawModeButton(canvas, _modeSumRect, "L+R", state.ChannelMode == InputChannelModeValue.Sum);

        // Gain knob (right side)
        float knobX = size.Width - Padding - KnobRadius - MeterWidth - 4;
        float knobY = contentTop + 30;
        GainKnob.Center = new SKPoint(knobX, knobY);
        GainKnob.Value = state.GainDb;
        GainKnob.Render(canvas);

        // Input meter (far right)
        float meterX = size.Width - Padding - MeterWidth;
        float meterHeight = size.Height - TitleBarHeight - Padding * 2 - 16;
        var meterRect = new SKRect(meterX, contentTop, meterX + MeterWidth, contentTop + meterHeight);
        _inputMeter.Update(state.InputLevel);
        _inputMeter.Render(canvas, meterRect, MeterOrientation.Vertical);

        // Dropdown list (if expanded)
        if (IsDropdownExpanded && state.Devices.Count > 0)
        {
            float maxListHeight = 150f;
            float listHeight = Math.Min(state.Devices.Count * DropdownItemHeight, maxListHeight);
            _dropdownListRect = new SKRect(_deviceDropdownRect.Left, _deviceDropdownRect.Bottom + 2, _deviceDropdownRect.Right, _deviceDropdownRect.Bottom + 2 + listHeight);

            canvas.DrawRoundRect(new SKRoundRect(_dropdownListRect, 4f), _dropdownPaint);
            canvas.DrawRoundRect(new SKRoundRect(_dropdownListRect, 4f), _borderPaint);

            canvas.Save();
            canvas.ClipRect(_dropdownListRect);

            float itemY = _dropdownListRect.Top;
            for (int i = 0; i < state.Devices.Count && itemY < _dropdownListRect.Bottom; i++)
            {
                var device = state.Devices[i];
                var itemRect = new SKRect(_dropdownListRect.Left, itemY, _dropdownListRect.Right, itemY + DropdownItemHeight);
                _dropdownItemRects.Add(itemRect);

                if (device.Id == state.SelectedDeviceId)
                {
                    canvas.DrawRect(itemRect, _dropdownItemHoverPaint);
                }

                string itemName = TruncateText(device.Name, itemRect.Width - 10, _dropdownTextPaint);
                canvas.DrawText(itemName, itemRect.Left + 6, itemRect.MidY + 4, _dropdownTextPaint);

                itemY += DropdownItemHeight;
            }

            canvas.Restore();
        }
        else
        {
            _dropdownListRect = SKRect.Empty;
        }

        // Outer border
        canvas.DrawRoundRect(roundRect, _borderPaint);

        canvas.Restore();
    }

    private void DrawModeButton(SKCanvas canvas, SKRect rect, string label, bool isActive)
    {
        var roundRect = new SKRoundRect(rect, 3f);
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
        // Check dropdown list items first (if expanded)
        if (IsDropdownExpanded)
        {
            for (int i = 0; i < _dropdownItemRects.Count; i++)
            {
                if (_dropdownItemRects[i].Contains(x, y))
                    return new InputSourceHitTest(InputSourceHitArea.DeviceItem, i);
            }

            // Click outside dropdown closes it
            if (!_dropdownListRect.IsEmpty && !_dropdownListRect.Contains(x, y) && !_deviceDropdownRect.Contains(x, y))
                return new InputSourceHitTest(InputSourceHitArea.CloseDropdown);
        }

        if (_closeButtonRect.Contains(x, y))
            return new InputSourceHitTest(InputSourceHitArea.CloseButton);

        if (_bypassButtonRect.Contains(x, y))
            return new InputSourceHitTest(InputSourceHitArea.BypassButton);

        if (_deviceDropdownRect.Contains(x, y))
            return new InputSourceHitTest(InputSourceHitArea.DeviceDropdown);

        if (GainKnob.HitTest(x, y))
            return new InputSourceHitTest(InputSourceHitArea.GainKnob);

        if (_modeLeftRect.Contains(x, y))
            return new InputSourceHitTest(InputSourceHitArea.ModeLeft);

        if (_modeRightRect.Contains(x, y))
            return new InputSourceHitTest(InputSourceHitArea.ModeRight);

        if (_modeSumRect.Contains(x, y))
            return new InputSourceHitTest(InputSourceHitArea.ModeSum);

        if (_titleBarRect.Contains(x, y))
            return new InputSourceHitTest(InputSourceHitArea.TitleBar);

        return new InputSourceHitTest(InputSourceHitArea.None);
    }

    public static SKSize GetPreferredSize() => new(240, 180);

    public void Dispose()
    {
        _inputMeter.Dispose();
        GainKnob.Dispose();
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _dropdownPaint.Dispose();
        _dropdownItemHoverPaint.Dispose();
        _modeButtonPaint.Dispose();
        _modeButtonActivePaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _labelPaint.Dispose();
        _dropdownTextPaint.Dispose();
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
    DeviceDropdown,
    DeviceItem,
    CloseDropdown,
    GainKnob,
    ModeLeft,
    ModeRight,
    ModeSum
}

public record struct InputSourceHitTest(InputSourceHitArea Area, int DeviceIndex = -1);
