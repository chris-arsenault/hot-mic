using System.Threading;
using HotMic.Core.Plugins;

namespace HotMic.Core.Analysis;

public enum AnalysisCaptureSource
{
    Output = 0,
    Plugin = 1
}

/// <summary>
/// Captures audio and analysis signal context for the analysis orchestrator.
/// </summary>
public sealed class AnalysisCaptureLink
{
    private AnalysisOrchestrator? _orchestrator;
    private AnalysisSignalBus? _signalBus;
    private readonly int[] _producerBufferA;
    private readonly int[] _producerBufferB;
    private int[] _activeProducers;
    private long _writeSampleTime;
    private long _lastCaptureSampleClock = long.MinValue;
    private int _lastCaptureSource;

    // Debug counters
    private long _captureCallCount;
    private long _skippedNoOrchestrator;
    private long _skippedNoConsumers;
    private long _skippedChannel;
    private long _skippedOutputOverride;
    private long _forwardedToOrchestrator;
    private long _lastBufferLength;

    public AnalysisCaptureLink()
    {
        int count = (int)AnalysisSignalId.Count;
        _producerBufferA = new int[count];
        _producerBufferB = new int[count];
        _activeProducers = _producerBufferA;
        ClearProducers(_producerBufferA);
        ClearProducers(_producerBufferB);
    }

    public AnalysisOrchestrator? Orchestrator
    {
        get => _orchestrator;
        set => _orchestrator = value;
    }

    public AnalysisSignalBus? SignalBus => Volatile.Read(ref _signalBus);

    public int[] SignalProducers => Volatile.Read(ref _activeProducers);

    public long WriteSampleTime => Interlocked.Read(ref _writeSampleTime);

    public long DebugCaptureCallCount => Interlocked.Read(ref _captureCallCount);
    public long DebugSkippedNoOrchestrator => Interlocked.Read(ref _skippedNoOrchestrator);
    public long DebugSkippedNoConsumers => Interlocked.Read(ref _skippedNoConsumers);
    public long DebugSkippedChannel => Interlocked.Read(ref _skippedChannel);
    public long DebugSkippedOutputOverride => Interlocked.Read(ref _skippedOutputOverride);
    public long DebugForwardedCount => Interlocked.Read(ref _forwardedToOrchestrator);
    public long DebugLastBufferLength => Interlocked.Read(ref _lastBufferLength);
    public long LastCaptureSampleClock => Volatile.Read(ref _lastCaptureSampleClock);
    public AnalysisCaptureSource LastCaptureSource => (AnalysisCaptureSource)Volatile.Read(ref _lastCaptureSource);

    public void Capture(ReadOnlySpan<float> buffer, long sampleClock, long sampleTime, int channelId,
        AnalysisSignalBus? bus, ReadOnlySpan<int> producers, AnalysisCaptureSource source)
    {
        Interlocked.Increment(ref _captureCallCount);
        Interlocked.Exchange(ref _lastBufferLength, buffer.Length);

        if (source == AnalysisCaptureSource.Output)
        {
            long lastClock = Volatile.Read(ref _lastCaptureSampleClock);
            int lastSource = Volatile.Read(ref _lastCaptureSource);
            if (lastClock == sampleClock && lastSource == (int)AnalysisCaptureSource.Plugin)
            {
                Interlocked.Increment(ref _skippedOutputOverride);
                return;
            }
        }

        var orchestrator = _orchestrator;
        if (orchestrator is null)
        {
            Interlocked.Increment(ref _skippedNoOrchestrator);
            return;
        }

        if (!orchestrator.HasActiveConsumers)
        {
            Interlocked.Increment(ref _skippedNoConsumers);
            return;
        }

        if (channelId != 0)
        {
            Interlocked.Increment(ref _skippedChannel);
            return;
        }

        Volatile.Write(ref _lastCaptureSampleClock, sampleClock);
        Volatile.Write(ref _lastCaptureSource, (int)source);

        if (bus is not null && producers.Length == _producerBufferA.Length)
        {
            var target = ReferenceEquals(_activeProducers, _producerBufferA) ? _producerBufferB : _producerBufferA;
            producers.CopyTo(target);
            Volatile.Write(ref _activeProducers, target);
            Volatile.Write(ref _signalBus, bus);
        }
        else
        {
            var target = ReferenceEquals(_activeProducers, _producerBufferA) ? _producerBufferB : _producerBufferA;
            ClearProducers(target);
            Volatile.Write(ref _activeProducers, target);
            Volatile.Write(ref _signalBus, null);
        }
        Volatile.Write(ref _writeSampleTime, sampleTime + buffer.Length);

        Interlocked.Increment(ref _forwardedToOrchestrator);
        orchestrator.EnqueueAudio(buffer, channelId);
    }

    public void Reset()
    {
        ClearProducers(_producerBufferA);
        ClearProducers(_producerBufferB);
        Volatile.Write(ref _activeProducers, _producerBufferA);
        Volatile.Write(ref _signalBus, null);
        Volatile.Write(ref _writeSampleTime, 0);
        Volatile.Write(ref _lastCaptureSampleClock, long.MinValue);
        Volatile.Write(ref _lastCaptureSource, 0);
    }

    private static void ClearProducers(int[] buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = -1;
        }
    }
}
