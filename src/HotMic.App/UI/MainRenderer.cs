using System;
using System.Collections.Generic;
using System.Globalization;
using HotMic.App.ViewModels;
using HotMic.Common.Configuration;
using HotMic.Common.Models;
using SkiaSharp;

namespace HotMic.App.UI;

public sealed class MainRenderer
{
    private const float CornerRadius = 12f;
    private const float TitleBarHeight = 52f;
    private const float Padding = 16f;
    private const float PanelRadius = 10f;
    private const float PanelPadding = 12f;
    private const float ContentSpacing = 16f;
    private const float DevicePanelHeight = 210f;
    private const float KnobSize = 68f;
    private const float KnobLabelHeight = 28f;
    private const float MeterWidth = 36f;
    private const float MeterHeight = 86f;
    private const float SlotSpacing = 6f;
    private const float MinSlotHeight = 22f;
    private const float MaxSlotHeight = 40f;
    private const float ToggleSize = 26f;
    private const float TopButtonSize = 26f;
    private const float DebugPanelRadius = 8f;
    private const float DebugLineHeight = 14f;

    private readonly HotMicTheme _theme = HotMicTheme.Default;
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _panelPaint;
    private readonly SKPaint _panelBorderPaint;
    private readonly SKPaint _surfacePaint;
    private readonly SKPaint _surfaceBorderPaint;
    private readonly SKPaint _accentPaint;
    private readonly SKPaint _textPaint;
    private readonly SKPaint _secondaryTextPaint;
    private readonly SKPaint _titlePaint;
    private readonly SKPaint _smallTextPaint;
    private readonly SKPaint _buttonTextPaint;
    private readonly SKPaint _buttonFillPaint;
    private readonly SKPaint _buttonActivePaint;
    private readonly SKPaint _debugPanelPaint;
    private readonly SKPaint _debugBorderPaint;
    private readonly SKPaint _debugTextPaint;
    private readonly SKPaint _meterGreenPaint;
    private readonly SKPaint _meterYellowPaint;
    private readonly SKPaint _meterRedPaint;
    private readonly SKPaint _meterPeakPaint;
    private readonly SKPaint _tickPaint;
    private readonly SKPaint _tickLabelPaint;
    private readonly SKPaint _iconStrokePaint;

    private readonly Dictionary<MainButton, SKRect> _topButtonRects = new();
    private readonly Dictionary<DevicePickerTarget, SKRect> _devicePickerRects = new();
    private readonly List<DeviceItemRect> _deviceItemRects = new();
    private readonly List<KnobRect> _knobRects = new();
    private readonly List<PluginSlotRect> _pluginSlots = new();
    private readonly List<ToggleRect> _toggleRects = new();
    private SKRect _applyDevicesRect;
    private SKRect _titleBarRect;
    private SKRect _deviceListRect;

    public MainRenderer()
    {
        _backgroundPaint = CreateFillPaint(_theme.BackgroundPrimary);
        _panelPaint = CreateFillPaint(_theme.BackgroundSecondary);
        _panelBorderPaint = CreateStrokePaint(_theme.Border, 1f);
        _surfacePaint = CreateFillPaint(_theme.BackgroundTertiary);
        _surfaceBorderPaint = CreateStrokePaint(_theme.Border, 1f);
        _accentPaint = CreateFillPaint(_theme.Accent);
        _textPaint = CreateTextPaint(_theme.TextPrimary, 14f);
        _secondaryTextPaint = CreateTextPaint(_theme.TextSecondary, 12f);
        _titlePaint = CreateTextPaint(_theme.TextPrimary, 18f, SKFontStyle.Bold);
        _smallTextPaint = CreateTextPaint(_theme.TextSecondary, 10f);
        _buttonTextPaint = CreateCenteredTextPaint(_theme.TextPrimary, 12f, SKFontStyle.Bold);
        _buttonFillPaint = CreateFillPaint(_theme.Surface);
        _buttonActivePaint = CreateFillPaint(_theme.Accent);
        _debugPanelPaint = CreateFillPaint(new SKColor(0x16, 0x16, 0x16, 0xDD));
        _debugBorderPaint = CreateStrokePaint(_theme.Border, 1f);
        _debugTextPaint = CreateTextPaint(_theme.TextSecondary, 11f);
        _meterGreenPaint = CreateFillPaint(_theme.MeterGreen);
        _meterYellowPaint = CreateFillPaint(_theme.MeterYellow);
        _meterRedPaint = CreateFillPaint(_theme.MeterRed);
        _meterPeakPaint = CreateStrokePaint(_theme.TextPrimary, 2f);
        _tickPaint = CreateStrokePaint(_theme.Border, 1f);
        _tickLabelPaint = CreateTextPaint(_theme.TextSecondary, 9f);
        _iconStrokePaint = CreateStrokePaint(_theme.TextPrimary, 2f);
    }

    public float DeviceListContentHeight { get; private set; }

    public float DeviceListViewportHeight { get; private set; }

    public void Render(SKCanvas canvas, SKSize size, MainViewModel viewModel, MainUiState uiState, float dpiScale)
    {
        ClearHitTargets();
        canvas.Clear(SKColors.Transparent);

        canvas.Save();
        canvas.Scale(dpiScale);
        size = new SKSize(size.Width / dpiScale, size.Height / dpiScale);

        DrawBackground(canvas, size);
        DrawTitleBar(canvas, size, viewModel);

        if (viewModel.IsMinimalView)
        {
            DrawMinimal(canvas, size, viewModel);
        }
        else
        {
            DrawFull(canvas, size, viewModel, uiState);
        }

        DrawDebugOverlay(canvas, size, viewModel);

        canvas.Restore();
    }

