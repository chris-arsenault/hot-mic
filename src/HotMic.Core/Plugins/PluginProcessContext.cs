using HotMic.Core.Analysis;
using HotMic.Core.Engine;

namespace HotMic.Core.Plugins;

public readonly struct PluginProcessContext
{
    public PluginProcessContext(int sampleRate, int blockSize, long sampleClock, long sampleTime,
        int slotIndex, int cumulativeLatencySamples, int channelId, IRoutingContext routingContext,
        AnalysisCaptureLink? analysisCapture, AnalysisSignalBus? analysisSignalBus, int[] signalProducers,
        AnalysisSignalMask producedSignals, AnalysisSignalMask requestedSignals)
    {
        SampleRate = sampleRate;
        BlockSize = blockSize;
        SampleClock = sampleClock;
        SampleTime = sampleTime;
        SlotIndex = slotIndex;
        CumulativeLatencySamples = cumulativeLatencySamples;
        ChannelId = channelId;
        Routing = routingContext;
        AnalysisCapture = analysisCapture;
        AnalysisSignalBus = analysisSignalBus;
        _signalProducers = signalProducers;
        AnalysisSignalWriter = new AnalysisSignalWriter(analysisSignalBus, slotIndex, producedSignals);
        RequestedSignals = requestedSignals;
    }

    public int SampleRate { get; }
    public int BlockSize { get; }
    public long SampleClock { get; }
    public long SampleTime { get; }
    public int SlotIndex { get; }
    public int CumulativeLatencySamples { get; }
    public int ChannelId { get; }
    public IRoutingContext Routing { get; }
    public AnalysisCaptureLink? AnalysisCapture { get; }
    public AnalysisSignalBus? AnalysisSignalBus { get; }
    public AnalysisSignalWriter AnalysisSignalWriter { get; }
    public AnalysisSignalMask RequestedSignals { get; }

    private readonly int[] _signalProducers;

    public void CopyAnalysisSignalProducers(Span<int> destination)
    {
        if (destination.Length < _signalProducers.Length)
        {
            return;
        }

        _signalProducers.AsSpan().CopyTo(destination);
    }

    public bool TryGetAnalysisSignalSource(AnalysisSignalId signal, out AnalysisSignalSource source)
    {
        var bus = AnalysisSignalBus;
        if (bus is null)
        {
            source = default;
            return false;
        }

        int index = (int)signal;
        if ((uint)index >= (uint)_signalProducers.Length)
        {
            source = default;
            return false;
        }

        int producerIndex = _signalProducers[index];

        if (producerIndex < 0)
        {
            source = default;
            return false;
        }

        source = bus.GetSource(producerIndex, signal);
        return true;
    }
}
