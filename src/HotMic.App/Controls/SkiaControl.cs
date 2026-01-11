using SkiaSharp;
using SkiaSharp.Views.WPF;

namespace HotMic.App.Controls;

public abstract class SkiaControl : SKElement
{
    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        Render(canvas, e.Info.Width, e.Info.Height);
    }

    protected abstract void Render(SKCanvas canvas, int width, int height);

    protected void Redraw() => InvalidateVisual();
}
