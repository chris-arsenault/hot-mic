namespace HotMic.Core.Presets;

internal static class BuiltInChainPresetCatalog
{
    public static List<ChainPreset> CreatePresets()
    {
        const string BroadcastPresetName = PluginPresetManager.BroadcastPresetName;
        const string CleanPresetName = PluginPresetManager.CleanPresetName;
        const string PodcastPresetName = PluginPresetManager.PodcastPresetName;
        const string Sm7bPresetName = PluginPresetManager.Sm7bPresetName;
        const string Nt1PresetName = PluginPresetManager.Nt1PresetName;
        const string BroadcastChainName = PluginPresetManager.BroadcastChainName;
        const string CleanChainName = PluginPresetManager.CleanChainName;
        const string PodcastChainName = PluginPresetManager.PodcastChainName;
        const string Sm7bChainName = PluginPresetManager.Sm7bChainName;
        const string Nt1ChainName = PluginPresetManager.Nt1ChainName;

        return
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
    }
}
