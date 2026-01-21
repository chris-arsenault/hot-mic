namespace HotMic.Core.Plugins;

/// <summary>
/// Supplies routing dependencies for a plugin so the engine can order channel processing.
/// </summary>
public interface IRoutingDependencyProvider
{
    /// <summary>
    /// Gets the maximum number of dependencies this plugin can report.
    /// </summary>
    int MaxRoutingDependencies { get; }

    /// <summary>
    /// Fills the provided span with dependencies and returns the number written.
    /// </summary>
    int GetRoutingDependencies(int channelId, Span<RoutingDependency> dependencies);
}
