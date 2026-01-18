using SkiaSharp;

namespace HotMic.App.UI;

internal sealed class MainPaintCache : IDisposable
{
    private bool _disposed;
    public MainPaintCache()
    {
        Theme = HotMicTheme.Default;
        BackgroundPaint = MainRenderPrimitives.CreateFillPaint(Theme.BackgroundPrimary);
        TitleBarPaint = MainRenderPrimitives.CreateFillPaint(Theme.BackgroundSecondary);
        HotbarPaint = MainRenderPrimitives.CreateFillPaint(new SKColor(0x16, 0x16, 0x1A));
        BorderPaint = MainRenderPrimitives.CreateStrokePaint(Theme.Border, 1f);
        SectionPaint = MainRenderPrimitives.CreateFillPaint(Theme.BackgroundTertiary);
        PluginSlotEmptyPaint = MainRenderPrimitives.CreateFillPaint(Theme.PluginSlotEmpty);
        PluginSlotFilledPaint = MainRenderPrimitives.CreateFillPaint(Theme.PluginSlotFilled);
        PluginSlotBypassedPaint = MainRenderPrimitives.CreateFillPaint(Theme.PluginSlotBypassed);
        AccentPaint = MainRenderPrimitives.CreateFillPaint(Theme.Accent);
        TextPaint = MainRenderPrimitives.CreateTextPaint(Theme.TextPrimary, 11f);
        TextSecondaryPaint = MainRenderPrimitives.CreateTextPaint(Theme.TextSecondary, 10f);
        TextMutedPaint = MainRenderPrimitives.CreateTextPaint(Theme.TextMuted, 9f);
        TitlePaint = MainRenderPrimitives.CreateTextPaint(Theme.TextPrimary, 13f, SKFontStyle.Bold);
        SmallTextPaint = MainRenderPrimitives.CreateTextPaint(Theme.TextSecondary, 8f);
        TinyTextPaint = MainRenderPrimitives.CreateTextPaint(Theme.TextMuted, 7f);
        MeterBackgroundPaint = MainRenderPrimitives.CreateFillPaint(Theme.MeterBackground);
        MeterSegmentOffPaint = MainRenderPrimitives.CreateFillPaint(Theme.MeterSegmentOff);
        IconPaint = MainRenderPrimitives.CreateStrokePaint(Theme.TextSecondary, 1.5f);
        MutePaint = MainRenderPrimitives.CreateFillPaint(Theme.Mute);
        SoloPaint = MainRenderPrimitives.CreateFillPaint(Theme.Solo);
        ButtonPaint = MainRenderPrimitives.CreateFillPaint(Theme.Surface);
        BridgePaint = MainRenderPrimitives.CreateStrokePaint(Theme.Accent.WithAlpha(180), 2f);
    }

    public HotMicTheme Theme { get; }
    public SKPaint BackgroundPaint { get; }
    public SKPaint TitleBarPaint { get; }
    public SKPaint HotbarPaint { get; }
    public SKPaint BorderPaint { get; }
    public SKPaint SectionPaint { get; }
    public SKPaint PluginSlotEmptyPaint { get; }
    public SKPaint PluginSlotFilledPaint { get; }
    public SKPaint PluginSlotBypassedPaint { get; }
    public SKPaint AccentPaint { get; }
    public SkiaTextPaint TextPaint { get; }
    public SkiaTextPaint TextSecondaryPaint { get; }
    public SkiaTextPaint TextMutedPaint { get; }
    public SkiaTextPaint TitlePaint { get; }
    public SkiaTextPaint SmallTextPaint { get; }
    public SkiaTextPaint TinyTextPaint { get; }
    public SKPaint MeterBackgroundPaint { get; }
    public SKPaint MeterSegmentOffPaint { get; }
    public SKPaint IconPaint { get; }
    public SKPaint MutePaint { get; }
    public SKPaint SoloPaint { get; }
    public SKPaint ButtonPaint { get; }
    public SKPaint BridgePaint { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        BackgroundPaint.Dispose();
        TitleBarPaint.Dispose();
        HotbarPaint.Dispose();
        BorderPaint.Dispose();
        SectionPaint.Dispose();
        PluginSlotEmptyPaint.Dispose();
        PluginSlotFilledPaint.Dispose();
        PluginSlotBypassedPaint.Dispose();
        AccentPaint.Dispose();
        TextPaint.Dispose();
        TextSecondaryPaint.Dispose();
        TextMutedPaint.Dispose();
        TitlePaint.Dispose();
        SmallTextPaint.Dispose();
        TinyTextPaint.Dispose();
        MeterBackgroundPaint.Dispose();
        MeterSegmentOffPaint.Dispose();
        IconPaint.Dispose();
        MutePaint.Dispose();
        SoloPaint.Dispose();
        ButtonPaint.Dispose();
        BridgePaint.Dispose();
    }
}
