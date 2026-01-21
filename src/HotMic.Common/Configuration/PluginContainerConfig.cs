namespace HotMic.Common.Configuration;

public sealed class PluginContainerConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsBypassed { get; set; }
    public List<int> PluginInstanceIds { get; set; } = new();
}
