using HotMic.App.ViewModels;
using SkiaSharp;

namespace HotMic.App.UI;

public sealed class PluginContainerRenderer
{
    private const float CornerRadius = 8f;
    private const float TitleBarHeight = 28f;
    private const float Padding = 8f;
    private const float CloseSize = 12f;

    private readonly HotMicTheme _theme = HotMicTheme.Default;
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _iconPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly MainRenderer _pluginRenderer = new();

    private SKRect _titleBarRect;
    private SKRect _closeRect;

    public PluginContainerRenderer()
    {
        _backgroundPaint = CreateFillPaint(_theme.BackgroundPrimary);
        _titleBarPaint = CreateFillPaint(_theme.BackgroundSecondary);
        _borderPaint = CreateStrokePaint(_theme.Border, 1f);
        _iconPaint = CreateStrokePaint(_theme.TextMuted, 1.4f);
        _titlePaint = CreateTextPaint(_theme.TextPrimary, 12f, SKFontStyle.Bold);
    }

    public void Render(SKCanvas canvas, SKSize size, PluginContainerWindowViewModel viewModel, float dpiScale)
    {
        ClearHitTargets();
        canvas.Clear(SKColors.Transparent);

        canvas.Save();
        canvas.Scale(dpiScale);
        size = new SKSize(size.Width / dpiScale, size.Height / dpiScale);

        DrawBackground(canvas, size);
        DrawTitleBar(canvas, size, viewModel);
        DrawPluginStrip(canvas, size, viewModel);

        canvas.Restore();
    }

    public bool HitTestTitleBar(float x, float y) => _titleBarRect.Contains(x, y);

    public bool HitTestClose(float x, float y) => _closeRect.Contains(x, y);

    public PluginSlotHit? HitTestPluginSlot(float x, float y, out PluginSlotRegion region) =>
        _pluginRenderer.HitTestPluginSlot(x, y, out region);

    public PluginKnobHit? HitTestPluginKnob(float x, float y) => _pluginRenderer.HitTestPluginKnob(x, y);

    private void ClearHitTargets()
    {
        _titleBarRect = SKRect.Empty;
        _closeRect = SKRect.Empty;
    }

    private void DrawBackground(SKCanvas canvas, SKSize size)
    {
        var rect = new SKRoundRect(new SKRect(0, 0, size.Width, size.Height), CornerRadius);
        canvas.DrawRoundRect(rect, _backgroundPaint);
        canvas.DrawRoundRect(rect, _borderPaint);
    }

    private void DrawTitleBar(SKCanvas canvas, SKSize size, PluginContainerWindowViewModel viewModel)
    {
        _titleBarRect = new SKRect(0, 0, size.Width, TitleBarHeight);
        canvas.DrawRect(_titleBarRect, _titleBarPaint);
        canvas.DrawLine(0, TitleBarHeight, size.Width, TitleBarHeight, _borderPaint);

        canvas.DrawText(viewModel.Name, Padding, TitleBarHeight / 2f + 4f, _titlePaint);

        float closeX = size.Width - Padding - CloseSize;
        float closeY = (TitleBarHeight - CloseSize) / 2f;
        _closeRect = new SKRect(closeX - 2f, closeY - 2f, closeX + CloseSize + 2f, closeY + CloseSize + 2f);
        canvas.DrawLine(closeX, closeY, closeX + CloseSize, closeY + CloseSize, _iconPaint);
        canvas.DrawLine(closeX + CloseSize, closeY, closeX, closeY + CloseSize, _iconPaint);
    }

    private void DrawPluginStrip(SKCanvas canvas, SKSize size, PluginContainerWindowViewModel viewModel)
    {
        float top = TitleBarHeight + Padding;
        var rect = new SKRect(Padding, top, size.Width - Padding, size.Height - Padding);
        _pluginRenderer.RenderPluginStrip(canvas, rect, viewModel.PluginSlots, viewModel.ChannelIndex, viewModel.MeterScaleVox);
    }

    private static SKPaint CreateFillPaint(SKColor color) => new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };

    private static SKPaint CreateStrokePaint(SKColor color, float width) => new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width };

    private static SkiaTextPaint CreateTextPaint(SKColor color, float size, SKFontStyle? style = null) =>
        new(color, size, style, SKTextAlign.Left);
}
