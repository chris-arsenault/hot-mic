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
    private readonly FiveBandEqPlugin _plugin;
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
    private readonly float[] _spectrumLevels = new float[FiveBandEqPlugin.SpectrumBins];
    private readonly float[] _spectrumPeaks = new float[FiveBandEqPlugin.SpectrumBins];

    // Knob parameter mapping: index -> (paramIndex, minValue, maxValue, isLogScale)
    private static readonly (int paramIndex, float min, float max, bool log)[] KnobParams =
    {
        (FiveBandEqPlugin.HpfFreqIndex, 40f, 200f, true),           // 0: HPF Freq
        (FiveBandEqPlugin.LowShelfGainIndex, -24f, 24f, false),     // 1: Low Shelf Gain
        (FiveBandEqPlugin.LowShelfFreqIndex, 60f, 300f, true),      // 2: Low Shelf Freq
        (FiveBandEqPlugin.Mid1GainIndex, -24f, 24f, false),         // 3: Low-Mid Gain
        (FiveBandEqPlugin.Mid1FreqIndex, 150f, 800f, true),         // 4: Low-Mid Freq
        (FiveBandEqPlugin.Mid2GainIndex, -24f, 24f, false),         // 5: High-Mid Gain
        (FiveBandEqPlugin.Mid2FreqIndex, 1000f, 6000f, true),       // 6: High-Mid Freq
        (FiveBandEqPlugin.HighShelfGainIndex, -24f, 24f, false),    // 7: High Shelf Gain
        (FiveBandEqPlugin.HighShelfFreqIndex, 6000f, 16000f, true)  // 8: High Shelf Freq
    };

    public EqWindow(FiveBandEqPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
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
            HpfFreq: _plugin.HpfFreq,
            LowShelfGainDb: _plugin.LowShelfGainDb,
            LowShelfFreq: _plugin.LowShelfFreq,
            Mid1GainDb: _plugin.Mid1GainDb,
            Mid1Freq: _plugin.Mid1Freq,
            Mid1Q: _plugin.Mid1Q,
            Mid2GainDb: _plugin.Mid2GainDb,
            Mid2Freq: _plugin.Mid2Freq,
            Mid2Q: _plugin.Mid2Q,
            HighShelfGainDb: _plugin.HighShelfGainDb,
            HighShelfFreq: _plugin.HighShelfFreq,
            InputLevel: _smoothedInputLevel,
            OutputLevel: _smoothedOutputLevel,
            SampleRate: _plugin.SampleRate,
            LatencyMs: _plugin.SampleRate > 0 ? _plugin.LatencySamples * 1000f / _plugin.SampleRate : 0f,
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
            FiveBandEqPlugin.HpfFreqIndex => _plugin.HpfFreq,
            FiveBandEqPlugin.LowShelfGainIndex => _plugin.LowShelfGainDb,
            FiveBandEqPlugin.LowShelfFreqIndex => _plugin.LowShelfFreq,
            FiveBandEqPlugin.Mid1GainIndex => _plugin.Mid1GainDb,
            FiveBandEqPlugin.Mid1FreqIndex => _plugin.Mid1Freq,
            FiveBandEqPlugin.Mid1QIndex => _plugin.Mid1Q,
            FiveBandEqPlugin.Mid2GainIndex => _plugin.Mid2GainDb,
            FiveBandEqPlugin.Mid2FreqIndex => _plugin.Mid2Freq,
            FiveBandEqPlugin.Mid2QIndex => _plugin.Mid2Q,
            FiveBandEqPlugin.HighShelfGainIndex => _plugin.HighShelfGainDb,
            FiveBandEqPlugin.HighShelfFreqIndex => _plugin.HighShelfFreq,
            _ => 0f
        };
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
