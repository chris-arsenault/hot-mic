using HotMic.Core.Engine;

namespace HotMic.Core.Plugins.BuiltIn;

/// <summary>
/// Sends the current buffer to the main output bus.
/// </summary>
public sealed class OutputSendPlugin : IPlugin, IChannelOutputPlugin
{
    private static readonly PluginParameter[] ParametersDefinition =
    [
        new PluginParameter
        {
            Index = 0,
            Name = "Send Mode",
            MinValue = 0f,
            MaxValue = (int)OutputSendMode.Both,
            DefaultValue = (int)OutputSendMode.Both,
            Unit = string.Empty,
            FormatValue = value => ((OutputSendMode)Math.Clamp((int)MathF.Round(value), 0, (int)OutputSendMode.Both)) switch
            {
                OutputSendMode.Left => "Left",
                OutputSendMode.Right => "Right",
                _ => "Both"
            }
        }
    ];

    private OutputSendMode _mode = OutputSendMode.Both;

    /// <summary>
    /// Gets the current output routing mode.
    /// </summary>
    public OutputSendMode Mode => _mode;

    public OutputSendMode OutputMode => _mode;

    public string Id => "builtin:output-send";

    public string Name => "Output Send";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters => ParametersDefinition;

    public void Initialize(int sampleRate, int blockSize)
    {
    }

    public void Process(Span<float> buffer)
    {
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        // Output send happens post-fader in ChannelStrip so mute/output gain are honored.
    }

    public void SetParameter(int index, float value)
    {
        if (index == 0)
        {
            int mode = (int)MathF.Round(value);
            if (mode < 0)
            {
                mode = 0;
            }
            if (mode > (int)OutputSendMode.Both)
            {
                mode = (int)OutputSendMode.Both;
            }

            _mode = (OutputSendMode)mode;
        }
    }

    public byte[] GetState()
    {
        return BitConverter.GetBytes((int)_mode);
    }

    public void SetState(byte[] state)
    {
        if (state is { Length: >= 4 })
        {
            int mode = BitConverter.ToInt32(state, 0);
            if (mode >= 0 && mode <= (int)OutputSendMode.Both)
            {
                _mode = (OutputSendMode)mode;
            }
        }
    }

    public void Dispose()
    {
    }
}
