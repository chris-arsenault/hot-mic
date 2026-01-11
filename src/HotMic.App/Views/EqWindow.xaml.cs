using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using HotMic.App.UI.PluginComponents;
using HotMic.Core.Plugins.BuiltIn;
using SkiaSharp;
using SkiaSharp.Views.WPF;

namespace HotMic.App.Views;

public partial class EqWindow : Window
{
    private readonly EqRenderer _renderer = new();
    private readonly ThreeBandEqPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;

    private int _activeKnob = -1;
    private float _dragStartY;
    private float _dragStartValue;
    private int _hoveredKnob = -1;
    private float _smoothedInputLevel;
    private float _smoothedOutputLevel;

    // Pre-allocated spectrum buffers to avoid allocations during rendering
    private readonly float[] _spectrumLevels = new float[ThreeBandEqPlugin.SpectrumBins];
    private readonly float[] _spectrumPeaks = new float[ThreeBandEqPlugin.SpectrumBins];

    // Knob parameter mapping: index -> (paramIndex, minValue, maxValue, isLogScale)
    private static readonly (int paramIndex, float min, float max, bool log)[] KnobParams =
    {
        (ThreeBandEqPlugin.LowGainIndex, -24f, 24f, false),    // 0: Low Gain
        (ThreeBandEqPlugin.LowFreqIndex, 20f, 500f, true),     // 1: Low Freq
        (ThreeBandEqPlugin.MidGainIndex, -24f, 24f, false),    // 2: Mid Gain
        (ThreeBandEqPlugin.MidFreqIndex, 200f, 5000f, true),   // 3: Mid Freq
        (ThreeBandEqPlugin.HighGainIndex, -24f, 24f, false),   // 4: High Gain
        (ThreeBandEqPlugin.HighFreqIndex, 2000f, 20000f, true) // 5: High Freq
    };

    public EqWindow(ThreeBandEqPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
    {
        InitializeComponent();
        _plugin = plugin;
        _parameterCallback = parameterCallback;
        _bypassCallback = bypassCallback;

        var preferredSize = EqRenderer.GetPreferredSize();
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
        float rawInput = _plugin.GetAndResetInputLevel();
        float rawOutput = _plugin.GetAndResetOutputLevel();
        _smoothedInputLevel = _smoothedInputLevel * 0.7f + rawInput * 0.3f;
        _smoothedOutputLevel = _smoothedOutputLevel * 0.7f + rawOutput * 0.3f;

        // Get spectrum data
        _plugin.GetSpectrum(_spectrumLevels, _spectrumPeaks);

        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var state = new EqState(
            LowGainDb: _plugin.LowGainDb,
            LowFreq: _plugin.LowFreq,
            LowQ: _plugin.LowQ,
            MidGainDb: _plugin.MidGainDb,
            MidFreq: _plugin.MidFreq,
            MidQ: _plugin.MidQ,
            HighGainDb: _plugin.HighGainDb,
            HighFreq: _plugin.HighFreq,
            HighQ: _plugin.HighQ,
            InputLevel: _smoothedInputLevel,
            OutputLevel: _smoothedOutputLevel,
            SampleRate: _plugin.SampleRate,
            IsBypassed: _plugin.IsBypassed,
            SpectrumLevels: _spectrumLevels,
            SpectrumPeaks: _spectrumPeaks,
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
            case EqHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;

            case EqHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;

            case EqHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;

            case EqHitArea.Knob:
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
            float newNormalized = RotaryKnob.CalculateValueFromDrag(_dragStartValue, -deltaY, 0.004f);
            ApplyKnobValue(_activeKnob, newNormalized);
            e.Handled = true;
        }
        else
        {
            var hit = _renderer.HitTest(x, y);
            _hoveredKnob = hit.Area == EqHitArea.Knob ? hit.KnobIndex : -1;
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
        if (knobIndex < 0 || knobIndex >= KnobParams.Length)
            return 0f;

        var (paramIndex, min, max, log) = KnobParams[knobIndex];
        float value = GetPluginParamValue(paramIndex);

        if (log)
        {
            return MathF.Log(value / min) / MathF.Log(max / min);
        }
        return (value - min) / (max - min);
    }

    private void ApplyKnobValue(int knobIndex, float normalizedValue)
    {
        if (knobIndex < 0 || knobIndex >= KnobParams.Length)
            return;

        normalizedValue = Math.Clamp(normalizedValue, 0f, 1f);
        var (paramIndex, min, max, log) = KnobParams[knobIndex];

        float value;
        if (log)
        {
            value = min * MathF.Pow(max / min, normalizedValue);
        }
        else
        {
            value = min + normalizedValue * (max - min);
        }

        _parameterCallback(paramIndex, value);
    }

    private float GetPluginParamValue(int paramIndex)
    {
        return paramIndex switch
        {
            ThreeBandEqPlugin.LowGainIndex => _plugin.LowGainDb,
            ThreeBandEqPlugin.LowFreqIndex => _plugin.LowFreq,
            ThreeBandEqPlugin.LowQIndex => _plugin.LowQ,
            ThreeBandEqPlugin.MidGainIndex => _plugin.MidGainDb,
            ThreeBandEqPlugin.MidFreqIndex => _plugin.MidFreq,
            ThreeBandEqPlugin.MidQIndex => _plugin.MidQ,
            ThreeBandEqPlugin.HighGainIndex => _plugin.HighGainDb,
            ThreeBandEqPlugin.HighFreqIndex => _plugin.HighFreq,
            ThreeBandEqPlugin.HighQIndex => _plugin.HighQ,
            _ => 0f
        };
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
