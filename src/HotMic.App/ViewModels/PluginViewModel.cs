using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HotMic.App.ViewModels;

public partial class PluginViewModel : ObservableObject
{
    private readonly Action? _action;
    private readonly Action? _remove;
    private readonly Action<HotMic.Core.Engine.ParameterChange>? _parameterSink;
    private readonly Action<int, bool>? _bypassConfigSink;
    private readonly int _channelId;

    private PluginViewModel(int channelId, int slotIndex, Action? action, Action? remove, Action<HotMic.Core.Engine.ParameterChange>? parameterSink, Action<int, bool>? bypassConfigSink)
    {
        _channelId = channelId;
        SlotIndex = slotIndex;
        _action = action;
        _remove = remove;
        _parameterSink = parameterSink;
        _bypassConfigSink = bypassConfigSink;
        ActionCommand = new RelayCommand(() => _action?.Invoke());
        RemoveCommand = new RelayCommand(() => _remove?.Invoke());
    }

    public int SlotIndex { get; }

    [ObservableProperty]
    private string displayName = string.Empty;

    [ObservableProperty]
    private bool isBypassed;

    [ObservableProperty]
    private bool isEmpty;

    public string ActionLabel => IsEmpty ? "Add Plugin" : "Edit Parameters";

    public IRelayCommand ActionCommand { get; }

    public IRelayCommand RemoveCommand { get; }

    public static PluginViewModel CreateEmpty(int channelId, int slotIndex, Action? action = null, Action? remove = null, Action<HotMic.Core.Engine.ParameterChange>? parameterSink = null, Action<int, bool>? bypassConfigSink = null)
    {
        var viewModel = new PluginViewModel(channelId, slotIndex, action, remove, parameterSink, bypassConfigSink)
        {
            IsEmpty = true,
            DisplayName = $"Slot {slotIndex}"
        };

        return viewModel;
    }

    public static PluginViewModel CreateFilled(int channelId, int slotIndex, string name, Action? action = null, Action? remove = null, Action<HotMic.Core.Engine.ParameterChange>? parameterSink = null, Action<int, bool>? bypassConfigSink = null)
    {
        var viewModel = new PluginViewModel(channelId, slotIndex, action, remove, parameterSink, bypassConfigSink)
        {
            IsEmpty = false,
            DisplayName = name
        };

        return viewModel;
    }

    partial void OnIsEmptyChanged(bool value)
    {
        OnPropertyChanged(nameof(ActionLabel));
    }

    partial void OnIsBypassedChanged(bool value)
    {
        _parameterSink?.Invoke(new HotMic.Core.Engine.ParameterChange
        {
            ChannelId = _channelId,
            Type = HotMic.Core.Engine.ParameterType.PluginBypass,
            PluginIndex = SlotIndex - 1,
            Value = value ? 1f : 0f
        });
        _bypassConfigSink?.Invoke(SlotIndex - 1, value);
    }
}
