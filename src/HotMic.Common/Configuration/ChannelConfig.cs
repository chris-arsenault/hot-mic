namespace HotMic.Common.Configuration;

public sealed class ChannelConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PresetName { get; set; } = string.Empty;
    public string InputDeviceId { get; set; } = string.Empty;
    public InputChannelMode InputChannel { get; set; } = InputChannelMode.Sum;
    public float InputGainDb { get; set; }
    public float OutputGainDb { get; set; }
    public bool IsMuted { get; set; }
    public bool IsSoloed { get; set; }
    public List<PluginConfig> Plugins { get; set; } = new();
    public List<PluginContainerConfig> Containers { get; set; } = new();
}
