namespace HotMic.Core.Plugins;

public interface ISidechainProducer
{
    SidechainSignalMask ProducedSignals { get; }
}

public interface ISidechainSignalBlocker
{
    /// <summary>Signals that should be suppressed for downstream plugins.</summary>
    SidechainSignalMask BlockedSignals { get; }
}

public interface ISidechainConsumer
{
    SidechainSignalMask RequiredSignals { get; }
    void SetSidechainAvailable(bool available);
}
