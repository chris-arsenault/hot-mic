using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HotMic.App.UI.PluginComponents;
using HotMic.Common.Configuration;
using SkiaSharp;
using SkiaSharp.Views.WPF;

namespace HotMic.App.Views;

public partial class InputSourceWindow : Window
{
    private readonly InputSourceRenderer _renderer = new();
    private readonly IReadOnlyList<InputSourceDevice> _devices;
    private readonly Func<string> _getSelectedDeviceId;
    private readonly Func<InputChannelMode> _getChannelMode;
    private readonly Func<float> _getInputGainDb;
    private readonly Func<float> _getInputLevel;
    private readonly Func<bool> _getIsBypassed;
    private readonly Action<string> _setDeviceCallback;
    private readonly Action<InputChannelMode> _setModeCallback;
    private readonly Action<float> _setGainCallback;
    private readonly Action<bool> _setBypassCallback;
    private readonly DispatcherTimer _renderTimer;

    public InputSourceWindow(
        IReadOnlyList<InputSourceDevice> devices,
        Func<string> getSelectedDeviceId,
        Func<InputChannelMode> getChannelMode,
        Func<float> getInputGainDb,
        Func<float> getInputLevel,
        Func<bool> getIsBypassed,
        Action<string> setDeviceCallback,
        Action<InputChannelMode> setModeCallback,
        Action<float> setGainCallback,
        Action<bool> setBypassCallback)
    {
        InitializeComponent();

        _devices = devices;
        _getSelectedDeviceId = getSelectedDeviceId;
        _getChannelMode = getChannelMode;
        _getInputGainDb = getInputGainDb;
        _getInputLevel = getInputLevel;
        _getIsBypassed = getIsBypassed;
        _setDeviceCallback = setDeviceCallback;
        _setModeCallback = setModeCallback;
        _setGainCallback = setGainCallback;
        _setBypassCallback = setBypassCallback;

        _renderer.GainKnob.ValueChanged += value => _setGainCallback(value);

        var preferredSize = InputSourceRenderer.GetPreferredSize();
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
        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var channelMode = _getChannelMode();
        var modeValue = channelMode switch
        {
            InputChannelMode.Left => InputChannelModeValue.Left,
            InputChannelMode.Right => InputChannelModeValue.Right,
            _ => InputChannelModeValue.Sum
        };

        var state = new InputSourceState(
            Devices: _devices,
            SelectedDeviceId: _getSelectedDeviceId(),
            ChannelMode: modeValue,
            GainDb: _getInputGainDb(),
            InputLevel: _getInputLevel(),
            IsBypassed: _getIsBypassed()
        );

        _renderer.Render(canvas, size, dpiScale, state);
    }

    private void SkiaCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        if (_renderer.GainKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.GainKnob.IsDragging)
                SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
            return;

        var hit = _renderer.HitTest(x, y);

        switch (hit.Area)
        {
            case InputSourceHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;

            case InputSourceHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;

            case InputSourceHitArea.BypassButton:
                _setBypassCallback(!_getIsBypassed());
                e.Handled = true;
                break;

            case InputSourceHitArea.ModeLeft:
                _setModeCallback(InputChannelMode.Left);
                e.Handled = true;
                break;

            case InputSourceHitArea.ModeRight:
                _setModeCallback(InputChannelMode.Right);
                e.Handled = true;
                break;

            case InputSourceHitArea.ModeSum:
                _setModeCallback(InputChannelMode.Sum);
                e.Handled = true;
                break;

            case InputSourceHitArea.DeviceItem:
                if (hit.DeviceIndex >= 0 && hit.DeviceIndex < _devices.Count)
                {
                    _setDeviceCallback(_devices[hit.DeviceIndex].Id);
                }
                e.Handled = true;
                break;
        }
    }

    private void SkiaCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        _renderer.GainKnob.HandleMouseMove(x, y, e.LeftButton == MouseButtonState.Pressed);
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _renderer.GainKnob.HandleMouseUp(e.ChangedButton);

        if (e.ChangedButton == MouseButton.Left)
            SkiaCanvas.ReleaseMouseCapture();
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
