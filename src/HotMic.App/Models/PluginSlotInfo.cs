namespace HotMic.App.Models;

public sealed record PluginSlotInfo
{
    public string Name { get; init; } = string.Empty;
    public bool IsBypassed { get; init; }
}
