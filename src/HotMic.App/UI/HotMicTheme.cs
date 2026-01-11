using SkiaSharp;

namespace HotMic.App.UI;

public sealed class HotMicTheme
{
    public static HotMicTheme Default { get; } = new();

    // Background hierarchy (darkest to lightest)
    public SKColor BackgroundPrimary { get; } = new(0x12, 0x12, 0x14);
    public SKColor BackgroundSecondary { get; } = new(0x1A, 0x1A, 0x1E);
    public SKColor BackgroundTertiary { get; } = new(0x22, 0x22, 0x28);
    public SKColor Surface { get; } = new(0x2A, 0x2A, 0x32);
    public SKColor SurfaceHover { get; } = new(0x32, 0x32, 0x3C);

    // Borders with subtle depth
    public SKColor Border { get; } = new(0x3A, 0x3A, 0x44);
    public SKColor BorderLight { get; } = new(0x48, 0x48, 0x54);
    public SKColor BorderAccent { get; } = new(0xFF, 0x6B, 0x00, 0x60);

    // Text hierarchy
    public SKColor TextPrimary { get; } = new(0xF0, 0xF0, 0xF2);
    public SKColor TextSecondary { get; } = new(0x8A, 0x8A, 0x96);
    public SKColor TextMuted { get; } = new(0x5A, 0x5A, 0x66);

    // Primary accent (warm orange)
    public SKColor Accent { get; } = new(0xFF, 0x6B, 0x00);
    public SKColor AccentHover { get; } = new(0xFF, 0x85, 0x20);
    public SKColor AccentMuted { get; } = new(0xFF, 0x6B, 0x00, 0x80);

    // Meter gradient colors (bottom to top)
    public SKColor MeterLow { get; } = new(0x00, 0xD4, 0xAA);      // Teal
    public SKColor MeterMid { get; } = new(0x4A, 0xE0, 0x50);      // Green
    public SKColor MeterHigh { get; } = new(0xE0, 0xE0, 0x40);     // Yellow
    public SKColor MeterWarn { get; } = new(0xFF, 0xA0, 0x30);     // Orange
    public SKColor MeterClip { get; } = new(0xFF, 0x40, 0x40);     // Red

    // Legacy meter colors (for backward compat)
    public SKColor MeterGreen { get; } = new(0x00, 0xD4, 0xAA);
    public SKColor MeterYellow { get; } = new(0xE0, 0xE0, 0x40);
    public SKColor MeterRed { get; } = new(0xFF, 0x40, 0x40);

    // Meter background
    public SKColor MeterBackground { get; } = new(0x18, 0x18, 0x1C);
    public SKColor MeterSegmentOff { get; } = new(0x28, 0x28, 0x30);

    // State colors
    public SKColor Mute { get; } = new(0xFF, 0x50, 0x50);
    public SKColor Solo { get; } = new(0xFF, 0xCC, 0x00);
    public SKColor Bypass { get; } = new(0x80, 0x80, 0x90);

    // Plugin slot states
    public SKColor PluginSlotEmpty { get; } = new(0x20, 0x20, 0x26);
    public SKColor PluginSlotFilled { get; } = new(0x28, 0x28, 0x30);
    public SKColor PluginSlotActive { get; } = new(0x30, 0x30, 0x3A);
    public SKColor PluginSlotBypassed { get; } = new(0x24, 0x24, 0x2A);

    // Channel section colors
    public SKColor ChannelInput { get; } = new(0x18, 0x22, 0x28);
    public SKColor ChannelPlugins { get; } = new(0x1C, 0x1C, 0x22);
    public SKColor ChannelOutput { get; } = new(0x22, 0x1C, 0x1C);
    public SKColor MasterSection { get; } = new(0x1E, 0x1A, 0x22);
}
