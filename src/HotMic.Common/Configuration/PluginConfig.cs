namespace HotMic.Common.Configuration;

public sealed class PluginConfig
{
    public int InstanceId { get; set; }
    public string Type { get; set; } = string.Empty;
    public bool IsBypassed { get; set; }
    public string PresetName { get; set; } = string.Empty;
    public Dictionary<string, float> Parameters { get; set; } = new();
    public byte[]? State { get; set; }
}
