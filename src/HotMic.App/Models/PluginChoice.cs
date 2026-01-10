namespace HotMic.App.Models;

public sealed record PluginChoice
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsVst3 { get; init; }
    public string Path { get; init; } = string.Empty;
}
