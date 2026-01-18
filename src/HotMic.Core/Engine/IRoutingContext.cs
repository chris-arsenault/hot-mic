namespace HotMic.Core.Engine;

/// <summary>
/// Minimal routing surface exposed to plugins during processing.
/// </summary>
public interface IRoutingContext
{
    /// <summary>
    /// Gets the number of channels available in the routing graph.
    /// </summary>
    int ChannelCount { get; }

    /// <summary>
    /// Reads live input for a channel into the provided buffer.
    /// </summary>
    int ReadInput(int channelId, Span<float> buffer);

    /// <summary>
    /// Gets the copy bus for a channel.
    /// </summary>
    CopyBus GetCopyBus(int channelId);

    /// <summary>
    /// Tries to read the current output for a channel.
    /// </summary>
    bool TryGetChannelOutput(int channelId, out ReadOnlySpan<float> buffer, out int length, out int latencySamples);
}
