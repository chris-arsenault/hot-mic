using System;
using System.Collections.Generic;
using HotMic.App.ViewModels;
using SkiaSharp;

namespace HotMic.App.UI;

public sealed class PluginParametersRenderer
{
    private const float CornerRadius = 12f;
    private const float TitleBarHeight = 48f;
    private const float Padding = 16f;
    private const float RowHeight = 56f;
    private const float SliderHeight = 6f;
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
    private readonly SKPaint _sliderTrackPaint;
    private readonly SKPaint _sliderFillPaint;
    private readonly SKPaint _latencyPaint;

    private readonly List<ParameterRect> _parameterRects = new();
    private SKRect _closeButtonRect;
    private SKRect _titleBarRect;
    private SKRect _learnButtonRect;

    public PluginParametersRenderer()
    {
        _backgroundPaint = CreateFillPaint(_theme.BackgroundPrimary);
        _panelPaint = CreateFillPaint(_theme.BackgroundSecondary);
        _borderPaint = CreateStrokePaint(_theme.Border, 1f);
        _textPaint = CreateTextPaint(_theme.TextPrimary, 13f);
        _titlePaint = CreateTextPaint(_theme.TextPrimary, 16f, SKFontStyle.Bold);
        _buttonPaint = CreateFillPaint(_theme.Surface);
        _buttonActivePaint = CreateFillPaint(_theme.Accent);
        _buttonTextPaint = CreateTextPaint(_theme.TextPrimary, 12f, SKFontStyle.Bold, SKTextAlign.Center);
        _sliderTrackPaint = CreateFillPaint(_theme.Surface);
        _sliderFillPaint = CreateFillPaint(_theme.Accent);
        _latencyPaint = CreateTextPaint(_theme.TextMuted, 10f, align: SKTextAlign.Right);
    }

    public float ListContentHeight { get; private set; }

    public float ListViewportHeight { get; private set; }

    public void Render(SKCanvas canvas, SKSize size, PluginParametersViewModel viewModel, float dpiScale, float scrollOffset)
    {
        _parameterRects.Clear();
        _learnButtonRect = SKRect.Empty;
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

        string title = string.IsNullOrWhiteSpace(viewModel.PluginName) ? "Parameters" : viewModel.PluginName;
        canvas.DrawText(title, Padding, TitleBarHeight / 2f + _titlePaint.TextSize / 2.5f, _titlePaint);

        if (viewModel.LatencyMs >= 0f)
        {
            string latencyLabel = $"LAT {viewModel.LatencyMs:0.0}ms";
            canvas.DrawText(latencyLabel, size.Width - Padding, TitleBarHeight / 2f + _latencyPaint.TextSize / 2.5f, _latencyPaint);
        }

        bool showGain = viewModel.GainReductionProvider is not null;
        bool showGate = viewModel.GateOpenProvider is not null;
        bool showLearn = viewModel.LearnNoiseAction is not null;

        float headerHeight = showGain || showGate || showLearn ? 60f : 0f;
        float listTop = TitleBarHeight + Padding;
        if (headerHeight > 0f)
        {
            var headerRect = new SKRect(Padding, listTop, size.Width - Padding, listTop + headerHeight);
            DrawHeader(canvas, headerRect, viewModel, showGain, showGate, showLearn);
            listTop = headerRect.Bottom + 12f;
        }
        float listBottom = size.Height - Padding - ButtonHeight - 12f;
        float listHeight = MathF.Max(RowHeight, listBottom - listTop);
        var listRect = new SKRect(Padding, listTop, size.Width - Padding, listTop + listHeight);

        canvas.DrawRoundRect(new SKRoundRect(listRect, 8f), _panelPaint);
        canvas.DrawRoundRect(new SKRoundRect(listRect, 8f), _borderPaint);

        ListViewportHeight = listRect.Height;
        ListContentHeight = viewModel.Parameters.Count * RowHeight;

        canvas.Save();
        canvas.ClipRect(listRect);

        float y = listRect.Top - scrollOffset;
        for (int i = 0; i < viewModel.Parameters.Count; i++)
        {
            var rowRect = new SKRect(listRect.Left, y, listRect.Right, y + RowHeight);
            if (rowRect.Bottom >= listRect.Top && rowRect.Top <= listRect.Bottom)
            {
                DrawParameterRow(canvas, rowRect, viewModel.Parameters[i], i);
            }

            y += RowHeight;
        }

        canvas.Restore();

        float buttonY = size.Height - Padding - ButtonHeight;
        _closeButtonRect = new SKRect(size.Width - Padding - ButtonWidth, buttonY, size.Width - Padding, buttonY + ButtonHeight);
        DrawButton(canvas, _closeButtonRect, "Close", isActive: false);

        canvas.Restore();
    }

