using SkiaSharp;
using HotMic.App.ViewModels;

namespace HotMic.App.UI;

internal sealed class MainPluginStripRenderer
{
    private readonly MainPaintCache _paints;
    private readonly MainMeterRenderer _meterRenderer;
    private readonly PluginShellRenderer _pluginShellRenderer;

    public MainPluginStripRenderer(MainPaintCache paints, MainMeterRenderer meterRenderer, PluginShellRenderer pluginShellRenderer)
    {
        _paints = paints;
        _meterRenderer = meterRenderer;
        _pluginShellRenderer = pluginShellRenderer;
    }

    public void Render(SKCanvas canvas, SKRect bounds, IReadOnlyList<PluginViewModel> slots, int channelIndex, bool voxScale)
    {
        var roundRect = new SKRoundRect(bounds, 4f);
        canvas.DrawRoundRect(roundRect, MainRenderPrimitives.CreateFillPaint(_paints.Theme.ChannelPlugins));
        canvas.DrawRoundRect(roundRect, _paints.BorderPaint);

        float slotX = bounds.Left + 4f;
        float slotY = bounds.Top + 4f;
        float slotHeight = bounds.Height - 8f;

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            float slotWidth = slot.IsEmpty
                ? (MainLayoutMetrics.PluginSlotWidth - MainLayoutMetrics.MiniMeterWidth - MainLayoutMetrics.PluginSlotInnerGap) * 0.6f
                : MainLayoutMetrics.PluginSlotWidth - MainLayoutMetrics.MiniMeterWidth - MainLayoutMetrics.PluginSlotInnerGap;
            _pluginShellRenderer.DrawSlot(canvas, new SKRect(slotX, slotY, slotX + slotWidth, slotY + slotHeight), slot, channelIndex, i);

            float miniMeterX = slotX + slotWidth;
            float meterLevel = slot.IsEmpty ? 0f : slot.OutputRmsLevel;
            _meterRenderer.DrawMiniMeter(canvas, miniMeterX, slotY + 2f, MainLayoutMetrics.MiniMeterWidth, slotHeight - 4f, meterLevel, voxScale);

            slotX += slotWidth + MainLayoutMetrics.MiniMeterWidth + MainLayoutMetrics.PluginSlotSpacing;
        }
    }
}
