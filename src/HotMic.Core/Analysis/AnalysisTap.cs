using System.Threading;

namespace HotMic.Core.Analysis;

/// <summary>
/// Fixed interception point for capturing post-chain audio for analysis.
/// Not a plugin - inserted directly into AudioEngine after plugin chain processing.
/// Zero overhead when no visualizers are active.
/// </summary>
public sealed class AnalysisTap
{
    private AnalysisOrchestrator? _orchestrator;

    // Debug counters
    private long _captureCallCount;
    private long _skippedNoOrchestrator;
    private long _skippedNoConsumers;
    private long _forwardedToOrchestrator;
    private long _lastBufferLength;

    public long DebugCaptureCallCount => Interlocked.Read(ref _captureCallCount);
    public long DebugSkippedNoOrchestrator => Interlocked.Read(ref _skippedNoOrchestrator);
    public long DebugSkippedNoConsumers => Interlocked.Read(ref _skippedNoConsumers);
    public long DebugForwardedCount => Interlocked.Read(ref _forwardedToOrchestrator);
    public long DebugLastBufferLength => Interlocked.Read(ref _lastBufferLength);

    /// <summary>
    /// Gets or sets the orchestrator that receives captured audio.
    /// </summary>
    public AnalysisOrchestrator? Orchestrator
    {
        get => _orchestrator;
        set => _orchestrator = value;
    }

    /// <summary>
    /// Capture audio samples for analysis. Called from audio thread.
    /// Zero overhead when orchestrator is null or has no consumers.
    /// </summary>
    /// <param name="buffer">Audio samples to analyze (mono, float32).</param>
    /// <param name="channelIndex">Which channel this audio is from (0 or 1).</param>
    public void Capture(ReadOnlySpan<float> buffer, int channelIndex)
    {
        Interlocked.Increment(ref _captureCallCount);
        Interlocked.Exchange(ref _lastBufferLength, buffer.Length);

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

        Interlocked.Increment(ref _forwardedToOrchestrator);
        orchestrator.EnqueueAudio(buffer, channelIndex);
    }

    /// <summary>
    /// Reset state (e.g., on device change).
    /// </summary>
    public void Reset()
    {
        _orchestrator?.Reset();
    }
}
