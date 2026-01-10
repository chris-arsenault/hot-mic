using SkiaSharp;

namespace HotMic.App.UI;

public sealed class HotMicTheme
{
    public static HotMicTheme Default { get; } = new();

    public SKColor BackgroundPrimary { get; } = new(0x1A, 0x1A, 0x1A);
    public SKColor BackgroundSecondary { get; } = new(0x24, 0x24, 0x24);
    public SKColor BackgroundTertiary { get; } = new(0x2D, 0x2D, 0x2D);
    public SKColor Surface { get; } = new(0x33, 0x33, 0x33);
    public SKColor Border { get; } = new(0x44, 0x44, 0x44);
    public SKColor TextPrimary { get; } = new(0xFF, 0xFF, 0xFF);
    public SKColor TextSecondary { get; } = new(0x88, 0x88, 0x88);
    public SKColor Accent { get; } = new(0xFF, 0x6B, 0x00);
    public SKColor MeterGreen { get; } = new(0x00, 0xFF, 0x00);
    public SKColor MeterYellow { get; } = new(0xFF, 0xFF, 0x00);
    public SKColor MeterRed { get; } = new(0xFF, 0x00, 0x00);
}
