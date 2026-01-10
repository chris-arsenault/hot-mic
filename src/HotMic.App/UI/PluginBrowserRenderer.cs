using System;
using System.Collections.Generic;
using HotMic.App.ViewModels;
using SkiaSharp;

namespace HotMic.App.UI;

public sealed class PluginBrowserRenderer
{
    private const float CornerRadius = 12f;
    private const float TitleBarHeight = 48f;
    private const float Padding = 16f;
    private const float RowHeight = 28f;
    private const float ButtonHeight = 32f;
    private const float ButtonWidth = 90f;

    private readonly HotMicTheme _theme = HotMicTheme.Default;
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _panelPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _textPaint;
    private readonly SKPaint _titlePaint;
    private readonly SKPaint _buttonPaint;
    private readonly SKPaint _buttonActivePaint;
    private readonly SKPaint _buttonTextPaint;

    private readonly List<ItemRect> _itemRects = new();
    private SKRect _addButtonRect;
    private SKRect _cancelButtonRect;
    private SKRect _titleBarRect;

    public PluginBrowserRenderer()
    {
        _backgroundPaint = CreateFillPaint(_theme.BackgroundPrimary);
        _panelPaint = CreateFillPaint(_theme.BackgroundSecondary);
        _borderPaint = CreateStrokePaint(_theme.Border, 1f);
        _textPaint = CreateTextPaint(_theme.TextPrimary, 13f);
        _titlePaint = CreateTextPaint(_theme.TextPrimary, 16f, SKFontStyle.Bold);
        _buttonPaint = CreateFillPaint(_theme.Surface);
        _buttonActivePaint = CreateFillPaint(_theme.Accent);
        _buttonTextPaint = CreateTextPaint(_theme.TextPrimary, 12f, SKFontStyle.Bold, SKTextAlign.Center);
    }

    public float ListContentHeight { get; private set; }

    public float ListViewportHeight { get; private set; }

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

        float listTop = TitleBarHeight + Padding;
        float listBottom = size.Height - Padding - ButtonHeight - 12f;
        float listHeight = MathF.Max(RowHeight, listBottom - listTop);
        var listRect = new SKRect(Padding, listTop, size.Width - Padding, listTop + listHeight);

        canvas.DrawRoundRect(new SKRoundRect(listRect, 8f), _panelPaint);
        canvas.DrawRoundRect(new SKRoundRect(listRect, 8f), _borderPaint);

        ListViewportHeight = listRect.Height;
        ListContentHeight = viewModel.Choices.Count * RowHeight;

        canvas.Save();
        canvas.ClipRect(listRect);

        float y = listRect.Top - scrollOffset;
        for (int i = 0; i < viewModel.Choices.Count; i++)
        {
            var rowRect = new SKRect(listRect.Left, y, listRect.Right, y + RowHeight);
            if (rowRect.Bottom >= listRect.Top && rowRect.Top <= listRect.Bottom)
            {
                bool isSelected = viewModel.SelectedChoice == viewModel.Choices[i];
                if (isSelected)
                {
                    canvas.DrawRect(rowRect, _buttonActivePaint);
                }

                var namePaint = isSelected ? CreateTextPaint(SKColors.Black, 12f) : _textPaint;
                DrawEllipsizedText(canvas, viewModel.Choices[i].Name, rowRect.Left + 10f, rowRect.MidY + 4f, rowRect.Width - 20f, namePaint);
                _itemRects.Add(new ItemRect(i, rowRect));
            }

            y += RowHeight;
        }

        canvas.Restore();

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

    public bool HitTestAdd(float x, float y) => _addButtonRect.Contains(x, y);

    public bool HitTestCancel(float x, float y) => _cancelButtonRect.Contains(x, y);

    public bool HitTestTitleBar(float x, float y) => _titleBarRect.Contains(x, y);

    private void DrawButton(SKCanvas canvas, SKRect rect, string label, bool isActive)
    {
        var round = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(round, isActive ? _buttonActivePaint : _buttonPaint);
        canvas.DrawRoundRect(round, _borderPaint);
        canvas.DrawText(label, rect.MidX, rect.MidY + 4f, _buttonTextPaint);
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

    private sealed record ItemRect(int Index, SKRect Rect);
}
