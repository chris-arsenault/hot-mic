using HotMic.Core.Engine;

namespace HotMic.Core.Plugins;

public readonly struct PluginProcessContext
{
    public PluginProcessContext(int sampleRate, int blockSize, long sampleClock, long sampleTime,
        int slotIndex, int cumulativeLatencySamples, int channelId, RoutingContext routingContext, SidechainBus? sidechainBus,
        int speechProducer, int voicedProducer, int unvoicedProducer, int sibilanceProducer,
        SidechainSignalMask producedSignals)
    {
        SampleRate = sampleRate;
        BlockSize = blockSize;
        SampleClock = sampleClock;
        SampleTime = sampleTime;
        SlotIndex = slotIndex;
        CumulativeLatencySamples = cumulativeLatencySamples;
        ChannelId = channelId;
        Routing = routingContext;
        SidechainBus = sidechainBus;
        _speechProducer = speechProducer;
        _voicedProducer = voicedProducer;
        _unvoicedProducer = unvoicedProducer;
        _sibilanceProducer = sibilanceProducer;
        SidechainWriter = new SidechainWriter(sidechainBus, slotIndex, producedSignals);
    }

    public int SampleRate { get; }
    public int BlockSize { get; }
    public long SampleClock { get; }
    public long SampleTime { get; }
    public int SlotIndex { get; }
    public int CumulativeLatencySamples { get; }
    public int ChannelId { get; }
    public RoutingContext Routing { get; }
    public SidechainBus? SidechainBus { get; }
    public SidechainWriter SidechainWriter { get; }

    private readonly int _speechProducer;
    private readonly int _voicedProducer;
    private readonly int _unvoicedProducer;
    private readonly int _sibilanceProducer;

    public bool TryGetSidechainSource(SidechainSignalId signal, out SidechainSource source)
    {
        var bus = SidechainBus;
        if (bus is null)
        {
            source = default;
            return false;
        }

        int producerIndex = signal switch
        {
            SidechainSignalId.SpeechPresence => _speechProducer,
            SidechainSignalId.VoicedProbability => _voicedProducer,
            SidechainSignalId.UnvoicedEnergy => _unvoicedProducer,
            SidechainSignalId.SibilanceEnergy => _sibilanceProducer,
            _ => -1
        };

        if (producerIndex < 0)
        {
            source = default;
            return false;
        }

        source = bus.GetSource(producerIndex, signal);
        return true;
    }
}
