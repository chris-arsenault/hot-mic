namespace HotMic.Core.Plugins;

public interface IAnalysisSignalProducer
{
    AnalysisSignalMask ProducedSignals { get; }
}

public interface IAnalysisSignalBlocker
{
    /// <summary>Signals that should be suppressed for downstream plugins.</summary>
    AnalysisSignalMask BlockedSignals { get; }
}

public interface IAnalysisSignalConsumer
{
    AnalysisSignalMask RequiredSignals { get; }
    void SetAnalysisSignalsAvailable(bool available);
}
