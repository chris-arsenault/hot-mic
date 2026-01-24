using System;
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
    private AnalysisSignalBus? _signalBusPlugin;
    private readonly int[] _producerBufferAPlugin;
    private readonly int[] _producerBufferBPlugin;
    private int[] _activeProducersPlugin;
    private long _writeSampleTimePlugin;
    private long _writeSampleTimeOutput;
    private long _lastCaptureSampleClockPlugin = long.MinValue;
    private long _lastCaptureSampleClockOutput = long.MinValue;
    private int _lastCaptureSource;

    // Debug counters
    private long _captureCallCount;
    private long _captureCallCountPlugin;
    private long _captureCallCountOutput;
    private long _skippedNoOrchestrator;
    private long _skippedNoConsumers;
    private long _skippedChannel;
    private long _skippedOutputOverride;
    private long _forwardedToOrchestrator;
    private long _forwardedToOrchestratorPlugin;
    private long _forwardedToOrchestratorOutput;
    private long _lastBufferLength;

    public AnalysisCaptureLink()
    {
        int count = (int)AnalysisSignalId.Count;
        _producerBufferAPlugin = new int[count];
        _producerBufferBPlugin = new int[count];
        _activeProducersPlugin = _producerBufferAPlugin;
        ClearProducers(_producerBufferAPlugin);
        ClearProducers(_producerBufferBPlugin);
    }

    public AnalysisOrchestrator? Orchestrator
    {
        get => _orchestrator;
        set => _orchestrator = value;
    }

    public AnalysisSignalBus? SignalBus => Volatile.Read(ref _signalBusPlugin);

    public int[] SignalProducers => Volatile.Read(ref _activeProducersPlugin);

    public long WriteSampleTime => Interlocked.Read(ref _writeSampleTimePlugin);

    public AnalysisSignalBus? GetSignalBus(AnalysisCaptureSource source)
        => source == AnalysisCaptureSource.Plugin ? Volatile.Read(ref _signalBusPlugin) : null;

    public int[] GetSignalProducers(AnalysisCaptureSource source)
        => source == AnalysisCaptureSource.Plugin ? Volatile.Read(ref _activeProducersPlugin) : Array.Empty<int>();

    public long GetWriteSampleTime(AnalysisCaptureSource source)
        => source == AnalysisCaptureSource.Plugin
            ? Interlocked.Read(ref _writeSampleTimePlugin)
            : Interlocked.Read(ref _writeSampleTimeOutput);

    public long DebugCaptureCallCount => Interlocked.Read(ref _captureCallCount);
    public long DebugCaptureCallCountPlugin => Interlocked.Read(ref _captureCallCountPlugin);
    public long DebugCaptureCallCountOutput => Interlocked.Read(ref _captureCallCountOutput);
    public long DebugSkippedNoOrchestrator => Interlocked.Read(ref _skippedNoOrchestrator);
    public long DebugSkippedNoConsumers => Interlocked.Read(ref _skippedNoConsumers);
    public long DebugSkippedChannel => Interlocked.Read(ref _skippedChannel);
    public long DebugSkippedOutputOverride => Interlocked.Read(ref _skippedOutputOverride);
    public long DebugForwardedCount => Interlocked.Read(ref _forwardedToOrchestrator);
    public long DebugForwardedCountPlugin => Interlocked.Read(ref _forwardedToOrchestratorPlugin);
    public long DebugForwardedCountOutput => Interlocked.Read(ref _forwardedToOrchestratorOutput);
    public long DebugLastBufferLength => Interlocked.Read(ref _lastBufferLength);
    public long LastCaptureSampleClock => Volatile.Read(ref _lastCaptureSampleClockPlugin);
    public AnalysisCaptureSource LastCaptureSource => (AnalysisCaptureSource)Volatile.Read(ref _lastCaptureSource);
    public long GetLastCaptureSampleClock(AnalysisCaptureSource source)
        => source == AnalysisCaptureSource.Plugin
            ? Volatile.Read(ref _lastCaptureSampleClockPlugin)
            : Volatile.Read(ref _lastCaptureSampleClockOutput);

    public bool HasCaptureData(AnalysisCaptureSource source)
        => GetLastCaptureSampleClock(source) != long.MinValue;

    public void Capture(ReadOnlySpan<float> buffer, long sampleClock, long sampleTime, int channelId,
        AnalysisSignalBus? bus, ReadOnlySpan<int> producers, AnalysisCaptureSource source)
    {
        Interlocked.Increment(ref _captureCallCount);
        if (source == AnalysisCaptureSource.Plugin)
        {
            Interlocked.Increment(ref _captureCallCountPlugin);
        }
        else
        {
            Interlocked.Increment(ref _captureCallCountOutput);
        }
        Interlocked.Exchange(ref _lastBufferLength, buffer.Length);

        Volatile.Write(ref _lastCaptureSource, (int)source);

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

        if (source == AnalysisCaptureSource.Plugin)
        {
            Volatile.Write(ref _lastCaptureSampleClockPlugin, sampleClock);
            if (bus is not null && producers.Length == _producerBufferAPlugin.Length)
            {
                var target = ReferenceEquals(_activeProducersPlugin, _producerBufferAPlugin) ? _producerBufferBPlugin : _producerBufferAPlugin;
                producers.CopyTo(target);
                Volatile.Write(ref _activeProducersPlugin, target);
                Volatile.Write(ref _signalBusPlugin, bus);
            }
            else
            {
                var target = ReferenceEquals(_activeProducersPlugin, _producerBufferAPlugin) ? _producerBufferBPlugin : _producerBufferAPlugin;
                ClearProducers(target);
                Volatile.Write(ref _activeProducersPlugin, target);
                Volatile.Write(ref _signalBusPlugin, null);
            }
            Volatile.Write(ref _writeSampleTimePlugin, sampleTime + buffer.Length);
        }
        else
        {
            Volatile.Write(ref _lastCaptureSampleClockOutput, sampleClock);
            Volatile.Write(ref _writeSampleTimeOutput, sampleTime + buffer.Length);
        }

        Interlocked.Increment(ref _forwardedToOrchestrator);
        if (source == AnalysisCaptureSource.Plugin)
        {
            Interlocked.Increment(ref _forwardedToOrchestratorPlugin);
        }
        else
        {
            Interlocked.Increment(ref _forwardedToOrchestratorOutput);
        }
        orchestrator.EnqueueAudio(buffer, channelId, source);
    }

    public void Reset()
    {
        ClearProducers(_producerBufferAPlugin);
        ClearProducers(_producerBufferBPlugin);
        Volatile.Write(ref _activeProducersPlugin, _producerBufferAPlugin);
        Volatile.Write(ref _signalBusPlugin, null);
        Volatile.Write(ref _writeSampleTimePlugin, 0);
        Volatile.Write(ref _writeSampleTimeOutput, 0);
        Volatile.Write(ref _lastCaptureSampleClockPlugin, long.MinValue);
        Volatile.Write(ref _lastCaptureSampleClockOutput, long.MinValue);
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
