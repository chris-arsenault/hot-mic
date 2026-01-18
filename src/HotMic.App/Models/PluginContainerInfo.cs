namespace HotMic.App.Models;

public sealed record PluginContainerInfo
{
    public int ContainerId { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsBypassed { get; init; }
    public IReadOnlyList<int> PluginInstanceIds { get; init; } = Array.Empty<int>();
}
