using System.Threading;
using HotMic.Core.Engine;

namespace HotMic.Core.Plugins.BuiltIn;

/// <summary>
/// Input plugin for copy-created channels that reads from a copy bus and re-emits sidechain signals.
/// </summary>
public sealed class BusInputPlugin : IPlugin, IChannelInputPlugin, ISidechainProducer
{
    private static readonly PluginParameter[] EmptyParameters = Array.Empty<PluginParameter>();
    private int _latencySamples;

    public string Id => "builtin:bus-input";

    public string Name => "Bus Input";

    public bool IsBypassed { get; set; }

    public int LatencySamples => Volatile.Read(ref _latencySamples);

    public IReadOnlyList<PluginParameter> Parameters => EmptyParameters;

    public ChannelInputKind InputKind => ChannelInputKind.Bus;

    public SidechainSignalMask ProducedSignals => SidechainSignalMask.All;

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
            Volatile.Write(ref _latencySamples, 0);
            return;
        }

        var bus = context.Routing.GetCopyBus(context.ChannelId);
        if (bus.SampleClock != context.SampleClock || bus.Length <= 0)
        {
            buffer.Clear();
            Volatile.Write(ref _latencySamples, 0);
            return;
        }

        var audio = bus.Audio;
        int length = Math.Min(buffer.Length, audio.Length);
        audio.Slice(0, length).CopyTo(buffer);
        if (length < buffer.Length)
        {
            buffer.Slice(length).Clear();
        }

        Volatile.Write(ref _latencySamples, bus.LatencySamples);

        if (!context.SidechainWriter.IsEnabled)
        {
            return;
        }

        WriteSidechain(context, bus, SidechainSignalId.SpeechPresence, SidechainSignalMask.SpeechPresence);
        WriteSidechain(context, bus, SidechainSignalId.VoicedProbability, SidechainSignalMask.VoicedProbability);
        WriteSidechain(context, bus, SidechainSignalId.UnvoicedEnergy, SidechainSignalMask.UnvoicedEnergy);
        WriteSidechain(context, bus, SidechainSignalId.SibilanceEnergy, SidechainSignalMask.SibilanceEnergy);
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

    private static void WriteSidechain(in PluginProcessContext context, CopyBus bus, SidechainSignalId signal, SidechainSignalMask mask)
    {
        if ((bus.Signals & mask) == 0)
        {
            return;
        }

        var data = bus.GetSidechain(signal);
        if (data.IsEmpty)
        {
            return;
        }

        context.SidechainWriter.WriteBlock(signal, bus.SampleTime, data);
    }
}
