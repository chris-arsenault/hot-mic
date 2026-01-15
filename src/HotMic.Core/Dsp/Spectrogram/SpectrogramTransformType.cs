namespace HotMic.Core.Dsp.Spectrogram;

/// <summary>
/// Transform algorithm for spectrogram computation.
/// </summary>
public enum SpectrogramTransformType
{
    /// <summary>
    /// Standard FFT with perceptual bin mapping.
    /// Computes all bins 0-Nyquist, maps to display range.
    /// </summary>
    Fft = 0,

    /// <summary>
    /// Zoom FFT focused on visible frequency range.
    /// Uses frequency-shift and decimation for higher resolution
    /// within the configured min/max frequency band.
    /// </summary>
    ZoomFft = 1,

    /// <summary>
    /// Constant-Q Transform with logarithmic frequency spacing.
    /// Provides constant relative frequency resolution (bins per octave).
    /// Better time resolution at high frequencies, better frequency
    /// resolution at low frequencies.
    /// </summary>
    Cqt = 2
}
