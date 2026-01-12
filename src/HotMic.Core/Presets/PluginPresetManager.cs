using HotMic.Core.Plugins;

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

public sealed record ChainPreset(string Name, IReadOnlyList<ChainPresetEntry> Entries, bool IsBuiltIn = true);

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
        _banks = new Dictionary<string, PluginPresetBank>(StringComparer.OrdinalIgnoreCase)
        {
            ["builtin:hpf"] = BuildHpfBank(),
            ["builtin:deepfilternet"] = BuildDeepFilterNetBank(),
            ["builtin:rnnoise"] = BuildRnNoiseBank(),
            ["builtin:voice-gate"] = BuildVoiceGateBank(),
            ["builtin:deesser"] = BuildDeEsserBank(),
            ["builtin:compressor"] = BuildCompressorBank(),
            ["builtin:eq3"] = BuildEqBank(),
            ["builtin:saturation"] = BuildSaturationBank(),
            ["builtin:limiter"] = BuildLimiterBank()
        };

        _builtInChainPresets =
        [
            new ChainPreset(BroadcastChainName,
            [
                new ChainPresetEntry("builtin:hpf", BroadcastPresetName),
                new ChainPresetEntry("builtin:deepfilternet", BroadcastPresetName),
                new ChainPresetEntry("builtin:voice-gate", BroadcastPresetName),
                new ChainPresetEntry("builtin:deesser", BroadcastPresetName),
                new ChainPresetEntry("builtin:compressor", BroadcastPresetName),
                new ChainPresetEntry("builtin:eq3", BroadcastPresetName),
                new ChainPresetEntry("builtin:limiter", BroadcastPresetName)
            ], IsBuiltIn: true),
            new ChainPreset(CleanChainName,
            [
                new ChainPresetEntry("builtin:hpf", CleanPresetName),
                new ChainPresetEntry("builtin:rnnoise", CleanPresetName),
                new ChainPresetEntry("builtin:voice-gate", CleanPresetName),
                new ChainPresetEntry("builtin:deesser", CleanPresetName),
                new ChainPresetEntry("builtin:compressor", CleanPresetName),
                new ChainPresetEntry("builtin:eq3", CleanPresetName),
                new ChainPresetEntry("builtin:limiter", CleanPresetName)
            ], IsBuiltIn: true),
            new ChainPreset(PodcastChainName,
            [
                new ChainPresetEntry("builtin:hpf", PodcastPresetName),
                new ChainPresetEntry("builtin:deepfilternet", PodcastPresetName),
                new ChainPresetEntry("builtin:voice-gate", PodcastPresetName),
                new ChainPresetEntry("builtin:deesser", PodcastPresetName),
                new ChainPresetEntry("builtin:compressor", PodcastPresetName),
                new ChainPresetEntry("builtin:eq3", PodcastPresetName),
                new ChainPresetEntry("builtin:saturation", PodcastPresetName),
                new ChainPresetEntry("builtin:limiter", PodcastPresetName)
            ], IsBuiltIn: true),
            new ChainPreset(Sm7bChainName,
            [
                new ChainPresetEntry("builtin:hpf", BroadcastPresetName),
                new ChainPresetEntry("builtin:deepfilternet", BroadcastPresetName),
                new ChainPresetEntry("builtin:voice-gate", BroadcastPresetName),
                new ChainPresetEntry("builtin:deesser", Sm7bPresetName),
                new ChainPresetEntry("builtin:compressor", BroadcastPresetName),
                new ChainPresetEntry("builtin:eq3", Sm7bPresetName),
                new ChainPresetEntry("builtin:limiter", BroadcastPresetName)
            ], IsBuiltIn: true),
            new ChainPreset(Nt1ChainName,
            [
                new ChainPresetEntry("builtin:hpf", BroadcastPresetName),
                new ChainPresetEntry("builtin:deepfilternet", BroadcastPresetName),
                new ChainPresetEntry("builtin:voice-gate", BroadcastPresetName),
                new ChainPresetEntry("builtin:deesser", Nt1PresetName),
                new ChainPresetEntry("builtin:compressor", BroadcastPresetName),
                new ChainPresetEntry("builtin:eq3", Nt1PresetName),
                new ChainPresetEntry("builtin:limiter", BroadcastPresetName)
            ], IsBuiltIn: true)
        ];

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
                entries.Add(new ChainPresetEntry(plugin.PluginId, CustomPresetName, parameters));
            }

            var preset = new ChainPreset(stored.Name, entries, IsBuiltIn: false);
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
    public bool SaveChainPreset(string name, IReadOnlyList<(string pluginId, Dictionary<string, float> parameters)> plugins)
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

        var stored = new StoredChainPreset
        {
            Name = name,
            Plugins = plugins.Select(p => new StoredChainEntry
            {
                PluginId = p.pluginId,
                Parameters = new Dictionary<string, float>(p.parameters)
            }).ToList()
        };

        if (!_storage.SaveChainPreset(stored))
        {
            return false;
        }

        // Update in-memory cache
        var entries = plugins.Select(p =>
            new ChainPresetEntry(p.pluginId, CustomPresetName, p.parameters)).ToList();
        var preset = new ChainPreset(name, entries, IsBuiltIn: false);

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
        var parameters = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in plugin.Parameters)
        {
            parameters[parameter.Name] = parameter.DefaultValue;
        }
        return new PluginPreset("Default", parameters);
    }

    private static PluginPresetBank BuildHpfBank()
    {
        return new PluginPresetBank("builtin:hpf",
        [
            CreatePreset(BroadcastPresetName,
                ("Cutoff", 100f),
                ("Slope", 18f)),
            CreatePreset(CleanPresetName,
                ("Cutoff", 80f),
                ("Slope", 12f)),
            CreatePreset(PodcastPresetName,
                ("Cutoff", 100f),
                ("Slope", 18f)),
            CreatePreset(Sm7bPresetName,
                ("Cutoff", 100f),
                ("Slope", 18f)),
            CreatePreset(Nt1PresetName,
                ("Cutoff", 100f),
                ("Slope", 18f))
        ]);
    }

    private static PluginPresetBank BuildDeepFilterNetBank()
    {
        return new PluginPresetBank("builtin:deepfilternet",
        [
            CreatePreset(BroadcastPresetName,
                ("Reduction", 80f),
                ("Attenuation Limit", 40f),
                ("Post-Filter", 1f)),
            CreatePreset(PodcastPresetName,
                ("Reduction", 100f),
                ("Attenuation Limit", 40f),
                ("Post-Filter", 1f))
        ]);
    }

    private static PluginPresetBank BuildRnNoiseBank()
    {
        return new PluginPresetBank("builtin:rnnoise",
        [
            CreatePreset(CleanPresetName,
                ("Reduction", 60f),
                ("VAD Threshold", 0f))
        ]);
    }

    private static PluginPresetBank BuildVoiceGateBank()
    {
        return new PluginPresetBank("builtin:voice-gate",
        [
            CreatePreset(BroadcastPresetName,
                ("Threshold", 0.5f),
                ("Attack", 6f),
                ("Release", 120f),
                ("Hold", 150f)),
            CreatePreset(CleanPresetName,
                ("Threshold", 0.4f),
                ("Attack", 6f),
                ("Release", 120f),
                ("Hold", 200f)),
            CreatePreset(PodcastPresetName,
                ("Threshold", 0.5f),
                ("Attack", 6f),
                ("Release", 120f),
                ("Hold", 100f))
        ]);
    }

    private static PluginPresetBank BuildDeEsserBank()
    {
        return new PluginPresetBank("builtin:deesser",
        [
            CreatePreset(BroadcastPresetName,
                ("Center Freq", 6000f),
                ("Bandwidth", 2000f),
                ("Threshold", -30f),
                ("Reduction", 6f),
                ("Max Range", 10f)),
            CreatePreset(CleanPresetName,
                ("Center Freq", 6000f),
                ("Bandwidth", 2000f),
                ("Threshold", -24f),
                ("Reduction", 3f),
                ("Max Range", 6f)),
            CreatePreset(PodcastPresetName,
                ("Center Freq", 6500f),
                ("Bandwidth", 2000f),
                ("Threshold", -28f),
                ("Reduction", 8f),
                ("Max Range", 10f)),
            CreatePreset(Sm7bPresetName,
                ("Center Freq", 6000f),
                ("Bandwidth", 2000f),
                ("Threshold", -30f),
                ("Reduction", 4f),
                ("Max Range", 8f)),
            CreatePreset(Nt1PresetName,
                ("Center Freq", 6000f),
                ("Bandwidth", 2000f),
                ("Threshold", -30f),
                ("Reduction", 8f),
                ("Max Range", 12f))
        ]);
    }

    private static PluginPresetBank BuildCompressorBank()
    {
        return new PluginPresetBank("builtin:compressor",
        [
            CreatePreset(BroadcastPresetName,
                ("Threshold", -20f),
                ("Ratio", 4f),
                ("Attack", 15f),
                ("Release", 120f),
                ("Makeup", 6f),
                ("Knee", 6f),
                ("Detector", 1f),
                ("Sidechain HPF", 0f)),
            CreatePreset(CleanPresetName,
                ("Threshold", -24f),
                ("Ratio", 2f),
                ("Attack", 20f),
                ("Release", 150f),
                ("Makeup", 0f),
                ("Knee", 6f),
                ("Detector", 1f),
                ("Sidechain HPF", 0f)),
            CreatePreset(PodcastPresetName,
                ("Threshold", -18f),
                ("Ratio", 5f),
                ("Attack", 10f),
                ("Release", 100f),
                ("Makeup", 6f),
                ("Knee", 6f),
                ("Detector", 1f),
                ("Sidechain HPF", 0f))
        ]);
    }

    private static PluginPresetBank BuildEqBank()
    {
        return new PluginPresetBank("builtin:eq3",
        [
            CreatePreset(BroadcastPresetName,
                ("HPF Freq", 40f),
                ("Low Shelf Gain", 3f),
                ("Low Shelf Freq", 120f),
                ("Low-Mid Gain", -3f),
                ("Low-Mid Freq", 300f),
                ("Low-Mid Q", 1f),
                ("High-Mid Gain", 3f),
                ("High-Mid Freq", 3000f),
                ("High-Mid Q", 1f),
                ("High Shelf Gain", 2f),
                ("High Shelf Freq", 10000f)),
            CreatePreset(CleanPresetName,
                ("HPF Freq", 40f),
                ("Low Shelf Gain", 0f),
                ("Low Shelf Freq", 120f),
                ("Low-Mid Gain", 0f),
                ("Low-Mid Freq", 300f),
                ("Low-Mid Q", 1f),
                ("High-Mid Gain", 0f),
                ("High-Mid Freq", 3000f),
                ("High-Mid Q", 1f),
                ("High Shelf Gain", 0f),
                ("High Shelf Freq", 10000f)),
            CreatePreset(PodcastPresetName,
                ("HPF Freq", 40f),
                ("Low Shelf Gain", 4f),
                ("Low Shelf Freq", 100f),
                ("Low-Mid Gain", -4f),
                ("Low-Mid Freq", 300f),
                ("Low-Mid Q", 1f),
                ("High-Mid Gain", 4f),
                ("High-Mid Freq", 3500f),
                ("High-Mid Q", 1f),
                ("High Shelf Gain", 3f),
                ("High Shelf Freq", 12000f)),
            CreatePreset(Sm7bPresetName,
                ("HPF Freq", 40f),
                ("Low Shelf Gain", 3f),
                ("Low Shelf Freq", 120f),
                ("Low-Mid Gain", -3f),
                ("Low-Mid Freq", 300f),
                ("Low-Mid Q", 1f),
                ("High-Mid Gain", 5f),
                ("High-Mid Freq", 4000f),
                ("High-Mid Q", 1f),
                ("High Shelf Gain", 1f),
                ("High Shelf Freq", 10000f)),
            CreatePreset(Nt1PresetName,
                ("HPF Freq", 40f),
                ("Low Shelf Gain", 3f),
                ("Low Shelf Freq", 120f),
                ("Low-Mid Gain", -3f),
                ("Low-Mid Freq", 300f),
                ("Low-Mid Q", 1f),
                ("High-Mid Gain", 2f),
                ("High-Mid Freq", 3000f),
                ("High-Mid Q", 1f),
                ("High Shelf Gain", 2f),
                ("High Shelf Freq", 10000f))
        ]);
    }

    private static PluginPresetBank BuildSaturationBank()
    {
        return new PluginPresetBank("builtin:saturation",
        [
            CreatePreset(PodcastPresetName,
                ("Warmth", 35f),
                ("Blend", 100f))
        ]);
    }

    private static PluginPresetBank BuildLimiterBank()
    {
        return new PluginPresetBank("builtin:limiter",
        [
            CreatePreset(BroadcastPresetName,
                ("Ceiling", -1f),
                ("Release", 50f)),
            CreatePreset(CleanPresetName,
                ("Ceiling", -1f),
                ("Release", 50f)),
            CreatePreset(PodcastPresetName,
                ("Ceiling", -0.5f),
                ("Release", 50f))
        ]);
    }

    private static PluginPreset CreatePreset(string name, params (string parameter, float value)[] entries)
    {
        var parameters = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < entries.Length; i++)
        {
            parameters[entries[i].parameter] = entries[i].value;
        }
        return new PluginPreset(name, parameters);
    }
}
