namespace HotMic.Core.Engine;

/// <summary>
/// Selects how a mono channel is routed into the stereo output bus.
/// </summary>
public enum OutputSendMode
{
    /// <summary>
    /// Route to the left output channel only.
    /// </summary>
    Left = 0,
    /// <summary>
    /// Route to the right output channel only.
    /// </summary>
    Right = 1,
    /// <summary>
    /// Route to both left and right output channels (mono).
    /// </summary>
    Both = 2
}
