using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotMic.App.Models;

namespace HotMic.App.ViewModels;

internal sealed class CloseRequestedEventArgs : EventArgs
{
    public CloseRequestedEventArgs(bool result) => Result = result;
    public bool Result { get; }
}

internal enum PluginBrowserTab
{
    BuiltIn,
    Vst
}

internal sealed partial class PluginBrowserViewModel : ObservableObject
{
    private readonly IReadOnlyList<PluginChoice> _allChoices;

    public PluginBrowserViewModel(IReadOnlyList<PluginChoice> choices)
    {
        _allChoices = choices;
        FilteredChoices = new ObservableCollection<PluginChoice>();
        OkCommand = new RelayCommand(() => CloseRequested?.Invoke(this, new CloseRequestedEventArgs(true)), () => SelectedChoice is not null);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, new CloseRequestedEventArgs(false)));
        ApplyFilter();
    }

    public ObservableCollection<PluginChoice> FilteredChoices { get; }

    public ObservableCollection<PluginCategoryGroup> GroupedChoices { get; } = new();

    [ObservableProperty]
    private PluginChoice? selectedChoice;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private PluginBrowserTab selectedTab = PluginBrowserTab.BuiltIn;

    public bool HasVstPlugins => _allChoices.Any(c => c.Category == PluginCategory.Vst);

    public IRelayCommand OkCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public event EventHandler<CloseRequestedEventArgs>? CloseRequested;

    partial void OnSelectedChoiceChanged(PluginChoice? value)
    {
        OkCommand.NotifyCanExecuteChanged();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedTabChanged(PluginBrowserTab value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredChoices.Clear();
        var searchNorm = SearchText.Trim().ToUpperInvariant();

        foreach (var choice in _allChoices)
        {
            // Filter by tab
            bool isVst = choice.Category == PluginCategory.Vst;
            if (SelectedTab == PluginBrowserTab.BuiltIn && isVst)
                continue;
            if (SelectedTab == PluginBrowserTab.Vst && !isVst)
                continue;

            if (string.IsNullOrEmpty(searchNorm) ||
                choice.Name.Contains(searchNorm, StringComparison.OrdinalIgnoreCase) ||
                choice.Description.Contains(searchNorm, StringComparison.OrdinalIgnoreCase))
            {
                FilteredChoices.Add(choice);
            }
        }

        UpdateGroupedChoices();

        if (SelectedChoice is not null && !FilteredChoices.Contains(SelectedChoice))
        {
            SelectedChoice = null;
        }
    }

    private void UpdateGroupedChoices()
    {
        GroupedChoices.Clear();

        var groups = FilteredChoices
            .GroupBy(c => c.Category)
            .OrderBy(g => GetCategoryOrder(g.Key));

        foreach (var group in groups)
        {
            GroupedChoices.Add(new PluginCategoryGroup(
                GetCategoryDisplayName(group.Key),
                group.ToList()));
        }
    }

    private static int GetCategoryOrder(PluginCategory category) => category switch
    {
        PluginCategory.Utility => 0,
        PluginCategory.Routing => 1,
        PluginCategory.Dynamics => 2,
        PluginCategory.Eq => 3,
        PluginCategory.NoiseReduction => 4,
        PluginCategory.Analysis => 5,
        PluginCategory.AiMl => 6,
        PluginCategory.Effects => 7,
        PluginCategory.Vst => 8,
        _ => 99
    };

    private static string GetCategoryDisplayName(PluginCategory category) => category switch
    {
        PluginCategory.Utility => "Containers",
        PluginCategory.Routing => "Routing",
        PluginCategory.Dynamics => "Dynamics",
        PluginCategory.Eq => "Equalizer",
        PluginCategory.NoiseReduction => "Noise Reduction",
        PluginCategory.Analysis => "Analysis",
        PluginCategory.AiMl => "AI / ML",
        PluginCategory.Effects => "Effects",
        PluginCategory.Vst => "VST Plugins",
        _ => "Other"
    };
}

internal sealed class PluginCategoryGroup
{
    public PluginCategoryGroup(string name, IReadOnlyList<PluginChoice> plugins)
    {
        Name = name;
        Plugins = plugins;
    }

    public string Name { get; }
    public IReadOnlyList<PluginChoice> Plugins { get; }
}
