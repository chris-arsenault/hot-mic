using HotMic.Core.Dsp.Spectrogram;
using HotMic.Core.Plugins;

namespace HotMic.Core.Analysis;

/// <summary>
/// Thread-safe storage for all analysis results. Single source of truth.
/// UI thread reads via version-checked bulk copies.
/// </summary>
public interface IAnalysisResultStore
{
    /// <summary>Current configuration snapshot.</summary>
    AnalysisConfiguration Config { get; }

    /// <summary>Sample rate of the audio being analyzed.</summary>
    int SampleRate { get; }

    /// <summary>Most recent frame ID that has been written.</summary>
    long LatestFrameId { get; }

    /// <summary>Number of frames available in the buffer.</summary>
    int AvailableFrames { get; }

    /// <summary>Maximum number of frames the buffer can hold.</summary>
    int FrameCapacity { get; }

    /// <summary>Number of display bins per frame.</summary>
    int DisplayBins { get; }

    /// <summary>Number of analysis bins per frame (may differ from display bins).</summary>
    int AnalysisBins { get; }

    /// <summary>Bin resolution in Hz (for linear transforms).</summary>
    float BinResolutionHz { get; }

    /// <summary>Current transform type.</summary>
    SpectrogramTransformType TransformType { get; }

    /// <summary>Data version for torn-read detection. Odd = write in progress.</summary>
    int DataVersion { get; }

    /// <summary>
    /// Copy display-resolution spectrogram magnitudes.
    /// </summary>
    bool TryGetSpectrogramRange(
        long sinceFrameId,
        float[] magnitudes,
        out long latestFrameId,
        out int availableFrames,
        out bool fullCopy);

    /// <summary>
    /// Copy analysis-resolution linear magnitudes.
    /// </summary>
    bool TryGetLinearMagnitudes(
        long sinceFrameId,
        float[] magnitudes,
        out int analysisBins,
        out float binResolutionHz,
        out SpectrogramTransformType transformType,
        out long latestFrameId,
        out int availableFrames,
        out bool fullCopy);

    /// <summary>
    /// Copy pitch track data.
    /// </summary>
    bool TryGetPitchRange(
        long sinceFrameId,
        float[] pitches,
        float[] confidences,
        byte[] voicing,
        out long latestFrameId,
        out int availableFrames,
        out bool fullCopy);

    /// <summary>
    /// Copy harmonic data.
    /// </summary>
    bool TryGetHarmonicRange(
        long sinceFrameId,
        float[] frequencies,
        float[] magnitudes,
        out long latestFrameId,
        out int availableFrames,
        out bool fullCopy);

    /// <summary>
    /// Copy waveform envelope data.
    /// </summary>
    bool TryGetWaveformRange(
        long sinceFrameId,
        float[] min,
        float[] max,
        out long latestFrameId,
        out int availableFrames,
        out bool fullCopy);

    /// <summary>
    /// Copy spectral feature data.
    /// </summary>
    bool TryGetSpectralFeatures(
        long sinceFrameId,
        float[] centroid,
        float[] slope,
        float[] flux,
        float[] hnr,
        float[] cpp,
        out long latestFrameId,
        out int availableFrames,
        out bool fullCopy);

    /// <summary>
    /// Copy a single analysis signal track.
    /// </summary>
    bool TryGetAnalysisSignalRange(
        AnalysisSignalId signal,
        long sinceFrameId,
        float[] values,
        out long latestFrameId,
        out int availableFrames,
        out bool fullCopy);

    /// <summary>
    /// Copy speech metrics data.
    /// </summary>
    bool TryGetSpeechMetrics(
        long sinceFrameId,
        float[] syllableRate,
        float[] articulationRate,
        float[] wordsPerMinute,
        float[] articulationWpm,
        float[] pauseRatio,
        float[] meanPauseDurationMs,
        float[] pausesPerMinute,
        float[] filledPauseRatio,
        float[] pauseMicroCount,
        float[] pauseShortCount,
        float[] pauseMediumCount,
        float[] pauseLongCount,
        float[] monotoneScore,
        float[] clarityScore,
        float[] intelligibility,
        float[] bandLowRatio,
        float[] bandMidRatio,
        float[] bandPresenceRatio,
        float[] bandHighRatio,
        float[] clarityRatio,
        byte[] speakingState,
        byte[] syllableMarkers,
        byte[] emphasisMarkers,
        out long latestFrameId,
        out int availableFrames,
        out bool fullCopy);

    /// <summary>
    /// Get the analysis descriptor for frequency mapping.
    /// </summary>
    SpectrogramAnalysisDescriptor? GetAnalysisDescriptor();

    /// <summary>
    /// Gets discontinuity events that occurred at or after the specified frame ID.
    /// Used by the renderer to display markers indicating parameter changes.
    /// </summary>
    IReadOnlyList<DiscontinuityEvent> GetDiscontinuities(long oldestFrameId);
}
