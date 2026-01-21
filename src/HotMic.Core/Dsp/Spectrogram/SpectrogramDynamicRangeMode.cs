namespace HotMic.Core.Dsp.Spectrogram;

/// <summary>
/// Dynamic range presets for spectrogram display scaling.
/// </summary>
public enum SpectrogramDynamicRangeMode
{
    Custom = 0,
    Full = 1,
    VoiceOptimized = 2,
    Compressed = 3,
    NoiseFloor = 4
}
