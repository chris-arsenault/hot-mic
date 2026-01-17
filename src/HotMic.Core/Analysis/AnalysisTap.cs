namespace HotMic.Core.Analysis;

/// <summary>
/// Fixed interception point for capturing post-chain audio for analysis.
/// Not a plugin - inserted directly into AudioEngine after plugin chain processing.
/// Zero overhead when no visualizers are active.
/// </summary>
public sealed class AnalysisTap
{
    private AnalysisOrchestrator? _orchestrator;

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
        var orchestrator = _orchestrator;
        if (orchestrator is null || !orchestrator.HasActiveConsumers)
            return;

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
