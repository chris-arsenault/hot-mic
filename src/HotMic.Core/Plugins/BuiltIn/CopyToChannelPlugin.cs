namespace HotMic.Core.Plugins.BuiltIn;

/// <summary>
/// Copies audio and sidechain data at the current slot into a copy bus for another channel.
/// </summary>
public sealed class CopyToChannelPlugin : IContextualPlugin
{
    private static readonly PluginParameter[] EmptyParameters = Array.Empty<PluginParameter>();
    private int _targetChannelId;

    public string Id => "builtin:copy";

    public string Name => "Copy to Channel";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters => EmptyParameters;

    public int TargetChannelId
    {
        get => _targetChannelId;
        set => _targetChannelId = value;
    }

    public void Initialize(int sampleRate, int blockSize)
    {
    }

    public void Process(Span<float> buffer)
    {
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return;
        }

        int targetIndex = _targetChannelId - 1;
        if ((uint)targetIndex >= (uint)context.Routing.ChannelCount)
        {
            return;
        }

        var bus = context.Routing.GetCopyBus(targetIndex);
        bus.Write(buffer, context);
    }

    public void SetParameter(int index, float value)
    {
    }

    public byte[] GetState()
    {
        return BitConverter.GetBytes(_targetChannelId);
    }

    public void SetState(byte[] state)
    {
        if (state is { Length: >= 4 })
        {
            _targetChannelId = BitConverter.ToInt32(state, 0);
        }
    }

    public void Dispose()
    {
    }
}
