using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HotMic.App.Models;
using HotMic.App.UI;
using HotMic.App.ViewModels;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace HotMic.App;

public partial class MainWindow : Window
{
    private const float DragThreshold = 6f;

    private readonly MainRenderer _renderer = new();
    private readonly MainUiState _uiState = new();
    private readonly DispatcherTimer _renderTimer;

    public MainWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        LocationChanged += (_, _) => UpdateWindowPosition();
        SizeChanged += (_, _) => UpdateWindowSize();
        Closed += (_, _) => DisposeViewModel();

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += (_, _) => SkiaCanvas.InvalidateVisual();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _renderTimer.Start();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();
        _renderer.Render(canvas, size, viewModel, _uiState, dpiScale);
    }

    private void SkiaCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        var topButton = _renderer.HitTestTopButton(x, y);
        if (topButton.HasValue)
        {
            HandleTopButton(topButton.Value, viewModel);
            e.Handled = true;
            return;
        }

        if (_renderer.HitTestTitleBar(x, y))
        {
            DragMove();
            e.Handled = true;
            return;
        }

        int deleteIndex = _renderer.HitTestChannelDelete(x, y);
        if (deleteIndex >= 0)
        {
            viewModel.RemoveChannel(deleteIndex);
            e.Handled = true;
            return;
        }

        if (_renderer.HitTestAddChannel(x, y))
        {
            viewModel.AddChannel();
            e.Handled = true;
            return;
        }

        if (viewModel.IsMinimalView)
        {
            return;
        }

        // Meter scale toggle in hotbar
        if (_renderer.HitTestMeterScaleToggle(x, y))
        {
            viewModel.MeterScaleVox = !viewModel.MeterScaleVox;
            e.Handled = true;
            return;
        }

        if (_renderer.HitTestQualityToggle(x, y))
        {
            viewModel.QualityMode = viewModel.QualityMode == HotMic.Common.Configuration.AudioQualityMode.LatencyPriority
                ? HotMic.Common.Configuration.AudioQualityMode.QualityPriority
                : HotMic.Common.Configuration.AudioQualityMode.LatencyPriority;
            e.Handled = true;
            return;
        }

        if (_renderer.HitTestMasterMeter(x, y))
        {
            viewModel.MasterMeterLufs = !viewModel.MasterMeterLufs;
            e.Handled = true;
            return;
        }

        if (_renderer.HitTestVisualizerButton(x, y))
        {
            viewModel.OpenAnalyzerWindow();
            e.Handled = true;
            return;
        }

        // Stats area click toggles debug overlay
        if (_renderer.HitTestStatsArea(x, y))
        {
            viewModel.ShowDebugOverlay = !viewModel.ShowDebugOverlay;
            e.Handled = true;
            return;
        }

        // Preset dropdown clicks
        if (_renderer.HitTestPresetDropdown(x, y))
        {
            ShowPresetDropdownMenu(viewModel);
            e.Handled = true;
            return;
        }

        // Check routing badges first (mode toggles L/R/Sum)
        var routingBadgeHit = _renderer.HitTestRoutingBadge(x, y);
        if (routingBadgeHit.HasValue)
        {
            SetActiveChannel(viewModel, routingBadgeHit.Value.ChannelIndex);
            HandleRoutingBadgeClick(viewModel, routingBadgeHit.Value);
            e.Handled = true;
            return;
        }

        // Check routing knobs first (they're on top of routing slots)
        var routingKnobHit = _renderer.HitTestRoutingKnob(x, y);
        if (routingKnobHit.HasValue)
        {
            SetActiveChannel(viewModel, routingKnobHit.Value.ChannelIndex);
            StartRoutingKnobDrag(viewModel, routingKnobHit.Value, y);
            SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        // Check plugin knobs first (they're on top of plugin slots)
        var pluginKnobHit = _renderer.HitTestPluginKnob(x, y);
        if (pluginKnobHit.HasValue)
        {
            SetActiveChannel(viewModel, pluginKnobHit.Value.ChannelIndex);
            StartPluginKnobDrag(viewModel, pluginKnobHit.Value, y);
            SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        var knobHit = _renderer.HitTestKnob(x, y);
        if (knobHit.HasValue)
        {
            SetActiveChannel(viewModel, knobHit.Value.ChannelIndex);
            StartKnobDrag(viewModel, knobHit.Value, y);
            SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        var toggleHit = _renderer.HitTestToggle(x, y);
        if (toggleHit.HasValue)
        {
            if (toggleHit.Value.ChannelIndex >= 0)
            {
                SetActiveChannel(viewModel, toggleHit.Value.ChannelIndex);
            }
            HandleToggle(viewModel, toggleHit.Value);
            e.Handled = true;
            return;
        }

        var containerHit = _renderer.HitTestContainerSlot(x, y, out var containerRegion);
        if (containerHit.HasValue)
        {
            SetActiveChannel(viewModel, containerHit.Value.ChannelIndex);
            HandleContainerSlotMouseDown(viewModel, containerHit.Value, containerRegion, x, y);
            e.Handled = true;
            return;
        }

        // Check routing slots before standard plugin slots
        var routingHit = _renderer.HitTestRoutingSlot(x, y, out var routingRegion);
        if (routingHit.HasValue)
        {
            SetActiveChannel(viewModel, routingHit.Value.ChannelIndex);
            HandleRoutingSlotClick(viewModel, routingHit.Value, routingRegion, x, y);
            e.Handled = true;
            return;
        }

        var pluginHit = _renderer.HitTestPluginSlot(x, y, out var region);
        if (pluginHit.HasValue)
        {
            SetActiveChannel(viewModel, pluginHit.Value.ChannelIndex);
            HandlePluginSlotClick(viewModel, pluginHit.Value, region, x, y);
            e.Handled = true;
        }
    }

    private void SkiaCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        if (_uiState.KnobDrag is { } knobDrag && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateKnobDrag(viewModel, knobDrag, y);
            e.Handled = true;
        }

        if (_uiState.ContainerDrag is { } containerDrag && e.LeftButton == MouseButtonState.Pressed)
        {
            float dx = MathF.Abs(x - containerDrag.StartX);
            float dy = MathF.Abs(y - containerDrag.StartY);
            bool isDragging = containerDrag.IsDragging || dx > DragThreshold || dy > DragThreshold;
            _uiState.ContainerDrag = containerDrag with { CurrentX = x, CurrentY = y, IsDragging = isDragging };
            e.Handled = true;
        }

        if (_uiState.PluginDrag is { } pluginDrag && e.LeftButton == MouseButtonState.Pressed)
        {
            float dx = MathF.Abs(x - pluginDrag.StartX);
            float dy = MathF.Abs(y - pluginDrag.StartY);
            bool isDragging = pluginDrag.IsDragging || dx > DragThreshold || dy > DragThreshold;
            _uiState.PluginDrag = pluginDrag with { CurrentX = x, CurrentY = y, IsDragging = isDragging };
            e.Handled = true;
        }
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        SkiaCanvas.ReleaseMouseCapture();

        if (_uiState.KnobDrag is not null)
        {
            _uiState.KnobDrag = null;
            e.Handled = true;
        }

        if (_uiState.ContainerDrag is { } containerDrag)
        {
            var pos = e.GetPosition(SkiaCanvas);
            float x = (float)pos.X;
            float y = (float)pos.Y;

            if (containerDrag.IsDragging)
            {
                int targetIndex = ResolveContainerDropTarget(viewModel, containerDrag.ChannelIndex, containerDrag.ContainerId, x, y);
                if (targetIndex >= 0)
                {
                    var channel = GetChannel(viewModel, containerDrag.ChannelIndex);
                    if (channel is not null)
                    {
                        channel.MoveContainer(containerDrag.ContainerId, targetIndex);
                    }
                }
            }
            else
            {
                HandleContainerSlotClick(viewModel, new MainContainerSlotHit(containerDrag.ChannelIndex, containerDrag.ContainerId, containerDrag.SlotIndex));
            }

            _uiState.ContainerDrag = null;
            e.Handled = true;
            return;
        }

        if (_uiState.PluginDrag is { } pluginDrag)
        {
            var pos = e.GetPosition(SkiaCanvas);
            float x = (float)pos.X;
            float y = (float)pos.Y;

            if (pluginDrag.IsDragging)
            {
                var target = _renderer.HitTestPluginSlot(x, y, out _);
                if (target.HasValue && !(target.Value.ChannelIndex == pluginDrag.ChannelIndex && target.Value.SlotIndex == pluginDrag.SlotIndex))
                {
                    var channel = GetChannel(viewModel, pluginDrag.ChannelIndex);
                    if (channel is not null)
                    {
                        channel.MovePlugin(pluginDrag.PluginInstanceId, target.Value.SlotIndex);
                    }
                }
            }
            else
            {
                ExecutePluginAction(viewModel, pluginDrag.ChannelIndex, pluginDrag.PluginInstanceId, pluginDrag.SlotIndex);
            }

            _uiState.PluginDrag = null;
            e.Handled = true;
        }
    }

    private void SkiaCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // No longer used for device picker - reserved for future scroll functionality
    }

    private void SkiaCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.IsMinimalView)
        {
            return;
        }

        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        // Check plugin knobs first
        var pluginKnobHit = _renderer.HitTestPluginKnob(x, y);
        if (pluginKnobHit.HasValue)
        {
            ShowKnobContextMenu(viewModel, pluginKnobHit.Value, pos);
            e.Handled = true;
            return;
        }

        // Check regular knobs (input/output gain)
        var knobHit = _renderer.HitTestKnob(x, y);
        if (knobHit.HasValue)
        {
            ShowGainKnobContextMenu(viewModel, knobHit.Value, pos);
            e.Handled = true;
            return;
        }

    }

    private void ShowKnobContextMenu(MainViewModel viewModel, PluginKnobHit hit, System.Windows.Point position)
    {
        var channel = GetChannel(viewModel, hit.ChannelIndex);
        if (channel is null)
        {
            return;
        }
        int slotIndex = FindPluginSlotIndex(channel, hit.PluginInstanceId);
        if ((uint)slotIndex >= (uint)channel.PluginSlots.Count)
        {
            return;
        }

        var slot = channel.PluginSlots[slotIndex];
        if (slot.ElevatedParams is null || slot.ElevatedParams.Length <= hit.ParamIndex)
        {
            return;
        }

        var paramDef = slot.ElevatedParams[hit.ParamIndex];
        string targetPath = $"channel{hit.ChannelIndex + 1}.plugin.{hit.PluginInstanceId}.{paramDef.Index}";
        string paramLabel = $"{slot.DisplayName} - {paramDef.Name}";

        ShowMidiContextMenu(viewModel, targetPath, paramLabel, paramDef.Min, paramDef.Max, position);
    }

    private void ShowGainKnobContextMenu(MainViewModel viewModel, KnobHit hit, System.Windows.Point position)
    {
        string targetPath = hit.KnobType == KnobType.InputGain
            ? $"channel{hit.ChannelIndex + 1}.inputGain"
            : $"channel{hit.ChannelIndex + 1}.outputGain";

        string label = hit.KnobType == KnobType.InputGain ? "Input Gain" : "Output Gain";
        string channelName = "Channel";
        var channel = GetChannel(viewModel, hit.ChannelIndex);
        if (channel is not null)
        {
            channelName = string.IsNullOrWhiteSpace(channel.Name) ? $"Ch {hit.ChannelIndex + 1}" : channel.Name;
        }

        ShowMidiContextMenu(viewModel, targetPath, $"{channelName} {label}", -60f, 12f, position);
    }

    private void ShowMidiContextMenu(MainViewModel viewModel, string targetPath, string label, float minValue, float maxValue, System.Windows.Point position)
    {
        var menu = new ContextMenu();

        var existingBinding = viewModel.GetMidiBinding(targetPath);

        // Header item (disabled, just for display)
        var headerItem = new MenuItem { Header = label, IsEnabled = false };
        menu.Items.Add(headerItem);
        menu.Items.Add(new Separator());

        if (existingBinding != null)
        {
            // Show current binding
            var currentItem = new MenuItem
            {
                Header = $"Bound to CC {existingBinding.CcNumber}" + (existingBinding.Channel.HasValue ? $" Ch {existingBinding.Channel}" : ""),
                IsEnabled = false
            };
            menu.Items.Add(currentItem);
            menu.Items.Add(new Separator());
        }

        // MIDI Learn item
        var learnItem = new MenuItem { Header = viewModel.IsMidiLearning ? "Cancel MIDI Learn" : "MIDI Learn" };
        learnItem.Click += (_, _) =>
        {
            if (viewModel.IsMidiLearning)
            {
                viewModel.CancelMidiLearn();
            }
            else
            {
                viewModel.StartMidiLearn(targetPath, (ccNumber, channel) =>
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        viewModel.AddMidiBinding(targetPath, ccNumber, channel, minValue, maxValue);
                    });
                });
            }
        };
        menu.Items.Add(learnItem);

        // Clear binding item (only if bound)
        if (existingBinding != null)
        {
            var clearItem = new MenuItem { Header = "Clear MIDI Binding" };
            clearItem.Click += (_, _) =>
            {
                viewModel.RemoveMidiBinding(targetPath);
            };
            menu.Items.Add(clearItem);
        }

        menu.PlacementTarget = SkiaCanvas;
        menu.IsOpen = true;
    }

    private void HandleTopButton(MainButton button, MainViewModel viewModel)
    {
        switch (button)
        {
            case MainButton.ToggleView:
                viewModel.ToggleViewCommand.Execute(null);
                break;
            case MainButton.Settings:
                viewModel.OpenSettingsCommand.Execute(null);
                break;
            case MainButton.Pin:
                viewModel.ToggleAlwaysOnTopCommand.Execute(null);
                break;
            case MainButton.Minimize:
                WindowState = WindowState.Minimized;
                break;
            case MainButton.Close:
                Close();
                break;
            case MainButton.SavePreset:
                ShowSavePresetMenu(viewModel, viewModel.ActiveChannelIndex);
                break;
        }
    }

    private void HandlePluginSlotClick(MainViewModel viewModel, PluginSlotHit hit, PluginSlotRegion region, float x, float y)
    {
        var channel = GetChannel(viewModel, hit.ChannelIndex);
        if (channel is null)
        {
            return;
        }
        int slotIndex = hit.PluginInstanceId > 0
            ? FindPluginSlotIndex(channel, hit.PluginInstanceId)
            : hit.SlotIndex;
        if (slotIndex < 0 || slotIndex >= channel.PluginSlots.Count)
        {
            return;
        }

        var slot = channel.PluginSlots[slotIndex];
        if (region == PluginSlotRegion.Bypass)
        {
            if (!slot.IsEmpty)
            {
                slot.IsBypassed = !slot.IsBypassed;
            }
            return;
        }

        if (region == PluginSlotRegion.Remove)
        {
            if (!slot.IsEmpty)
            {
                slot.RemoveCommand.Execute(null);
            }
            return;
        }

        if (region == PluginSlotRegion.DeltaStrip)
        {
            if (!slot.IsEmpty)
            {
                slot.ToggleDeltaDisplayMode();
            }
            return;
        }

        if (slot.IsEmpty)
        {
            slot.ActionCommand.Execute(null);
            return;
        }

        if (slot.PluginId == "builtin:input" || slot.PluginId == "builtin:bus-input")
        {
            ExecutePluginAction(viewModel, hit.ChannelIndex, slot.InstanceId, slotIndex);
            return;
        }

        _uiState.PluginDrag = new PluginDragState(hit.ChannelIndex, slot.InstanceId, slotIndex, x, y, x, y, false);
    }

    private void HandleRoutingSlotClick(MainViewModel viewModel, RoutingSlotHit hit, RoutingSlotRegion region, float x, float y)
    {
        var channel = GetChannel(viewModel, hit.ChannelIndex);
        if (channel is null)
        {
            return;
        }

        int slotIndex = FindPluginSlotIndex(channel, hit.PluginInstanceId);
        if (slotIndex < 0 || slotIndex >= channel.PluginSlots.Count)
        {
            return;
        }

        var slot = channel.PluginSlots[slotIndex];

        if (region == RoutingSlotRegion.Remove)
        {
            if (!slot.IsEmpty)
            {
                slot.RemoveCommand.Execute(null);
            }
            return;
        }

        // For routing plugins, start drag to allow reordering in the chain
        // The actual action (popup) will execute on mouse up if no drag occurred
        _uiState.PluginDrag = new PluginDragState(hit.ChannelIndex, hit.PluginInstanceId, slotIndex, x, y, x, y, false);
        SkiaCanvas.CaptureMouse();
    }

    private void HandleRoutingBadgeClick(MainViewModel viewModel, RoutingBadgeHit hit)
    {
        var channel = GetChannel(viewModel, hit.ChannelIndex);
        if (channel is null)
        {
            return;
        }

        switch (hit.BadgeType)
        {
            case RoutingBadgeType.InputChannelMode:
                // Cycle: Left -> Right -> Sum -> Left
                channel.InputChannelMode = channel.InputChannelMode switch
                {
                    HotMic.Common.Configuration.InputChannelMode.Left => HotMic.Common.Configuration.InputChannelMode.Right,
                    HotMic.Common.Configuration.InputChannelMode.Right => HotMic.Common.Configuration.InputChannelMode.Sum,
                    _ => HotMic.Common.Configuration.InputChannelMode.Left
                };
                break;
            case RoutingBadgeType.OutputSendMode:
                // Cycle through output send mode parameter
                int slotIndex = FindPluginSlotIndex(channel, hit.PluginInstanceId);
                if ((uint)slotIndex < (uint)channel.PluginSlots.Count)
                {
                    var slot = channel.PluginSlots[slotIndex];
                    // Cycle: 0 (Left) -> 1 (Right) -> 2 (Both) -> 0
                    slot.Param0Value = slot.Param0Value switch
                    {
                        0f => 1f,
                        1f => 2f,
                        _ => 0f
                    };
                }
                break;
        }
    }

    private void StartRoutingKnobDrag(MainViewModel viewModel, RoutingKnobHit hit, float startY)
    {
        var channel = GetChannel(viewModel, hit.ChannelIndex);
        if (channel is null)
        {
            return;
        }

        float startValue;
        KnobType knobType;

        if (hit.KnobType == RoutingKnobType.InputGain)
        {
            startValue = channel.InputGainDb;
            knobType = KnobType.InputGain;
        }
        else
        {
            startValue = channel.OutputGainDb;
            knobType = KnobType.OutputGain;
        }

        _uiState.KnobDrag = new KnobDragState(
            hit.ChannelIndex,
            knobType,
            startValue,
            startY,
            hit.PluginInstanceId,
            hit.MinValue,
            hit.MaxValue);
    }

    private void HandleContainerSlotMouseDown(MainViewModel viewModel, MainContainerSlotHit hit, MainContainerSlotRegion region, float x, float y)
    {
        var channel = GetChannel(viewModel, hit.ChannelIndex);
        if (channel is null)
        {
            return;
        }
        if ((uint)hit.SlotIndex >= (uint)channel.Containers.Count)
        {
            return;
        }

        var container = channel.Containers[hit.SlotIndex];
        bool isPlaceholder = container.ContainerId <= 0;
        if (region == MainContainerSlotRegion.Bypass)
        {
            if (!isPlaceholder)
            {
                container.IsBypassed = !container.IsBypassed;
            }
            return;
        }

        if (region == MainContainerSlotRegion.Remove)
        {
            if (!isPlaceholder)
            {
                container.RemoveCommand.Execute(null);
            }
            return;
        }

        if (isPlaceholder)
        {
            return;
        }

        _uiState.ContainerDrag = new ContainerDragState(hit.ChannelIndex, container.ContainerId, hit.SlotIndex, x, y, x, y, false);
        SkiaCanvas.CaptureMouse();
    }

    private void HandleContainerSlotClick(MainViewModel viewModel, MainContainerSlotHit hit)
    {
        var channel = GetChannel(viewModel, hit.ChannelIndex);
        if (channel is null)
        {
            return;
        }
        int slotIndex = FindContainerSlotIndex(channel, hit.ContainerId);
        if ((uint)slotIndex >= (uint)channel.Containers.Count)
        {
            return;
        }

        channel.Containers[slotIndex].ActionCommand.Execute(null);
    }

    private void ExecutePluginAction(MainViewModel viewModel, int channelIndex, int pluginInstanceId, int slotIndex)
    {
        var channel = GetChannel(viewModel, channelIndex);
        if (channel is null)
        {
            return;
        }
        int resolvedIndex = pluginInstanceId > 0
            ? FindPluginSlotIndex(channel, pluginInstanceId)
            : slotIndex;
        if (resolvedIndex < 0 || resolvedIndex >= channel.PluginSlots.Count)
        {
            return;
        }

        channel.PluginSlots[resolvedIndex].ActionCommand.Execute(null);
    }

    private void StartKnobDrag(MainViewModel viewModel, KnobHit hit, float startY)
    {
        var channel = GetChannel(viewModel, hit.ChannelIndex);
        if (channel is null)
        {
            return;
        }

        float startValue = hit.KnobType == KnobType.InputGain
            ? channel.InputGainDb
            : channel.OutputGainDb;

        _uiState.KnobDrag = new KnobDragState(hit.ChannelIndex, hit.KnobType, startValue, startY);
    }

    private void StartPluginKnobDrag(MainViewModel viewModel, PluginKnobHit hit, float startY)
    {
        var channel = GetChannel(viewModel, hit.ChannelIndex);
        if (channel is null)
        {
            return;
        }
        int slotIndex = FindPluginSlotIndex(channel, hit.PluginInstanceId);
        if ((uint)slotIndex >= (uint)channel.PluginSlots.Count)
        {
            return;
        }

        var slot = channel.PluginSlots[slotIndex];
        float startValue = hit.ParamIndex == 0 ? slot.Param0Value : slot.Param1Value;
        var knobType = hit.ParamIndex == 0 ? KnobType.PluginParam0 : KnobType.PluginParam1;

        _uiState.KnobDrag = new KnobDragState(
            hit.ChannelIndex,
            knobType,
            startValue,
            startY,
            hit.PluginInstanceId,
            hit.MinValue,
            hit.MaxValue);
    }

    private static void UpdateKnobDrag(MainViewModel viewModel, KnobDragState drag, float currentY)
    {
        float delta = drag.StartY - currentY;
        float min = drag.MinValue;
        float max = drag.MaxValue;
        float range = max - min;
        float sensitivity = range / 150f;
        float nextValue = drag.StartValue + delta * sensitivity;
        nextValue = Math.Clamp(nextValue, min, max);

        var channel = GetChannel(viewModel, drag.ChannelIndex);
        if (channel is null)
        {
            return;
        }

        switch (drag.KnobType)
        {
            case KnobType.InputGain:
                channel.InputGainDb = nextValue;
                break;
            case KnobType.OutputGain:
                channel.OutputGainDb = nextValue;
                break;
            case KnobType.PluginParam0:
                UpdatePluginKnobValue(channel, drag.PluginInstanceId, isParam0: true, nextValue);
                break;
            case KnobType.PluginParam1:
                UpdatePluginKnobValue(channel, drag.PluginInstanceId, isParam0: false, nextValue);
                break;
        }
    }

    private static void UpdatePluginKnobValue(ChannelStripViewModel channel, int instanceId, bool isParam0, float value)
    {
        int slotIndex = FindPluginSlotIndex(channel, instanceId);
        if (slotIndex < 0 || slotIndex >= channel.PluginSlots.Count)
        {
            return;
        }

        if (isParam0)
        {
            channel.PluginSlots[slotIndex].Param0Value = value;
        }
        else
        {
            channel.PluginSlots[slotIndex].Param1Value = value;
        }
    }

    private static int FindPluginSlotIndex(ChannelStripViewModel channel, int instanceId)
    {
        if (instanceId <= 0)
        {
            return -1;
        }

        int count = Math.Max(0, channel.PluginSlots.Count - 1);
        for (int i = 0; i < count; i++)
        {
            if (channel.PluginSlots[i].InstanceId == instanceId)
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindContainerSlotIndex(ChannelStripViewModel channel, int containerId)
    {
        if (containerId <= 0)
        {
            return -1;
        }

        for (int i = 0; i < channel.Containers.Count; i++)
        {
            if (channel.Containers[i].ContainerId == containerId)
            {
                return i;
            }
        }

        return -1;
    }

    private int ResolveContainerDropTarget(MainViewModel viewModel, int channelIndex, int containerId, float x, float y)
    {
        var channel = GetChannel(viewModel, channelIndex);
        if (channel is null)
        {
            return -1;
        }

        var containerHit = _renderer.HitTestContainerSlot(x, y, out _);
        if (containerHit.HasValue && containerHit.Value.ContainerId != containerId)
        {
            return GetContainerInsertIndex(channel, containerHit.Value.SlotIndex);
        }

        var pluginHit = _renderer.HitTestPluginSlot(x, y, out _);
        if (pluginHit.HasValue)
        {
            if (pluginHit.Value.PluginInstanceId > 0)
            {
                return FindPluginSlotIndex(channel, pluginHit.Value.PluginInstanceId);
            }

            return channel.PluginSlots.Count - 1;
        }

        return -1;
    }

    private static int GetContainerInsertIndex(ChannelStripViewModel channel, int containerSlotIndex)
    {
        if ((uint)containerSlotIndex >= (uint)channel.Containers.Count)
        {
            return channel.PluginSlots.Count - 1;
        }

        var target = channel.Containers[containerSlotIndex];
        if (target.PluginInstanceIds.Count == 0)
        {
            return channel.PluginSlots.Count - 1;
        }

        int instanceId = target.PluginInstanceIds[0];
        int index = FindPluginSlotIndex(channel, instanceId);
        return index >= 0 ? index : channel.PluginSlots.Count - 1;
    }

    private static void HandleToggle(MainViewModel viewModel, ToggleHit toggle)
    {
        switch (toggle.ToggleType)
        {
            case ToggleType.Mute:
                var channel = GetChannel(viewModel, toggle.ChannelIndex);
                if (channel is not null)
                {
                    channel.IsMuted = !channel.IsMuted;
                }
                break;
            case ToggleType.Solo:
                channel = GetChannel(viewModel, toggle.ChannelIndex);
                if (channel is not null)
                {
                    channel.IsSoloed = !channel.IsSoloed;
                }
                break;
            case ToggleType.MasterMute:
                viewModel.MasterMuted = !viewModel.MasterMuted;
                break;
        }
    }

    private static ChannelStripViewModel? GetChannel(MainViewModel viewModel, int channelIndex)
    {
        if ((uint)channelIndex >= (uint)viewModel.Channels.Count)
        {
            return null;
        }

        return viewModel.Channels[channelIndex];
    }

    private static void SetActiveChannel(MainViewModel viewModel, int channelIndex)
    {
        if ((uint)channelIndex >= (uint)viewModel.Channels.Count)
        {
            return;
        }

        if (viewModel.ActiveChannelIndex != channelIndex)
        {
            viewModel.ActiveChannelIndex = channelIndex;
        }
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }

    private void UpdateWindowPosition()
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.UpdateWindowPosition(Left, Top);
        }
    }

    private void UpdateWindowSize()
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.UpdateWindowSize(Width, Height);
        }
    }

    private void DisposeViewModel()
    {
        _renderTimer.Stop();
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Dispose();
        }
    }

    private void ShowPresetDropdownMenu(MainViewModel viewModel)
    {
        var menu = new ContextMenu();
        var presetOptions = viewModel.GetPresetOptions();
        int channelIndex = viewModel.ActiveChannelIndex;
        if ((uint)channelIndex >= (uint)viewModel.Channels.Count)
        {
            return;
        }
        string currentPreset = viewModel.ActiveChannelPresetName;

        foreach (var presetName in presetOptions)
        {
            var item = new MenuItem
            {
                Header = presetName,
                IsCheckable = true,
                IsChecked = string.Equals(presetName, currentPreset, StringComparison.OrdinalIgnoreCase)
            };

            string capturedName = presetName;
            item.Click += (_, _) =>
            {
                viewModel.SelectChannelPreset(channelIndex, capturedName);
            };

            menu.Items.Add(item);
        }

        // Position menu below the dropdown
        var rect = _renderer.GetPresetDropdownRect();
        menu.PlacementTarget = SkiaCanvas;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Relative;
        menu.HorizontalOffset = rect.Left;
        menu.VerticalOffset = rect.Bottom;
        menu.IsOpen = true;
    }

    private void ShowSavePresetMenu(MainViewModel viewModel, int channelIndex)
    {
        var menu = new ContextMenu();
        if ((uint)channelIndex >= (uint)viewModel.Channels.Count)
        {
            return;
        }

        string currentPreset = viewModel.ActiveChannelPresetName;

        // Save as new option
        var saveNewItem = new MenuItem { Header = "Save as New..." };
        saveNewItem.Click += (_, _) =>
        {
            ShowSavePresetDialog(viewModel, channelIndex, null);
        };
        menu.Items.Add(saveNewItem);

        // Overwrite current (only if current is a user preset that can be overwritten)
        if (viewModel.CanOverwritePreset(currentPreset))
        {
            var overwriteItem = new MenuItem { Header = $"Overwrite \"{currentPreset}\"" };
            overwriteItem.Click += (_, _) =>
            {
                viewModel.SaveCurrentAsPreset(channelIndex, currentPreset);
            };
            menu.Items.Add(overwriteItem);
        }

        menu.PlacementTarget = SkiaCanvas;
        menu.IsOpen = true;
    }

    private void ShowSavePresetDialog(MainViewModel viewModel, int channelIndex, string? suggestedName)
    {
        if ((uint)channelIndex >= (uint)viewModel.Channels.Count)
        {
            return;
        }

        var dialog = new Views.InputDialog("Save Preset", "Enter preset name:", suggestedName ?? "My Preset")
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputValue))
        {
            string presetName = dialog.InputValue.Trim();

            // Check if this would overwrite a built-in preset
            if (!viewModel.CanOverwritePreset(presetName) &&
                !string.Equals(presetName, HotMic.Core.Presets.PluginPresetManager.CustomPresetName, StringComparison.OrdinalIgnoreCase))
            {
                // Check if it's a built-in preset name
                var options = viewModel.GetPresetOptions();
                if (options.Contains(presetName))
                {
                    System.Windows.MessageBox.Show($"Cannot overwrite built-in preset \"{presetName}\".", "Save Preset", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            viewModel.SaveCurrentAsPreset(channelIndex, presetName);
        }
    }

}
