using System;
using SkiaSharp;

namespace HotMic.App.UI;

public sealed class SkiaTextPaint : IDisposable
{
    private readonly SKTypeface _typeface;

    public SkiaTextPaint(SKColor color, float size, SKFontStyle? style = null, SKTextAlign align = SKTextAlign.Left)
        : this(color, size, SKTypeface.FromFamilyName("Segoe UI", style ?? SKFontStyle.Normal), align)
    {
    }

    public SkiaTextPaint(SKColor color, float size, SKTypeface typeface, SKTextAlign align = SKTextAlign.Left)
    {
        _typeface = typeface ?? SKTypeface.Default;
        Align = align;

        Paint = new SKPaint
        {
            Color = color,
            IsAntialias = true
        };

        Font = new SKFont(_typeface, size)
        {
            Edging = SKFontEdging.Antialias,
            Subpixel = true
        };
    }

    public SKPaint Paint { get; }
    public SKFont Font { get; }
    public SKTextAlign Align { get; set; }

    public SKColor Color
    {
        get => Paint.Color;
        set => Paint.Color = value;
    }

    public SKTextAlign TextAlign
    {
        get => Align;
        set => Align = value;
    }

    public float Size
    {
        get => Font.Size;
        set => Font.Size = value;
    }

    public float TextSize
    {
        get => Font.Size;
        set => Font.Size = value;
    }

    public float MeasureText(string text) => Font.MeasureText(text);

    public void DrawText(SKCanvas canvas, string text, float x, float y)
        => canvas.DrawText(text, x, y, Align, Font, Paint);

    public void DrawText(SKCanvas canvas, string text, float x, float y, SKTextAlign align)
        => canvas.DrawText(text, x, y, align, Font, Paint);

    public void Dispose()
    {
        Font.Dispose();
        Paint.Dispose();
        _typeface.Dispose();
    }
}

internal static class SkiaTextExtensions
{
    public static void DrawText(this SKCanvas canvas, string text, float x, float y, SkiaTextPaint paint)
        => paint.DrawText(canvas, text, x, y);
}
