using SkiaSharp;

namespace HotMic.App.UI;

internal sealed class MainHitTargetRegistry
{
    public Dictionary<MainButton, SKRect> TopButtons { get; } = new();
    public List<KnobRect> Knobs { get; } = new();
    public List<ContainerSlotRect> ContainerSlots { get; } = new();
    public List<ToggleRect> Toggles { get; } = new();
    public List<PluginAreaRect> PluginAreas { get; } = new();
    public Dictionary<int, SKRect> ChannelNames { get; } = new();
    public List<ChannelDeleteRect> ChannelDeletes { get; } = new();

    public SKRect TitleBarRect { get; set; } = SKRect.Empty;
    public SKRect MeterScaleToggleRect { get; set; } = SKRect.Empty;
    public SKRect QualityToggleRect { get; set; } = SKRect.Empty;
    public SKRect StatsAreaRect { get; set; } = SKRect.Empty;
    public SKRect ReinitializeAudioRect { get; set; } = SKRect.Empty;
    public SKRect DebugOverlayCopyRect { get; set; } = SKRect.Empty;
    public SKRect PresetDropdownRect { get; set; } = SKRect.Empty;
    public SKRect MasterMeterRect { get; set; } = SKRect.Empty;
    public SKRect VisualizerButtonRect { get; set; } = SKRect.Empty;
    public SKRect AddChannelRect { get; set; } = SKRect.Empty;

    public void Clear()
    {
        TopButtons.Clear();
        Knobs.Clear();
        ContainerSlots.Clear();
        Toggles.Clear();
        PluginAreas.Clear();
        ChannelNames.Clear();
        ChannelDeletes.Clear();
        TitleBarRect = SKRect.Empty;
        MeterScaleToggleRect = SKRect.Empty;
        QualityToggleRect = SKRect.Empty;
        StatsAreaRect = SKRect.Empty;
        ReinitializeAudioRect = SKRect.Empty;
        DebugOverlayCopyRect = SKRect.Empty;
        PresetDropdownRect = SKRect.Empty;
        MasterMeterRect = SKRect.Empty;
        VisualizerButtonRect = SKRect.Empty;
        AddChannelRect = SKRect.Empty;
    }
}

internal sealed record KnobRect(int ChannelIndex, KnobType KnobType, SKRect Rect);
internal sealed record ContainerSlotRect(int ChannelIndex, int ContainerId, int SlotIndex, SKRect Rect, SKRect BypassRect, SKRect RemoveRect);
internal sealed record PluginAreaRect(int ChannelIndex, SKRect Rect);
internal sealed record ToggleRect(int ChannelIndex, ToggleType ToggleType, SKRect Rect);
internal sealed record ChannelDeleteRect(int ChannelIndex, SKRect Rect, bool IsEnabled);
