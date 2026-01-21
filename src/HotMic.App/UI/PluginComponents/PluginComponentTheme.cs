using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Shared styling for plugin UI components.
/// </summary>
public sealed class PluginComponentTheme
{
    public static PluginComponentTheme Default { get; } = new();
    public static PluginComponentTheme BlueOnBlack { get; } = new()
    {
        PanelBackground = new SKColor(0x0A, 0x0D, 0x12),
        PanelBackgroundLight = new SKColor(0x11, 0x16, 0x20),
        PanelBorder = new SKColor(0x20, 0x2A, 0x3A),
        WaveformBackground = new SKColor(0x06, 0x08, 0x0C),
        WaveformLine = new SKColor(0x2B, 0xA8, 0xFF),
        WaveformFill = new SKColor(0x2B, 0xA8, 0xFF, 0x40),
        WaveformGateOpen = new SKColor(0x2B, 0xA8, 0xFF, 0x30),
        WaveformGateClosed = new SKColor(0x1A, 0x20, 0x2C, 0x20),
        ThresholdLine = new SKColor(0x4C, 0xA6, 0xFF),
        ThresholdLineGlow = new SKColor(0x4C, 0xA6, 0xFF, 0x40),
        KnobBackground = new SKColor(0x13, 0x19, 0x22),
        KnobTrack = new SKColor(0x0C, 0x10, 0x16),
        KnobArc = new SKColor(0x2E, 0x8B, 0xFF),
        KnobPointer = new SKColor(0xE6, 0xF2, 0xFF),
        KnobHighlight = new SKColor(0x28, 0x34, 0x49),
        GateOpen = new SKColor(0x4C, 0xC9, 0xFF),
        GateOpenGlow = new SKColor(0x4C, 0xC9, 0xFF, 0x60),
        GateClosed = new SKColor(0x29, 0x32, 0x45),
        GateClosedDim = new SKColor(0x1A, 0x22, 0x30),
        EnvelopeLine = new SKColor(0x2E, 0x8B, 0xFF),
        EnvelopeFill = new SKColor(0x2E, 0x8B, 0xFF, 0x30),
        EnvelopeGrid = new SKColor(0x1E, 0x27, 0x36),
        TextPrimary = new SKColor(0xE8, 0xF1, 0xFF),
        TextSecondary = new SKColor(0x8A, 0xA1, 0xC4),
        TextMuted = new SKColor(0x5B, 0x6F, 0x8E),
        LabelBackground = new SKColor(0x11, 0x16, 0x20),
        LabelBorder = new SKColor(0x20, 0x2A, 0x3A),
        MeterBackground = new SKColor(0x0B, 0x0F, 0x16),
        AccentSecondary = new SKColor(0x33, 0xB2, 0xFF)
    };

    // Panel backgrounds
    public SKColor PanelBackground { get; init; } = new(0x18, 0x18, 0x1C);
    public SKColor PanelBackgroundLight { get; init; } = new(0x22, 0x22, 0x28);
    public SKColor PanelBorder { get; init; } = new(0x3A, 0x3A, 0x44);

    // Waveform display
    public SKColor WaveformBackground { get; init; } = new(0x0C, 0x0C, 0x10);
    public SKColor WaveformLine { get; init; } = new(0x00, 0xD4, 0xAA);
    public SKColor WaveformFill { get; init; } = new(0x00, 0xD4, 0xAA, 0x40);
    public SKColor WaveformGateOpen { get; init; } = new(0x00, 0xD4, 0xAA, 0x30);
    public SKColor WaveformGateClosed { get; init; } = new(0x80, 0x80, 0x90, 0x20);
    public SKColor ThresholdLine { get; init; } = new(0xFF, 0x6B, 0x00);
    public SKColor ThresholdLineGlow { get; init; } = new(0xFF, 0x6B, 0x00, 0x40);

    // Knobs
    public SKColor KnobBackground { get; init; } = new(0x28, 0x28, 0x30);
    public SKColor KnobTrack { get; init; } = new(0x1A, 0x1A, 0x1E);
    public SKColor KnobArc { get; init; } = new(0xFF, 0x6B, 0x00);
    public SKColor KnobPointer { get; init; } = new(0xF0, 0xF0, 0xF2);
    public SKColor KnobHighlight { get; init; } = new(0x40, 0x40, 0x48);

    // Gate indicator
    public SKColor GateOpen { get; init; } = new(0x00, 0xD4, 0xAA);
    public SKColor GateOpenGlow { get; init; } = new(0x00, 0xD4, 0xAA, 0x60);
    public SKColor GateClosed { get; init; } = new(0x40, 0x40, 0x48);
    public SKColor GateClosedDim { get; init; } = new(0x28, 0x28, 0x30);

    // Envelope curve
    public SKColor EnvelopeLine { get; init; } = new(0xFF, 0x6B, 0x00);
    public SKColor EnvelopeFill { get; init; } = new(0xFF, 0x6B, 0x00, 0x30);
    public SKColor EnvelopeGrid { get; init; } = new(0x2A, 0x2A, 0x32);

    // Text
    public SKColor TextPrimary { get; init; } = new(0xF0, 0xF0, 0xF2);
    public SKColor TextSecondary { get; init; } = new(0x8A, 0x8A, 0x96);
    public SKColor TextMuted { get; init; } = new(0x5A, 0x5A, 0x66);

    // Labels
    public SKColor LabelBackground { get; init; } = new(0x22, 0x22, 0x28);
    public SKColor LabelBorder { get; init; } = new(0x3A, 0x3A, 0x44);

    // Meters
    public SKColor MeterBackground { get; init; } = new(0x12, 0x12, 0x16);
    public SKColor AccentSecondary { get; init; } = new(0x80, 0x40, 0xFF);
}
