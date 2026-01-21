using System;
using SkiaSharp;
using HotMic.App.ViewModels;

namespace HotMic.App.UI;

internal sealed class MainPluginChainRenderer
{
    private readonly MainPaintCache _paints;
    private readonly MainRenderPrimitives _primitives;
    private readonly MainMeterRenderer _meterRenderer;
    private readonly MainHitTargetRegistry _hitTargets;
    private readonly PluginShellRenderer _pluginShellRenderer;
    private readonly RoutingSlotRenderer _routingSlotRenderer;
    private readonly Dictionary<int, int> _containerIndexByPluginId = new();
    private readonly HashSet<int> _drawnContainerIndices = new();

    public MainPluginChainRenderer(
        MainPaintCache paints,
        MainRenderPrimitives primitives,
        MainMeterRenderer meterRenderer,
        MainHitTargetRegistry hitTargets,
        PluginShellRenderer pluginShellRenderer,
        RoutingSlotRenderer routingSlotRenderer)
    {
        _paints = paints;
        _primitives = primitives;
        _meterRenderer = meterRenderer;
        _hitTargets = hitTargets;
        _pluginShellRenderer = pluginShellRenderer;
        _routingSlotRenderer = routingSlotRenderer;
    }

    public void Render(SKCanvas canvas, SKRect rect, ChannelStripViewModel channel, int channelIndex, bool voxScale, IList<CopyBridgeRect> copyBridges, IList<MergeBridgeRect> mergeBridges)
    {
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, MainRenderPrimitives.CreateFillPaint(_paints.Theme.ChannelPlugins));
        canvas.DrawRoundRect(roundRect, _paints.BorderPaint);
        _hitTargets.PluginAreas.Add(new PluginAreaRect(channelIndex, rect));

        float slotX = rect.Left + MainLayoutMetrics.PluginAreaPadding;
        float slotY = rect.Top + MainLayoutMetrics.PluginAreaPadding;
        float slotHeight = rect.Height - MainLayoutMetrics.PluginAreaPadding * 2f;

        int slotCount = channel.PluginSlots.Count;
        if (slotCount == 0)
        {
            return;
        }

        int addSlotIndex = slotCount - 1;
        int pluginCount = Math.Max(0, addSlotIndex);

        _containerIndexByPluginId.Clear();
        for (int i = 0; i < channel.Containers.Count; i++)
        {
            var container = channel.Containers[i];
            var pluginIds = container.PluginInstanceIds;
            for (int j = 0; j < pluginIds.Count; j++)
            {
                int instanceId = pluginIds[j];
                if (instanceId > 0 && !_containerIndexByPluginId.ContainsKey(instanceId))
                {
                    _containerIndexByPluginId[instanceId] = i;
                }
            }
        }

        _drawnContainerIndices.Clear();

