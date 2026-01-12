using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HotMic.App.UI.PluginComponents;
using HotMic.Core.Plugins.BuiltIn;
using SkiaSharp;
using SkiaSharp.Views.WPF;

namespace HotMic.App.Views;

public partial class RNNoiseWindow : Window
{
    private readonly RNNoiseRenderer _renderer = new();
    private readonly RNNoisePlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;

    private int _activeKnob = -1;
    private float _dragStartY;
    private float _dragStartValue;
    private int _hoveredKnob = -1;

    public RNNoiseWindow(RNNoisePlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
    {
        InitializeComponent();
        _plugin = plugin;
        _parameterCallback = parameterCallback;
        _bypassCallback = bypassCallback;

        var preferredSize = RNNoiseRenderer.GetPreferredSize();
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

        float sampleRate = 48000f; // RNNoise requires 48kHz
        float latencyMs = _plugin.LatencySamples > 0 ? _plugin.LatencySamples * 1000f / sampleRate : 0f;

        var state = new RNNoiseState(
            ReductionPercent: GetParameterValue(RNNoisePlugin.ReductionIndex),
            VadThreshold: GetParameterValue(RNNoisePlugin.VadThresholdIndex),
            VadProbability: _plugin.VadProbability,
            LatencyMs: latencyMs,
            IsBypassed: _plugin.IsBypassed,
            StatusMessage: _plugin.StatusMessage,
            HoveredKnob: _hoveredKnob
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
            case RNNoiseHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;

            case RNNoiseHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;

            case RNNoiseHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;

            case RNNoiseHitArea.Knob:
                _activeKnob = hit.KnobIndex;
                _dragStartY = y;
                _dragStartValue = GetKnobNormalizedValue(hit.KnobIndex);
                SkiaCanvas.CaptureMouse();
                e.Handled = true;
                break;
        }
    }

    private void SkiaCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        if (_activeKnob >= 0 && e.LeftButton == MouseButtonState.Pressed)
        {
            float deltaY = _dragStartY - y;
            float newNormalized = RotaryKnob.CalculateValueFromDrag(_dragStartValue, -deltaY, 0.003f);
            ApplyKnobValue(_activeKnob, newNormalized);
            e.Handled = true;
        }
        else
        {
            var hit = _renderer.HitTest(x, y);
            _hoveredKnob = hit.Area == RNNoiseHitArea.Knob ? hit.KnobIndex : -1;
        }
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        _activeKnob = -1;
        SkiaCanvas.ReleaseMouseCapture();
    }

    private float GetParameterValue(int index)
    {
        var state = _plugin.GetState();
        if (state.Length >= (index + 1) * sizeof(float))
        {
            return BitConverter.ToSingle(state, index * sizeof(float));
        }
        return _plugin.Parameters[index].DefaultValue;
    }

    private float GetKnobNormalizedValue(int knobIndex)
    {
        return knobIndex switch
        {
            0 => GetParameterValue(RNNoisePlugin.ReductionIndex) / 100f,
            1 => GetParameterValue(RNNoisePlugin.VadThresholdIndex) / 100f,
            _ => 0f
        };
    }

    private void ApplyKnobValue(int knobIndex, float normalizedValue)
    {
        float value = knobIndex switch
        {
            0 => normalizedValue * 100f,  // Reduction: 0 to 100%
            1 => normalizedValue * 100f,  // VAD Threshold: 0 to 100%
            _ => 0f
        };

        int paramIndex = knobIndex switch
        {
            0 => RNNoisePlugin.ReductionIndex,
            1 => RNNoisePlugin.VadThresholdIndex,
            _ => -1
        };

        if (paramIndex >= 0)
        {
            _parameterCallback(paramIndex, value);
        }
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
