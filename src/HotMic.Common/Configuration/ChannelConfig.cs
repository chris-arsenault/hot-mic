using System.Collections.ObjectModel;
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
    /// Canonical chain structure. Ordered list of plugin and container nodes.
    /// Processing order is top-to-bottom; containers process their children in order.
    /// </summary>
    public IList<ChainNodeConfig> Nodes { get; set; } = new List<ChainNodeConfig>();

    /// <summary>Legacy: flat plugin list. Used for migration from old config format.</summary>
    [Obsolete("Use Nodes instead. Kept for backwards-compatible deserialization.")]
    public IList<PluginConfig> Plugins { get; set; } = new List<PluginConfig>();

    /// <summary>Legacy: container list referencing plugins by ID. Used for migration from old config format.</summary>
    [Obsolete("Use Nodes instead. Kept for backwards-compatible deserialization.")]
    public IList<PluginContainerConfig> Containers { get; set; } = new List<PluginContainerConfig>();
}
