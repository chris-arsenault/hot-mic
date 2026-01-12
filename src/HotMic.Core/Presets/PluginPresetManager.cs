using HotMic.Core.Plugins;

namespace HotMic.Core.Presets;

public sealed record PluginPreset(string Name, IReadOnlyDictionary<string, float> Parameters);

public sealed class PluginPresetBank
{
    private readonly Dictionary<string, PluginPreset> _presetLookup;
    private readonly string[] _presetNames;

    public PluginPresetBank(string pluginId, IReadOnlyList<PluginPreset> presets)
    {
        PluginId = pluginId;
        Presets = presets;
        _presetLookup = new Dictionary<string, PluginPreset>(StringComparer.OrdinalIgnoreCase);
        _presetNames = new string[presets.Count];
        for (int i = 0; i < presets.Count; i++)
        {
            var preset = presets[i];
            _presetLookup[preset.Name] = preset;
            _presetNames[i] = preset.Name;
        }
    }

    public string PluginId { get; }

    public IReadOnlyList<PluginPreset> Presets { get; }

    public IReadOnlyList<string> PresetNames => _presetNames;

    public bool TryGetPreset(string name, out PluginPreset preset)
    {
        return _presetLookup.TryGetValue(name, out preset!);
    }
}

public sealed record ChainPresetEntry(string PluginId, string PresetName);

public sealed record ChainPreset(string Name, IReadOnlyList<ChainPresetEntry> Entries);

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
    private readonly ChainPreset[] _chainPresets;
    private readonly string[] _chainPresetNames;

    private PluginPresetManager()
    {
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

        _chainPresets =
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
            ]),
            new ChainPreset(CleanChainName,
            [
                new ChainPresetEntry("builtin:hpf", CleanPresetName),
                new ChainPresetEntry("builtin:rnnoise", CleanPresetName),
                new ChainPresetEntry("builtin:voice-gate", CleanPresetName),
                new ChainPresetEntry("builtin:deesser", CleanPresetName),
                new ChainPresetEntry("builtin:compressor", CleanPresetName),
                new ChainPresetEntry("builtin:eq3", CleanPresetName),
                new ChainPresetEntry("builtin:limiter", CleanPresetName)
            ]),
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
            ]),
            new ChainPreset(Sm7bChainName,
            [
                new ChainPresetEntry("builtin:hpf", BroadcastPresetName),
                new ChainPresetEntry("builtin:deepfilternet", BroadcastPresetName),
                new ChainPresetEntry("builtin:voice-gate", BroadcastPresetName),
                new ChainPresetEntry("builtin:deesser", Sm7bPresetName),
                new ChainPresetEntry("builtin:compressor", BroadcastPresetName),
                new ChainPresetEntry("builtin:eq3", Sm7bPresetName),
                new ChainPresetEntry("builtin:limiter", BroadcastPresetName)
            ]),
            new ChainPreset(Nt1ChainName,
            [
                new ChainPresetEntry("builtin:hpf", BroadcastPresetName),
                new ChainPresetEntry("builtin:deepfilternet", BroadcastPresetName),
                new ChainPresetEntry("builtin:voice-gate", BroadcastPresetName),
                new ChainPresetEntry("builtin:deesser", Nt1PresetName),
                new ChainPresetEntry("builtin:compressor", BroadcastPresetName),
                new ChainPresetEntry("builtin:eq3", Nt1PresetName),
                new ChainPresetEntry("builtin:limiter", BroadcastPresetName)
            ])
        ];

        _chainLookup = new Dictionary<string, ChainPreset>(StringComparer.OrdinalIgnoreCase);
        _chainPresetNames = new string[_chainPresets.Length];
        for (int i = 0; i < _chainPresets.Length; i++)
        {
            var preset = _chainPresets[i];
            _chainLookup[preset.Name] = preset;
            _chainPresetNames[i] = preset.Name;
        }
    }

    public IReadOnlyList<ChainPreset> ChainPresets => _chainPresets;

    public IReadOnlyList<string> GetChainPresetNames(bool includeCustom = true)
    {
        if (!includeCustom)
        {
            return _chainPresetNames;
        }

        var names = new string[_chainPresetNames.Length + 1];
        names[0] = CustomPresetName;
        Array.Copy(_chainPresetNames, 0, names, 1, _chainPresetNames.Length);
        return names;
    }

    public bool TryGetChainPreset(string name, out ChainPreset preset)
    {
        return _chainLookup.TryGetValue(name, out preset!);
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
                ("Drive", 20f),
                ("Mix", 40f))
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
