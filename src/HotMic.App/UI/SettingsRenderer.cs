using System.Collections.Generic;
using HotMic.App.ViewModels;
using HotMic.Common.Configuration;
using HotMic.Common.Models;
using SkiaSharp;

namespace HotMic.App.UI;

public sealed class SettingsRenderer
{
    private const float Padding = 20f;
    private const float LabelHeight = 14f;
    private const float FieldHeight = 36f;
    private const float RowSpacing = 16f;
    private const float ColumnSpacing = 16f;
    private const float ButtonHeight = 40f;
    private const float TitleBarHeight = 48f;
    private const float CornerRadius = 12f;

    private readonly HotMicTheme _theme = HotMicTheme.Default;
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _fieldPaint;
    private readonly SKPaint _fieldBorderPaint;
    private readonly SKPaint _buttonPaint;
    private readonly SKPaint _buttonAccentPaint;
    private readonly SKPaint _titlePaint;
    private readonly SKPaint _labelPaint;
    private readonly SKPaint _textPaint;
    private readonly SKPaint _buttonTextPaint;
    private readonly SKPaint _iconPaint;

    private readonly Dictionary<SettingsField, SKRect> _fieldRects = new();
    private readonly List<DropdownItemRect> _dropdownItems = new();
    private SKRect _applyButtonRect;
    private SKRect _cancelButtonRect;
    private SKRect _titleBarRect;
    private SKRect _dropdownListRect;

    public SettingsRenderer()
    {
        _backgroundPaint = CreateFillPaint(_theme.BackgroundPrimary);
        _titleBarPaint = CreateFillPaint(_theme.BackgroundSecondary);
        _borderPaint = CreateStrokePaint(_theme.Border, 1f);
        _fieldPaint = CreateFillPaint(_theme.Surface);
        _fieldBorderPaint = CreateStrokePaint(_theme.Border, 1f);
        _buttonPaint = CreateFillPaint(_theme.Surface);
        _buttonAccentPaint = CreateFillPaint(_theme.Accent);
        _titlePaint = CreateTextPaint(_theme.TextPrimary, 16f, SKFontStyle.Bold);
        _labelPaint = CreateTextPaint(_theme.TextSecondary, 11f);
        _textPaint = CreateTextPaint(_theme.TextPrimary, 13f);
        _buttonTextPaint = CreateCenteredTextPaint(_theme.TextPrimary, 13f, SKFontStyle.Bold);
        _iconPaint = CreateStrokePaint(_theme.TextSecondary, 1.5f);
    }

    public float DropdownContentHeight { get; private set; }
    public float DropdownViewportHeight { get; private set; }

    public void Render(SKCanvas canvas, SKSize size, SettingsViewModel viewModel, SettingsUiState uiState, float dpiScale)
    {
        ClearHitTargets();
        canvas.Clear(SKColors.Transparent);

        canvas.Save();
        canvas.Scale(dpiScale);
        size = new SKSize(size.Width / dpiScale, size.Height / dpiScale);

        DrawBackground(canvas, size);
        DrawTitleBar(canvas, size);
        DrawContent(canvas, size, viewModel, uiState);
        DrawDropdownOverlay(canvas, viewModel, uiState);

        canvas.Restore();
    }

    private void DrawBackground(SKCanvas canvas, SKSize size)
    {
        var rect = new SKRoundRect(new SKRect(0, 0, size.Width, size.Height), CornerRadius);
        canvas.DrawRoundRect(rect, _backgroundPaint);
        canvas.DrawRoundRect(rect, _borderPaint);
    }

    private void DrawTitleBar(SKCanvas canvas, SKSize size)
    {
        _titleBarRect = new SKRect(0, 0, size.Width, TitleBarHeight);

        using var titleBarClip = new SKPath();
        titleBarClip.AddRoundRect(new SKRoundRect(new SKRect(0, 0, size.Width, TitleBarHeight + CornerRadius), CornerRadius));
        titleBarClip.AddRect(new SKRect(0, TitleBarHeight, size.Width, TitleBarHeight + CornerRadius));
        canvas.Save();
        canvas.ClipPath(titleBarClip);
        canvas.DrawRect(_titleBarRect, _titleBarPaint);
        canvas.Restore();

        canvas.DrawLine(0, TitleBarHeight, size.Width, TitleBarHeight, _borderPaint);
        canvas.DrawText("Audio Settings", Padding, TitleBarHeight / 2f + 5f, _titlePaint);
    }

