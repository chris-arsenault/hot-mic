using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotMic.App.Models;
using HotMic.Common.Configuration;

namespace HotMic.App.ViewModels;

public partial class ChannelStripViewModel : ObservableObject
{
    private readonly Action<HotMic.Core.Engine.ParameterChange>? _parameterSink;
    private readonly Action<int, int>? _pluginActionSink;
    private readonly Action<int>? _pluginRemoveSink;
    private readonly Action<int, int>? _pluginReorderSink;
    private readonly Action<int, bool>? _pluginBypassConfigSink;
    private readonly Action<int>? _containerActionSink;
    private readonly Action<int>? _containerRemoveSink;
    private readonly Action<int, bool>? _containerBypassSink;
    private readonly Action<int, int>? _containerReorderSink;

    public ChannelStripViewModel(int channelId, string name, Action<HotMic.Core.Engine.ParameterChange>? parameterSink = null, Action<int, int>? pluginActionSink = null, Action<int>? pluginRemoveSink = null, Action<int, int>? pluginReorderSink = null, Action<int, bool>? pluginBypassConfigSink = null, Action<int>? containerActionSink = null, Action<int>? containerRemoveSink = null, Action<int, bool>? containerBypassSink = null, Action<int, int>? containerReorderSink = null)
    {
        ChannelId = channelId;
        Name = name;
        _parameterSink = parameterSink;
        _pluginActionSink = pluginActionSink;
        _pluginRemoveSink = pluginRemoveSink;
        _pluginReorderSink = pluginReorderSink;
        _pluginBypassConfigSink = pluginBypassConfigSink;
        _containerActionSink = containerActionSink;
        _containerRemoveSink = containerRemoveSink;
        _containerBypassSink = containerBypassSink;
        _containerReorderSink = containerReorderSink;
        PluginSlots = new ObservableCollection<PluginViewModel>();
        Containers = new ObservableCollection<PluginContainerViewModel>();

        // Start with just one add placeholder
        PluginSlots.Add(PluginViewModel.CreateEmpty(ChannelId, 1, 0, () => _pluginActionSink?.Invoke(0, 0), () => _pluginRemoveSink?.Invoke(0), _parameterSink, (instanceId, value) => _pluginBypassConfigSink?.Invoke(instanceId, value)));

        ToggleMuteCommand = new RelayCommand(() => IsMuted = !IsMuted);
        ToggleSoloCommand = new RelayCommand(() => IsSoloed = !IsSoloed);
    }

    public int ChannelId { get; }

    public string Name { get; private set; }

    public ObservableCollection<PluginViewModel> PluginSlots { get; }

    public ObservableCollection<PluginContainerViewModel> Containers { get; }

    [ObservableProperty]
    private float inputGainDb;

    [ObservableProperty]
    private float outputGainDb;

    [ObservableProperty]
    private float inputPeakLevel;

    [ObservableProperty]
    private float inputRmsLevel;

    [ObservableProperty]
    private float outputPeakLevel;

    [ObservableProperty]
    private float outputRmsLevel;

    [ObservableProperty]
    private string inputDeviceId = string.Empty;

    [ObservableProperty]
    private string inputDeviceLabel = "No Input";

    [ObservableProperty]
    private InputChannelMode inputChannelMode = InputChannelMode.Sum;

    [ObservableProperty]
    private bool isMuted;

    [ObservableProperty]
    private bool isSoloed;

    public string PeakDbLabel => $"{ToDb(OutputPeakLevel):0} dB";

    public IRelayCommand ToggleMuteCommand { get; }

    public IRelayCommand ToggleSoloCommand { get; }

    partial void OnOutputPeakLevelChanged(float value)
    {
        OnPropertyChanged(nameof(PeakDbLabel));
    }

    partial void OnInputGainDbChanged(float value)
    {
        _parameterSink?.Invoke(new HotMic.Core.Engine.ParameterChange
        {
            ChannelId = ChannelId,
            Type = HotMic.Core.Engine.ParameterType.InputGainDb,
            Value = value
        });
    }

