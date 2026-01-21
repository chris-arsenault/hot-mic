namespace HotMic.Core.Dsp.Spectrogram;

/// <summary>
/// Normalization modes for spectrogram magnitude scaling.
/// </summary>
public enum SpectrogramNormalizationMode
{
    None = 0,
    Peak = 1,
    Rms = 2,
    AWeighted = 3
}
