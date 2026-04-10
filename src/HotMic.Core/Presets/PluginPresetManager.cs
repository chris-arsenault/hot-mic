using HotMic.Common;
using HotMic.Core.Plugins;

namespace HotMic.Core.Presets;

public sealed class PluginPresetManager
{
    public const string CustomPresetName = "Custom";

    public const string BroadcastPresetName = "Broadcast";
    public const string CleanPresetName = "Clean";
    public const string PodcastPresetName = "Podcast";
    public const string Sm7bPresetName = "SM7B";
    public const string Nt1PresetName = "NT1";

    public const string BroadcastChainName = "Broadcast Radio";
    public const string CleanChainName = "Clean/Natural";
    public const string PodcastChainName = "Podcast/Voiceover";
    public const string Sm7bChainName = "SM7B Optimized";
    public const string Nt1ChainName = "NT1 Optimized";

    public static PluginPresetManager Default { get; } = new();

    private readonly Dictionary<string, PluginPresetBank> _banks;
    private readonly Dictionary<string, ChainPreset> _chainLookup;
    private readonly List<ChainPreset> _builtInChainPresets;
    private readonly List<ChainPreset> _userChainPresets;
    private readonly HashSet<string> _builtInChainNames;
    private readonly UserPresetStorage _storage;

    public event EventHandler? PresetsChanged;

    private PluginPresetManager()
    {
        _storage = new UserPresetStorage();
        _banks = BuiltInPresetCatalog.CreateBanks();
        _builtInChainPresets = BuiltInChainPresetCatalog.CreatePresets();

        _builtInChainNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _chainLookup = new Dictionary<string, ChainPreset>(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in _builtInChainPresets)
        {
            _chainLookup[preset.Name] = preset;
            _builtInChainNames.Add(preset.Name);
        }

        // Load user presets
        _userChainPresets = new List<ChainPreset>();
        LoadUserPresets();
    }

