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

public partial class NoiseGateWindow : Window
{
    private readonly NoiseGateRenderer _renderer = new();
    private readonly NoiseGatePlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly DispatcherTimer _renderTimer;

    private int _activeKnob = -1;
    private float _dragStartY;
    private float _dragStartValue;
    private int _hoveredKnob = -1;

    public NoiseGateWindow(NoiseGatePlugin plugin, Action<int, float> parameterCallback)
    {
        InitializeComponent();
        _plugin = plugin;
        _parameterCallback = parameterCallback;

        var preferredSize = NoiseGateRenderer.GetPreferredSize();
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
        // Update waveform buffer with current input level
        float inputLevel = _plugin.GetAndResetInputLevel();
        bool gateOpen = _plugin.IsGateOpen();
        _renderer.WaveformBuffer.Push(inputLevel, gateOpen);

        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var state = new NoiseGateState(
            ThresholdDb: _plugin.ThresholdDb,
            HysteresisDb: _plugin.HysteresisDb,
            AttackMs: _plugin.AttackMs,
            HoldMs: _plugin.HoldMs,
            ReleaseMs: _plugin.ReleaseMs,
            IsGateOpen: _plugin.IsGateOpen(),
            IsBypassed: _plugin.IsBypassed,
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
            case NoiseGateHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;

            case NoiseGateHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;

            case NoiseGateHitArea.BypassButton:
                _plugin.IsBypassed = !_plugin.IsBypassed;
                e.Handled = true;
                break;

            case NoiseGateHitArea.Knob:
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
            _hoveredKnob = hit.Area == NoiseGateHitArea.Knob ? hit.KnobIndex : -1;
        }
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        _activeKnob = -1;
        SkiaCanvas.ReleaseMouseCapture();
    }

    private float GetKnobNormalizedValue(int knobIndex)
    {
        return knobIndex switch
        {
            0 => (_plugin.ThresholdDb - (-80f)) / (0f - (-80f)),
            1 => _plugin.HysteresisDb / 12f,
            2 => (_plugin.AttackMs - 0.1f) / (50f - 0.1f),
            3 => _plugin.HoldMs / 500f,
            4 => (_plugin.ReleaseMs - 10f) / (500f - 10f),
            _ => 0f
        };
    }

    private void ApplyKnobValue(int knobIndex, float normalizedValue)
    {
        float value = knobIndex switch
        {
            0 => -80f + normalizedValue * 80f,            // Threshold: -80 to 0 dB
            1 => normalizedValue * 12f,                    // Hysteresis: 0 to 12 dB
            2 => 0.1f + normalizedValue * 49.9f,          // Attack: 0.1 to 50 ms
            3 => normalizedValue * 500f,                   // Hold: 0 to 500 ms
            4 => 10f + normalizedValue * 490f,            // Release: 10 to 500 ms
            _ => 0f
        };

        int paramIndex = knobIndex switch
        {
            0 => NoiseGatePlugin.ThresholdIndex,
            1 => NoiseGatePlugin.HysteresisIndex,
            2 => NoiseGatePlugin.AttackIndex,
            3 => NoiseGatePlugin.HoldIndex,
            4 => NoiseGatePlugin.ReleaseIndex,
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
