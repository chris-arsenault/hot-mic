namespace HotMic.Common.Models;

public sealed record AudioDevice
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsDefault { get; init; }
    public bool IsVirtual { get; init; }
}
