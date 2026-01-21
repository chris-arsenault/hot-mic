namespace HotMic.Core.Presets;

public sealed record PluginPreset(string Name, IReadOnlyDictionary<string, float> Parameters);

public sealed class PluginPresetBank
{
    private readonly Dictionary<string, PluginPreset> _presetLookup;
    private readonly List<string> _presetNames;

    public PluginPresetBank(string pluginId, IReadOnlyList<PluginPreset> presets)
    {
        PluginId = pluginId;
        _presetLookup = new Dictionary<string, PluginPreset>(StringComparer.OrdinalIgnoreCase);
        _presetNames = new List<string>(presets.Count);
        for (int i = 0; i < presets.Count; i++)
        {
            var preset = presets[i];
            _presetLookup[preset.Name] = preset;
            _presetNames.Add(preset.Name);
        }
    }

    public string PluginId { get; }

    public IReadOnlyList<string> PresetNames => _presetNames;

    public bool TryGetPreset(string name, out PluginPreset preset)
    {
        return _presetLookup.TryGetValue(name, out preset!);
    }

    internal void AddPreset(PluginPreset preset)
    {
        _presetLookup[preset.Name] = preset;
        if (!_presetNames.Contains(preset.Name, StringComparer.OrdinalIgnoreCase))
        {
            _presetNames.Add(preset.Name);
        }
    }

    internal bool RemovePreset(string name)
    {
        if (_presetLookup.Remove(name))
        {
            _presetNames.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            return true;
        }
        return false;
    }
}

public sealed record ChainPresetEntry(string PluginId, string PresetName, IReadOnlyDictionary<string, float>? Parameters = null);

public sealed record ChainPresetContainer(string Name, IReadOnlyList<int> PluginIndices, bool IsBypassed = false);

public sealed record ChainPreset(string Name, IReadOnlyList<ChainPresetEntry> Entries, bool IsBuiltIn = true)
{
    public IReadOnlyList<ChainPresetContainer> Containers { get; init; } = Array.Empty<ChainPresetContainer>();
}