    public MainButton? HitTestTopButton(float x, float y)
    {
        foreach (var (button, rect) in _topButtonRects)
        {
            if (rect.Contains(x, y))
            {
                return button;
            }
        }

        return null;
    }

    public DevicePickerTarget HitTestDevicePicker(float x, float y)
    {
        foreach (var (target, rect) in _devicePickerRects)
        {
            if (rect.Contains(x, y))
            {
                return target;
            }
        }

        return DevicePickerTarget.None;
    }

    public DeviceItemHit? HitTestDeviceItem(float x, float y)
    {
        foreach (var item in _deviceItemRects)
        {
            if (item.Rect.Contains(x, y))
            {
                return new DeviceItemHit(item.Target, item.Index);
            }
        }

        return null;
    }

    public KnobHit? HitTestKnob(float x, float y)
    {
        foreach (var knob in _knobRects)
        {
            if (knob.Rect.Contains(x, y))
            {
                return new KnobHit(knob.ChannelIndex, knob.KnobType);
            }
        }

        return null;
    }

    public PluginSlotHit? HitTestPluginSlot(float x, float y, out PluginSlotRegion region)
    {
        foreach (var slot in _pluginSlots)
        {
            if (!slot.Rect.Contains(x, y))
            {
                continue;
            }

            if (slot.BypassRect.Contains(x, y))
            {
                region = PluginSlotRegion.Bypass;
                return new PluginSlotHit(slot.ChannelIndex, slot.SlotIndex);
            }

            if (slot.RemoveRect.Contains(x, y))
            {
                region = PluginSlotRegion.Remove;
                return new PluginSlotHit(slot.ChannelIndex, slot.SlotIndex);
            }

            region = PluginSlotRegion.Action;
            return new PluginSlotHit(slot.ChannelIndex, slot.SlotIndex);
        }

        region = PluginSlotRegion.None;
        return null;
    }

    public ToggleHit? HitTestToggle(float x, float y)
    {
        foreach (var toggle in _toggleRects)
        {
            if (toggle.Rect.Contains(x, y))
            {
                return new ToggleHit(toggle.ChannelIndex, toggle.ToggleType);
            }
        }

        return null;
    }

    public bool HitTestApplyDevices(float x, float y) => _applyDevicesRect.Contains(x, y);

    public bool HitTestTitleBar(float x, float y) => _titleBarRect.Contains(x, y);

    public bool HitTestDeviceList(float x, float y) => !_deviceListRect.IsEmpty && _deviceListRect.Contains(x, y);

    private void DrawBackground(SKCanvas canvas, SKSize size)
    {
        var rect = new SKRoundRect(new SKRect(0, 0, size.Width, size.Height), CornerRadius);
        canvas.DrawRoundRect(rect, _backgroundPaint);
        canvas.DrawRoundRect(rect, _panelBorderPaint);
    }

    private void DrawTitleBar(SKCanvas canvas, SKSize size, MainViewModel viewModel)
    {
        _titleBarRect = new SKRect(0, 0, size.Width, TitleBarHeight);
        canvas.DrawRect(_titleBarRect, _panelPaint);
        canvas.DrawLine(0, TitleBarHeight, size.Width, TitleBarHeight, _panelBorderPaint);

        float titleX = Padding;
        float titleY = TitleBarHeight / 2f + _titlePaint.TextSize / 2.5f;
        canvas.DrawText("HotMic", titleX, titleY, _titlePaint);

        if (!string.IsNullOrWhiteSpace(viewModel.StatusMessage))
        {
            var statusPaint = CreateTextPaint(_theme.Accent, 12f);
            canvas.DrawText(viewModel.StatusMessage, titleX + 88f, titleY - 2f, statusPaint);
        }

        DrawTopButtons(canvas, size, viewModel);
    }

    private void DrawTopButtons(SKCanvas canvas, SKSize size, MainViewModel viewModel)
    {
        _topButtonRects.Clear();
        float right = size.Width - Padding;
        float centerY = TitleBarHeight / 2f;
        float buttonSpacing = 8f;

        SKRect closeRect = new(right - TopButtonSize, centerY - TopButtonSize / 2f, right, centerY + TopButtonSize / 2f);
        DrawIconButton(canvas, closeRect, string.Empty, MainButton.Close, isActive: false, IconType.Close);
        right -= TopButtonSize + buttonSpacing;

        SKRect minimizeRect = new(right - TopButtonSize, centerY - TopButtonSize / 2f, right, centerY + TopButtonSize / 2f);
        DrawIconButton(canvas, minimizeRect, string.Empty, MainButton.Minimize, isActive: false, IconType.Minimize);
        right -= TopButtonSize + buttonSpacing;

        SKRect pinRect = new(right - TopButtonSize, centerY - TopButtonSize / 2f, right, centerY + TopButtonSize / 2f);
        DrawIconButton(canvas, pinRect, string.Empty, MainButton.Pin, isActive: viewModel.AlwaysOnTop, IconType.Pin);
        right -= TopButtonSize + buttonSpacing;

        SKRect settingsRect = new(right - TopButtonSize, centerY - TopButtonSize / 2f, right, centerY + TopButtonSize / 2f);
        DrawIconButton(canvas, settingsRect, string.Empty, MainButton.Settings, isActive: false, IconType.Settings);
        right -= TopButtonSize + buttonSpacing;

        string viewLabel = viewModel.IsMinimalView ? "Full" : "Minimal";
        float toggleWidth = 80f;
        SKRect viewRect = new(right - toggleWidth, centerY - TopButtonSize / 2f, right, centerY + TopButtonSize / 2f);
        DrawTextButton(canvas, viewRect, viewLabel, viewModel.IsMinimalView, MainButton.ToggleView);
    }

