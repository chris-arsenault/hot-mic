using SkiaSharp;

namespace HotMic.App.UI;

internal sealed class MainLayoutEngine
{
    public MainLayoutFrame Build(SKSize size, bool isMinimal)
    {
        var titleBar = new SKRect(0, 0, size.Width, MainLayoutMetrics.TitleBarHeight);

        if (isMinimal)
        {
            var content = new SKRect(
                MainLayoutMetrics.Padding,
                MainLayoutMetrics.TitleBarHeight + MainLayoutMetrics.MinimalViewPadding,
                size.Width - MainLayoutMetrics.Padding,
                size.Height - MainLayoutMetrics.MinimalViewPadding);
            return new MainLayoutFrame(size, titleBar, SKRect.Empty, content);
        }

        var hotbar = new SKRect(
            0,
            MainLayoutMetrics.TitleBarHeight,
            size.Width,
            MainLayoutMetrics.TitleBarHeight + MainLayoutMetrics.HotbarHeight);

        var fullContent = new SKRect(
            MainLayoutMetrics.Padding,
            MainLayoutMetrics.TitleBarHeight + MainLayoutMetrics.HotbarHeight + MainLayoutMetrics.Padding,
            size.Width - MainLayoutMetrics.Padding,
            size.Height - MainLayoutMetrics.Padding);

        return new MainLayoutFrame(size, titleBar, hotbar, fullContent);
    }
}

internal readonly record struct MainLayoutFrame(SKSize Size, SKRect TitleBarRect, SKRect HotbarRect, SKRect ContentRect)
{
    public bool HasHotbar => !HotbarRect.IsEmpty;
}
