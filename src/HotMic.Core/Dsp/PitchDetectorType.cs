namespace HotMic.Core.Dsp;

/// <summary>
/// Available pitch detection algorithms for vocal analysis.
/// </summary>
public enum PitchDetectorType
{
    Yin,
    Autocorrelation,
    Cepstral
}
