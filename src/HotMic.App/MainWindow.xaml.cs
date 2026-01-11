using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HotMic.App.UI;
using HotMic.App.ViewModels;
using SkiaSharp;
using SkiaSharp.Views.WPF;

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

        if (viewModel.IsMinimalView)
        {
            return;
        }

        var knobHit = _renderer.HitTestKnob(x, y);
        if (knobHit.HasValue)
        {
            StartKnobDrag(viewModel, knobHit.Value, y);
            SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        var toggleHit = _renderer.HitTestToggle(x, y);
        if (toggleHit.HasValue)
        {
            ToggleMuteSolo(viewModel, toggleHit.Value);
            e.Handled = true;
            return;
        }

        var pluginHit = _renderer.HitTestPluginSlot(x, y, out var region);
        if (pluginHit.HasValue)
        {
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
                    var channel = pluginDrag.ChannelIndex == 0 ? viewModel.Channel1 : viewModel.Channel2;
                    channel.MovePlugin(pluginDrag.SlotIndex, target.Value.SlotIndex);
                }
            }
            else
            {
                ExecutePluginAction(viewModel, pluginDrag.ChannelIndex, pluginDrag.SlotIndex);
            }

            _uiState.PluginDrag = null;
            e.Handled = true;
        }
    }

    private void SkiaCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // No longer used for device picker - reserved for future scroll functionality
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
        }
    }

    private void HandlePluginSlotClick(MainViewModel viewModel, PluginSlotHit hit, PluginSlotRegion region, float x, float y)
    {
        var channel = hit.ChannelIndex == 0 ? viewModel.Channel1 : viewModel.Channel2;
        if (hit.SlotIndex >= channel.PluginSlots.Count)
        {
            return;
        }

        var slot = channel.PluginSlots[hit.SlotIndex];
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

        if (slot.IsEmpty)
        {
            slot.ActionCommand.Execute(null);
            return;
        }

        _uiState.PluginDrag = new PluginDragState(hit.ChannelIndex, hit.SlotIndex, x, y, x, y, false);
    }

    private void ExecutePluginAction(MainViewModel viewModel, int channelIndex, int slotIndex)
    {
        var channel = channelIndex == 0 ? viewModel.Channel1 : viewModel.Channel2;
        if ((uint)slotIndex >= (uint)channel.PluginSlots.Count)
        {
            return;
        }

        channel.PluginSlots[slotIndex].ActionCommand.Execute(null);
    }

    private void StartKnobDrag(MainViewModel viewModel, KnobHit hit, float startY)
    {
        float startValue = hit.KnobType == KnobType.InputGain
            ? GetChannel(viewModel, hit.ChannelIndex).InputGainDb
            : GetChannel(viewModel, hit.ChannelIndex).OutputGainDb;

        _uiState.KnobDrag = new KnobDragState(hit.ChannelIndex, hit.KnobType, startValue, startY);
    }

    private static void UpdateKnobDrag(MainViewModel viewModel, KnobDragState drag, float currentY)
    {
        float delta = drag.StartY - currentY;
        float min = -60f;
        float max = 12f;
        float range = max - min;
        float sensitivity = range / 150f;
        float nextValue = drag.StartValue + delta * sensitivity;
        nextValue = Math.Clamp(nextValue, min, max);

        var channel = GetChannel(viewModel, drag.ChannelIndex);
        if (drag.KnobType == KnobType.InputGain)
        {
            channel.InputGainDb = nextValue;
        }
        else
        {
            channel.OutputGainDb = nextValue;
        }
    }

    private static void ToggleMuteSolo(MainViewModel viewModel, ToggleHit toggle)
    {
        var channel = GetChannel(viewModel, toggle.ChannelIndex);
        if (toggle.ToggleType == ToggleType.Mute)
        {
            channel.IsMuted = !channel.IsMuted;
        }
        else
        {
            channel.IsSoloed = !channel.IsSoloed;
        }
    }

    private static ChannelStripViewModel GetChannel(MainViewModel viewModel, int channelIndex)
    {
        return channelIndex == 0 ? viewModel.Channel1 : viewModel.Channel2;
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
}
