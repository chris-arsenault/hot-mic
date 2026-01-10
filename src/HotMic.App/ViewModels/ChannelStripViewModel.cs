using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotMic.App.Models;

namespace HotMic.App.ViewModels;

public partial class ChannelStripViewModel : ObservableObject
{
    private readonly Action<HotMic.Core.Engine.ParameterChange>? _parameterSink;
    private readonly Action<int>? _pluginActionSink;
    private readonly Action<int>? _pluginRemoveSink;
    private readonly Action<int, int>? _pluginReorderSink;
    private readonly Action<int, bool>? _pluginBypassConfigSink;

    public ChannelStripViewModel(int channelId, string name, Action<HotMic.Core.Engine.ParameterChange>? parameterSink = null, Action<int>? pluginActionSink = null, Action<int>? pluginRemoveSink = null, Action<int, int>? pluginReorderSink = null, Action<int, bool>? pluginBypassConfigSink = null)
    {
        ChannelId = channelId;
        Name = name;
        _parameterSink = parameterSink;
        _pluginActionSink = pluginActionSink;
        _pluginRemoveSink = pluginRemoveSink;
        _pluginReorderSink = pluginReorderSink;
        _pluginBypassConfigSink = pluginBypassConfigSink;
        PluginSlots = new ObservableCollection<PluginViewModel>();
        for (int i = 0; i < 5; i++)
        {
            int slotIndex = i + 1;
            int slot = i;
            PluginSlots.Add(PluginViewModel.CreateEmpty(ChannelId, slotIndex, () => _pluginActionSink?.Invoke(slot), () => _pluginRemoveSink?.Invoke(slot), _parameterSink, (index, value) => _pluginBypassConfigSink?.Invoke(index, value)));
        }

        ToggleMuteCommand = new RelayCommand(() => IsMuted = !IsMuted);
        ToggleSoloCommand = new RelayCommand(() => IsSoloed = !IsSoloed);
    }

    public int ChannelId { get; }

    public string Name { get; private set; }

    public ObservableCollection<PluginViewModel> PluginSlots { get; }

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
        PluginSlots.Clear();
        for (int i = 0; i < 5; i++)
        {
            if (i < slots.Count && !string.IsNullOrWhiteSpace(slots[i].Name))
            {
                int slotIndex = i + 1;
                int slot = i;
                var viewModel = PluginViewModel.CreateFilled(ChannelId, slotIndex, slots[i].Name, () => _pluginActionSink?.Invoke(slot), () => _pluginRemoveSink?.Invoke(slot), _parameterSink, (index, value) => _pluginBypassConfigSink?.Invoke(index, value));
                viewModel.IsBypassed = slots[i].IsBypassed;
                PluginSlots.Add(viewModel);
            }
            else
            {
                int slotIndex = i + 1;
                int slot = i;
                PluginSlots.Add(PluginViewModel.CreateEmpty(ChannelId, slotIndex, () => _pluginActionSink?.Invoke(slot), () => _pluginRemoveSink?.Invoke(slot), _parameterSink, (index, value) => _pluginBypassConfigSink?.Invoke(index, value)));
            }
        }
    }

    public void MovePlugin(int fromIndex, int toIndex)
    {
        if ((uint)fromIndex >= (uint)PluginSlots.Count || (uint)toIndex >= (uint)PluginSlots.Count)
        {
            return;
        }

        var item = PluginSlots[fromIndex];
        PluginSlots.RemoveAt(fromIndex);
        PluginSlots.Insert(toIndex, item);
        _pluginReorderSink?.Invoke(fromIndex, toIndex);
    }
}
