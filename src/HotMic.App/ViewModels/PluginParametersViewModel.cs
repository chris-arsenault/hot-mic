using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HotMic.App.ViewModels;

public partial class PluginParametersViewModel : ObservableObject
{
    public PluginParametersViewModel(string pluginName, IEnumerable<PluginParameterViewModel> parameters,
        Func<float>? gainReductionProvider = null, Func<bool>? gateOpenProvider = null, Action? learnNoiseAction = null)
    {
        PluginName = pluginName;
        Parameters = new ObservableCollection<PluginParameterViewModel>(parameters);
        GainReductionProvider = gainReductionProvider;
        GateOpenProvider = gateOpenProvider;
        LearnNoiseAction = learnNoiseAction;
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke());
    }

    public string PluginName { get; }

    public ObservableCollection<PluginParameterViewModel> Parameters { get; }

    public Func<float>? GainReductionProvider { get; }

    public Func<bool>? GateOpenProvider { get; }

    public Action? LearnNoiseAction { get; }

    public IRelayCommand CloseCommand { get; }

    public event Action? CloseRequested;
}
