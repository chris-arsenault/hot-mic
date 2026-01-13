using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotMic.App.Models;

namespace HotMic.App.ViewModels;

public partial class PluginBrowserViewModel : ObservableObject
{
    private readonly IReadOnlyList<PluginChoice> _allChoices;

    public PluginBrowserViewModel(IReadOnlyList<PluginChoice> choices)
    {
        _allChoices = choices;
        FilteredChoices = new ObservableCollection<PluginChoice>(choices);
        OkCommand = new RelayCommand(() => CloseRequested?.Invoke(true), () => SelectedChoice is not null);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(false));
        UpdateGroupedChoices();
    }

    public ObservableCollection<PluginChoice> FilteredChoices { get; }

    public ObservableCollection<PluginCategoryGroup> GroupedChoices { get; } = new();

    [ObservableProperty]
    private PluginChoice? selectedChoice;

    [ObservableProperty]
    private string searchText = string.Empty;

    public IRelayCommand OkCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public event Action<bool>? CloseRequested;

    partial void OnSelectedChoiceChanged(PluginChoice? value)
    {
        OkCommand.NotifyCanExecuteChanged();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredChoices.Clear();
        var searchLower = SearchText.Trim().ToLowerInvariant();

        foreach (var choice in _allChoices)
        {
            if (string.IsNullOrEmpty(searchLower) ||
                choice.Name.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ||
                choice.Description.Contains(searchLower, StringComparison.OrdinalIgnoreCase))
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
        PluginCategory.Dynamics => 0,
        PluginCategory.Eq => 1,
        PluginCategory.NoiseReduction => 2,
        PluginCategory.Analysis => 3,
        PluginCategory.AiMl => 4,
        PluginCategory.Effects => 5,
        PluginCategory.Vst => 6,
        _ => 99
    };

    private static string GetCategoryDisplayName(PluginCategory category) => category switch
    {
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

public sealed class PluginCategoryGroup
{
    public PluginCategoryGroup(string name, IReadOnlyList<PluginChoice> plugins)
    {
        Name = name;
        Plugins = plugins;
    }

    public string Name { get; }
    public IReadOnlyList<PluginChoice> Plugins { get; }
}