        for (int i = 0; i < pluginCount; i++)
        {
            var slot = channel.PluginSlots[i];
            if (slot.InstanceId <= 0 || slot.IsEmpty)
            {
                continue;
            }

            if (_containerIndexByPluginId.TryGetValue(slot.InstanceId, out int containerIndex))
            {
                if (!_drawnContainerIndices.Add(containerIndex))
                {
                    continue;
                }

                var container = channel.Containers[containerIndex];
                float slotWidth = container.IsEmpty
                    ? (MainLayoutMetrics.PluginSlotWidth - MainLayoutMetrics.MiniMeterWidth - MainLayoutMetrics.PluginSlotInnerGap) * 0.6f
                    : MainLayoutMetrics.PluginSlotWidth - MainLayoutMetrics.MiniMeterWidth - MainLayoutMetrics.PluginSlotInnerGap;
                DrawContainerSlot(canvas, slotX, slotY, slotWidth, slotHeight, container, channelIndex, containerIndex);

                float miniMeterX = slotX + slotWidth;
                float meterLevel = container.IsEmpty ? 0f : container.OutputRmsLevel;
                _meterRenderer.DrawMiniMeter(canvas, miniMeterX, slotY + 2f, MainLayoutMetrics.MiniMeterWidth, slotHeight - 4f, meterLevel, voxScale);

                slotX += slotWidth + MainLayoutMetrics.MiniMeterWidth + MainLayoutMetrics.PluginSlotSpacing;
                continue;
            }

            float routingWidth = RoutingSlotRenderer.GetRoutingSlotWidth(slot.PluginId);
            if (routingWidth > 0f)
            {
                var slotRect = new SKRect(slotX, slotY, slotX + routingWidth, slotY + slotHeight);
                _routingSlotRenderer.DrawRoutingSlot(canvas, slotRect, slot, channelIndex, i, channel, voxScale);

                if (slot.PluginId == "builtin:copy" && slot.CopyTargetChannelId > 0)
                {
                    copyBridges.Add(new CopyBridgeRect(channelIndex, slot.CopyTargetChannelId - 1, slotRect));
                }

                slotX += routingWidth + MainLayoutMetrics.PluginSlotSpacing;
                continue;
            }

            float pluginSlotWidth = slot.IsEmpty
                ? (MainLayoutMetrics.PluginSlotWidth - MainLayoutMetrics.MiniMeterWidth - MainLayoutMetrics.PluginSlotInnerGap) * 0.6f
                : MainLayoutMetrics.PluginSlotWidth - MainLayoutMetrics.MiniMeterWidth - MainLayoutMetrics.PluginSlotInnerGap;
            var standardSlotRect = new SKRect(slotX, slotY, slotX + pluginSlotWidth, slotY + slotHeight);
            _pluginShellRenderer.DrawSlot(canvas, standardSlotRect, slot, channelIndex, i);

            float pluginMeterX = slotX + pluginSlotWidth;
            float pluginMeterLevel = slot.IsEmpty ? 0f : slot.OutputRmsLevel;
            _meterRenderer.DrawMiniMeter(canvas, pluginMeterX, slotY + 2f, MainLayoutMetrics.MiniMeterWidth, slotHeight - 4f, pluginMeterLevel, voxScale);

            slotX += pluginSlotWidth + MainLayoutMetrics.MiniMeterWidth + MainLayoutMetrics.PluginSlotSpacing;
        }

        for (int i = 0; i < channel.Containers.Count; i++)
        {
            var container = channel.Containers[i];
            if (container.PluginInstanceIds.Count > 0)
            {
                continue;
            }

            float slotWidth = (MainLayoutMetrics.PluginSlotWidth - MainLayoutMetrics.MiniMeterWidth - MainLayoutMetrics.PluginSlotInnerGap) * 0.6f;
            DrawContainerSlot(canvas, slotX, slotY, slotWidth, slotHeight, container, channelIndex, i);

            float miniMeterX = slotX + slotWidth;
            _meterRenderer.DrawMiniMeter(canvas, miniMeterX, slotY + 2f, MainLayoutMetrics.MiniMeterWidth, slotHeight - 4f, 0f, voxScale);

            slotX += slotWidth + MainLayoutMetrics.MiniMeterWidth + MainLayoutMetrics.PluginSlotSpacing;
        }

        var addSlot = channel.PluginSlots[addSlotIndex];
        float addWidth = addSlot.IsEmpty
            ? (MainLayoutMetrics.PluginSlotWidth - MainLayoutMetrics.MiniMeterWidth - MainLayoutMetrics.PluginSlotInnerGap) * 0.6f
            : MainLayoutMetrics.PluginSlotWidth - MainLayoutMetrics.MiniMeterWidth - MainLayoutMetrics.PluginSlotInnerGap;
        _pluginShellRenderer.DrawSlot(canvas, new SKRect(slotX, slotY, slotX + addWidth, slotY + slotHeight), addSlot, channelIndex, addSlotIndex);