    private void DrawFull(SKCanvas canvas, SKSize size, MainViewModel viewModel, MainUiState uiState)
    {
        float contentTop = TitleBarHeight + Padding;
        float contentBottom = size.Height - Padding;
        float contentLeft = Padding;
        float contentRight = size.Width - Padding;

        var devicePanelRect = new SKRect(contentLeft, contentTop, contentRight, contentTop + DevicePanelHeight);
        DrawDevicePanel(canvas, devicePanelRect, viewModel, uiState);

        float channelTop = devicePanelRect.Bottom + ContentSpacing;
        float channelHeight = contentBottom - channelTop;
        float channelWidth = (contentRight - contentLeft - ContentSpacing) / 2f;

        var channel1Rect = new SKRect(contentLeft, channelTop, contentLeft + channelWidth, channelTop + channelHeight);
        var channel2Rect = new SKRect(contentLeft + channelWidth + ContentSpacing, channelTop, contentRight, channelTop + channelHeight);

        DrawChannelStrip(canvas, channel1Rect, viewModel.Channel1, 0);
        DrawChannelStrip(canvas, channel2Rect, viewModel.Channel2, 1);

        DrawDeviceListOverlay(canvas, viewModel, uiState);
    }

    private void DrawMinimal(SKCanvas canvas, SKSize size, MainViewModel viewModel)
    {
        float panelWidth = size.Width - Padding * 2;
        float panelHeight = size.Height - TitleBarHeight - Padding * 2;
        var panelRect = new SKRect(Padding, TitleBarHeight + Padding, Padding + panelWidth, TitleBarHeight + Padding + panelHeight);

        var panelRound = new SKRoundRect(panelRect, PanelRadius);
        canvas.DrawRoundRect(panelRound, _panelPaint);
        canvas.DrawRoundRect(panelRound, _panelBorderPaint);

        float rowHeight = 32f;
        float rowSpacing = 10f;
        float startY = panelRect.Top + PanelPadding;

        DrawMinimalRow(canvas, panelRect, startY, viewModel.Channel1);
        DrawMinimalRow(canvas, panelRect, startY + rowHeight + rowSpacing, viewModel.Channel2);
    }

    private void DrawDebugOverlay(SKCanvas canvas, SKSize size, MainViewModel viewModel)
    {
        var lines = viewModel.DebugLines;
        if (lines.Count == 0)
        {
            return;
        }

        float padding = 8f;
        float maxWidth = 0f;
        foreach (var line in lines)
        {
            float width = _debugTextPaint.MeasureText(line);
            if (width > maxWidth)
            {
                maxWidth = width;
            }
        }

        float panelWidth = MathF.Min(size.Width - Padding * 2f, MathF.Max(220f, maxWidth + padding * 2f));
        float panelHeight = lines.Count * DebugLineHeight + padding * 2f;
        float x = Padding;
        float y = size.Height - Padding - panelHeight;
        float minY = TitleBarHeight + Padding;
        if (y < minY)
        {
            y = minY;
        }

        var rect = new SKRect(x, y, x + panelWidth, y + panelHeight);
        var roundRect = new SKRoundRect(rect, DebugPanelRadius);
        canvas.DrawRoundRect(roundRect, _debugPanelPaint);
        canvas.DrawRoundRect(roundRect, _debugBorderPaint);

        float textY = y + padding + DebugLineHeight - 3f;
        float textX = x + padding;
        foreach (var line in lines)
        {
            canvas.DrawText(line, textX, textY, _debugTextPaint);
            textY += DebugLineHeight;
        }
    }

    private void DrawMinimalRow(SKCanvas canvas, SKRect panelRect, float y, ChannelStripViewModel channel)
    {
        float nameWidth = 60f;
        float valueWidth = 52f;
        float meterLeft = panelRect.Left + PanelPadding + nameWidth;
        float meterRight = panelRect.Right - PanelPadding - valueWidth;
        float meterHeight = 16f;
        float meterTop = y + 6f;

        canvas.DrawText(channel.Name, panelRect.Left + PanelPadding, y + 16f, _textPaint);

        var meterRect = new SKRect(meterLeft, meterTop, meterRight, meterTop + meterHeight);
        DrawHorizontalMeter(canvas, meterRect, channel.OutputPeakLevel, channel.OutputRmsLevel);

        canvas.DrawText(channel.PeakDbLabel, meterRight + 8f, y + 16f, _secondaryTextPaint);
    }

