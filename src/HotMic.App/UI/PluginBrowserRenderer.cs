using System;
using System.Collections.Generic;
using HotMic.App.Models;
using HotMic.App.ViewModels;
using SkiaSharp;

namespace HotMic.App.UI;

public sealed class PluginBrowserRenderer
{
    private const float CornerRadius = 12f;
    private const float TitleBarHeight = 48f;
    private const float SearchBarHeight = 40f;
    private const float Padding = 16f;
    private const float CategoryHeaderHeight = 28f;
    private const float RowHeight = 44f;
    private const float ButtonHeight = 32f;
    private const float ButtonWidth = 90f;

    private readonly HotMicTheme _theme = HotMicTheme.Default;
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _panelPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _textPaint;
    private readonly SKPaint _descriptionPaint;
    private readonly SKPaint _titlePaint;
    private readonly SKPaint _categoryPaint;
    private readonly SKPaint _buttonPaint;
    private readonly SKPaint _buttonActivePaint;
    private readonly SKPaint _buttonTextPaint;
    private readonly SKPaint _searchBgPaint;
    private readonly SKPaint _searchTextPaint;
    private readonly SKPaint _placeholderPaint;
    private readonly SKPaint _selectedPaint;

    private readonly List<ItemRect> _itemRects = new();
    private SKRect _addButtonRect;
    private SKRect _cancelButtonRect;
    private SKRect _titleBarRect;
    private SKRect _searchBoxRect;

    public PluginBrowserRenderer()
    {
        _backgroundPaint = CreateFillPaint(_theme.BackgroundPrimary);
        _panelPaint = CreateFillPaint(_theme.BackgroundSecondary);
        _borderPaint = CreateStrokePaint(_theme.Border, 1f);
        _textPaint = CreateTextPaint(_theme.TextPrimary, 13f);
        _descriptionPaint = CreateTextPaint(_theme.TextSecondary, 11f);
        _titlePaint = CreateTextPaint(_theme.TextPrimary, 16f, SKFontStyle.Bold);
        _categoryPaint = CreateTextPaint(_theme.Accent, 11f, SKFontStyle.Bold);
        _buttonPaint = CreateFillPaint(_theme.Surface);
        _buttonActivePaint = CreateFillPaint(_theme.Accent);
        _buttonTextPaint = CreateTextPaint(_theme.TextPrimary, 12f, SKFontStyle.Bold, SKTextAlign.Center);
        _searchBgPaint = CreateFillPaint(_theme.Surface);
        _searchTextPaint = CreateTextPaint(_theme.TextPrimary, 13f);
        _placeholderPaint = CreateTextPaint(_theme.TextSecondary, 13f);
        _selectedPaint = CreateFillPaint(new SKColor(0x3d, 0x3d, 0x42));
    }

    public float ListContentHeight { get; private set; }
    public float ListViewportHeight { get; private set; }
    public bool IsSearchBoxFocused { get; set; }