    public int HitTestParameter(float x, float y)
    {
        foreach (var item in _parameterRects)
        {
            if (item.SliderRect.Contains(x, y))
            {
                return item.Index;
            }
        }

        return -1;
    }

    public bool HitTestClose(float x, float y) => _closeButtonRect.Contains(x, y);

    public bool HitTestLearn(float x, float y) => !_learnButtonRect.IsEmpty && _learnButtonRect.Contains(x, y);

    public bool HitTestTitleBar(float x, float y) => _titleBarRect.Contains(x, y);

    public SKRect GetSliderRect(int index)
    {
        foreach (var item in _parameterRects)
        {
            if (item.Index == index)
            {
                return item.SliderRect;
            }
        }

        return SKRect.Empty;
    }

    private void DrawParameterRow(SKCanvas canvas, SKRect rowRect, PluginParameterViewModel parameter, int index)
    {
        canvas.DrawText(parameter.Name, rowRect.Left + 10f, rowRect.Top + 18f, _textPaint);
        canvas.DrawText(parameter.DisplayValue, rowRect.Right - 10f, rowRect.Top + 18f, CreateTextPaint(_theme.TextSecondary, 11f, align: SKTextAlign.Right));

        float sliderLeft = rowRect.Left + 10f;
        float sliderRight = rowRect.Right - 10f;
        float sliderTop = rowRect.Bottom - 18f;
        var sliderRect = new SKRect(sliderLeft, sliderTop, sliderRight, sliderTop + SliderHeight);
        canvas.DrawRect(sliderRect, _sliderTrackPaint);

        float normalized = (parameter.Value - parameter.Min) / MathF.Max(0.0001f, parameter.Max - parameter.Min);
        normalized = Math.Clamp(normalized, 0f, 1f);
        var fillRect = new SKRect(sliderRect.Left, sliderRect.Top, sliderRect.Left + sliderRect.Width * normalized, sliderRect.Bottom);
        canvas.DrawRect(fillRect, _sliderFillPaint);
        canvas.DrawRect(sliderRect, _borderPaint);

        _parameterRects.Add(new ParameterRect(index, sliderRect));
    }

    private void DrawHeader(SKCanvas canvas, SKRect rect, PluginParametersViewModel viewModel, bool showGain, bool showGate, bool showLearn)
    {
        _learnButtonRect = SKRect.Empty;
        var round = new SKRoundRect(rect, 8f);
        canvas.DrawRoundRect(round, _panelPaint);
        canvas.DrawRoundRect(round, _borderPaint);

        float x = rect.Left + 12f;
        float y = rect.Top + 12f;

        if (showGain && viewModel.GainReductionProvider is not null)
        {
            canvas.DrawText("Gain Reduction", x, y + 12f, _textPaint);
            float gr = MathF.Max(0f, viewModel.GainReductionProvider());
            float normalized = Math.Clamp(gr / 24f, 0f, 1f);
            var barRect = new SKRect(x, y + 22f, x + 160f, y + 22f + 6f);
            canvas.DrawRect(barRect, _sliderTrackPaint);
            var fillRect = new SKRect(barRect.Left, barRect.Top, barRect.Left + barRect.Width * normalized, barRect.Bottom);
            canvas.DrawRect(fillRect, _sliderFillPaint);
            canvas.DrawRect(barRect, _borderPaint);
            x += 180f;
        }

        if (showGate && viewModel.GateOpenProvider is not null)
        {
            canvas.DrawText("Gate", x, y + 12f, _textPaint);
            bool open = viewModel.GateOpenProvider();
            var indicatorRect = new SKRect(x, y + 22f, x + 14f, y + 36f);
            var paint = open ? _sliderFillPaint : _buttonPaint;
            canvas.DrawOval(indicatorRect, paint);
            canvas.DrawOval(indicatorRect, _borderPaint);
            x += 80f;
        }

        if (showLearn)
        {
            _learnButtonRect = new SKRect(rect.Right - 12f - ButtonWidth, rect.MidY - ButtonHeight / 2f,
                rect.Right - 12f, rect.MidY + ButtonHeight / 2f);
            DrawButton(canvas, _learnButtonRect, "Learn", isActive: false);
        }
    }

    private void DrawButton(SKCanvas canvas, SKRect rect, string label, bool isActive)
    {
        var round = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(round, isActive ? _buttonActivePaint : _buttonPaint);
        canvas.DrawRoundRect(round, _borderPaint);
        canvas.DrawText(label, rect.MidX, rect.MidY + 4f, _buttonTextPaint);
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

    private sealed record ParameterRect(int Index, SKRect SliderRect);
}