    private void DrawContent(SKCanvas canvas, SKSize size, SettingsViewModel viewModel, SettingsUiState uiState)
    {
        float y = TitleBarHeight + Padding;
        float contentWidth = size.Width - Padding * 2f;
        float halfWidth = (contentWidth - ColumnSpacing) / 2f;
        float thirdWidth = (contentWidth - ColumnSpacing * 2f) / 3f;

        // Row 1: Input devices
        DrawFieldRow(canvas, "Input 1", GetDeviceName(viewModel.SelectedInputDevice1), Padding, y, halfWidth, SettingsField.Input1);
        DrawFieldRow(canvas, "Input 2", GetDeviceName(viewModel.SelectedInputDevice2), Padding + halfWidth + ColumnSpacing, y, halfWidth, SettingsField.Input2);
        y += LabelHeight + 6f + FieldHeight + RowSpacing;

        // Row 2: Output devices
        DrawFieldRow(canvas, "Output (VB-Cable)", GetDeviceName(viewModel.SelectedOutputDevice), Padding, y, halfWidth, SettingsField.Output);
        DrawFieldRow(canvas, "Monitor", GetDeviceName(viewModel.SelectedMonitorDevice), Padding + halfWidth + ColumnSpacing, y, halfWidth, SettingsField.Monitor);
        y += LabelHeight + 6f + FieldHeight + RowSpacing;

        // Row 3: Sample rate & buffer
        DrawFieldRow(canvas, "Sample Rate", FormatSampleRate(viewModel.SelectedSampleRate), Padding, y, halfWidth, SettingsField.SampleRate);
        DrawFieldRow(canvas, "Buffer Size", $"{viewModel.SelectedBufferSize} samples", Padding + halfWidth + ColumnSpacing, y, halfWidth, SettingsField.BufferSize);
        y += LabelHeight + 6f + FieldHeight + RowSpacing;

        // Row 4: Channel modes
        DrawFieldRow(canvas, "Input 1 Channel", FormatInputChannel(viewModel.SelectedInput1Channel), Padding, y, thirdWidth, SettingsField.Input1Channel);
        DrawFieldRow(canvas, "Input 2 Channel", FormatInputChannel(viewModel.SelectedInput2Channel), Padding + thirdWidth + ColumnSpacing, y, thirdWidth, SettingsField.Input2Channel);
        DrawFieldRow(canvas, "Output Routing", FormatOutputRouting(viewModel.SelectedOutputRouting), Padding + (thirdWidth + ColumnSpacing) * 2f, y, thirdWidth, SettingsField.OutputRouting);
        y += LabelHeight + 6f + FieldHeight + RowSpacing + 8f;

        // Buttons
        float buttonWidth = 100f;
        float buttonsX = size.Width - Padding - buttonWidth * 2f - ColumnSpacing;
        DrawButton(canvas, "Cancel", buttonsX, y, buttonWidth, false, out _cancelButtonRect);
        DrawButton(canvas, "Apply", buttonsX + buttonWidth + ColumnSpacing, y, buttonWidth, true, out _applyButtonRect);
    }

    private void DrawFieldRow(SKCanvas canvas, string label, string value, float x, float y, float width, SettingsField field)
    {
        canvas.DrawText(label, x, y + LabelHeight - 2f, _labelPaint);

        float fieldTop = y + LabelHeight + 6f;
        var rect = new SKRect(x, fieldTop, x + width, fieldTop + FieldHeight);
        _fieldRects[field] = rect;

        var roundRect = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(roundRect, _fieldPaint);
        canvas.DrawRoundRect(roundRect, _fieldBorderPaint);

        DrawEllipsizedText(canvas, value, rect.Left + 12f, rect.MidY + 4f, rect.Width - 36f, _textPaint);
        DrawChevron(canvas, new SKPoint(rect.Right - 16f, rect.MidY), 5f);
    }

    private void DrawButton(SKCanvas canvas, string text, float x, float y, float width, bool isAccent, out SKRect rect)
    {
        rect = new SKRect(x, y, x + width, y + ButtonHeight);
        var roundRect = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(roundRect, isAccent ? _buttonAccentPaint : _buttonPaint);
        canvas.DrawRoundRect(roundRect, _fieldBorderPaint);

        var textPaint = isAccent
            ? CreateCenteredTextPaint(new SKColor(0x12, 0x12, 0x14), 13f, SKFontStyle.Bold)
            : _buttonTextPaint;
        canvas.DrawText(text, rect.MidX, rect.MidY + 4f, textPaint);
    }

