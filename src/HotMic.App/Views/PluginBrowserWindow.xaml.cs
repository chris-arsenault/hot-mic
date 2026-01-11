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

public partial class PluginBrowserWindow : Window
{
    private readonly PluginBrowserRenderer _renderer = new();
    private readonly DispatcherTimer _renderTimer;
    private float _scrollOffset;

    public PluginBrowserWindow(PluginBrowserViewModel viewModel)
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
        if (DataContext is not PluginBrowserViewModel viewModel)
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
        if (DataContext is not PluginBrowserViewModel viewModel || e.ChangedButton != MouseButton.Left)
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

        if (_renderer.HitTestAdd(x, y))
        {
            if (viewModel.SelectedChoice is not null)
            {
                DialogResult = true;
                Close();
            }
            e.Handled = true;
            return;
        }

        if (_renderer.HitTestCancel(x, y))
        {
            DialogResult = false;
            Close();
            e.Handled = true;
            return;
        }

        int index = _renderer.HitTestItem(x, y);
        if (index >= 0 && index < viewModel.Choices.Count)
        {
            viewModel.SelectedChoice = viewModel.Choices[index];
            e.Handled = true;
        }
    }

    private void SkiaCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        float maxScroll = MathF.Max(0f, _renderer.ListContentHeight - _renderer.ListViewportHeight);
        float next = _scrollOffset - e.Delta / 4f;
        _scrollOffset = Math.Clamp(next, 0f, maxScroll);
        e.Handled = true;
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
