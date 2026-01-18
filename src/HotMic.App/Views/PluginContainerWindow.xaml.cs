using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HotMic.App.UI;
using HotMic.App.ViewModels;
using SkiaSharp;
using SkiaSharp.Views.WPF;

namespace HotMic.App.Views;

public partial class PluginContainerWindow : Window
{
    private const float DragThreshold = 6f;
    private readonly PluginContainerRenderer _renderer = new();
    private readonly DispatcherTimer _renderTimer;
    private ContainerDragState? _dragState;
    private KnobDragState? _knobDrag;

    public PluginContainerWindow(PluginContainerWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += (_, _) => SkiaCanvas.InvalidateVisual();
        Loaded += (_, _) => _renderTimer.Start();
        Closed += (_, _) => _renderTimer.Stop();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        if (DataContext is not PluginContainerWindowViewModel viewModel)
        {
            return;
        }

        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();
        _renderer.Render(canvas, size, viewModel, dpiScale);
    }

    private void SkiaCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not PluginContainerWindowViewModel viewModel || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        if (_renderer.HitTestClose(x, y))
        {
            Close();
            e.Handled = true;
            return;
        }

        var pluginKnobHit = _renderer.HitTestPluginKnob(x, y);
        if (pluginKnobHit.HasValue)
        {
            StartPluginKnobDrag(viewModel, pluginKnobHit.Value, y);
            SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (_renderer.HitTestTitleBar(x, y))
        {
            DragMove();
            e.Handled = true;
            return;
        }

        var hit = _renderer.HitTestPluginSlot(x, y, out var region);
        if (!hit.HasValue)
        {
            return;
        }

        int slotIndex = hit.Value.SlotIndex;
        if ((uint)slotIndex >= (uint)viewModel.PluginSlots.Count)
        {
            return;
        }

        var slot = viewModel.PluginSlots[slotIndex];
        if (region == PluginSlotRegion.Bypass)
        {
            if (!slot.IsEmpty)
            {
                slot.IsBypassed = !slot.IsBypassed;
            }
            e.Handled = true;
            return;
        }

        if (region == PluginSlotRegion.Remove)
        {
            if (!slot.IsEmpty)
            {
                slot.RemoveCommand.Execute(null);
            }
            e.Handled = true;
            return;
        }

        if (region == PluginSlotRegion.DeltaStrip)
        {
            if (!slot.IsEmpty)
            {
                slot.ToggleDeltaDisplayMode();
            }
            e.Handled = true;
            return;
        }

        if (slot.IsEmpty)
        {
            slot.ActionCommand.Execute(null);
            e.Handled = true;
            return;
        }

        _dragState = new ContainerDragState(slot.InstanceId, slotIndex, x, y, x, y, false);
        SkiaCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void SkiaCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (DataContext is not PluginContainerWindowViewModel viewModel)
        {
            return;
        }

        if (_knobDrag is { } knobDrag && e.LeftButton == MouseButtonState.Pressed)
        {
            var knobPos = e.GetPosition(SkiaCanvas);
            float knobY = (float)knobPos.Y;
            UpdateKnobDrag(viewModel, knobDrag, knobY);
            e.Handled = true;
            return;
        }

        if (_dragState is not { } drag || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        float dx = MathF.Abs(x - drag.StartX);
        float dy = MathF.Abs(y - drag.StartY);
        bool isDragging = drag.IsDragging || dx > DragThreshold || dy > DragThreshold;
        _dragState = drag with { CurrentX = x, CurrentY = y, IsDragging = isDragging };
        e.Handled = true;
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not PluginContainerWindowViewModel viewModel || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        SkiaCanvas.ReleaseMouseCapture();

        if (_knobDrag is not null)
        {
            _knobDrag = null;
            e.Handled = true;
            return;
        }

        if (_dragState is not { } drag)
        {
            return;
        }

        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        if (drag.IsDragging)
        {
            var target = _renderer.HitTestPluginSlot(x, y, out _);
            if (target.HasValue && target.Value.SlotIndex != drag.SlotIndex)
            {
                viewModel.MovePlugin(drag.PluginInstanceId, target.Value.SlotIndex);
            }
        }
        else
        {
            int slotIndex = drag.SlotIndex;
            if ((uint)slotIndex < (uint)viewModel.PluginSlots.Count)
            {
                var slot = viewModel.PluginSlots[slotIndex];
                if (!slot.IsEmpty)
                {
                    slot.ActionCommand.Execute(null);
                }
            }
        }

        _dragState = null;
        e.Handled = true;
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }

    private void StartPluginKnobDrag(PluginContainerWindowViewModel viewModel, PluginKnobHit hit, float startY)
    {
        int slotIndex = FindPluginSlotIndex(viewModel, hit.PluginInstanceId);
        if ((uint)slotIndex >= (uint)viewModel.PluginSlots.Count)
        {
            return;
        }

        var slot = viewModel.PluginSlots[slotIndex];
        float startValue = hit.ParamIndex == 0 ? slot.Param0Value : slot.Param1Value;
        var knobType = hit.ParamIndex == 0 ? KnobType.PluginParam0 : KnobType.PluginParam1;

        _knobDrag = new KnobDragState(
            viewModel.ChannelIndex,
            knobType,
            startValue,
            startY,
            hit.PluginInstanceId,
            hit.MinValue,
            hit.MaxValue);
    }

    private static void UpdateKnobDrag(PluginContainerWindowViewModel viewModel, KnobDragState drag, float currentY)
    {
        float delta = drag.StartY - currentY;
        float min = drag.MinValue;
        float max = drag.MaxValue;
        float range = max - min;
        float sensitivity = range / 150f;
        float nextValue = drag.StartValue + delta * sensitivity;
        nextValue = Math.Clamp(nextValue, min, max);

        int slotIndex = FindPluginSlotIndex(viewModel, drag.PluginInstanceId);
        if (slotIndex < 0 || slotIndex >= viewModel.PluginSlots.Count)
        {
            return;
        }

        if (drag.KnobType == KnobType.PluginParam0)
        {
            viewModel.PluginSlots[slotIndex].Param0Value = nextValue;
        }
        else if (drag.KnobType == KnobType.PluginParam1)
        {
            viewModel.PluginSlots[slotIndex].Param1Value = nextValue;
        }
    }

    private static int FindPluginSlotIndex(PluginContainerWindowViewModel viewModel, int instanceId)
    {
        if (instanceId <= 0)
        {
            return -1;
        }

        int count = Math.Max(0, viewModel.PluginSlots.Count - 1);
        for (int i = 0; i < count; i++)
        {
            if (viewModel.PluginSlots[i].InstanceId == instanceId)
            {
                return i;
            }
        }

        return -1;
    }

    private readonly record struct ContainerDragState(int PluginInstanceId, int SlotIndex, float StartX, float StartY, float CurrentX, float CurrentY, bool IsDragging);
}
