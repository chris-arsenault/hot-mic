using System;
using System.Collections.Generic;
using HotMic.App.Models;
using HotMic.App.ViewModels;
using SkiaSharp;

namespace HotMic.App.UI;

public sealed class PluginBrowserRenderer
{
    private const float CornerRadius = 10f;
    private const float TitleBarHeight = 40f;
    private const float TabBarHeight = 36f;
    private const float SearchBarHeight = 32f;
    private const float Padding = 10f;
    private const float CategoryHeaderHeight = 24f;
    private const float RowHeight = 40f;
    private const float ButtonHeight = 28f;
    private const float ButtonWidth = 80f;

    // Grid layout for built-in plugins
    private const float GridCellWidth = 200f;
    private const float GridCellHeight = 48f;
    private const float GridCellPadding = 4f;

    private readonly HotMicTheme _theme = HotMicTheme.Default;
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _panelPaint;
    private readonly SKPaint _borderPaint;
    private readonly SkiaTextPaint _textPaint;
    private readonly SkiaTextPaint _descriptionPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _categoryPaint;
    private readonly SKPaint _buttonPaint;
    private readonly SKPaint _buttonActivePaint;
    private readonly SkiaTextPaint _buttonTextPaint;
    private readonly SKPaint _searchBgPaint;
    private readonly SkiaTextPaint _searchTextPaint;
    private readonly SkiaTextPaint _placeholderPaint;
    private readonly SKPaint _selectedPaint;
    private readonly SKPaint _tabActivePaint;
    private readonly SKPaint _tabInactivePaint;
    private readonly SkiaTextPaint _tabTextPaint;
    private readonly SkiaTextPaint _tabTextActivePaint;

    private readonly List<ItemRect> _itemRects = new();
    private SKRect _addButtonRect;
    private SKRect _cancelButtonRect;
    private SKRect _titleBarRect;
    private SKRect _searchBoxRect;
    private SKRect _builtInTabRect;
    private SKRect _vstTabRect;

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
        _tabActivePaint = CreateFillPaint(_theme.Surface);
        _tabInactivePaint = CreateFillPaint(_theme.BackgroundSecondary);
        _tabTextPaint = CreateTextPaint(_theme.TextSecondary, 12f, SKFontStyle.Bold, SKTextAlign.Center);
        _tabTextActivePaint = CreateTextPaint(_theme.TextPrimary, 12f, SKFontStyle.Bold, SKTextAlign.Center);
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

        // Title bar
        _titleBarRect = new SKRect(0, 0, size.Width, TitleBarHeight);
        canvas.DrawRect(_titleBarRect, _panelPaint);
        canvas.DrawLine(0, TitleBarHeight, size.Width, TitleBarHeight, _borderPaint);
        canvas.DrawText("Add Plugin", Padding, TitleBarHeight / 2f + _titlePaint.Size / 2.5f, _titlePaint);

        // Tab bar
        float tabY = TitleBarHeight;
        DrawTabBar(canvas, size.Width, tabY, viewModel);

        // Search box
        float searchTop = TitleBarHeight + TabBarHeight + Padding;
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

        // Plugin list area
        float listTop = searchTop + SearchBarHeight + Padding;
        float listBottom = size.Height - Padding - ButtonHeight - 12f;
        float listHeight = MathF.Max(RowHeight, listBottom - listTop);
        var listRect = new SKRect(Padding, listTop, size.Width - Padding, listTop + listHeight);

        canvas.DrawRoundRect(new SKRoundRect(listRect, 8f), _panelPaint);
        canvas.DrawRoundRect(new SKRoundRect(listRect, 8f), _borderPaint);

        ListViewportHeight = listRect.Height;

        if (viewModel.SelectedTab == PluginBrowserTab.BuiltIn)
        {
            RenderBuiltInGrid(canvas, listRect, viewModel, scrollOffset);
        }
        else
        {
            RenderVstList(canvas, listRect, viewModel, scrollOffset);
        }

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

    private void DrawTabBar(SKCanvas canvas, float width, float y, PluginBrowserViewModel viewModel)
    {
        const float tabWidth = 100f;
        const float tabHeight = TabBarHeight - 8f;
        float startX = Padding;

        // Built-in tab
        _builtInTabRect = new SKRect(startX, y + 4f, startX + tabWidth, y + 4f + tabHeight);
        bool builtInActive = viewModel.SelectedTab == PluginBrowserTab.BuiltIn;
        canvas.DrawRoundRect(new SKRoundRect(_builtInTabRect, 6f), builtInActive ? _tabActivePaint : _tabInactivePaint);
        if (builtInActive)
        {
            canvas.DrawRoundRect(new SKRoundRect(_builtInTabRect, 6f), _borderPaint);
        }
        canvas.DrawText("Built-in", _builtInTabRect.MidX, _builtInTabRect.MidY + 4f, builtInActive ? _tabTextActivePaint : _tabTextPaint);

        // VST tab
        float vstX = startX + tabWidth + 8f;
        _vstTabRect = new SKRect(vstX, y + 4f, vstX + tabWidth, y + 4f + tabHeight);
        bool vstActive = viewModel.SelectedTab == PluginBrowserTab.Vst;
        canvas.DrawRoundRect(new SKRoundRect(_vstTabRect, 6f), vstActive ? _tabActivePaint : _tabInactivePaint);
        if (vstActive)
        {
            canvas.DrawRoundRect(new SKRoundRect(_vstTabRect, 6f), _borderPaint);
        }
        canvas.DrawText("VST", _vstTabRect.MidX, _vstTabRect.MidY + 4f, vstActive ? _tabTextActivePaint : _tabTextPaint);
    }

