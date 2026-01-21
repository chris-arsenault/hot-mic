using System.Threading;
using HotMic.Core.Engine;

namespace HotMic.Core.Plugins.BuiltIn;

/// <summary>
/// Input plugin for copy-created channels that reads from a copy bus and re-emits analysis signals.
/// </summary>
public sealed class BusInputPlugin : IPlugin, IChannelInputPlugin, IAnalysisSignalProducer
{
    private static readonly PluginParameter[] EmptyParameters = Array.Empty<PluginParameter>();
    private int _latencySamples;

    public string Id => "builtin:bus-input";

    public string Name => "Bus Input";

    public bool IsBypassed { get; set; }

    public int LatencySamples => Volatile.Read(ref _latencySamples);

    public IReadOnlyList<PluginParameter> Parameters => EmptyParameters;

    public ChannelInputKind InputKind => ChannelInputKind.Bus;

    public AnalysisSignalMask ProducedSignals => AnalysisSignalMask.All;

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

        if (!context.AnalysisSignalWriter.IsEnabled)
        {
            return;
        }

        WriteAnalysisSignals(context, bus);
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

    private static void WriteAnalysisSignals(in PluginProcessContext context, CopyBus bus)
    {
        var allowed = context.AnalysisSignalWriter.AllowedSignals;
        if (allowed == AnalysisSignalMask.None || bus.Signals == AnalysisSignalMask.None)
        {
            return;
        }

        for (int i = 0; i < (int)AnalysisSignalId.Count; i++)
        {
            var mask = (AnalysisSignalMask)(1 << i);
            if ((allowed & mask) == 0 || (bus.Signals & mask) == 0)
            {
                continue;
            }

            var signal = (AnalysisSignalId)i;
            var data = bus.GetAnalysisSignal(signal);
            if (data.IsEmpty)
            {
                continue;
            }

            context.AnalysisSignalWriter.WriteBlock(signal, bus.SampleTime, data);
        }
    }
}
