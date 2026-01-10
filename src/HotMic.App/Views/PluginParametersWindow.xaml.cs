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

public partial class PluginParametersWindow : Window
{
    private readonly PluginParametersRenderer _renderer = new();
    private readonly DispatcherTimer _renderTimer;
    private float _scrollOffset;
    private int _activeParameterIndex = -1;

    public PluginParametersWindow(PluginParametersViewModel viewModel)
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
        if (DataContext is not PluginParametersViewModel viewModel)
        {
            return;
        }

        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();
        _renderer.Render(canvas, size, viewModel, dpiScale, _scrollOffset);
    }

    private void SkiaCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not PluginParametersViewModel viewModel || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        if (_renderer.HitTestTitleBar(x, y))
        {
            DragMove();
            e.Handled = true;
            return;
        }

        if (_renderer.HitTestClose(x, y))
        {
            Close();
            e.Handled = true;
            return;
        }

        if (_renderer.HitTestLearn(x, y))
        {
            viewModel.LearnNoiseAction?.Invoke();
            e.Handled = true;
            return;
        }

        int index = _renderer.HitTestParameter(x, y);
        if (index >= 0)
        {
            _activeParameterIndex = index;
            UpdateParameterValue(viewModel, index, x);
            SkiaCanvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void SkiaCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (DataContext is not PluginParametersViewModel viewModel || _activeParameterIndex < 0 || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var pos = e.GetPosition(SkiaCanvas);
        UpdateParameterValue(viewModel, _activeParameterIndex, (float)pos.X);
        e.Handled = true;
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _activeParameterIndex = -1;
        SkiaCanvas.ReleaseMouseCapture();
    }

    private void SkiaCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        float maxScroll = MathF.Max(0f, _renderer.ListContentHeight - _renderer.ListViewportHeight);
        float next = _scrollOffset - e.Delta / 4f;
        _scrollOffset = Math.Clamp(next, 0f, maxScroll);
        e.Handled = true;
    }

    private void UpdateParameterValue(PluginParametersViewModel viewModel, int index, float x)
    {
        if ((uint)index >= (uint)viewModel.Parameters.Count)
        {
            return;
        }

        var rect = _renderer.GetSliderRect(index);
        if (rect.IsEmpty)
        {
            return;
        }

        float normalized = (x - rect.Left) / MathF.Max(1f, rect.Width);
        normalized = Math.Clamp(normalized, 0f, 1f);

        var parameter = viewModel.Parameters[index];
        float value = parameter.Min + normalized * (parameter.Max - parameter.Min);
        parameter.Value = value;
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
