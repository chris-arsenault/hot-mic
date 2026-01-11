using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Shared styling for plugin UI components.
/// </summary>
public sealed class PluginComponentTheme
{
    public static PluginComponentTheme Default { get; } = new();

    // Panel backgrounds
    public SKColor PanelBackground { get; } = new(0x18, 0x18, 0x1C);
    public SKColor PanelBackgroundLight { get; } = new(0x22, 0x22, 0x28);
    public SKColor PanelBorder { get; } = new(0x3A, 0x3A, 0x44);

    // Waveform display
    public SKColor WaveformBackground { get; } = new(0x0C, 0x0C, 0x10);
    public SKColor WaveformLine { get; } = new(0x00, 0xD4, 0xAA);
    public SKColor WaveformFill { get; } = new(0x00, 0xD4, 0xAA, 0x40);
    public SKColor WaveformGateOpen { get; } = new(0x00, 0xD4, 0xAA, 0x30);
    public SKColor WaveformGateClosed { get; } = new(0x80, 0x80, 0x90, 0x20);
    public SKColor ThresholdLine { get; } = new(0xFF, 0x6B, 0x00);
    public SKColor ThresholdLineGlow { get; } = new(0xFF, 0x6B, 0x00, 0x40);

    // Knobs
    public SKColor KnobBackground { get; } = new(0x28, 0x28, 0x30);
    public SKColor KnobTrack { get; } = new(0x1A, 0x1A, 0x1E);
    public SKColor KnobArc { get; } = new(0xFF, 0x6B, 0x00);
    public SKColor KnobPointer { get; } = new(0xF0, 0xF0, 0xF2);
    public SKColor KnobHighlight { get; } = new(0x40, 0x40, 0x48);

    // Gate indicator
    public SKColor GateOpen { get; } = new(0x00, 0xD4, 0xAA);
    public SKColor GateOpenGlow { get; } = new(0x00, 0xD4, 0xAA, 0x60);
    public SKColor GateClosed { get; } = new(0x40, 0x40, 0x48);
    public SKColor GateClosedDim { get; } = new(0x28, 0x28, 0x30);

    // Envelope curve
    public SKColor EnvelopeLine { get; } = new(0xFF, 0x6B, 0x00);
    public SKColor EnvelopeFill { get; } = new(0xFF, 0x6B, 0x00, 0x30);
    public SKColor EnvelopeGrid { get; } = new(0x2A, 0x2A, 0x32);

    // Text
    public SKColor TextPrimary { get; } = new(0xF0, 0xF0, 0xF2);
    public SKColor TextSecondary { get; } = new(0x8A, 0x8A, 0x96);
    public SKColor TextMuted { get; } = new(0x5A, 0x5A, 0x66);

    // Labels
    public SKColor LabelBackground { get; } = new(0x22, 0x22, 0x28);
    public SKColor LabelBorder { get; } = new(0x3A, 0x3A, 0x44);
}
