using System.Windows;
using System.Windows.Controls;
using HotMic.App.UI.PluginComponents;
using HotMic.Core.Presets;
using SkiaSharp;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace HotMic.App.Views;

/// <summary>
/// Helper class for managing plugin presets in plugin windows.
/// Provides common functionality for preset selection and saving.
/// </summary>
public sealed class PluginPresetHelper
{
    private readonly string _pluginId;
    private readonly PluginPresetManager _presetManager;
    private readonly Action<string, IReadOnlyDictionary<string, float>> _applyPreset;
    private readonly Func<Dictionary<string, float>> _getCurrentParameters;

    public PluginPresetHelper(
        string pluginId,
        PluginPresetManager presetManager,
        Action<string, IReadOnlyDictionary<string, float>> applyPreset,
        Func<Dictionary<string, float>> getCurrentParameters)
    {
        _pluginId = pluginId;
        _presetManager = presetManager;
        _applyPreset = applyPreset;
        _getCurrentParameters = getCurrentParameters;
        CurrentPresetName = PluginPresetManager.CustomPresetName;
    }

    public string CurrentPresetName { get; private set; }

    /// <summary>
    /// Gets the list of available presets for this plugin.
    /// </summary>
    public IReadOnlyList<string> GetPresetOptions()
    {
        return _presetManager.GetPluginPresetNames(_pluginId);
    }

    /// <summary>
    /// Selects and applies a preset.
    /// </summary>
    public void SelectPreset(string presetName)
    {
        if (string.Equals(presetName, PluginPresetManager.CustomPresetName, StringComparison.OrdinalIgnoreCase))
        {
            CurrentPresetName = PluginPresetManager.CustomPresetName;
            return;
        }

        if (_presetManager.TryGetPreset(_pluginId, presetName, out var preset))
        {
            _applyPreset(presetName, preset.Parameters);
            CurrentPresetName = presetName;
        }
    }

    /// <summary>
    /// Marks the current preset as custom (called when parameters change).
    /// </summary>
    public void MarkAsCustom()
    {
        CurrentPresetName = PluginPresetManager.CustomPresetName;
    }

    /// <summary>
    /// Shows the preset dropdown menu.
    /// </summary>
    public void ShowPresetMenu(FrameworkElement placementTarget, SKRect dropdownRect)
    {
        var menu = new ContextMenu();
        var presetOptions = GetPresetOptions();

        foreach (var presetName in presetOptions)
        {
            var item = new MenuItem
            {
                Header = presetName,
                IsCheckable = true,
                IsChecked = string.Equals(presetName, CurrentPresetName, StringComparison.OrdinalIgnoreCase)
            };

            string capturedName = presetName;
            item.Click += (_, _) => SelectPreset(capturedName);
            menu.Items.Add(item);
        }

        menu.PlacementTarget = placementTarget;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Relative;
        menu.HorizontalOffset = dropdownRect.Left;
        menu.VerticalOffset = dropdownRect.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>
    /// Shows the save preset menu.
    /// </summary>
    public void ShowSaveMenu(FrameworkElement placementTarget, Window owner)
    {
        var menu = new ContextMenu();

        // Save as new option
        var saveNewItem = new MenuItem { Header = "Save as New..." };
        saveNewItem.Click += (_, _) => ShowSaveDialog(owner, null);
        menu.Items.Add(saveNewItem);

        // Overwrite current (only if current is a user preset that can be overwritten)
        if (CanOverwritePreset(CurrentPresetName))
        {
            var overwriteItem = new MenuItem { Header = $"Overwrite \"{CurrentPresetName}\"" };
            overwriteItem.Click += (_, _) => SaveCurrentAsPreset(CurrentPresetName);
            menu.Items.Add(overwriteItem);
        }

        menu.PlacementTarget = placementTarget;
        menu.IsOpen = true;
    }

    private bool CanOverwritePreset(string presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
            return false;

        if (string.Equals(presetName, PluginPresetManager.CustomPresetName, StringComparison.OrdinalIgnoreCase))
            return false;

        // Built-in presets cannot be overwritten
        var builtInPresets = _presetManager.GetPluginPresetNames(_pluginId, includeCustom: false);
        return !builtInPresets.Contains(presetName);
    }

    private void ShowSaveDialog(Window owner, string? suggestedName)
    {
        var dialog = new InputDialog("Save Preset", "Enter preset name:", suggestedName ?? "My Preset")
        {
            Owner = owner
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputValue))
        {
            string presetName = dialog.InputValue.Trim();

            // Check if this would overwrite a built-in preset
            var builtInPresets = _presetManager.GetPluginPresetNames(_pluginId, includeCustom: false);
            if (builtInPresets.Contains(presetName))
            {
                System.Windows.MessageBox.Show(
                    $"Cannot overwrite built-in preset \"{presetName}\".",
                    "Save Preset",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            SaveCurrentAsPreset(presetName);
        }
    }

    private void SaveCurrentAsPreset(string presetName)
    {
        var parameters = _getCurrentParameters();
        // Note: Per-plugin user presets would need additional storage implementation
        // For now, we just update the current preset name
        CurrentPresetName = presetName;
    }
}
