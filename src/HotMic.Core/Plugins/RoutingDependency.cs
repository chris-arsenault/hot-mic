namespace HotMic.Core.Plugins;

/// <summary>
/// Describes a processing dependency between two channels in the routing graph.
/// </summary>
public readonly struct RoutingDependency
{
    public RoutingDependency(int sourceChannelId, int targetChannelId)
    {
        SourceChannelId = sourceChannelId;
        TargetChannelId = targetChannelId;
    }

    /// <summary>
    /// Gets the 1-based source channel id.
    /// </summary>
    public int SourceChannelId { get; }

    /// <summary>
    /// Gets the 1-based target channel id.
    /// </summary>
    public int TargetChannelId { get; }
}
