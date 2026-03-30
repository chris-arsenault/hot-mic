namespace HotMic.Core.Plugins;

/// <summary>
/// Allows plugins to clear transient state when the engine resets meters/state.
/// </summary>
public interface IResettablePlugin
{
    /// <summary>
    /// Resets transient plugin state (meters, caches, scratch buffers).
    /// </summary>
    void ResetState();
}
