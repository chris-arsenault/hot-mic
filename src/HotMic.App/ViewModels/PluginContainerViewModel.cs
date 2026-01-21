using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HotMic.App.ViewModels;

public partial class PluginContainerViewModel : ObservableObject
{
    private readonly Action? _action;
    private readonly Action? _remove;
    private readonly Action<int, bool>? _bypassSink;

    public PluginContainerViewModel(int channelId, int containerId, string name, Action? action, Action? remove, Action<int, bool>? bypassSink)
    {
        ChannelId = channelId;
        ContainerId = containerId;
        Name = name;
        _action = action;
        _remove = remove;
        _bypassSink = bypassSink;
        ActionCommand = new RelayCommand(() => _action?.Invoke());
        RemoveCommand = new RelayCommand(() => _remove?.Invoke());
    }

    public int ChannelId { get; }

    public int ContainerId { get; }

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private bool isBypassed;

    [ObservableProperty]
    private bool isEmpty;

    [ObservableProperty]
    private float outputPeakLevel;

    [ObservableProperty]
    private float outputRmsLevel;

    public IReadOnlyList<int> PluginInstanceIds { get; private set; } = Array.Empty<int>();

    public IRelayCommand ActionCommand { get; }

    public IRelayCommand RemoveCommand { get; }

    public string ActionLabel => ContainerId <= 0 ? "Add Container" : "Open";

    public void UpdatePluginIds(IReadOnlyList<int> instanceIds)
    {
        PluginInstanceIds = instanceIds.ToArray();
        OnPropertyChanged(nameof(PluginInstanceIds));
        IsEmpty = PluginInstanceIds.Count == 0;
    }

    partial void OnIsEmptyChanged(bool value)
    {
        OnPropertyChanged(nameof(ActionLabel));
    }

    partial void OnIsBypassedChanged(bool value)
    {
        if (ContainerId <= 0)
        {
            return;
        }

        _bypassSink?.Invoke(ContainerId, value);
    }

    /// <summary>
    /// Updates bypass state without firing configuration changes.
    /// </summary>
    public void SetBypassSilent(bool value)
    {
#pragma warning disable MVVMTK0034
        if (isBypassed == value)
        {
            return;
        }
        isBypassed = value;
#pragma warning restore MVVMTK0034
        OnPropertyChanged(nameof(IsBypassed));
    }
}
