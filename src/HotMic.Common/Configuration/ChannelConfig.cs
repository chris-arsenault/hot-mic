namespace HotMic.Common.Configuration;

public sealed class ChannelConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public float InputGainDb { get; set; }
    public float OutputGainDb { get; set; }
    public bool IsMuted { get; set; }
    public bool IsSoloed { get; set; }
    public List<PluginConfig> Plugins { get; set; } = new();
}