    private void DrawDropdownOverlay(SKCanvas canvas, SettingsViewModel viewModel, SettingsUiState uiState)
    {
        if (uiState.ActiveDropdown == SettingsField.None || !_fieldRects.TryGetValue(uiState.ActiveDropdown, out var anchor))
        {
            _dropdownListRect = SKRect.Empty;
            return;
        }

        var items = GetDropdownItems(viewModel, uiState.ActiveDropdown, out int selectedIndex);

        float itemHeight = 32f;
        float maxHeight = 200f;
        float listWidth = anchor.Width;
        float contentHeight = items.Count * itemHeight;
        float listHeight = MathF.Min(maxHeight, MathF.Max(itemHeight, contentHeight));

        var listRect = new SKRect(anchor.Left, anchor.Bottom + 4f, anchor.Left + listWidth, anchor.Bottom + 4f + listHeight);
        _dropdownListRect = listRect;
        DropdownContentHeight = contentHeight;
        DropdownViewportHeight = listHeight;

        using var shadow = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 60),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8f)
        };
        canvas.DrawRoundRect(new SKRoundRect(listRect, 8f), shadow);

        var roundRect = new SKRoundRect(listRect, 8f);
        canvas.DrawRoundRect(roundRect, CreateFillPaint(_theme.BackgroundSecondary));
        canvas.DrawRoundRect(roundRect, _borderPaint);

        canvas.Save();
        canvas.ClipRect(listRect);

        float scroll = MathF.Max(0f, uiState.DropdownScroll);
        float y = listRect.Top - scroll;
        _dropdownItems.Clear();

        for (int i = 0; i < items.Count; i++)
        {
            var itemRect = new SKRect(listRect.Left, y, listRect.Right, y + itemHeight);
            if (itemRect.Bottom >= listRect.Top && itemRect.Top <= listRect.Bottom)
            {
                bool isSelected = i == selectedIndex;
                if (isSelected)
                {
                    canvas.DrawRect(itemRect, CreateFillPaint(_theme.Surface));
                }

                DrawEllipsizedText(canvas, items[i], itemRect.Left + 12f, itemRect.MidY + 4f, itemRect.Width - 24f, _textPaint);
                _dropdownItems.Add(new DropdownItemRect(uiState.ActiveDropdown, i, itemRect));
            }
            y += itemHeight;
        }

        canvas.Restore();
    }

    private static IReadOnlyList<string> GetDropdownItems(SettingsViewModel viewModel, SettingsField field, out int selectedIndex)
    {
        selectedIndex = -1;
        return field switch
        {
            SettingsField.Input1 => BuildDeviceList(viewModel.InputDevices, viewModel.SelectedInputDevice1, out selectedIndex),
            SettingsField.Input2 => BuildDeviceList(viewModel.InputDevices, viewModel.SelectedInputDevice2, out selectedIndex),
            SettingsField.Output => BuildDeviceList(viewModel.OutputDevices, viewModel.SelectedOutputDevice, out selectedIndex),
            SettingsField.Monitor => BuildDeviceList(viewModel.OutputDevices, viewModel.SelectedMonitorDevice, out selectedIndex),
            SettingsField.SampleRate => BuildOptionList(viewModel.SampleRateOptions, viewModel.SelectedSampleRate, FormatSampleRate, out selectedIndex),
            SettingsField.BufferSize => BuildOptionList(viewModel.BufferSizeOptions, viewModel.SelectedBufferSize, b => $"{b} samples", out selectedIndex),
            SettingsField.Input1Channel => BuildEnumList(viewModel.InputChannelOptions, viewModel.SelectedInput1Channel, FormatInputChannel, out selectedIndex),
            SettingsField.Input2Channel => BuildEnumList(viewModel.InputChannelOptions, viewModel.SelectedInput2Channel, FormatInputChannel, out selectedIndex),
            SettingsField.OutputRouting => BuildEnumList(viewModel.OutputRoutingOptions, viewModel.SelectedOutputRouting, FormatOutputRouting, out selectedIndex),
            _ => []
        };
    }

    private static IReadOnlyList<string> BuildDeviceList(IReadOnlyList<AudioDevice> devices, AudioDevice? selected, out int selectedIndex)
    {
        selectedIndex = -1;
        var list = new List<string>(devices.Count);
        for (int i = 0; i < devices.Count; i++)
        {
            list.Add(devices[i].Name);
            if (selected is not null && devices[i].Id == selected.Id)
            {
                selectedIndex = i;
            }
        }
        return list;
    }

    private static IReadOnlyList<string> BuildOptionList(IReadOnlyList<int> options, int selected, Func<int, string> format, out int selectedIndex)
    {
        selectedIndex = -1;
        var list = new List<string>(options.Count);
        for (int i = 0; i < options.Count; i++)
        {
            list.Add(format(options[i]));
            if (options[i] == selected) selectedIndex = i;
        }
        return list;
    }

    private static IReadOnlyList<string> BuildEnumList<T>(IReadOnlyList<T> options, T selected, Func<T, string> format, out int selectedIndex) where T : struct
    {
        selectedIndex = -1;
        var list = new List<string>(options.Count);
        for (int i = 0; i < options.Count; i++)
        {
            list.Add(format(options[i]));
            if (EqualityComparer<T>.Default.Equals(options[i], selected)) selectedIndex = i;
        }
        return list;
    }

    private static string GetDeviceName(AudioDevice? device) => device?.Name ?? "Select...";
    private static string FormatSampleRate(int rate) => rate >= 1000 ? $"{rate / 1000f:0.#} kHz" : $"{rate} Hz";
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

    private void DrawChevron(SKCanvas canvas, SKPoint center, float size)
    {
        canvas.DrawLine(center.X - size, center.Y - size / 2f, center.X, center.Y + size / 2f, _iconPaint);
        canvas.DrawLine(center.X, center.Y + size / 2f, center.X + size, center.Y - size / 2f, _iconPaint);
    }

    private void DrawEllipsizedText(SKCanvas canvas, string text, float x, float y, float maxWidth, SKPaint paint)
    {
        if (paint.MeasureText(text) <= maxWidth)
        {
            canvas.DrawText(text, x, y, paint);
            return;
        }

        const string ellipsis = "...";
        float available = MathF.Max(0f, maxWidth - paint.MeasureText(ellipsis));
        int len = text.Length;
        while (len > 0 && paint.MeasureText(text.AsSpan(0, len).ToString()) > available)
            len--;
        canvas.DrawText(len > 0 ? $"{text[..len]}{ellipsis}" : ellipsis, x, y, paint);
    }

    private void ClearHitTargets()
    {
        _fieldRects.Clear();
        _dropdownItems.Clear();
        _applyButtonRect = SKRect.Empty;
        _cancelButtonRect = SKRect.Empty;
        _dropdownListRect = SKRect.Empty;
    }

    public SettingsField HitTestField(float x, float y)
    {
        foreach (var (field, rect) in _fieldRects)
            if (rect.Contains(x, y)) return field;
        return SettingsField.None;
    }

    public DropdownItemHit? HitTestDropdownItem(float x, float y)
    {
        foreach (var item in _dropdownItems)
            if (item.Rect.Contains(x, y)) return new DropdownItemHit(item.Field, item.Index);
        return null;
    }

    public bool HitTestApply(float x, float y) => _applyButtonRect.Contains(x, y);
    public bool HitTestCancel(float x, float y) => _cancelButtonRect.Contains(x, y);
    public bool HitTestTitleBar(float x, float y) => _titleBarRect.Contains(x, y);
    public bool HitTestDropdownList(float x, float y) => !_dropdownListRect.IsEmpty && _dropdownListRect.Contains(x, y);

    private static SKPaint CreateFillPaint(SKColor color) => new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
    private static SKPaint CreateStrokePaint(SKColor color, float width) => new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width };
    private static SKPaint CreateTextPaint(SKColor color, float size, SKFontStyle? style = null) => new()
    {
        Color = color, IsAntialias = true, TextSize = size, TextAlign = SKTextAlign.Left,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", style ?? SKFontStyle.Normal)
    };
    private static SKPaint CreateCenteredTextPaint(SKColor color, float size, SKFontStyle? style = null) => new()
    {
        Color = color, IsAntialias = true, TextSize = size, TextAlign = SKTextAlign.Center,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", style ?? SKFontStyle.Normal)
    };

    private sealed record DropdownItemRect(SettingsField Field, int Index, SKRect Rect);
}

public enum SettingsField
{
    None,
    Input1, Input2, Output, Monitor,
    SampleRate, BufferSize,
    Input1Channel, Input2Channel, OutputRouting
}

public readonly record struct DropdownItemHit(SettingsField Field, int Index);

public sealed class SettingsUiState
{
    public SettingsField ActiveDropdown { get; set; }
    public float DropdownScroll { get; set; }
}
