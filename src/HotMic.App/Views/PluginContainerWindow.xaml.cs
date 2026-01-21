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

public partial class PluginContainerWindow : Window, IDisposable
{
    private const float DragThreshold = 6f;
    private readonly PluginContainerRenderer _renderer = new();
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginStripUiState _uiState = new();
    private KnobDragState? _knobDrag;
    private bool _disposed;

    public PluginContainerWindow(PluginContainerWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += (_, _) => SkiaCanvas.InvalidateVisual();
        Loaded += (_, _) => _renderTimer.Start();
        Closed += (_, _) => Dispose();
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
        _renderer.Render(canvas, size, viewModel, dpiScale, _uiState);
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

        // Get source rect for drag visuals
        var sourceRect = _renderer.GetPluginSlotRect(viewModel.ChannelIndex, slotIndex);
        string displayName = slot.DisplayName ?? $"Plugin {slotIndex + 1}";

        _uiState.PluginDrag = new PluginStripDragState(
            viewModel.ChannelIndex, slot.InstanceId, slotIndex,
            x, y, x, y, false, sourceRect, displayName);
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

        if (_uiState.PluginDrag is not { } drag || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        float dx = MathF.Abs(x - drag.StartX);
        float dy = MathF.Abs(y - drag.StartY);
        bool isDragging = drag.IsDragging || dx > DragThreshold || dy > DragThreshold;
        _uiState.PluginDrag = drag with { CurrentX = x, CurrentY = y, IsDragging = isDragging };

        // Update drop target when dragging
        if (isDragging)
        {
            _uiState.CurrentDropTarget = ComputeDropTarget(viewModel, x, y, drag.SlotIndex);
        }
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

        if (_uiState.PluginDrag is not { } drag)
        {
            return;
        }

        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        if (drag.IsDragging)
        {
            int targetIndex = ResolveDropTarget(viewModel, x, y, drag.SlotIndex);
            if (targetIndex >= 0)
            {
                viewModel.MovePlugin(drag.PluginInstanceId, targetIndex);
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

        _uiState.PluginDrag = null;
        _uiState.CurrentDropTarget = null;
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

    private int ResolveDropTarget(PluginContainerWindowViewModel viewModel, float x, float y, int sourceSlot)
    {
        var hit = _renderer.HitTestPluginSlot(x, y, out _, out var rect);
        if (!hit.HasValue || hit.Value.SlotIndex == sourceSlot)
        {
            return -1;
        }

        bool insertBefore = x < rect.MidX;
        int dropIndex = hit.Value.SlotIndex + (insertBefore ? 0 : 1);
        return ResolvePluginInsertIndex(viewModel, sourceSlot, dropIndex);
    }

    private static int ResolvePluginInsertIndex(PluginContainerWindowViewModel viewModel, int sourceIndex, int dropIndex)
    {
        int lastPluginIndex = viewModel.PluginSlots.Count - 2;
        if (lastPluginIndex < 0)
        {
            return -1;
        }

        int maxInsertIndex = lastPluginIndex + 1;
        if (dropIndex < 0)
        {
            dropIndex = 0;
        }
        else if (dropIndex > maxInsertIndex)
        {
            dropIndex = maxInsertIndex;
        }

        if (dropIndex > sourceIndex)
        {
            dropIndex--;
        }

        if (dropIndex < 0)
        {
            dropIndex = 0;
        }
        else if (dropIndex > lastPluginIndex)
        {
            dropIndex = lastPluginIndex;
        }

        if (dropIndex == sourceIndex)
        {
            return -1;
        }

        return dropIndex;
    }

    private DropTarget? ComputeDropTarget(PluginContainerWindowViewModel viewModel, float x, float y, int sourceSlot)
    {
        // Check specific slot for insertion point
        var hit = _renderer.HitTestPluginSlot(x, y, out _, out SkiaSharp.SKRect rect);
        if (!hit.HasValue)
        {
            return null; // No slot under cursor
        }

        // Over a plugin slot - check if same as source
        if (hit.Value.SlotIndex == sourceSlot)
        {
            return null; // Same slot, no feedback
        }

        // All drops within the same container are valid
        bool insertBefore = x < rect.MidX;
        float lineX = insertBefore ? rect.Left - 2f : rect.Right + 2f;

        return new DropTarget(true, rect, lineX, rect.Top, rect.Bottom);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _renderTimer.Stop();
        _renderer.Dispose();
        GC.SuppressFinalize(this);
    }
}
