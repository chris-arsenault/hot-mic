namespace HotMic.Vst3;

public sealed record Vst3PluginInfo
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
}
