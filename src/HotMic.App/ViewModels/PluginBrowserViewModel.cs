using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotMic.App.Models;

namespace HotMic.App.ViewModels;

public partial class PluginBrowserViewModel : ObservableObject
{
    public PluginBrowserViewModel(IReadOnlyList<PluginChoice> choices)
    {
        Choices = new ObservableCollection<PluginChoice>(choices);
        OkCommand = new RelayCommand(() => CloseRequested?.Invoke(true), () => SelectedChoice is not null);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(false));
    }

    public ObservableCollection<PluginChoice> Choices { get; }

    [ObservableProperty]
    private PluginChoice? selectedChoice;

    public IRelayCommand OkCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public event Action<bool>? CloseRequested;

    partial void OnSelectedChoiceChanged(PluginChoice? value)
    {
        OkCommand.NotifyCanExecuteChanged();
    }
}