    private void DrawDevicePanel(SKCanvas canvas, SKRect rect, MainViewModel viewModel, MainUiState uiState)
    {
        var roundRect = new SKRoundRect(rect, PanelRadius);
        canvas.DrawRoundRect(roundRect, _panelPaint);
        canvas.DrawRoundRect(roundRect, _panelBorderPaint);

        _devicePickerRects.Clear();
        _deviceItemRects.Clear();
        _deviceListRect = SKRect.Empty;
        DeviceListContentHeight = 0f;
        DeviceListViewportHeight = 0f;

        float labelHeight = 14f;
        float fieldHeight = 30f;
        float columnSpacing = 12f;
        float rowSpacing = 14f;
        float applyWidth = 80f;
        float rowHeight = labelHeight + 6f + fieldHeight;
        float row1Top = rect.Top + PanelPadding;
        float row2Top = row1Top + rowHeight + rowSpacing;
        float row3Top = row2Top + rowHeight + rowSpacing;

        float x = rect.Left + PanelPadding;
        float fieldsWidth = rect.Width - PanelPadding * 2 - applyWidth - columnSpacing;
        float fieldWidth = (fieldsWidth - columnSpacing * 3) / 4f;

        float labelY = row1Top + labelHeight;
        float fieldTop = row1Top + labelHeight + 6f;

        DrawPickerField(canvas, "Input 1", labelY, viewModel.SelectedInputDevice1?.Name ?? "Select...", new SKRect(x, fieldTop, x + fieldWidth, fieldTop + fieldHeight), DevicePickerTarget.Input1);
        x += fieldWidth + columnSpacing;
        DrawPickerField(canvas, "Input 2", labelY, viewModel.SelectedInputDevice2?.Name ?? "Select...", new SKRect(x, fieldTop, x + fieldWidth, fieldTop + fieldHeight), DevicePickerTarget.Input2);
        x += fieldWidth + columnSpacing;
        DrawPickerField(canvas, "Output", labelY, viewModel.SelectedOutputDevice?.Name ?? "Select...", new SKRect(x, fieldTop, x + fieldWidth, fieldTop + fieldHeight), DevicePickerTarget.Output);
        x += fieldWidth + columnSpacing;
        DrawPickerField(canvas, "Monitor", labelY, viewModel.SelectedMonitorDevice?.Name ?? "Select...", new SKRect(x, fieldTop, x + fieldWidth, fieldTop + fieldHeight), DevicePickerTarget.Monitor);

        var applyRect = new SKRect(rect.Right - PanelPadding - applyWidth, fieldTop, rect.Right - PanelPadding, fieldTop + fieldHeight);
        _applyDevicesRect = applyRect;
        DrawPanelButton(canvas, applyRect, "Apply", isActive: false);

        float row2LabelY = row2Top + labelHeight;
        float row2FieldTop = row2Top + labelHeight + 6f;
        float row2FieldWidth = (rect.Width - PanelPadding * 2 - columnSpacing) / 2f;
        x = rect.Left + PanelPadding;

        DrawPickerField(canvas, "Sample Rate", row2LabelY, FormatSampleRate(viewModel.SelectedSampleRate),
            new SKRect(x, row2FieldTop, x + row2FieldWidth, row2FieldTop + fieldHeight), DevicePickerTarget.SampleRate);
        x += row2FieldWidth + columnSpacing;
        DrawPickerField(canvas, "Buffer", row2LabelY, $"{viewModel.SelectedBufferSize} samples",
            new SKRect(x, row2FieldTop, x + row2FieldWidth, row2FieldTop + fieldHeight), DevicePickerTarget.BufferSize);

        float row3LabelY = row3Top + labelHeight;
        float row3FieldTop = row3Top + labelHeight + 6f;
        float row3FieldWidth = (rect.Width - PanelPadding * 2 - columnSpacing * 2) / 3f;
        x = rect.Left + PanelPadding;

        DrawPickerField(canvas, "Input 1 Chan", row3LabelY, FormatInputChannel(viewModel.SelectedInput1Channel),
            new SKRect(x, row3FieldTop, x + row3FieldWidth, row3FieldTop + fieldHeight), DevicePickerTarget.Input1Channel);
        x += row3FieldWidth + columnSpacing;
        DrawPickerField(canvas, "Input 2 Chan", row3LabelY, FormatInputChannel(viewModel.SelectedInput2Channel),
            new SKRect(x, row3FieldTop, x + row3FieldWidth, row3FieldTop + fieldHeight), DevicePickerTarget.Input2Channel);
        x += row3FieldWidth + columnSpacing;
        DrawPickerField(canvas, "Output Route", row3LabelY, FormatOutputRouting(viewModel.SelectedOutputRouting),
            new SKRect(x, row3FieldTop, x + row3FieldWidth, row3FieldTop + fieldHeight), DevicePickerTarget.OutputRouting);

    }

    private void DrawDeviceListOverlay(SKCanvas canvas, MainViewModel viewModel, MainUiState uiState)
    {
        if (uiState.ActiveDevicePicker == DevicePickerTarget.None)
        {
            return;
        }

        if (_devicePickerRects.TryGetValue(uiState.ActiveDevicePicker, out var fieldRect))
        {
            DrawDeviceList(canvas, fieldRect, viewModel, uiState);
        }
    }

    private void DrawPickerField(SKCanvas canvas, string label, float labelY, string displayText, SKRect rect, DevicePickerTarget target)
    {
        _devicePickerRects[target] = rect;

        canvas.DrawText(label, rect.Left, labelY, _secondaryTextPaint);
        canvas.DrawRoundRect(new SKRoundRect(rect, 6f), _surfacePaint);
        canvas.DrawRoundRect(new SKRoundRect(rect, 6f), _surfaceBorderPaint);

        DrawEllipsizedText(canvas, displayText, rect.Left + 8f, rect.MidY + 4f, rect.Width - 16f, _textPaint);

        DrawChevron(canvas, new SKPoint(rect.Right - 14f, rect.MidY), 6f);
    }

    private void DrawDeviceList(SKCanvas canvas, SKRect anchor, MainViewModel viewModel, MainUiState uiState)
    {
        var items = GetPickerItems(viewModel, uiState.ActiveDevicePicker, out int selectedIndex);

        float itemHeight = 26f;
        float maxHeight = 200f;
        float listWidth = MathF.Max(anchor.Width, 240f);
        float listLeft = anchor.Left;
        float listTop = anchor.Bottom + 6f;
        float contentHeight = items.Count * itemHeight;
        float listHeight = MathF.Min(maxHeight, MathF.Max(itemHeight, contentHeight));

        var listRect = new SKRect(listLeft, listTop, listLeft + listWidth, listTop + listHeight);
        _deviceListRect = listRect;
        DeviceListContentHeight = contentHeight;
        DeviceListViewportHeight = listHeight;

        var roundRect = new SKRoundRect(listRect, 8f);
        canvas.DrawRoundRect(roundRect, _panelPaint);
        canvas.DrawRoundRect(roundRect, _panelBorderPaint);

        canvas.Save();
        canvas.ClipRect(listRect);

        float scroll = MathF.Max(0f, uiState.DevicePickerScroll);
        float y = listTop - scroll;
        _deviceItemRects.Clear();

        for (int i = 0; i < items.Count; i++)
        {
            var itemRect = new SKRect(listLeft, y, listLeft + listWidth, y + itemHeight);
            if (itemRect.Bottom >= listTop && itemRect.Top <= listRect.Bottom)
            {
                bool isSelected = i == selectedIndex;
                if (isSelected)
                {
                    canvas.DrawRect(itemRect, _surfacePaint);
                }

                DrawEllipsizedText(canvas, items[i], itemRect.Left + 8f, itemRect.MidY + 4f, itemRect.Width - 16f, _textPaint);
                _deviceItemRects.Add(new DeviceItemRect(uiState.ActiveDevicePicker, i, itemRect));
            }

            y += itemHeight;
        }

        canvas.Restore();
    }

