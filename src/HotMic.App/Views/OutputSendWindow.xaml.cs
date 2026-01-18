using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HotMic.App.UI.PluginComponents;
using HotMic.Core.Engine;
using HotMic.Core.Plugins.BuiltIn;
using SkiaSharp;
using SkiaSharp.Views.WPF;

namespace HotMic.App.Views;

public partial class OutputSendWindow : Window
{
    private readonly OutputSendRenderer _renderer = new();
    private readonly OutputSendPlugin _plugin;
    private readonly string _outputDeviceName;
    private readonly Func<float> _getOutputLevel;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;

    private float _smoothedOutputLevel;

    public OutputSendWindow(
        OutputSendPlugin plugin,
        string outputDeviceName,
        Func<float> getOutputLevel,
        Action<int, float> parameterCallback,
        Action<bool> bypassCallback)
    {
        InitializeComponent();

        _plugin = plugin;
        _outputDeviceName = outputDeviceName;
        _getOutputLevel = getOutputLevel;
        _parameterCallback = parameterCallback;
        _bypassCallback = bypassCallback;

        var preferredSize = OutputSendRenderer.GetPreferredSize();
        Width = preferredSize.Width;
        Height = preferredSize.Height;

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += OnRenderTick;
        Loaded += (_, _) => _renderTimer.Start();
        Closed += (_, _) =>
        {
            _renderTimer.Stop();
            _renderer.Dispose();
        };
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        float rawOutput = _getOutputLevel();
        _smoothedOutputLevel = _smoothedOutputLevel * 0.7f + rawOutput * 0.3f;
        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var state = new OutputSendState(
            OutputDeviceName: _outputDeviceName,
            SendMode: _plugin.Mode,
            OutputLevel: _smoothedOutputLevel,
            IsBypassed: _plugin.IsBypassed
        );

        _renderer.Render(canvas, size, dpiScale, state);
    }

    private void SkiaCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        var hit = _renderer.HitTest(x, y);

        switch (hit.Area)
        {
            case OutputSendHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;

            case OutputSendHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;

            case OutputSendHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;

            case OutputSendHitArea.ModeLeft:
                _parameterCallback(0, (float)OutputSendMode.Left);
                e.Handled = true;
                break;

            case OutputSendHitArea.ModeRight:
                _parameterCallback(0, (float)OutputSendMode.Right);
                e.Handled = true;
                break;

            case OutputSendHitArea.ModeBoth:
                _parameterCallback(0, (float)OutputSendMode.Both);
                e.Handled = true;
                break;
        }
    }

    private void SkiaCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        // No drag interactions in this window
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        // No drag interactions in this window
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