    private void LoadUserPresets()
    {
        _userChainPresets.Clear();
        var storedPresets = _storage.LoadChainPresets();

        foreach (var stored in storedPresets)
        {
            // Skip if name conflicts with built-in
            if (_builtInChainNames.Contains(stored.Name))
            {
                continue;
            }

            var entries = new List<ChainPresetEntry>();
            foreach (var plugin in stored.Plugins)
            {
                var parameters = new Dictionary<string, float>(plugin.Parameters, StringComparer.OrdinalIgnoreCase);
                entries.Add(new ChainPresetEntry(plugin.PluginId, CustomPresetName, parameters,
                    plugin.ContainerName, plugin.ContainerBypassed));
            }

            // Build containers from per-entry ContainerName (new format).
            // Fall back to legacy PluginIndices if no entries have ContainerName.
            bool hasEntryContainers = entries.Any(e => !string.IsNullOrEmpty(e.ContainerName));
            var containers = new List<ChainPresetContainer>();

            if (hasEntryContainers)
            {
                // New format: group entries by ContainerName
                var seen = new Dictionary<string, (List<int> Indices, bool Bypassed)>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if (string.IsNullOrEmpty(entry.ContainerName))
                        continue;
                    if (!seen.TryGetValue(entry.ContainerName, out var group))
                    {
                        group = (new List<int>(), entry.ContainerBypassed);
                        seen[entry.ContainerName] = group;
                    }
                    group.Indices.Add(i);
                }
                foreach (var (name, (indices, bypassed)) in seen)
                {
                    containers.Add(new ChainPresetContainer(name, indices, bypassed));
                }
            }
            else
            {
                // Legacy format: PluginIndices on separate container objects
                foreach (var container in stored.Containers)
                {
                    containers.Add(new ChainPresetContainer(container.Name, container.PluginIndices.ToArray(), container.IsBypassed));
                }
            }

            var preset = new ChainPreset(stored.Name, entries, IsBuiltIn: false)
            {
                Containers = containers
            };
            _userChainPresets.Add(preset);
            _chainLookup[preset.Name] = preset;
        }
    }

    public IReadOnlyList<ChainPreset> BuiltInChainPresets => _builtInChainPresets;

    public IReadOnlyList<ChainPreset> UserChainPresets => _userChainPresets;

    public IReadOnlyList<string> GetChainPresetNames(bool includeCustom = true)
    {
        var names = new List<string>();

        if (includeCustom)
        {
            names.Add(CustomPresetName);
        }

        // Built-in presets first
        foreach (var preset in _builtInChainPresets)
        {
            names.Add(preset.Name);
        }

        // Then user presets
        foreach (var preset in _userChainPresets)
        {
            names.Add(preset.Name);
        }

        return names;
    }

    public bool TryGetChainPreset(string name, out ChainPreset preset)
    {
        return _chainLookup.TryGetValue(name, out preset!);
    }

    public bool IsBuiltInPreset(string name)
    {
        return _builtInChainNames.Contains(name);
    }

    /// <summary>
    /// Saves a new user chain preset or overwrites an existing user preset.
    /// Returns false if trying to overwrite a built-in preset.
    /// </summary>
    public bool SaveChainPreset(string name, IReadOnlyList<(string pluginId, Dictionary<string, float> parameters)> plugins, IReadOnlyList<ChainPresetContainer>? containers = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        // Cannot overwrite built-in presets
        if (_builtInChainNames.Contains(name))
        {
            return false;
        }

        // Build index → container lookup from the container list
        var indexToContainer = new Dictionary<int, (string Name, bool Bypassed)>();
        if (containers is not null)
        {
            foreach (var c in containers)
            {
                foreach (int idx in c.PluginIndices)
                    indexToContainer[idx] = (c.Name, c.IsBypassed);
            }
        }

        var stored = new StoredChainPreset { Name = name };
        for (int i = 0; i < plugins.Count; i++)
        {
            var p = plugins[i];
            var entry = new StoredChainEntry { PluginId = p.pluginId };
            foreach (var kvp in p.parameters)
                entry.Parameters[kvp.Key] = kvp.Value;
            if (indexToContainer.TryGetValue(i, out var container))
            {
                entry.ContainerName = container.Name;
                entry.ContainerBypassed = container.Bypassed;
            }
            stored.Plugins.Add(entry);
        }
        // No longer write separate Containers list — container membership
        // is embedded in each entry via ContainerName.

        if (!_storage.SaveChainPreset(stored))
        {
            return false;
        }

        // Update in-memory cache with container membership on each entry
        var entries = new List<ChainPresetEntry>(plugins.Count);
        for (int i = 0; i < plugins.Count; i++)
        {
            var p = plugins[i];
            indexToContainer.TryGetValue(i, out var c);
            entries.Add(new ChainPresetEntry(p.pluginId, CustomPresetName, p.parameters,
                c.Name, c.Bypassed));
        }
        var preset = new ChainPreset(name, entries, IsBuiltIn: false)
        {
            Containers = containers ?? Array.Empty<ChainPresetContainer>()
        };

        // Remove old version if exists
        _userChainPresets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        _userChainPresets.Add(preset);
        _chainLookup[name] = preset;

        PresetsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Deletes a user chain preset. Returns false if preset doesn't exist or is built-in.
    /// </summary>
    public bool DeleteChainPreset(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        // Cannot delete built-in presets
        if (_builtInChainNames.Contains(name))
        {
            return false;
        }

        if (!_storage.DeleteChainPreset(name))
        {
            return false;
        }

        _userChainPresets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        _chainLookup.Remove(name);

        PresetsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Reloads user presets from disk.
    /// </summary>
    public void RefreshUserPresets()
    {
        // Remove old user presets from lookup
        foreach (var preset in _userChainPresets)
        {
            _chainLookup.Remove(preset.Name);
        }

        LoadUserPresets();
        PresetsChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool TryGetPreset(string pluginId, string presetName, out PluginPreset preset)
    {
        preset = null!;
        return _banks.TryGetValue(pluginId, out var bank) && bank.TryGetPreset(presetName, out preset);
    }

    public IReadOnlyList<string> GetPluginPresetNames(string pluginId, bool includeCustom = true)
    {
        if (!_banks.TryGetValue(pluginId, out var bank))
        {
            return includeCustom ? new[] { CustomPresetName } : Array.Empty<string>();
        }

        if (!includeCustom)
        {
            return bank.PresetNames;
        }

        var names = new string[bank.PresetNames.Count + 1];
        names[0] = CustomPresetName;
        for (int i = 0; i < bank.PresetNames.Count; i++)
        {
            names[i + 1] = bank.PresetNames[i];
        }
        return names;
    }

    public PluginPreset GetDefaultPreset(IPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        var parameters = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in plugin.Parameters)
        {
            parameters[parameter.Name] = parameter.DefaultValue;
        }
        return new PluginPreset("Default", parameters);
    }
}
