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

public partial class HighPassFilterWindow : Window
{
    private readonly HighPassFilterRenderer _renderer = new();
    private readonly HighPassFilterPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;

    private bool _isKnobActive;
    private float _dragStartY;
    private float _dragStartValue;
    private HpfElement _hoveredElement = HpfElement.None;
    private float _smoothedInputLevel;
    private float _smoothedOutputLevel;
    private readonly float[] _spectrum = new float[32];

    public HighPassFilterWindow(HighPassFilterPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
    {
        InitializeComponent();
        _plugin = plugin;
        _parameterCallback = parameterCallback;
        _bypassCallback = bypassCallback;

        var preferredSize = HighPassFilterRenderer.GetPreferredSize();
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

        _plugin.GetSpectrum(_spectrum);

        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var state = new HighPassFilterState(
            CutoffHz: _plugin.CutoffHz,
            SlopeDbOct: _plugin.SlopeDbOct,
            InputLevel: _smoothedInputLevel,
            OutputLevel: _smoothedOutputLevel,
            LatencyMs: _plugin.SampleRate > 0 ? _plugin.LatencySamples * 1000f / _plugin.SampleRate : 0f,
            IsBypassed: _plugin.IsBypassed,
            HoveredElement: _hoveredElement,
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
            case HpfHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;

            case HpfHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;

            case HpfHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;

            case HpfHitArea.SlopeButton:
                float newSlope = hit.Element == HpfElement.Slope18 ? 18f : 12f;
                _parameterCallback(HighPassFilterPlugin.SlopeIndex, newSlope);
                e.Handled = true;
                break;

            case HpfHitArea.Knob:
                _isKnobActive = true;
                _dragStartY = y;
                _dragStartValue = GetKnobNormalizedValue();
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

        if (_isKnobActive && e.LeftButton == MouseButtonState.Pressed)
        {
            float deltaY = _dragStartY - y;
            float newNormalized = RotaryKnob.CalculateValueFromDrag(_dragStartValue, -deltaY, 0.004f);
            ApplyKnobValue(newNormalized);
            e.Handled = true;
        }
        else
        {
            var hit = _renderer.HitTest(x, y);
            _hoveredElement = hit.Area == HpfHitArea.Knob ? HpfElement.CutoffKnob : HpfElement.None;
        }
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        _isKnobActive = false;
        SkiaCanvas.ReleaseMouseCapture();
    }

    private float GetKnobNormalizedValue()
    {
        // Log scale: 40Hz to 200Hz
        return (MathF.Log10(_plugin.CutoffHz) - MathF.Log10(40f)) / (MathF.Log10(200f) - MathF.Log10(40f));
    }

    private void ApplyKnobValue(float normalizedValue)
    {
        // Log scale: 40Hz to 200Hz
        float logMin = MathF.Log10(40f);
        float logMax = MathF.Log10(200f);
        float cutoffHz = MathF.Pow(10, logMin + normalizedValue * (logMax - logMin));
        _parameterCallback(HighPassFilterPlugin.CutoffIndex, cutoffHz);
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
