using System;
using System.Globalization;
using SkiaSharp;

namespace HotMic.App.UI;

internal sealed class MainRenderPrimitives
{
    private readonly MainPaintCache _paints;

    public MainRenderPrimitives(MainPaintCache paints)
    {
        _paints = paints;
    }

    public void DrawBackground(SKCanvas canvas, SKSize size)
    {
        using var rect = new SKRoundRect(new SKRect(0, 0, size.Width, size.Height), MainLayoutMetrics.CornerRadius);
        canvas.DrawRoundRect(rect, _paints.BackgroundPaint);
        canvas.DrawRoundRect(rect, _paints.BorderPaint);
    }

    public void DrawEllipsizedText(SKCanvas canvas, string text, float x, float y, float maxWidth, SkiaTextPaint paint)
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
        {
            len--;
        }

        canvas.DrawText(len > 0 ? $"{text[..len]}{ellipsis}" : ellipsis, x, y, paint);
    }

    public void DrawToggleButton(SKCanvas canvas, SKRect rect, string label, bool isActive, SKPaint activePaint)
    {
        using var roundRect = new SKRoundRect(rect, 3f);
        canvas.DrawRoundRect(roundRect, isActive ? activePaint : _paints.ButtonPaint);
        canvas.DrawRoundRect(roundRect, _paints.BorderPaint);

        using var _p45 = CreateCenteredTextPaint(new SKColor(0x12, 0x12, 0x14), 9f, SKFontStyle.Bold);


        using var _p46 = CreateCenteredTextPaint(_paints.Theme.TextSecondary, 9f);


        var textPaint = isActive ? _p45 : _p46;
        canvas.DrawText(label, rect.MidX, rect.MidY + 3f, textPaint);
    }

    public void DrawMiniKnob(SKCanvas canvas, float x, float y, float size, float value)
    {
        var center = new SKPoint(x + size / 2f, y + size / 2f - 2f);
        float radius = size / 2f - 3f;

        using var _p47 = CreateFillPaint(_paints.Theme.Surface);
        canvas.DrawCircle(center, radius, _p47);
        canvas.DrawCircle(center, radius, _paints.BorderPaint);

        float normalized = (value + 60f) / 72f;
        normalized = Math.Clamp(normalized, 0f, 1f);
        float startAngle = 135f;
        float sweepAngle = 270f * normalized;

        using var arc = new SKPath();
        arc.AddArc(new SKRect(center.X - radius, center.Y - radius, center.X + radius, center.Y + radius), startAngle, sweepAngle);
        using var _p48 = CreateStrokePaint(_paints.Theme.Accent, 2f);
        canvas.DrawPath(arc, _p48);

        float angle = (startAngle + sweepAngle) * MathF.PI / 180f;
        float innerR = radius * 0.4f;
        float outerR = radius * 0.8f;
        using var _p49 = CreateStrokePaint(_paints.Theme.TextPrimary, 1.5f);
        canvas.DrawLine(
            center.X + MathF.Cos(angle) * innerR, center.Y + MathF.Sin(angle) * innerR,
            center.X + MathF.Cos(angle) * outerR, center.Y + MathF.Sin(angle) * outerR,
            _p49);

        string valueText = value.ToString("0", CultureInfo.InvariantCulture);
        canvas.DrawText(valueText, x + 2f, y + size - 1f, _paints.TinyTextPaint);
    }

    public static SKPaint CreateFillPaint(SKColor color) => new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };

    public static SKPaint CreateStrokePaint(SKColor color, float width) => new()
    {
        Color = color,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = width
    };

    public static SkiaTextPaint CreateTextPaint(SKColor color, float size, SKFontStyle? style = null) =>
        new(color, size, style, SKTextAlign.Left);

    public static SkiaTextPaint CreateCenteredTextPaint(SKColor color, float size, SKFontStyle? style = null) =>
        new(color, size, style, SKTextAlign.Center);
}

