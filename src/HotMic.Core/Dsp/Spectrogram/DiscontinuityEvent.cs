namespace HotMic.Core.Dsp.Spectrogram;

/// <summary>
/// Types of discontinuities that can occur in the spectrogram analysis.
/// These indicate where visual artifacts or changes in analysis method may appear.
/// </summary>
[Flags]
public enum DiscontinuityType
{
    /// <summary>No discontinuity.</summary>
    None = 0,

    /// <summary>Transform type changed (FFT ↔ ZoomFFT ↔ CQT).</summary>
    TransformChange = 1,

    /// <summary>Resolution changed (FFT size, CQT bins per octave).</summary>
    ResolutionChange = 2,

    /// <summary>Frequency range changed (min/max Hz).</summary>
    FrequencyRangeChange = 4,

    /// <summary>Window function changed.</summary>
    WindowChange = 8,

    /// <summary>Filter settings changed (HPF, pre-emphasis).</summary>
    FilterChange = 16,

    /// <summary>Audio buffer dropout detected.</summary>
    BufferDrop = 32,

    /// <summary>Time window changed (frame capacity).</summary>
    TimeWindowChange = 64,

    /// <summary>Overlap changed.</summary>
    OverlapChange = 128
}

/// <summary>
/// Records a discontinuity event at a specific frame in the spectrogram.
/// Used to display markers indicating where analysis parameters changed.
/// </summary>
/// <param name="FrameId">The frame ID where the discontinuity occurred.</param>
/// <param name="Type">The type(s) of discontinuity that occurred.</param>
/// <param name="Description">Human-readable description of what changed.</param>
public readonly record struct DiscontinuityEvent(
    long FrameId,
    DiscontinuityType Type,
    string Description)
{
    /// <summary>
    /// Gets a short label for the discontinuity type, suitable for display.
    /// </summary>
    public string ShortLabel => Type switch
    {
        DiscontinuityType.TransformChange => "Transform",
        DiscontinuityType.ResolutionChange => "Resolution",
        DiscontinuityType.FrequencyRangeChange => "Freq Range",
        DiscontinuityType.WindowChange => "Window",
        DiscontinuityType.FilterChange => "Filter",
        DiscontinuityType.BufferDrop => "Dropout",
        DiscontinuityType.TimeWindowChange => "Time",
        DiscontinuityType.OverlapChange => "Overlap",
        _ when Type.HasFlag(DiscontinuityType.TransformChange) => "Transform",
        _ when Type.HasFlag(DiscontinuityType.ResolutionChange) => "Resolution",
        _ => "Change"
    };
}