    private static IReadOnlyList<string> GetPickerItems(MainViewModel viewModel, DevicePickerTarget target, out int selectedIndex)
    {
        selectedIndex = -1;
        switch (target)
        {
            case DevicePickerTarget.Input1:
                return BuildDeviceList(viewModel.InputDevices, viewModel.SelectedInputDevice1, out selectedIndex);
            case DevicePickerTarget.Input2:
                return BuildDeviceList(viewModel.InputDevices, viewModel.SelectedInputDevice2, out selectedIndex);
            case DevicePickerTarget.Output:
                return BuildDeviceList(viewModel.OutputDevices, viewModel.SelectedOutputDevice, out selectedIndex);
            case DevicePickerTarget.Monitor:
                return BuildDeviceList(viewModel.OutputDevices, viewModel.SelectedMonitorDevice, out selectedIndex);
            case DevicePickerTarget.SampleRate:
                return BuildOptionList(viewModel.SampleRateOptions, viewModel.SelectedSampleRate, FormatSampleRate, out selectedIndex);
            case DevicePickerTarget.BufferSize:
                return BuildOptionList(viewModel.BufferSizeOptions, viewModel.SelectedBufferSize, size => $"{size} samples", out selectedIndex);
            case DevicePickerTarget.Input1Channel:
                return BuildEnumList(viewModel.InputChannelOptions, viewModel.SelectedInput1Channel, FormatInputChannel, out selectedIndex);
            case DevicePickerTarget.Input2Channel:
                return BuildEnumList(viewModel.InputChannelOptions, viewModel.SelectedInput2Channel, FormatInputChannel, out selectedIndex);
            case DevicePickerTarget.OutputRouting:
                return BuildEnumList(viewModel.OutputRoutingOptions, viewModel.SelectedOutputRouting, FormatOutputRouting, out selectedIndex);
            default:
                return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> BuildDeviceList(IReadOnlyList<AudioDevice> devices, AudioDevice? selected, out int selectedIndex)
    {
        selectedIndex = -1;
        var list = new List<string>(devices.Count);
        for (int i = 0; i < devices.Count; i++)
        {
            var device = devices[i];
            list.Add(device.Name);
            if (selected is not null && device.Id == selected.Id)
            {
                selectedIndex = i;
            }
        }

        return list;
    }

    private static IReadOnlyList<string> BuildOptionList(IReadOnlyList<int> options, int selected, Func<int, string> formatter, out int selectedIndex)
    {
        selectedIndex = -1;
        var list = new List<string>(options.Count);
        for (int i = 0; i < options.Count; i++)
        {
            int value = options[i];
            list.Add(formatter(value));
            if (value == selected)
            {
                selectedIndex = i;
            }
        }

        return list;
    }

    private static IReadOnlyList<string> BuildEnumList<T>(IReadOnlyList<T> options, T selected, Func<T, string> formatter, out int selectedIndex)
        where T : struct
    {
        selectedIndex = -1;
        var list = new List<string>(options.Count);
        for (int i = 0; i < options.Count; i++)
        {
            var value = options[i];
            list.Add(formatter(value));
            if (EqualityComparer<T>.Default.Equals(value, selected))
            {
                selectedIndex = i;
            }
        }

        return list;
    }

    private static string FormatSampleRate(int value)
    {
        if (value >= 1000)
        {
            return $"{value / 1000f:0.#} kHz";
        }

        return $"{value} Hz";
    }

    private static string FormatInputChannel(InputChannelMode mode) => mode switch
    {
        InputChannelMode.Left => "Left",
        InputChannelMode.Right => "Right",
        _ => "Sum (L+R)"
    };

    private static string FormatOutputRouting(OutputRoutingMode mode) => mode switch
    {
        OutputRoutingMode.Sum => "Sum (L+R)",
        _ => "Split (1=L, 2=R)"
    };

    private void DrawChannelStrip(SKCanvas canvas, SKRect rect, ChannelStripViewModel channel, int channelIndex)
    {
        var card = new SKRoundRect(rect, PanelRadius);
        canvas.DrawRoundRect(card, _panelPaint);
        canvas.DrawRoundRect(card, _panelBorderPaint);

        float x = rect.Left + PanelPadding;
        float y = rect.Top + PanelPadding;
        float innerRight = rect.Right - PanelPadding;

        canvas.DrawText(channel.Name, x, y + _titlePaint.TextSize, _titlePaint);
        y += _titlePaint.TextSize + 8f;

        float rowHeight = MathF.Max(KnobSize + KnobLabelHeight, MeterHeight);
        var inputKnobRect = new SKRect(x, y, x + KnobSize, y + rowHeight);
        DrawKnob(canvas, inputKnobRect, channel.InputGainDb, -60f, 12f, "INPUT");
        _knobRects.Add(new KnobRect(channelIndex, KnobType.InputGain, inputKnobRect));

        var inputMeterRect = new SKRect(innerRight - MeterWidth, y, innerRight, y + MeterHeight);
        DrawVerticalMeter(canvas, inputMeterRect, channel.InputPeakLevel, channel.InputRmsLevel, showTicks: true);

        y += rowHeight + 10f;

        float outputRowHeight = rowHeight;
        float muteRowHeight = 32f;
        float outputRowBottom = rect.Bottom - PanelPadding - muteRowHeight - 6f;
        float pluginAreaBottom = outputRowBottom - 10f;
        float pluginAreaHeight = MathF.Max(0f, pluginAreaBottom - y);
        float slotHeight = (pluginAreaHeight - SlotSpacing * 4) / 5f;
        slotHeight = Math.Clamp(slotHeight, MinSlotHeight, MaxSlotHeight);
        float slotTotalHeight = slotHeight * 5 + SlotSpacing * 4;
        float slotStartY = y + MathF.Max(0f, (pluginAreaHeight - slotTotalHeight) / 2f);

        for (int i = 0; i < channel.PluginSlots.Count; i++)
        {
            if (i >= 5)
            {
                break;
            }

            var slotRect = new SKRect(x, slotStartY, innerRight, slotStartY + slotHeight);
            DrawPluginSlot(canvas, slotRect, channel.PluginSlots[i], channelIndex, i);
            slotStartY += slotHeight + SlotSpacing;
        }

        y = outputRowBottom - outputRowHeight;
        var outputKnobRect = new SKRect(x, y, x + KnobSize, y + rowHeight);
        DrawKnob(canvas, outputKnobRect, channel.OutputGainDb, -60f, 12f, "OUTPUT");
        _knobRects.Add(new KnobRect(channelIndex, KnobType.OutputGain, outputKnobRect));

        var outputMeterRect = new SKRect(innerRight - MeterWidth, y, innerRight, y + MeterHeight);
        DrawVerticalMeter(canvas, outputMeterRect, channel.OutputPeakLevel, channel.OutputRmsLevel, showTicks: true);

        DrawToggleButtons(canvas, rect, channel, channelIndex);
    }

    private void DrawToggleButtons(SKCanvas canvas, SKRect rect, ChannelStripViewModel channel, int channelIndex)
    {
        float centerX = rect.MidX;
        float y = rect.Bottom - PanelPadding - ToggleSize;
        float spacing = 10f;

        var muteRect = new SKRect(centerX - ToggleSize - spacing / 2f, y, centerX - spacing / 2f, y + ToggleSize);
        DrawToggle(canvas, muteRect, "M", channel.IsMuted);
        _toggleRects.Add(new ToggleRect(channelIndex, ToggleType.Mute, muteRect));

        var soloRect = new SKRect(centerX + spacing / 2f, y, centerX + spacing / 2f + ToggleSize, y + ToggleSize);
        DrawToggle(canvas, soloRect, "S", channel.IsSoloed);
        _toggleRects.Add(new ToggleRect(channelIndex, ToggleType.Solo, soloRect));
    }

    private void DrawToggle(SKCanvas canvas, SKRect rect, string label, bool isActive)
    {
        var round = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(round, isActive ? _buttonActivePaint : _buttonFillPaint);
        canvas.DrawRoundRect(round, _surfaceBorderPaint);
        var paint = isActive ? CreateCenteredTextPaint(SKColors.Black, 12f, SKFontStyle.Bold) : _buttonTextPaint;
        canvas.DrawText(label, rect.MidX, rect.MidY + 4f, paint);
    }

    private void DrawKnob(SKCanvas canvas, SKRect rect, float value, float min, float max, string label)
    {
        float knobCenterY = rect.Top + KnobSize / 2f;
        var center = new SKPoint(rect.Left + KnobSize / 2f, knobCenterY);
        float radius = KnobSize / 2f;

        canvas.DrawCircle(center, radius, _surfacePaint);
        canvas.DrawCircle(center, radius, _surfaceBorderPaint);

        float normalized = (value - min) / MathF.Max(0.0001f, max - min);
        normalized = Math.Clamp(normalized, 0f, 1f);
        float startAngle = 225f;
        float sweepAngle = 270f * normalized;
        using var arc = new SKPath();
        arc.AddArc(new SKRect(center.X - radius, center.Y - radius, center.X + radius, center.Y + radius), startAngle, sweepAngle);
        var ringPaint = new SKPaint
        {
            Color = _theme.Accent,
            IsAntialias = true,
            StrokeWidth = 4f,
            Style = SKPaintStyle.Stroke
        };
        canvas.DrawPath(arc, ringPaint);

        float angle = (startAngle + sweepAngle) * (MathF.PI / 180f);
        var indicator = new SKPoint(center.X + MathF.Cos(angle) * radius * 0.8f,
            center.Y + MathF.Sin(angle) * radius * 0.8f);
        canvas.DrawLine(center, indicator, _meterPeakPaint);

        float labelY = rect.Top + KnobSize + 12f;
        canvas.DrawText(label, rect.Left, labelY, _smallTextPaint);
        string valueText = value.ToString("0.0", CultureInfo.InvariantCulture);
        canvas.DrawText(valueText, rect.Left + 2f, labelY + 12f, _smallTextPaint);
    }

    private void DrawVerticalMeter(SKCanvas canvas, SKRect rect, float peakLevel, float rmsLevel, bool showTicks)
    {
        canvas.DrawRect(rect, _surfacePaint);
        canvas.DrawRect(rect, _surfaceBorderPaint);

        float rms = Math.Clamp(rmsLevel, 0f, 1f);
        float peak = Math.Clamp(peakLevel, 0f, 1f);
        float rmsHeight = rect.Height * rms;
        var rmsRect = new SKRect(rect.Left + 2f, rect.Bottom - rmsHeight, rect.Right - 2f, rect.Bottom);
        canvas.DrawRect(rmsRect, GetMeterPaint(rms));

        float peakY = rect.Bottom - rect.Height * peak;
        var peakPaint = peak >= 1f ? _meterRedPaint : _meterPeakPaint;
        canvas.DrawLine(rect.Left, peakY, rect.Right, peakY, peakPaint);

        if (showTicks)
        {
            DrawMeterTicks(canvas, rect);
        }
    }

    private void DrawHorizontalMeter(SKCanvas canvas, SKRect rect, float peakLevel, float rmsLevel)
    {
        canvas.DrawRect(rect, _surfacePaint);
        canvas.DrawRect(rect, _surfaceBorderPaint);

        float rms = Math.Clamp(rmsLevel, 0f, 1f);
        float peak = Math.Clamp(peakLevel, 0f, 1f);
        float rmsWidth = rect.Width * rms;
        var rmsRect = new SKRect(rect.Left, rect.Top, rect.Left + rmsWidth, rect.Bottom);
        canvas.DrawRect(rmsRect, GetMeterPaint(rms));

        float peakX = rect.Left + rect.Width * peak;
        var peakPaint = peak >= 1f ? _meterRedPaint : _meterPeakPaint;
        canvas.DrawLine(peakX, rect.Top, peakX, rect.Bottom, peakPaint);
    }

    private void DrawMeterTicks(SKCanvas canvas, SKRect rect)
    {
        float[] dbMarks = { 0f, -6f, -12f, -24f, -48f };
        foreach (float db in dbMarks)
        {
            float level = MathF.Pow(10f, db / 20f);
            float y = rect.Bottom - rect.Height * level;
            canvas.DrawLine(rect.Left - 6f, y, rect.Left, y, _tickPaint);
            canvas.DrawText($"{db:0}", rect.Left - 24f, y + 3f, _tickLabelPaint);
        }
    }

    private SKPaint GetMeterPaint(float level)
    {
        if (level > 0.95f)
        {
            return _meterRedPaint;
        }

        if (level > 0.7f)
        {
            return _meterYellowPaint;
        }

        return _meterGreenPaint;
    }

    private void DrawPluginSlot(SKCanvas canvas, SKRect rect, PluginViewModel slot, int channelIndex, int slotIndex)
    {
        var round = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(round, _surfacePaint);
        canvas.DrawRoundRect(round, _surfaceBorderPaint);

        float padding = 8f;
        float right = rect.Right - padding;
        float iconSize = 16f;

        var removeRect = new SKRect(right - iconSize, rect.MidY - iconSize / 2f, right, rect.MidY + iconSize / 2f);
        right -= iconSize + 8f;
        var bypassRect = new SKRect(right - iconSize, rect.MidY - iconSize / 2f, right, rect.MidY + iconSize / 2f);

        if (!slot.IsEmpty)
        {
            DrawBypassIcon(canvas, bypassRect, slot.IsBypassed);
            DrawRemoveIcon(canvas, removeRect);
        }

        string title = slot.IsEmpty ? "+ Add Plugin" : $"[{slotIndex + 1}] {slot.DisplayName}";
        var textPaint = slot.IsBypassed ? _secondaryTextPaint : _textPaint;
        DrawEllipsizedText(canvas, title, rect.Left + padding, rect.MidY + 4f, rect.Width - padding * 2 - 40f, textPaint);

        _pluginSlots.Add(new PluginSlotRect(channelIndex, slotIndex, rect, bypassRect, removeRect));
    }

    private void DrawTextButton(SKCanvas canvas, SKRect rect, string label, bool isActive, MainButton button, SKPaint? overridePaint = null)
    {
        var round = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(round, isActive ? _buttonActivePaint : overridePaint ?? _buttonFillPaint);
        canvas.DrawRoundRect(round, _surfaceBorderPaint);
        canvas.DrawText(label, rect.MidX, rect.MidY + 4f, _buttonTextPaint);
        _topButtonRects[button] = rect;
    }

    private void DrawPanelButton(SKCanvas canvas, SKRect rect, string label, bool isActive)
    {
        var round = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(round, isActive ? _buttonActivePaint : _buttonFillPaint);
        canvas.DrawRoundRect(round, _surfaceBorderPaint);
        canvas.DrawText(label, rect.MidX, rect.MidY + 4f, _buttonTextPaint);
    }

    private void DrawIconButton(SKCanvas canvas, SKRect rect, string label, MainButton button, bool isActive, IconType icon)
    {
        var round = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(round, isActive ? _buttonActivePaint : _buttonFillPaint);
        canvas.DrawRoundRect(round, _surfaceBorderPaint);

        switch (icon)
        {
            case IconType.Close:
                DrawCloseIcon(canvas, rect);
                break;
            case IconType.Minimize:
                DrawMinimizeIcon(canvas, rect);
                break;
            case IconType.Pin:
                DrawPinIcon(canvas, rect);
                break;
            case IconType.Settings:
                DrawSettingsIcon(canvas, rect);
                break;
        }

        if (!string.IsNullOrWhiteSpace(label))
        {
            canvas.DrawText(label, rect.MidX, rect.MidY + 4f, _buttonTextPaint);
        }

        _topButtonRects[button] = rect;
    }

    private void DrawCloseIcon(SKCanvas canvas, SKRect rect)
    {
        canvas.DrawLine(rect.Left + 7f, rect.Top + 7f, rect.Right - 7f, rect.Bottom - 7f, _iconStrokePaint);
        canvas.DrawLine(rect.Right - 7f, rect.Top + 7f, rect.Left + 7f, rect.Bottom - 7f, _iconStrokePaint);
    }

    private void DrawMinimizeIcon(SKCanvas canvas, SKRect rect)
    {
        canvas.DrawLine(rect.Left + 6f, rect.Bottom - 8f, rect.Right - 6f, rect.Bottom - 8f, _iconStrokePaint);
    }

    private void DrawPinIcon(SKCanvas canvas, SKRect rect)
    {
        var center = new SKPoint(rect.MidX, rect.MidY - 2f);
        canvas.DrawCircle(center, 4f, _iconStrokePaint);
        canvas.DrawLine(center.X, center.Y + 4f, center.X, rect.Bottom - 6f, _iconStrokePaint);
        canvas.DrawLine(center.X - 3f, rect.Bottom - 6f, center.X + 3f, rect.Bottom - 6f, _iconStrokePaint);
    }

    private void DrawSettingsIcon(SKCanvas canvas, SKRect rect)
    {
        var center = new SKPoint(rect.MidX, rect.MidY);
        canvas.DrawCircle(center, 4f, _iconStrokePaint);
        for (int i = 0; i < 6; i++)
        {
            float angle = i * 60f * (MathF.PI / 180f);
            float x1 = center.X + MathF.Cos(angle) * 6f;
            float y1 = center.Y + MathF.Sin(angle) * 6f;
            float x2 = center.X + MathF.Cos(angle) * 8f;
            float y2 = center.Y + MathF.Sin(angle) * 8f;
            canvas.DrawLine(x1, y1, x2, y2, _iconStrokePaint);
        }
    }

    private void DrawBypassIcon(SKCanvas canvas, SKRect rect, bool isBypassed)
    {
        var round = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(round, isBypassed ? _buttonActivePaint : _buttonFillPaint);
        canvas.DrawRoundRect(round, _surfaceBorderPaint);
        canvas.DrawText("B", rect.MidX, rect.MidY + 4f, _smallTextPaint);
    }

    private void DrawRemoveIcon(SKCanvas canvas, SKRect rect)
    {
        canvas.DrawLine(rect.Left + 3f, rect.Top + 3f, rect.Right - 3f, rect.Bottom - 3f, _iconStrokePaint);
        canvas.DrawLine(rect.Right - 3f, rect.Top + 3f, rect.Left + 3f, rect.Bottom - 3f, _iconStrokePaint);
    }

    private void DrawChevron(SKCanvas canvas, SKPoint center, float size)
    {
        canvas.DrawLine(center.X - size, center.Y - size / 2f, center.X, center.Y + size / 2f, _iconStrokePaint);
        canvas.DrawLine(center.X, center.Y + size / 2f, center.X + size, center.Y - size / 2f, _iconStrokePaint);
    }

    private void DrawEllipsizedText(SKCanvas canvas, string text, float x, float y, float maxWidth, SKPaint paint)
    {
        if (paint.MeasureText(text) <= maxWidth)
        {
            canvas.DrawText(text, x, y, paint);
            return;
        }

        const string ellipsis = "...";
        float available = Math.Max(0f, maxWidth - paint.MeasureText(ellipsis));
        int length = text.Length;
        while (length > 0 && paint.MeasureText(text.AsSpan(0, length).ToString()) > available)
        {
            length--;
        }

        string trimmed = length > 0 ? $"{text[..length]}{ellipsis}" : ellipsis;
        canvas.DrawText(trimmed, x, y, paint);
    }

    private void ClearHitTargets()
    {
        _topButtonRects.Clear();
        _devicePickerRects.Clear();
        _deviceItemRects.Clear();
        _knobRects.Clear();
        _pluginSlots.Clear();
        _toggleRects.Clear();
        _applyDevicesRect = SKRect.Empty;
        _deviceListRect = SKRect.Empty;
    }

    private static SKPaint CreateFillPaint(SKColor color) => new()
    {
        Color = color,
        IsAntialias = true,
        Style = SKPaintStyle.Fill
    };

    private static SKPaint CreateStrokePaint(SKColor color, float strokeWidth) => new()
    {
        Color = color,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = strokeWidth
    };

    private static SKPaint CreateTextPaint(SKColor color, float size, SKFontStyle? style = null) => new()
    {
        Color = color,
        IsAntialias = true,
        TextSize = size,
        TextAlign = SKTextAlign.Left,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", style ?? SKFontStyle.Normal)
    };

    private static SKPaint CreateCenteredTextPaint(SKColor color, float size, SKFontStyle? style = null) => new()
    {
        Color = color,
        IsAntialias = true,
        TextSize = size,
        TextAlign = SKTextAlign.Center,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", style ?? SKFontStyle.Normal)
    };

    private sealed record DeviceItemRect(DevicePickerTarget Target, int Index, SKRect Rect);

    private sealed record KnobRect(int ChannelIndex, KnobType KnobType, SKRect Rect);

    private sealed record PluginSlotRect(int ChannelIndex, int SlotIndex, SKRect Rect, SKRect BypassRect, SKRect RemoveRect);

    private sealed record ToggleRect(int ChannelIndex, ToggleType ToggleType, SKRect Rect);

    private enum IconType
    {
        Close,
        Minimize,
        Pin,
        Settings
    }
}

public readonly record struct DeviceItemHit(DevicePickerTarget Target, int Index);

public readonly record struct KnobHit(int ChannelIndex, KnobType KnobType);

public readonly record struct PluginSlotHit(int ChannelIndex, int SlotIndex);

public enum PluginSlotRegion
{
    None,
    Action,
    Bypass,
    Remove
}

public readonly record struct ToggleHit(int ChannelIndex, ToggleType ToggleType);
