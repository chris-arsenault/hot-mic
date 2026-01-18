namespace HotMic.Core.Plugins.BuiltIn;

/// <summary>
/// Input source plugin that reads mono audio for the channel from the routing context.
/// </summary>
public sealed class InputPlugin : IContextualPlugin
{
    private static readonly PluginParameter[] EmptyParameters = Array.Empty<PluginParameter>();

    public string Id => "builtin:input";

    public string Name => "Input";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters => EmptyParameters;

    public void Initialize(int sampleRate, int blockSize)
    {
    }

    public void Process(Span<float> buffer)
    {
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        if (IsBypassed)
        {
            buffer.Clear();
            return;
        }

        context.Routing.ReadInput(context.ChannelId, buffer);
    }

    public void SetParameter(int index, float value)
    {
    }

    public byte[] GetState()
    {
        return Array.Empty<byte>();
    }

    public void SetState(byte[] state)
    {
    }

    public void Dispose()
    {
    }
}
