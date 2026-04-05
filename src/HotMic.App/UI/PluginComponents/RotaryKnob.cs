using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Options for customizing knob rendering style.
/// </summary>
internal record struct KnobStyle
{
    /// <summary>Standard knob with shadow, inner circle, and labels.</summary>
    public static KnobStyle Standard => new() { ShowShadow = true, ShowInnerCircle = true, ShowLabels = true };

    /// <summary>Compact knob without shadow, inner circle, or labels.</summary>
    public static KnobStyle Compact => new() { ShowShadow = false, ShowInnerCircle = false, ShowLabels = false };

    /// <summary>Bipolar knob that draws arc from center (for +/- values like gain).</summary>
    public static KnobStyle Bipolar => new() { ShowShadow = true, ShowInnerCircle = true, ShowLabels = true, IsBipolar = true };

    /// <summary>Whether to render drop shadow.</summary>
    public bool ShowShadow { get; init; }

    /// <summary>Whether to render inner gradient circle.</summary>
    public bool ShowInnerCircle { get; init; }

    /// <summary>Whether to render label above and value/unit below.</summary>
    public bool ShowLabels { get; init; }

    /// <summary>Whether arc draws from center (0.5) rather than start.</summary>
    public bool IsBipolar { get; init; }

    /// <summary>Custom arc color override (null uses theme default).</summary>
    public SKColor? ArcColor { get; init; }

    /// <summary>Custom track stroke width (0 uses default based on radius).</summary>
    public float TrackWidth { get; init; }

    /// <summary>Custom arc stroke width (0 uses default based on radius).</summary>
    public float ArcWidth { get; init; }

    /// <summary>Custom pointer stroke width (0 uses default based on radius).</summary>
    public float PointerWidth { get; init; }
}
