using System.IO;
using System.Threading;
using HotMic.Common.Configuration;

namespace HotMic.Core.Plugins.BuiltIn;

/// <summary>
/// Input source plugin that reads mono audio for the channel from the routing context.
/// </summary>
public sealed class InputPlugin : IPlugin, IChannelInputPlugin
{
    private static readonly PluginParameter[] EmptyParameters = Array.Empty<PluginParameter>();
    private const byte StateVersion = 1;
    private string _deviceId = string.Empty;
    private int _channelMode = (int)InputChannelMode.Sum;

    public string Id => "builtin:input";

    public string Name => "Input";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters => EmptyParameters;

    public ChannelInputKind InputKind => ChannelInputKind.Device;

    public string DeviceId => Volatile.Read(ref _deviceId);

    public InputChannelMode ChannelMode => (InputChannelMode)Volatile.Read(ref _channelMode);

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
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(StateVersion);
        writer.Write(Volatile.Read(ref _channelMode));
        writer.Write(Volatile.Read(ref _deviceId) ?? string.Empty);
        return stream.ToArray();
    }

    public void SetState(byte[] state)
    {
        if (state is null || state.Length == 0)
        {
            return;
        }

        try
        {
            using var stream = new MemoryStream(state);
            using var reader = new BinaryReader(stream);
            if (reader.ReadByte() != StateVersion)
            {
                return;
            }

            int mode = reader.ReadInt32();
            string deviceId = reader.ReadString();
            SetChannelMode((InputChannelMode)mode);
            SetDeviceId(deviceId);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
    }

    public void SetDeviceId(string deviceId)
    {
        Volatile.Write(ref _deviceId, deviceId ?? string.Empty);
    }

    public void SetChannelMode(InputChannelMode mode)
    {
        int value = (int)mode;
        if (value < (int)InputChannelMode.Sum)
        {
            value = (int)InputChannelMode.Sum;
        }
        if (value > (int)InputChannelMode.Right)
        {
            value = (int)InputChannelMode.Right;
        }

        Volatile.Write(ref _channelMode, value);
    }
}
