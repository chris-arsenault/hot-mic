namespace HotMic.App.Models;

public sealed record PluginSlotInfo
{
    public string PluginId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsBypassed { get; init; }
    public float LatencyMs { get; init; }
    public int InstanceId { get; init; }
    public int CopyTargetChannelId { get; init; }

    /// <summary>
    /// Current values for elevated parameters (typically 2 per plugin).
    /// Index 0 = first elevated param, Index 1 = second elevated param.
    /// </summary>
    public float[] ElevatedParamValues { get; init; } = [];
}
