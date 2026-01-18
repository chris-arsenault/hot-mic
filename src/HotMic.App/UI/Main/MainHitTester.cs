namespace HotMic.App.UI;

internal sealed class MainHitTester
{
    private readonly MainHitTargetRegistry _targets;
    private readonly PluginShellRenderer _pluginShellRenderer;
    private readonly RoutingSlotRenderer _routingSlotRenderer;

    public MainHitTester(MainHitTargetRegistry targets, PluginShellRenderer pluginShellRenderer, RoutingSlotRenderer routingSlotRenderer)
    {
        _targets = targets;
        _pluginShellRenderer = pluginShellRenderer;
        _routingSlotRenderer = routingSlotRenderer;
    }

    public MainButton? HitTestTopButton(float x, float y)
    {
        foreach (var (button, rect) in _targets.TopButtons)
        {
            if (rect.Contains(x, y))
            {
                return button;
            }
        }

        return null;
    }

    public KnobHit? HitTestKnob(float x, float y)
    {
        foreach (var knob in _targets.Knobs)
        {
            if (knob.Rect.Contains(x, y))
            {
                return new KnobHit(knob.ChannelIndex, knob.KnobType);
            }
        }

        return null;
    }

    public PluginKnobHit? HitTestPluginKnob(float x, float y) => _pluginShellRenderer.HitTestKnob(x, y);

    public PluginSlotHit? HitTestPluginSlot(float x, float y, out PluginSlotRegion region) =>
        _pluginShellRenderer.HitTestSlot(x, y, out region);

    public RoutingSlotHit? HitTestRoutingSlot(float x, float y, out RoutingSlotRegion region) =>
        _routingSlotRenderer.HitTestSlot(x, y, out region);

    public RoutingKnobHit? HitTestRoutingKnob(float x, float y) => _routingSlotRenderer.HitTestKnob(x, y);

    public RoutingBadgeHit? HitTestRoutingBadge(float x, float y) => _routingSlotRenderer.HitTestBadge(x, y);

    public MainContainerSlotHit? HitTestContainerSlot(float x, float y, out MainContainerSlotRegion region)
    {
        foreach (var slot in _targets.ContainerSlots)
        {
            if (!slot.Rect.Contains(x, y))
            {
                continue;
            }

            if (slot.BypassRect.Contains(x, y))
            {
                region = MainContainerSlotRegion.Bypass;
                return new MainContainerSlotHit(slot.ChannelIndex, slot.ContainerId, slot.SlotIndex);
            }

            if (slot.RemoveRect.Contains(x, y))
            {
                region = MainContainerSlotRegion.Remove;
                return new MainContainerSlotHit(slot.ChannelIndex, slot.ContainerId, slot.SlotIndex);
            }

            region = MainContainerSlotRegion.Action;
            return new MainContainerSlotHit(slot.ChannelIndex, slot.ContainerId, slot.SlotIndex);
        }

        region = MainContainerSlotRegion.None;
        return null;
    }

    public int HitTestPluginArea(float x, float y)
    {
        foreach (var area in _targets.PluginAreas)
        {
            if (area.Rect.Contains(x, y))
            {
                return area.ChannelIndex;
            }
        }

        return -1;
    }

    public ToggleHit? HitTestToggle(float x, float y)
    {
        foreach (var toggle in _targets.Toggles)
        {
            if (toggle.Rect.Contains(x, y))
            {
                return new ToggleHit(toggle.ChannelIndex, toggle.ToggleType);
            }
        }

        return null;
    }

    public int HitTestChannelDelete(float x, float y)
    {
        for (int i = 0; i < _targets.ChannelDeletes.Count; i++)
        {
            var deleteRect = _targets.ChannelDeletes[i];
            if (deleteRect.Rect.Contains(x, y) && deleteRect.IsEnabled)
            {
                return deleteRect.ChannelIndex;
            }
        }

        return -1;
    }

    public int HitTestChannelName(float x, float y)
    {
        foreach (var (channelIndex, rect) in _targets.ChannelNames)
        {
            if (rect.Contains(x, y))
            {
                return channelIndex;
            }
        }

        return -1;
    }

    public bool HitTestAddChannel(float x, float y) => _targets.AddChannelRect.Contains(x, y);

    public bool HitTestMasterMeter(float x, float y) => _targets.MasterMeterRect.Contains(x, y);

    public bool HitTestVisualizerButton(float x, float y) => _targets.VisualizerButtonRect.Contains(x, y);

    public bool HitTestMeterScaleToggle(float x, float y) => _targets.MeterScaleToggleRect.Contains(x, y);

    public bool HitTestQualityToggle(float x, float y) => _targets.QualityToggleRect.Contains(x, y);

    public bool HitTestStatsArea(float x, float y) => _targets.StatsAreaRect.Contains(x, y);

    public bool HitTestTitleBar(float x, float y) => _targets.TitleBarRect.Contains(x, y);

    public bool HitTestPresetDropdown(float x, float y) => _targets.PresetDropdownRect.Contains(x, y);

    public SkiaSharp.SKRect GetPresetDropdownRect() => _targets.PresetDropdownRect;
}