    public void Render(SKCanvas canvas, SKSize size, PluginBrowserViewModel viewModel, float dpiScale, float scrollOffset)
    {
        _itemRects.Clear();
        canvas.Clear(SKColors.Transparent);

        canvas.Save();
        canvas.Scale(dpiScale);
        size = new SKSize(size.Width / dpiScale, size.Height / dpiScale);

        var background = new SKRoundRect(new SKRect(0, 0, size.Width, size.Height), CornerRadius);
        canvas.DrawRoundRect(background, _backgroundPaint);
        canvas.DrawRoundRect(background, _borderPaint);

        _titleBarRect = new SKRect(0, 0, size.Width, TitleBarHeight);
        canvas.DrawRect(_titleBarRect, _panelPaint);
        canvas.DrawLine(0, TitleBarHeight, size.Width, TitleBarHeight, _borderPaint);
        canvas.DrawText("Add Plugin", Padding, TitleBarHeight / 2f + _titlePaint.TextSize / 2.5f, _titlePaint);

        // Search box
        float searchTop = TitleBarHeight + Padding;
        _searchBoxRect = new SKRect(Padding, searchTop, size.Width - Padding, searchTop + SearchBarHeight);
        canvas.DrawRoundRect(new SKRoundRect(_searchBoxRect, 6f), _searchBgPaint);
        canvas.DrawRoundRect(new SKRoundRect(_searchBoxRect, 6f), _borderPaint);

        // Search icon
        DrawSearchIcon(canvas, _searchBoxRect.Left + 14f, _searchBoxRect.MidY);

        // Search text or placeholder
        float textX = _searchBoxRect.Left + 36f;
        if (string.IsNullOrEmpty(viewModel.SearchText))
        {
            canvas.DrawText("Search plugins...", textX, _searchBoxRect.MidY + 4f, _placeholderPaint);
        }
        else
        {
            canvas.DrawText(viewModel.SearchText, textX, _searchBoxRect.MidY + 4f, _searchTextPaint);
        }

        // Plugin list
        float listTop = searchTop + SearchBarHeight + Padding;
        float listBottom = size.Height - Padding - ButtonHeight - 12f;
        float listHeight = MathF.Max(RowHeight, listBottom - listTop);
        var listRect = new SKRect(Padding, listTop, size.Width - Padding, listTop + listHeight);

        canvas.DrawRoundRect(new SKRoundRect(listRect, 8f), _panelPaint);
        canvas.DrawRoundRect(new SKRoundRect(listRect, 8f), _borderPaint);

        ListViewportHeight = listRect.Height;

        // Calculate total content height
        float contentHeight = 0f;
        foreach (var group in viewModel.GroupedChoices)
        {
            contentHeight += CategoryHeaderHeight;
            contentHeight += group.Plugins.Count * RowHeight;
        }
        ListContentHeight = contentHeight;

        canvas.Save();
        canvas.ClipRect(listRect);

        float y = listRect.Top - scrollOffset;
        int globalIndex = 0;

        foreach (var group in viewModel.GroupedChoices)
        {
            // Category header
            var headerRect = new SKRect(listRect.Left, y, listRect.Right, y + CategoryHeaderHeight);
            if (headerRect.Bottom >= listRect.Top && headerRect.Top <= listRect.Bottom)
            {
                canvas.DrawText(group.Name.ToUpperInvariant(), headerRect.Left + 12f, headerRect.MidY + 4f, _categoryPaint);
            }
            y += CategoryHeaderHeight;

            // Plugins in category
            foreach (var plugin in group.Plugins)
            {
                var rowRect = new SKRect(listRect.Left, y, listRect.Right, y + RowHeight);
                if (rowRect.Bottom >= listRect.Top && rowRect.Top <= listRect.Bottom)
                {
                    bool isSelected = viewModel.SelectedChoice == plugin;
                    if (isSelected)
                    {
                        canvas.DrawRect(rowRect, _selectedPaint);
                    }

                    // Plugin name
                    canvas.DrawText(plugin.Name, rowRect.Left + 12f, rowRect.Top + 18f, _textPaint);

                    // Plugin description
                    if (!string.IsNullOrEmpty(plugin.Description))
                    {
                        DrawEllipsizedText(canvas, plugin.Description, rowRect.Left + 12f, rowRect.Top + 34f, rowRect.Width - 24f, _descriptionPaint);
                    }

                    _itemRects.Add(new ItemRect(globalIndex, plugin, rowRect));
                }
                y += RowHeight;
                globalIndex++;
            }
        }

        canvas.Restore();

        // Scrollbar
        if (ListContentHeight > ListViewportHeight)
        {
            float scrollbarHeight = MathF.Max(20f, ListViewportHeight * ListViewportHeight / ListContentHeight);
            float scrollbarY = listRect.Top + (scrollOffset / ListContentHeight) * ListViewportHeight;
            var scrollbarRect = new SKRect(listRect.Right - 6f, scrollbarY, listRect.Right - 2f, scrollbarY + scrollbarHeight);
            using var scrollbarPaint = CreateFillPaint(new SKColor(0x60, 0x60, 0x68));
            canvas.DrawRoundRect(new SKRoundRect(scrollbarRect, 2f), scrollbarPaint);
        }

        // Buttons
        float buttonY = size.Height - Padding - ButtonHeight;
        _cancelButtonRect = new SKRect(size.Width - Padding - ButtonWidth * 2 - 8f, buttonY, size.Width - Padding - ButtonWidth - 8f, buttonY + ButtonHeight);
        _addButtonRect = new SKRect(size.Width - Padding - ButtonWidth, buttonY, size.Width - Padding, buttonY + ButtonHeight);

        DrawButton(canvas, _cancelButtonRect, "Cancel", isActive: false);
        DrawButton(canvas, _addButtonRect, "Add", isActive: viewModel.SelectedChoice is not null);

        canvas.Restore();
    }

    public int HitTestItem(float x, float y)
    {
        foreach (var item in _itemRects)
        {
            if (item.Rect.Contains(x, y))
            {
                return item.Index;
            }
        }
        return -1;
    }

    public PluginChoice? HitTestPlugin(float x, float y)
    {
        foreach (var item in _itemRects)
        {
            if (item.Rect.Contains(x, y))
            {
                return item.Plugin;
            }
        }
        return null;
    }

    public bool HitTestAdd(float x, float y) => _addButtonRect.Contains(x, y);
    public bool HitTestCancel(float x, float y) => _cancelButtonRect.Contains(x, y);
    public bool HitTestTitleBar(float x, float y) => _titleBarRect.Contains(x, y);
    public bool HitTestSearchBox(float x, float y) => _searchBoxRect.Contains(x, y);

    private void DrawButton(SKCanvas canvas, SKRect rect, string label, bool isActive)
    {
        var round = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(round, isActive ? _buttonActivePaint : _buttonPaint);
        canvas.DrawRoundRect(round, _borderPaint);

        var textPaint = isActive
            ? CreateTextPaint(new SKColor(0x12, 0x12, 0x14), 12f, SKFontStyle.Bold, SKTextAlign.Center)
            : _buttonTextPaint;
        canvas.DrawText(label, rect.MidX, rect.MidY + 4f, textPaint);
    }

    private void DrawSearchIcon(SKCanvas canvas, float cx, float cy)
    {
        using var iconPaint = CreateStrokePaint(_theme.TextSecondary, 1.5f);
        canvas.DrawCircle(cx, cy, 6f, iconPaint);
        canvas.DrawLine(cx + 4f, cy + 4f, cx + 8f, cy + 8f, iconPaint);
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

    private static SKPaint CreateTextPaint(SKColor color, float size, SKFontStyle? style = null, SKTextAlign align = SKTextAlign.Left) => new()
    {
        Color = color,
        IsAntialias = true,
        TextSize = size,
        TextAlign = align,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", style ?? SKFontStyle.Normal)
    };

    private sealed record ItemRect(int Index, PluginChoice Plugin, SKRect Rect);
}