    partial void OnOutputGainDbChanged(float value)
    {
        _parameterSink?.Invoke(new HotMic.Core.Engine.ParameterChange
        {
            ChannelId = ChannelId,
            Type = HotMic.Core.Engine.ParameterType.OutputGainDb,
            Value = value
        });
    }

    partial void OnIsMutedChanged(bool value)
    {
        _parameterSink?.Invoke(new HotMic.Core.Engine.ParameterChange
        {
            ChannelId = ChannelId,
            Type = HotMic.Core.Engine.ParameterType.Mute,
            Value = value ? 1f : 0f
        });
    }

    partial void OnIsSoloedChanged(bool value)
    {
        _parameterSink?.Invoke(new HotMic.Core.Engine.ParameterChange
        {
            ChannelId = ChannelId,
            Type = HotMic.Core.Engine.ParameterType.Solo,
            Value = value ? 1f : 0f
        });
    }

    private static float ToDb(float linear)
    {
        if (linear <= 0f)
        {
            return -60f;
        }

        return 20f * MathF.Log10(linear + 1e-6f);
    }

    public void UpdateName(string name)
    {
        Name = name;
        OnPropertyChanged(nameof(Name));
    }

    public void UpdatePlugins(IReadOnlyList<PluginSlotInfo> slots)
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

        for (int i = 0; i < slots.Count; i++)
        {
            var slotInfo = slots[i];
            if (string.IsNullOrWhiteSpace(slotInfo.Name))
            {
                continue;
            }

            int displayIndex = i + 1;
            int instanceId = slotInfo.InstanceId;

            if (!existing.TryGetValue(instanceId, out var viewModel))
            {
                viewModel = PluginViewModel.CreateFilled(
                    ChannelId,
                    displayIndex,
                    instanceId,
                    slotInfo.PluginId,
                    slotInfo.Name,
                    slotInfo.LatencyMs,
                    slotInfo.ElevatedParamValues,
                    () => _pluginActionSink?.Invoke(instanceId, 0),
                    () => _pluginRemoveSink?.Invoke(instanceId),
                    _parameterSink,
                    (id, value) => _pluginBypassConfigSink?.Invoke(id, value));
            }
            else
            {
                viewModel.UpdateSlotIndex(displayIndex);
                viewModel.UpdateActions(
                    () => _pluginActionSink?.Invoke(instanceId, 0),
                    () => _pluginRemoveSink?.Invoke(instanceId));
            }

            viewModel.UpdateFromSlotInfo(slotInfo);
            PluginSlots.Add(viewModel);
        }

        int addSlotIndex = PluginSlots.Count;
        PluginSlots.Add(PluginViewModel.CreateEmpty(
            ChannelId,
            addSlotIndex + 1,
            0,
            () => _pluginActionSink?.Invoke(0, PluginSlots.Count - 1),
            () => { },
            _parameterSink,
            (id, value) => { }));
    }

    public void UpdateContainers(IReadOnlyList<PluginContainerInfo> containers)
    {
        Containers.Clear();

        for (int i = 0; i < containers.Count; i++)
        {
            var container = containers[i];
            var viewModel = new PluginContainerViewModel(
                ChannelId,
                container.ContainerId,
                container.Name,
                () => _containerActionSink?.Invoke(container.ContainerId),
                () => _containerRemoveSink?.Invoke(container.ContainerId),
                (id, bypass) => _containerBypassSink?.Invoke(id, bypass));
            viewModel.UpdatePluginIds(container.PluginInstanceIds);
            viewModel.SetBypassSilent(container.IsBypassed);
            Containers.Add(viewModel);
        }
    }

    public void MovePlugin(int instanceId, int toIndex)
    {
        // Keep the "+1" placeholder pinned to the end; only reorder real plugins.
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

    public void MoveContainer(int containerId, int targetIndex)
    {
        if (containerId <= 0)
        {
            return;
        }

        _containerReorderSink?.Invoke(containerId, targetIndex);
    }
}