    private void RenderBuiltInGrid(SKCanvas canvas, SKRect listRect, PluginBrowserViewModel viewModel, float scrollOffset)
    {
        // Calculate how many columns fit
        float availableWidth = listRect.Width - GridCellPadding * 2;
        int columns = Math.Max(1, (int)((availableWidth + GridCellPadding) / (GridCellWidth + GridCellPadding)));

        // Recalculate cell width to fill space evenly
        float actualCellWidth = (availableWidth - (columns - 1) * GridCellPadding) / columns;

        // Calculate total content height with category headers
        float contentHeight = GridCellPadding;
        foreach (var group in viewModel.GroupedChoices)
        {
            contentHeight += CategoryHeaderHeight;
            int rowsInGroup = (group.Plugins.Count + columns - 1) / columns;
            contentHeight += rowsInGroup * (GridCellHeight + GridCellPadding);
        }
        ListContentHeight = contentHeight;

        canvas.Save();
        canvas.ClipRect(listRect);

        float y = listRect.Top + GridCellPadding - scrollOffset;
        int globalIndex = 0;

        foreach (var group in viewModel.GroupedChoices)
        {
            // Category header
            var headerRect = new SKRect(listRect.Left, y, listRect.Right, y + CategoryHeaderHeight);
            if (headerRect.Bottom >= listRect.Top && headerRect.Top <= listRect.Bottom)
            {
                canvas.DrawText(group.Name.ToUpperInvariant(), headerRect.Left + 8f, headerRect.MidY + 4f, _categoryPaint);
            }
            y += CategoryHeaderHeight;

            // Plugins in grid
            for (int i = 0; i < group.Plugins.Count; i++)
            {
                int col = i % columns;
                int row = i / columns;

                float cellX = listRect.Left + GridCellPadding + col * (actualCellWidth + GridCellPadding);
                float cellY = y + row * (GridCellHeight + GridCellPadding);

                var cellRect = new SKRect(cellX, cellY, cellX + actualCellWidth, cellY + GridCellHeight);

                // Only render if visible
                if (cellRect.Bottom >= listRect.Top && cellRect.Top <= listRect.Bottom)
                {
                    var plugin = group.Plugins[i];
                    bool isSelected = viewModel.SelectedChoice == plugin;

                    if (isSelected)
                    {
                        canvas.DrawRoundRect(new SKRoundRect(cellRect, 4f), _selectedPaint);
                    }

                    // Plugin name
                    canvas.DrawText(plugin.Name, cellRect.Left + 6f, cellRect.Top + 16f, _textPaint);

                    // Plugin description (truncated)
                    if (!string.IsNullOrEmpty(plugin.Description))
                    {
                        DrawEllipsizedText(canvas, plugin.Description, cellRect.Left + 6f, cellRect.Top + 32f, actualCellWidth - 12f, _descriptionPaint);
                    }

                    _itemRects.Add(new ItemRect(globalIndex, plugin, cellRect));
                }
                globalIndex++;
            }

            int rowsInGroup = (group.Plugins.Count + columns - 1) / columns;
            y += rowsInGroup * (GridCellHeight + GridCellPadding);
        }

        canvas.Restore();
    }

    private void RenderVstList(SKCanvas canvas, SKRect listRect, PluginBrowserViewModel viewModel, float scrollOffset)
    {
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
                canvas.DrawText(group.Name.ToUpperInvariant(), headerRect.Left + 8f, headerRect.MidY + 4f, _categoryPaint);
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
                    canvas.DrawText(plugin.Name, rowRect.Left + 8f, rowRect.Top + 16f, _textPaint);

                    // Plugin description
                    if (!string.IsNullOrEmpty(plugin.Description))
                    {
                        DrawEllipsizedText(canvas, plugin.Description, rowRect.Left + 8f, rowRect.Top + 30f, rowRect.Width - 16f, _descriptionPaint);
                    }

                    _itemRects.Add(new ItemRect(globalIndex, plugin, rowRect));
                }
                y += RowHeight;
                globalIndex++;
            }
        }

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
    public bool HitTestBuiltInTab(float x, float y) => _builtInTabRect.Contains(x, y);
    public bool HitTestVstTab(float x, float y) => _vstTabRect.Contains(x, y);

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

    private void DrawEllipsizedText(SKCanvas canvas, string text, float x, float y, float maxWidth, SkiaTextPaint paint)
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

    private static SkiaTextPaint CreateTextPaint(SKColor color, float size, SKFontStyle? style = null, SKTextAlign align = SKTextAlign.Left) =>
        new(color, size, style, align);

    private sealed record ItemRect(int Index, PluginChoice Plugin, SKRect Rect);
}