        float addMeterX = slotX + addWidth;
        _meterRenderer.DrawMiniMeter(canvas, addMeterX, slotY + 2f, MainLayoutMetrics.MiniMeterWidth, slotHeight - 4f, 0f, voxScale);
    }

    private void DrawContainerSlot(SKCanvas canvas, float x, float y, float width, float height, PluginContainerViewModel container, int channelIndex, int slotIndex)
    {
        var rect = new SKRect(x, y, x + width, y + height);
        var roundRect = new SKRoundRect(rect, 3f);

        SKPaint bgPaint = container.IsEmpty ? _paints.PluginSlotEmptyPaint :
            container.IsBypassed ? _paints.PluginSlotBypassedPaint : _paints.PluginSlotFilledPaint;
        canvas.DrawRoundRect(roundRect, bgPaint);
        canvas.DrawRoundRect(roundRect, _paints.BorderPaint);

        var bypassRect = SKRect.Empty;
        var removeRect = SKRect.Empty;

        bool isPlaceholder = container.ContainerId <= 0;
        if (isPlaceholder)
        {
            canvas.DrawText($"{slotIndex + 1}", x + 3f, y + 10f, _paints.TinyTextPaint);

            float centerX = x + width / 2f;
            float centerY = y + height / 2f;
            canvas.DrawLine(centerX - 6f, centerY, centerX + 6f, centerY, _paints.IconPaint);
            canvas.DrawLine(centerX, centerY - 6f, centerX, centerY + 6f, _paints.IconPaint);

            _primitives.DrawEllipsizedText(canvas, container.ActionLabel, centerX, y + height - 10f, width - 8f,
                MainRenderPrimitives.CreateCenteredTextPaint(_paints.Theme.TextMuted, 8f));
        }
        else
        {
            float topRowY = y + 2f;
            float topRowH = 12f;
            float bypassW = 22f;
            float bypassX = x + 3f;
            bypassRect = new SKRect(bypassX, topRowY, bypassX + bypassW, topRowY + topRowH);
            var bypassColor = container.IsBypassed ? _paints.Theme.Bypass : _paints.Theme.Surface;
            canvas.DrawRoundRect(new SKRoundRect(bypassRect, 2f), MainRenderPrimitives.CreateFillPaint(bypassColor));
            var bypassTextPaint = container.IsBypassed
                ? MainRenderPrimitives.CreateCenteredTextPaint(new SKColor(0x12, 0x12, 0x14), 7f, SKFontStyle.Bold)
                : MainRenderPrimitives.CreateCenteredTextPaint(_paints.Theme.TextMuted, 7f);
            canvas.DrawText("BYP", bypassRect.MidX, bypassRect.MidY + 2.5f, bypassTextPaint);

            float removeSize = 8f;
            float removeX = x + width - removeSize - 4f;
            float removeY = topRowY + (topRowH - removeSize) / 2f;
            removeRect = new SKRect(removeX - 2f, removeY - 2f, removeX + removeSize + 2f, removeY + removeSize + 2f);
            canvas.DrawLine(removeX, removeY, removeX + removeSize, removeY + removeSize, _paints.IconPaint);
            canvas.DrawLine(removeX + removeSize, removeY, removeX, removeY + removeSize, _paints.IconPaint);

            string displayName = string.IsNullOrWhiteSpace(container.Name) ? $"Container {slotIndex + 1}" : container.Name;
            float nameLeft = bypassX + bypassW + 4f;
            float nameRight = removeX - 4f;
            float nameY = topRowY + topRowH - 2f;
            float nameCenterX = nameLeft + (nameRight - nameLeft) / 2f;
            var namePaint = container.IsBypassed
                ? MainRenderPrimitives.CreateCenteredTextPaint(_paints.Theme.TextMuted, 8f)
                : MainRenderPrimitives.CreateCenteredTextPaint(_paints.Theme.TextSecondary, 8f);
            _primitives.DrawEllipsizedText(canvas, displayName, nameCenterX, nameY, nameRight - nameLeft, namePaint);

            int pluginCount = container.PluginInstanceIds.Count;
            string countText = pluginCount == 1 ? "1 plugin" : $"{pluginCount} plugins";
            _primitives.DrawEllipsizedText(canvas, countText, x + width / 2f, y + height / 2f + 6f, width - 8f,
                MainRenderPrimitives.CreateCenteredTextPaint(_paints.Theme.TextMuted, 8f));

            _primitives.DrawEllipsizedText(canvas, container.ActionLabel, x + width / 2f, y + height - 10f, width - 8f,
                MainRenderPrimitives.CreateCenteredTextPaint(_paints.Theme.TextMuted, 8f));
        }

        _hitTargets.ContainerSlots.Add(new ContainerSlotRect(channelIndex, container.ContainerId, slotIndex, rect, bypassRect, removeRect));
    }
}
