using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HotMic.App.Models;

namespace HotMic.App.ViewModels;

public partial class PluginContainerWindowViewModel : ObservableObject
{
    private readonly Action<int, int>? _pluginActionSink;
    private readonly Action<int>? _pluginRemoveSink;
    private readonly Action<int, int>? _pluginReorderSink;
    private readonly Action<HotMic.Core.Engine.ParameterChange>? _parameterSink;
    private readonly Action<int, bool>? _pluginBypassConfigSink;

    public PluginContainerWindowViewModel(int channelIndex, int containerId, string name, Action<int, int>? pluginActionSink, Action<int>? pluginRemoveSink, Action<int, int>? pluginReorderSink, Action<HotMic.Core.Engine.ParameterChange>? parameterSink, Action<int, bool>? pluginBypassConfigSink, bool meterScaleVox)
    {
        ChannelIndex = channelIndex;
        ContainerId = containerId;
        _pluginActionSink = pluginActionSink;
        _pluginRemoveSink = pluginRemoveSink;
        _pluginReorderSink = pluginReorderSink;
        _parameterSink = parameterSink;
        _pluginBypassConfigSink = pluginBypassConfigSink;
        Name = name;
        MeterScaleVox = meterScaleVox;
        PluginSlots = new ObservableCollection<PluginViewModel>();
        UpdateWindowSize();
    }

    public int ChannelIndex { get; }

    public int ContainerId { get; }

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private double windowWidth;

    [ObservableProperty]
    private double windowHeight;

    [ObservableProperty]
    private bool meterScaleVox;

    public ObservableCollection<PluginViewModel> PluginSlots { get; }

    public void UpdateName(string name)
    {
        Name = name;
    }

    public void UpdatePlugins(IReadOnlyList<PluginSlotInfo> channelSlots, IReadOnlyList<int> containerInstanceIds)
    {
        var existing = new Dictionary<int, PluginViewModel>();
        for (int i = 0; i < PluginSlots.Count; i++)
        {
            var slot = PluginSlots[i];
            if (!slot.IsEmpty && slot.InstanceId > 0)
            {
                existing[slot.InstanceId] = slot;
            }
        }

        PluginSlots.Clear();

        var slotLookup = new Dictionary<int, PluginSlotInfo>(channelSlots.Count);
        for (int i = 0; i < channelSlots.Count; i++)
        {
            if (channelSlots[i].InstanceId > 0)
            {
                slotLookup[channelSlots[i].InstanceId] = channelSlots[i];
            }
        }

        for (int i = 0; i < containerInstanceIds.Count; i++)
        {
            int instanceId = containerInstanceIds[i];
            if (!slotLookup.TryGetValue(instanceId, out var slot))
            {
                continue;
            }

            int displayIndex = PluginSlots.Count + 1;
            if (!existing.TryGetValue(instanceId, out var viewModel))
            {
                viewModel = PluginViewModel.CreateFilled(
                    ChannelIndex,
                    displayIndex,
                    slot.InstanceId,
                    slot.PluginId,
                    slot.Name,
                    slot.LatencyMs,
                    slot.ElevatedParamValues,
                    () => _pluginActionSink?.Invoke(slot.InstanceId, 0),
                    () => _pluginRemoveSink?.Invoke(slot.InstanceId),
                    _parameterSink,
                    (id, value) => _pluginBypassConfigSink?.Invoke(id, value));
            }
            else
            {
                viewModel.UpdateSlotIndex(displayIndex);
                viewModel.UpdateActions(
                    () => _pluginActionSink?.Invoke(slot.InstanceId, 0),
                    () => _pluginRemoveSink?.Invoke(slot.InstanceId));
            }

            viewModel.UpdateFromSlotInfo(slot);
            PluginSlots.Add(viewModel);
        }

        int addSlotIndex = PluginSlots.Count;
        PluginSlots.Add(PluginViewModel.CreateEmpty(
            ChannelIndex,
            addSlotIndex + 1,
            0,
            () => _pluginActionSink?.Invoke(0, PluginSlots.Count - 1),
            () => { },
            _parameterSink,
            (id, value) => { }));
        UpdateWindowSize();
    }

    public void MovePlugin(int instanceId, int toIndex)
    {
        int lastPluginIndex = PluginSlots.Count - 2;
        if (lastPluginIndex < 0)
        {
            return;
        }

        int fromIndex = -1;
        for (int i = 0; i <= lastPluginIndex; i++)
        {
            if (PluginSlots[i].InstanceId == instanceId)
            {
                fromIndex = i;
                break;
            }
        }

        if (fromIndex < 0)
        {
            return;
        }

        if (toIndex < 0)
        {
            toIndex = 0;
        }
        else if (toIndex > lastPluginIndex)
        {
            toIndex = lastPluginIndex;
        }

        if (fromIndex == toIndex)
        {
            return;
        }

        var item = PluginSlots[fromIndex];
        PluginSlots.RemoveAt(fromIndex);
        PluginSlots.Insert(toIndex, item);
        _pluginReorderSink?.Invoke(instanceId, toIndex);
    }

    private void UpdateWindowSize()
    {
        const double titleBarHeight = 28;
        const double padding = 8;
        const double pluginAreaHeight = 90;
        const double pluginSlotWidth = 130;
        const double miniMeterWidth = 6;
        const double pluginSlotSpacing = 2;
        const double innerPadding = 8;

        double baseSlotWidth = pluginSlotWidth - miniMeterWidth - 2;
        double totalSlotsWidth = 0;
        int slotCount = PluginSlots.Count;
        for (int i = 0; i < slotCount; i++)
        {
            bool isEmpty = PluginSlots[i].IsEmpty;
            double slotWidth = isEmpty ? baseSlotWidth * 0.6 : baseSlotWidth;
            totalSlotsWidth += slotWidth + miniMeterWidth;
            if (i < slotCount - 1)
            {
                totalSlotsWidth += pluginSlotSpacing;
            }
        }

        double pluginAreaWidth = totalSlotsWidth + innerPadding;
        double minAreaWidth = pluginSlotWidth + innerPadding;
        if (pluginAreaWidth < minAreaWidth)
        {
            pluginAreaWidth = minAreaWidth;
        }

        WindowWidth = padding * 2 + pluginAreaWidth;
        WindowHeight = titleBarHeight + padding * 2 + pluginAreaHeight;
    }
}
