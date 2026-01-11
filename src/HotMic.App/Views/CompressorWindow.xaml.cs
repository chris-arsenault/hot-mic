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

public partial class CompressorWindow : Window
{
    private readonly CompressorRenderer _renderer = new();
    private readonly CompressorPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;

    private int _activeKnob = -1;
    private float _dragStartY;
    private float _dragStartValue;
    private int _hoveredKnob = -1;
    private float _smoothedInputLevel;

    public CompressorWindow(CompressorPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
    {
        InitializeComponent();
        _plugin = plugin;
        _parameterCallback = parameterCallback;
        _bypassCallback = bypassCallback;

        var preferredSize = CompressorRenderer.GetPreferredSize();
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
        // Smooth the input level for display
        float rawLevel = _plugin.GetAndResetInputLevel();
        _smoothedInputLevel = _smoothedInputLevel * 0.7f + rawLevel * 0.3f;

        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var state = new CompressorState(
            ThresholdDb: _plugin.ThresholdDb,
            Ratio: _plugin.Ratio,
            AttackMs: _plugin.AttackMs,
            ReleaseMs: _plugin.ReleaseMs,
            MakeupDb: _plugin.MakeupDb,
            GainReductionDb: _plugin.GetGainReductionDb(),
            InputLevel: _smoothedInputLevel,
            LatencyMs: _plugin.SampleRate > 0 ? _plugin.LatencySamples * 1000f / _plugin.SampleRate : 0f,
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
                _bypassCallback(!_plugin.IsBypassed);
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
            0 => (_plugin.ThresholdDb - (-60f)) / (0f - (-60f)),
            1 => (_plugin.Ratio - 1f) / (20f - 1f),
            2 => (_plugin.AttackMs - 0.1f) / (100f - 0.1f),
            3 => (_plugin.ReleaseMs - 10f) / (1000f - 10f),
            4 => _plugin.MakeupDb / 24f,
            _ => 0f
        };
    }

    private void ApplyKnobValue(int knobIndex, float normalizedValue)
    {
        float value = knobIndex switch
        {
            0 => -60f + normalizedValue * 60f,             // Threshold: -60 to 0 dB
            1 => 1f + normalizedValue * 19f,               // Ratio: 1:1 to 20:1
            2 => 0.1f + normalizedValue * 99.9f,           // Attack: 0.1 to 100 ms
            3 => 10f + normalizedValue * 990f,             // Release: 10 to 1000 ms
            4 => normalizedValue * 24f,                     // Makeup: 0 to 24 dB
            _ => 0f
        };

        int paramIndex = knobIndex switch
        {
            0 => CompressorPlugin.ThresholdIndex,
            1 => CompressorPlugin.RatioIndex,
            2 => CompressorPlugin.AttackIndex,
            3 => CompressorPlugin.ReleaseIndex,
            4 => CompressorPlugin.MakeupIndex,
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
