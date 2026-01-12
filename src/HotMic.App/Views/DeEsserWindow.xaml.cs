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

public partial class DeEsserWindow : Window
{
    private readonly DeEsserRenderer _renderer = new();
    private readonly DeEsserPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;

    private DeEsserKnob _activeKnob = DeEsserKnob.None;
    private float _dragStartY;
    private float _dragStartValue;
    private DeEsserKnob _hoveredKnob = DeEsserKnob.None;
    private float _smoothedInputLevel;
    private float _smoothedSibilanceLevel;
    private float _smoothedGainReduction;
    private readonly float[] _spectrum = new float[DeEsserPlugin.SpectrumBins];

    public DeEsserWindow(DeEsserPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
    {
        InitializeComponent();
        _plugin = plugin;
        _parameterCallback = parameterCallback;
        _bypassCallback = bypassCallback;

        var preferredSize = DeEsserRenderer.GetPreferredSize();
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
        float rawSib = _plugin.GetAndResetSibilanceLevel();
        float rawGr = _plugin.GetGainReductionDb();

        _smoothedInputLevel = _smoothedInputLevel * 0.7f + rawInput * 0.3f;
        _smoothedSibilanceLevel = _smoothedSibilanceLevel * 0.7f + rawSib * 0.3f;
        _smoothedGainReduction = _smoothedGainReduction * 0.8f + rawGr * 0.2f;

        _plugin.GetSpectrum(_spectrum);

        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var state = new DeEsserState(
            CenterHz: _plugin.CenterHz,
            BandwidthHz: _plugin.BandwidthHz,
            ThresholdDb: _plugin.ThresholdDb,
            ReductionDb: _plugin.ReductionDb,
            MaxRangeDb: _plugin.MaxRangeDb,
            InputLevel: _smoothedInputLevel,
            SibilanceLevel: _smoothedSibilanceLevel,
            GainReductionDb: _smoothedGainReduction,
            LatencyMs: _plugin.SampleRate > 0 ? _plugin.LatencySamples * 1000f / _plugin.SampleRate : 0f,
            IsBypassed: _plugin.IsBypassed,
            HoveredKnob: _hoveredKnob,
            Spectrum: _spectrum
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
            case DeEsserHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;

            case DeEsserHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;

            case DeEsserHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;

            case DeEsserHitArea.Knob:
                _activeKnob = hit.Knob;
                _dragStartY = y;
                _dragStartValue = GetKnobNormalizedValue(hit.Knob);
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

        if (_activeKnob != DeEsserKnob.None && e.LeftButton == MouseButtonState.Pressed)
        {
            float deltaY = _dragStartY - y;
            float newNormalized = RotaryKnob.CalculateValueFromDrag(_dragStartValue, -deltaY, 0.004f);
            ApplyKnobValue(_activeKnob, newNormalized);
            e.Handled = true;
        }
        else
        {
            var hit = _renderer.HitTest(x, y);
            _hoveredKnob = hit.Area == DeEsserHitArea.Knob ? hit.Knob : DeEsserKnob.None;
        }
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        _activeKnob = DeEsserKnob.None;
        SkiaCanvas.ReleaseMouseCapture();
    }

    private float GetKnobNormalizedValue(DeEsserKnob knob) => knob switch
    {
        DeEsserKnob.CenterFreq => (MathF.Log10(_plugin.CenterHz) - MathF.Log10(4000f)) / (MathF.Log10(9000f) - MathF.Log10(4000f)),
        DeEsserKnob.Bandwidth => (_plugin.BandwidthHz - 1000f) / 3000f,
        DeEsserKnob.Threshold => (_plugin.ThresholdDb + 40f) / 40f,
        DeEsserKnob.Reduction => _plugin.ReductionDb / 12f,
        DeEsserKnob.MaxRange => _plugin.MaxRangeDb / 20f,
        _ => 0f
    };

    private void ApplyKnobValue(DeEsserKnob knob, float normalizedValue)
    {
        switch (knob)
        {
            case DeEsserKnob.CenterFreq:
                // Log scale: 4kHz to 9kHz
                float logMin = MathF.Log10(4000f);
                float logMax = MathF.Log10(9000f);
                float centerHz = MathF.Pow(10, logMin + normalizedValue * (logMax - logMin));
                _parameterCallback(DeEsserPlugin.CenterFreqIndex, centerHz);
                break;

            case DeEsserKnob.Bandwidth:
                float bandwidthHz = 1000f + normalizedValue * 3000f;
                _parameterCallback(DeEsserPlugin.BandwidthIndex, bandwidthHz);
                break;

            case DeEsserKnob.Threshold:
                float thresholdDb = -40f + normalizedValue * 40f;
                _parameterCallback(DeEsserPlugin.ThresholdIndex, thresholdDb);
                break;

            case DeEsserKnob.Reduction:
                float reductionDb = normalizedValue * 12f;
                _parameterCallback(DeEsserPlugin.ReductionIndex, reductionDb);
                break;

            case DeEsserKnob.MaxRange:
                float maxRangeDb = normalizedValue * 20f;
                _parameterCallback(DeEsserPlugin.MaxRangeIndex, maxRangeDb);
                break;
        }
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
