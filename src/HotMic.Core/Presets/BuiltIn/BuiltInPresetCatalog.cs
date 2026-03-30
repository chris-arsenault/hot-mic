namespace HotMic.Core.Presets;

internal static partial class BuiltInPresetCatalog
{
    private const string BroadcastPresetName = PluginPresetManager.BroadcastPresetName;
    private const string CleanPresetName = PluginPresetManager.CleanPresetName;
    private const string PodcastPresetName = PluginPresetManager.PodcastPresetName;
    private const string Sm7bPresetName = PluginPresetManager.Sm7bPresetName;
    private const string Nt1PresetName = PluginPresetManager.Nt1PresetName;

    public static Dictionary<string, PluginPresetBank> CreateBanks()
    {
        return new Dictionary<string, PluginPresetBank>(StringComparer.OrdinalIgnoreCase)
        {
            ["builtin:hpf"] = BuildHpfBank(),
            ["builtin:rnnoise"] = BuildRnNoiseBank(),
            ["builtin:voice-gate"] = BuildVoiceGateBank(),
            ["builtin:deesser"] = BuildDeEsserBank(),
            ["builtin:compressor"] = BuildCompressorBank(),
            ["builtin:eq3"] = BuildEqBank(),
            ["builtin:saturation"] = BuildSaturationBank(),
            ["builtin:limiter"] = BuildLimiterBank(),
            ["analysis:visualizer"] = BuildAnalyzerBank(),
            ["builtin:signal-generator"] = BuildSignalGeneratorBank()
        };
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
