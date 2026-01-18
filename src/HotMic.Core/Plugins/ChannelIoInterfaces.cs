using HotMic.Core.Engine;

namespace HotMic.Core.Plugins;

/// <summary>
/// Describes the input source role for a channel input plugin.
/// Higher values take precedence when multiple inputs are present.
/// </summary>
public enum ChannelInputKind
{
    Device = 0,
    Bus = 1
}

/// <summary>
/// Marks a plugin as the channel input provider.
/// </summary>
public interface IChannelInputPlugin
{
    /// <summary>
    /// Gets the input kind for the plugin.
    /// </summary>
    ChannelInputKind InputKind { get; }
}

/// <summary>
/// Marks a plugin as the channel output endpoint.
/// </summary>
public interface IChannelOutputPlugin
{
    /// <summary>
    /// Gets the output routing mode for the plugin.
    /// </summary>
    OutputSendMode OutputMode { get; }
}
