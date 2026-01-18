using HotMic.App.ViewModels;
using SkiaSharp;

namespace HotMic.App.UI;

public sealed class MainRenderer
{
    private readonly MainPaintCache _paints = new();
    private readonly MainRenderPrimitives _primitives;
    private readonly MainMeterRenderer _meterRenderer;
    private readonly MainHitTargetRegistry _hitTargets = new();
    private readonly PluginShellRenderer _pluginShellRenderer = new();
    private readonly RoutingSlotRenderer _routingSlotRenderer = new();
    private readonly MainHitTester _hitTester;
    private readonly MainLayoutEngine _layoutEngine = new();
    private readonly MainTitleBarRenderer _titleBarRenderer;
    private readonly MainHotbarRenderer _hotbarRenderer;
    private readonly MainPluginChainRenderer _pluginChainRenderer;
    private readonly MainChannelStripRenderer _channelStripRenderer;
    private readonly MainMasterSectionRenderer _masterRenderer;
    private readonly MainFullViewRenderer _fullViewRenderer;
    private readonly MainMinimalViewRenderer _minimalViewRenderer;
    private readonly MainDebugOverlayRenderer _debugOverlayRenderer;
    private readonly MainPluginStripRenderer _pluginStripRenderer;
    private readonly MainDragOverlayRenderer _dragOverlayRenderer = new();

    public MainRenderer()
    {
        _primitives = new MainRenderPrimitives(_paints);
        _meterRenderer = new MainMeterRenderer(_paints);
        _hitTester = new MainHitTester(_hitTargets, _pluginShellRenderer, _routingSlotRenderer);

        _pluginChainRenderer = new MainPluginChainRenderer(_paints, _primitives, _meterRenderer, _hitTargets, _pluginShellRenderer, _routingSlotRenderer);
        _channelStripRenderer = new MainChannelStripRenderer(_paints, _primitives, _hitTargets, _pluginChainRenderer);
        _masterRenderer = new MainMasterSectionRenderer(_paints, _primitives, _meterRenderer, _hitTargets);
        _fullViewRenderer = new MainFullViewRenderer(_paints, _primitives, _hitTargets, _channelStripRenderer, _masterRenderer);
        _minimalViewRenderer = new MainMinimalViewRenderer(_paints, _meterRenderer);
        _debugOverlayRenderer = new MainDebugOverlayRenderer(_paints);
        _titleBarRenderer = new MainTitleBarRenderer(_paints, _primitives, _hitTargets);
        _hotbarRenderer = new MainHotbarRenderer(_paints, _primitives, _hitTargets);
        _pluginStripRenderer = new MainPluginStripRenderer(_paints, _meterRenderer, _pluginShellRenderer);
    }

    public void Render(SKCanvas canvas, SKSize size, MainViewModel viewModel, MainUiState uiState, float dpiScale)
    {
        ResetHitTargets();
        canvas.Clear(SKColors.Transparent);

        canvas.Save();
        canvas.Scale(dpiScale);
        size = new SKSize(size.Width / dpiScale, size.Height / dpiScale);

        var layout = _layoutEngine.Build(size, viewModel.IsMinimalView);
        _primitives.DrawBackground(canvas, size);
        _titleBarRenderer.Render(canvas, layout, viewModel);

        if (viewModel.IsMinimalView)
        {
            _minimalViewRenderer.Render(canvas, layout, viewModel);
        }
        else
        {
            _hotbarRenderer.Render(canvas, layout, viewModel);
            _fullViewRenderer.Render(canvas, layout, viewModel);

            if (viewModel.ShowDebugOverlay)
            {
                _debugOverlayRenderer.Render(canvas, layout, viewModel);
            }

            // Render drag overlay on top when dragging
            if (uiState.PluginDrag?.IsDragging == true || uiState.ContainerDrag?.IsDragging == true)
            {
                _dragOverlayRenderer.Render(canvas, uiState);
            }
        }

        canvas.Restore();
    }

