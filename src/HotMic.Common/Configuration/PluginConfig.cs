namespace HotMic.Common.Configuration;

public sealed class PluginConfig
{
    public string Type { get; set; } = string.Empty;
    public bool IsBypassed { get; set; }
    public Dictionary<string, float> Parameters { get; set; } = new();
    public byte[]? State { get; set; }
}
