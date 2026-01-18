using HotMic.Core.Dsp.Spectrogram;

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
    /// Copy formant data.
    /// </summary>
    bool TryGetFormantRange(
        long sinceFrameId,
        float[] frequencies,
        float[] bandwidths,
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
    /// Copy speech metrics data.
    /// </summary>
    bool TryGetSpeechMetrics(
        long sinceFrameId,
        float[] syllableRate,
        float[] articulationRate,
        float[] pauseRatio,
        float[] monotoneScore,
        float[] clarityScore,
        float[] intelligibility,
        byte[] speakingState,
        byte[] syllableMarkers,
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
