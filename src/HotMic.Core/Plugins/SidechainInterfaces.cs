namespace HotMic.Core.Plugins;

public interface ISidechainProducer
{
    SidechainSignalMask ProducedSignals { get; }
}

public interface ISidechainConsumer
{
    SidechainSignalMask RequiredSignals { get; }
    void SetSidechainAvailable(bool available);
}
