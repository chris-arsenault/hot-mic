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

    /// <summary>
    /// Ordered list of plugin and container nodes. Processing order is top-to-bottom;
    /// containers process their children in order.
    /// </summary>
    public IList<ChainNodeConfig> Nodes { get; set; } = new List<ChainNodeConfig>();
}
