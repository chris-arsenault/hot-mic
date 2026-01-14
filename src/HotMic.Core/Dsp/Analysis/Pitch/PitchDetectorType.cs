namespace HotMic.Core.Dsp.Analysis.Pitch;

/// <summary>
/// Available pitch detection algorithms for vocal analysis.
/// </summary>
public enum PitchDetectorType
{
    Yin = 0,
    Autocorrelation = 1,
    Cepstral = 2,
    Pyin = 3,
    Swipe = 4
}