    public void RenderPluginStrip(SKCanvas canvas, SKRect bounds, IReadOnlyList<PluginViewModel> slots, int channelIndex, bool voxScale)
    {
        ResetHitTargets();
        _pluginStripRenderer.Render(canvas, bounds, slots, channelIndex, voxScale);
    }

    public MainButton? HitTestTopButton(float x, float y) => _hitTester.HitTestTopButton(x, y);

    public KnobHit? HitTestKnob(float x, float y) => _hitTester.HitTestKnob(x, y);

    public PluginKnobHit? HitTestPluginKnob(float x, float y) => _hitTester.HitTestPluginKnob(x, y);

    public PluginSlotHit? HitTestPluginSlot(float x, float y, out PluginSlotRegion region) => _hitTester.HitTestPluginSlot(x, y, out region);

    public PluginSlotHit? HitTestPluginSlot(float x, float y, out PluginSlotRegion region, out SKRect rect) =>
        _hitTester.HitTestPluginSlot(x, y, out region, out rect);

    public RoutingSlotHit? HitTestRoutingSlot(float x, float y, out RoutingSlotRegion region) => _hitTester.HitTestRoutingSlot(x, y, out region);

    public RoutingSlotHit? HitTestRoutingSlot(float x, float y, out RoutingSlotRegion region, out SKRect rect) =>
        _hitTester.HitTestRoutingSlot(x, y, out region, out rect);

    public RoutingKnobHit? HitTestRoutingKnob(float x, float y) => _hitTester.HitTestRoutingKnob(x, y);

    public RoutingBadgeHit? HitTestRoutingBadge(float x, float y) => _hitTester.HitTestRoutingBadge(x, y);

    public MainContainerSlotHit? HitTestContainerSlot(float x, float y, out MainContainerSlotRegion region) => _hitTester.HitTestContainerSlot(x, y, out region);

    public MainContainerSlotHit? HitTestContainerSlot(float x, float y, out MainContainerSlotRegion region, out SKRect rect) =>
        _hitTester.HitTestContainerSlot(x, y, out region, out rect);

    public int HitTestPluginArea(float x, float y) => _hitTester.HitTestPluginArea(x, y);

    public ToggleHit? HitTestToggle(float x, float y) => _hitTester.HitTestToggle(x, y);

    public int HitTestChannelDelete(float x, float y) => _hitTester.HitTestChannelDelete(x, y);

    public int HitTestChannelName(float x, float y) => _hitTester.HitTestChannelName(x, y);

    public bool HitTestAddChannel(float x, float y) => _hitTester.HitTestAddChannel(x, y);

    public bool HitTestMasterMeter(float x, float y) => _hitTester.HitTestMasterMeter(x, y);

    public bool HitTestVisualizerButton(float x, float y) => _hitTester.HitTestVisualizerButton(x, y);

    public bool HitTestMeterScaleToggle(float x, float y) => _hitTester.HitTestMeterScaleToggle(x, y);

    public bool HitTestQualityToggle(float x, float y) => _hitTester.HitTestQualityToggle(x, y);

    public bool HitTestStatsArea(float x, float y) => _hitTester.HitTestStatsArea(x, y);

    public bool HitTestTitleBar(float x, float y) => _hitTester.HitTestTitleBar(x, y);

    public bool HitTestPresetDropdown(float x, float y) => _hitTester.HitTestPresetDropdown(x, y);

    public SKRect GetPresetDropdownRect() => _hitTester.GetPresetDropdownRect();

    public SKRect GetPluginSlotRect(int channelIndex, int slotIndex) =>
        _pluginShellRenderer.GetSlotRectByIndex(channelIndex, slotIndex);

    public SKRect GetRoutingSlotRect(int channelIndex, int slotIndex) =>
        _routingSlotRenderer.GetSlotRectByIndex(channelIndex, slotIndex);

    private void ResetHitTargets()
    {
        _hitTargets.Clear();
        _pluginShellRenderer.ClearHitTargets();
        _routingSlotRenderer.ClearHitTargets();
    }
}
