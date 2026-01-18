namespace HotMic.Core.Plugins.BuiltIn;

/// <summary>
/// Generator types available for each slot.
/// </summary>
public enum GeneratorType
{
    Sine = 0,
    Square = 1,
    Saw = 2,
    Triangle = 3,
    WhiteNoise = 4,
    PinkNoise = 5,
    BrownNoise = 6,
    BlueNoise = 7,
    Impulse = 8,
    Chirp = 9,
    Sample = 10,
    /// <summary>
    /// Diagnostic: outputs constant 0.5 (DC). If vertical bars appear with DC,
    /// the issue is in capture/processing, not generation.
    /// </summary>
    DcTest = 11
}

/// <summary>
/// Sweep direction for oscillators.
/// </summary>
public enum SweepDirection
{
    Up = 0,
    Down = 1,
    PingPong = 2
}

/// <summary>
/// Sweep curve type.
/// </summary>
public enum SweepCurve
{
    Linear = 0,
    Logarithmic = 1
}

/// <summary>
/// Sample loop mode.
/// </summary>
public enum SampleLoopMode
{
    Loop = 0,
    OneShot = 1,
    PingPong = 2
}

/// <summary>
/// Headroom compensation mode for summing.
/// </summary>
public enum HeadroomMode
{
    None = 0,
    AutoCompensate = 1,
    Normalize = 2
}

/// <summary>
/// Output level presets.
/// </summary>
public enum OutputPreset
{
    Custom = 0,
    VocalConversation = 1, // -18 dBFS
    VocalPerformance = 2,  // -12 dBFS
    Unity = 3              // 0 dBFS
}
