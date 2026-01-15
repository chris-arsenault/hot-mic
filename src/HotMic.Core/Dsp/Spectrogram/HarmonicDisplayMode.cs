namespace HotMic.Core.Dsp.Spectrogram;

/// <summary>
/// Controls how harmonics are displayed on the spectrogram overlay.
/// </summary>
public enum HarmonicDisplayMode
{
    /// <summary>
    /// Only show harmonics with magnitude above the detection threshold (relative to fundamental).
    /// </summary>
    Detected,

    /// <summary>
    /// Show all theoretical harmonic positions at integer multiples of the fundamental.
    /// </summary>
    Theoretical,

    /// <summary>
    /// Show both: detected harmonics as solid markers, theoretical positions as faint guides.
    /// </summary>
    Both
}
