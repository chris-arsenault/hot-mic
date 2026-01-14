using HotMic.Core.Dsp;
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
            ["builtin:rnnoise"] = BuildRnNoiseBank(),
            ["builtin:voice-gate"] = BuildVoiceGateBank(),
            ["builtin:deesser"] = BuildDeEsserBank(),
            ["builtin:compressor"] = BuildCompressorBank(),
            ["builtin:eq3"] = BuildEqBank(),
            ["builtin:saturation"] = BuildSaturationBank(),
            ["builtin:limiter"] = BuildLimiterBank(),
            ["builtin:vocal-spectrograph"] = BuildVocalSpectrographBank()
        };

        _builtInChainPresets =
        [
            new ChainPreset(BroadcastChainName,
            [
                new ChainPresetEntry("builtin:hpf", BroadcastPresetName),
                new ChainPresetEntry("builtin:speechdenoiser", BroadcastPresetName),
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
                new ChainPresetEntry("builtin:speechdenoiser", PodcastPresetName),
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
                new ChainPresetEntry("builtin:speechdenoiser", BroadcastPresetName),
                new ChainPresetEntry("builtin:voice-gate", BroadcastPresetName),
                new ChainPresetEntry("builtin:deesser", Sm7bPresetName),
                new ChainPresetEntry("builtin:compressor", BroadcastPresetName),
                new ChainPresetEntry("builtin:eq3", Sm7bPresetName),
                new ChainPresetEntry("builtin:limiter", BroadcastPresetName)
            ], IsBuiltIn: true),
            new ChainPreset(Nt1ChainName,
            [
                new ChainPresetEntry("builtin:hpf", BroadcastPresetName),
                new ChainPresetEntry("builtin:speechdenoiser", BroadcastPresetName),
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
                ("Warmth", 50f),
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

    private static PluginPresetBank BuildVocalSpectrographBank()
    {
        var baseParameters = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            ["FFT Size"] = 2048f,
            ["Window"] = (float)WindowFunction.Hann,
            ["Overlap"] = 0.75f,
            ["Scale"] = (float)FrequencyScale.Mel,
            ["Min Freq"] = 80f,
            ["Max Freq"] = 8000f,
            ["Min dB"] = -80f,
            ["Max dB"] = 0f,
            ["Time Window"] = 5f,
            ["Color Map"] = 6f,
            ["Pitch Overlay"] = 1f,
            ["Formants"] = 1f,
            ["Harmonics"] = 1f,
            ["Voicing"] = 1f,
            ["Pre-Emphasis"] = 1f,
            ["HPF Enabled"] = 1f,
            ["HPF Cutoff"] = 60f,
            ["LPC Order"] = 12f,
            ["Reassign"] = (float)SpectrogramReassignMode.Off,
            ["Reassign Threshold"] = -60f,
            ["Reassign Spread"] = 1f,
            ["Clarity Mode"] = (float)ClarityProcessingMode.Full,
            ["Clarity Noise"] = 1f,
            ["Clarity Harmonic"] = 1f,
            ["Clarity Smoothing"] = 0.3f,
            ["Pitch Algorithm"] = (float)PitchDetectorType.Yin,
            ["Axis Mode"] = (float)SpectrogramAxisMode.Hz,
            ["Voice Range"] = (float)VocalRangeType.Tenor,
            ["Range Overlay"] = 1f,
            ["Guides"] = 1f,
            ["Waveform View"] = 1f,
            ["Spectrum View"] = 1f,
            ["Pitch Meter"] = 1f,
            ["Vowel View"] = 1f,
            ["Smoothing Mode"] = (float)SpectrogramSmoothingMode.Ema,
            ["Brightness"] = 1f,
            ["Gamma"] = 0.8f,
            ["Contrast"] = 1.2f,
            ["Color Levels"] = 32f,
            ["Normalization"] = (float)SpectrogramNormalizationMode.None,
            ["Dynamic Range"] = (float)SpectrogramDynamicRangeMode.Custom
        };

        PluginPreset CreateVocalPreset(string name, params (string parameter, float value)[] overrides)
        {
            var parameters = new Dictionary<string, float>(baseParameters, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < overrides.Length; i++)
            {
                parameters[overrides[i].parameter] = overrides[i].value;
            }
            return new PluginPreset(name, parameters);
        }

        return new PluginPresetBank("builtin:vocal-spectrograph",
        [
            CreateVocalPreset("SpeechMale",
                ("Voice Range", (float)VocalRangeType.Baritone),
                ("Min Freq", 60f),
                ("Time Window", 6f),
                ("Clarity Noise", 0.8f),
                ("Clarity Harmonic", 0.8f)),
            CreateVocalPreset("SpeechFemale",
                ("Voice Range", (float)VocalRangeType.Alto),
                ("Min Freq", 100f),
                ("Max Freq", 9000f),
                ("Time Window", 6f),
                ("Clarity Noise", 0.8f),
                ("Clarity Harmonic", 0.8f)),
            CreateVocalPreset("SingingClassical",
                ("FFT Size", 4096f),
                ("Window", (float)WindowFunction.BlackmanHarris),
                ("Overlap", 0.875f),
                ("Scale", (float)FrequencyScale.Logarithmic),
                ("Min Freq", 60f),
                ("Max Freq", 10000f),
                ("Min dB", -80f),
                ("Time Window", 8f),
                ("Reassign", (float)SpectrogramReassignMode.Frequency),
                ("Clarity Smoothing", 0.35f),
                ("Smoothing Mode", (float)SpectrogramSmoothingMode.Bilateral),
                ("Brightness", 1.1f),
                ("Gamma", 0.75f),
                ("Contrast", 1.25f)),
            CreateVocalPreset("SingingContemporary",
                ("Max Freq", 12000f),
                ("Time Window", 6f),
                ("Color Map", 1f),
                ("Voice Range", (float)VocalRangeType.MezzoSoprano),
                ("Clarity Noise", 0.9f),
                ("Clarity Harmonic", 0.9f),
                ("Brightness", 1.15f),
                ("Gamma", 0.78f),
                ("Contrast", 1.3f)),
            CreateVocalPreset("VoiceoverAnalysis",
                ("FFT Size", 4096f),
                ("Window", (float)WindowFunction.BlackmanHarris),
                ("Overlap", 0.875f),
                ("Min Freq", 70f),
                ("Max Freq", 9000f),
                ("Min dB", -65f),
                ("Time Window", 6f),
                ("Reassign", (float)SpectrogramReassignMode.Frequency),
                ("Clarity Smoothing", 0.35f),
                ("Smoothing Mode", (float)SpectrogramSmoothingMode.Bilateral),
                ("Gamma", 0.75f),
                ("Contrast", 1.25f)),
            CreateVocalPreset("PitchTracking",
                ("FFT Size", 1024f),
                ("Overlap", 0.875f),
                ("Scale", (float)FrequencyScale.Logarithmic),
                ("Time Window", 4f),
                ("Formants", 0f),
                ("Harmonics", 0f),
                ("Vowel View", 0f),
                ("Clarity Mode", (float)ClarityProcessingMode.Noise),
                ("Clarity Noise", 0.6f),
                ("Clarity Harmonic", 0f),
                ("Clarity Smoothing", 0.2f),
                ("Smoothing Mode", (float)SpectrogramSmoothingMode.Ema)),
            CreateVocalPreset("FormantAnalysis",
                ("Window", (float)WindowFunction.Gaussian),
                ("Min dB", -75f),
                ("Pitch Overlay", 0f),
                ("Harmonics", 0f),
                ("LPC Order", 16f),
                ("Clarity Noise", 0.8f),
                ("Clarity Harmonic", 0.6f),
                ("Clarity Smoothing", 0.25f)),
            CreateVocalPreset("HarmonicDetail",
                ("FFT Size", 4096f),
                ("Window", (float)WindowFunction.BlackmanHarris),
                ("Overlap", 0.875f),
                ("Scale", (float)FrequencyScale.Logarithmic),
                ("Min dB", -75f),
                ("Formants", 0f),
                ("Reassign", (float)SpectrogramReassignMode.Frequency),
                ("Clarity Noise", 0.8f),
                ("Clarity Harmonic", 1f),
                ("Clarity Smoothing", 0.35f),
                ("Smoothing Mode", (float)SpectrogramSmoothingMode.Bilateral),
                ("Time Window", 6f)),
            CreateVocalPreset("TransientCapture",
                ("FFT Size", 1024f),
                ("Overlap", 0.5f),
                ("Scale", (float)FrequencyScale.Linear),
                ("Time Window", 3f),
                ("Min dB", -60f),
                ("Pitch Overlay", 0f),
                ("Formants", 0f),
                ("Harmonics", 0f),
                ("Voicing", 0f),
                ("Pitch Meter", 0f),
                ("Vowel View", 0f),
                ("Clarity Mode", (float)ClarityProcessingMode.Noise),
                ("Clarity Noise", 0.4f),
                ("Clarity Harmonic", 0f),
                ("Clarity Smoothing", 0f),
                ("Smoothing Mode", (float)SpectrogramSmoothingMode.Off)),
            CreateVocalPreset("Presentation",
                ("Time Window", 10f),
                ("Axis Mode", (float)SpectrogramAxisMode.Both),
                ("Brightness", 1.2f),
                ("Gamma", 0.75f),
                ("Contrast", 1.35f),
                ("Color Levels", 24f),
                ("Clarity Smoothing", 0.35f),
                ("Smoothing Mode", (float)SpectrogramSmoothingMode.Bilateral)),
            CreateVocalPreset("Technical",
                ("Min dB", -90f),
                ("Axis Mode", (float)SpectrogramAxisMode.Both),
                ("Clarity Mode", (float)ClarityProcessingMode.None),
                ("Clarity Noise", 0f),
                ("Clarity Harmonic", 0f),
                ("Clarity Smoothing", 0f),
                ("Smoothing Mode", (float)SpectrogramSmoothingMode.Off),
                ("Brightness", 1f),
                ("Gamma", 1f),
                ("Contrast", 1f),
                ("Color Levels", 64f)),
            CreateVocalPreset("Minimal",
                ("Time Window", 8f),
                ("Pitch Overlay", 0f),
                ("Formants", 0f),
                ("Harmonics", 0f),
                ("Voicing", 0f),
                ("Range Overlay", 0f),
                ("Guides", 0f),
                ("Waveform View", 0f),
                ("Spectrum View", 0f),
                ("Pitch Meter", 0f),
                ("Vowel View", 0f)),
            CreateVocalPreset("Maximum Clarity",
                ("Min dB", -65f),
                ("Clarity Mode", (float)ClarityProcessingMode.Full),
                ("Clarity Noise", 1f),
                ("Clarity Harmonic", 1f),
                ("Clarity Smoothing", 0.35f),
                ("Smoothing Mode", (float)SpectrogramSmoothingMode.Bilateral),
                ("Gamma", 0.75f)),
            CreateVocalPreset("Balanced"),
            CreateVocalPreset("Low Latency",
                ("FFT Size", 1024f),
                ("Overlap", 0.5f),
                ("Min dB", -60f),
                ("Clarity Mode", (float)ClarityProcessingMode.Noise),
                ("Clarity Noise", 0.5f),
                ("Clarity Harmonic", 0f),
                ("Clarity Smoothing", 0.2f),
                ("Smoothing Mode", (float)SpectrogramSmoothingMode.Ema),
                ("Gamma", 0.85f)),
            CreateVocalPreset("Analysis Mode",
                ("Min dB", -80f),
                ("Clarity Mode", (float)ClarityProcessingMode.None),
                ("Clarity Noise", 0f),
                ("Clarity Harmonic", 0f),
                ("Clarity Smoothing", 0f),
                ("Smoothing Mode", (float)SpectrogramSmoothingMode.Off),
                ("Gamma", 1f),
                ("Contrast", 1f),
                ("Brightness", 1f))
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
