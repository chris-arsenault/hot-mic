using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HotMic.App.ViewModels;

public partial class PluginParametersViewModel : ObservableObject
{
    public PluginParametersViewModel(string pluginName, IEnumerable<PluginParameterViewModel> parameters)
    {
        PluginName = pluginName;
        Parameters = new ObservableCollection<PluginParameterViewModel>(parameters);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke());
    }

    public string PluginName { get; }

    public ObservableCollection<PluginParameterViewModel> Parameters { get; }

    public IRelayCommand CloseCommand { get; }

    public event Action? CloseRequested;
}
