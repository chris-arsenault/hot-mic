namespace HotMic.Core.Analysis;

/// <summary>
/// Flags indicating which analysis features are required by a visualizer.
/// The orchestrator activates only providers needed by active consumers.
/// </summary>
[Flags]
public enum AnalysisCapabilities
{
    None = 0,

    /// <summary>FFT/CQT/ZoomFFT magnitude spectrogram.</summary>
    Spectrogram = 1 << 0,

    /// <summary>Fundamental frequency (F0) tracking.</summary>
    Pitch = 1 << 1,

    /// <summary>Formant frequencies (F1-F5) and bandwidths.</summary>
    Formants = 1 << 2,

    /// <summary>Harmonic peak frequencies and magnitudes.</summary>
    Harmonics = 1 << 3,

    /// <summary>Waveform min/max envelope per frame.</summary>
    Waveform = 1 << 4,

    /// <summary>Speech metrics (syllable rate, pauses, clarity).</summary>
    SpeechMetrics = 1 << 5,

    /// <summary>Voiced/unvoiced/silence state per frame.</summary>
    VoicingState = 1 << 6,

    /// <summary>Spectral features (centroid, slope, flux).</summary>
    SpectralFeatures = 1 << 7,
}
