using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HotMic.App.UI.PluginComponents;
using HotMic.Core.Dsp;
using HotMic.Core.Plugins.BuiltIn;
using SkiaSharp;
using SkiaSharp.Views.WPF;

namespace HotMic.App.Views;

public partial class FrequencyAnalyzerWindow : Window
{
    private readonly FrequencyAnalyzerRenderer _renderer = new(PluginComponentTheme.BlueOnBlack);
    private readonly FrequencyAnalyzerPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;

    private int _activeKnob = -1;
    private float _dragStartY;
    private float _dragStartValue;
    private int _hoveredKnob = -1;

    private float[] _spectrum = Array.Empty<float>();

    private static readonly int[] FftSizes = { 1024, 2048, 4096, 8192 };
    private static readonly int[] DisplayBins = { 32, 64, 128, 256 };
    private static readonly FrequencyScale[] Scales =
    {
        FrequencyScale.Linear,
        FrequencyScale.Logarithmic,
        FrequencyScale.Mel,
        FrequencyScale.Erb,
        FrequencyScale.Bark
    };

    private static readonly (int paramIndex, float min, float max)[] KnobParams =
    {
        (FrequencyAnalyzerPlugin.MinFrequencyIndex, 20f, 2000f),
        (FrequencyAnalyzerPlugin.MaxFrequencyIndex, 2000f, 12000f),
        (FrequencyAnalyzerPlugin.MinDbIndex, -120f, -20f),
        (FrequencyAnalyzerPlugin.MaxDbIndex, -40f, 0f)
    };

    public FrequencyAnalyzerWindow(FrequencyAnalyzerPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
    {
        InitializeComponent();
        _plugin = plugin;
        _parameterCallback = parameterCallback;
        _bypassCallback = bypassCallback;

        var preferredSize = FrequencyAnalyzerRenderer.GetPreferredSize();
        Width = preferredSize.Width;
        Height = preferredSize.Height;

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += OnRenderTick;
        Loaded += (_, _) =>
        {
            _plugin.SetVisualizationActive(true);
            _renderTimer.Start();
        };
        Closed += (_, _) =>
        {
            _renderTimer.Stop();
            _plugin.SetVisualizationActive(false);
            _renderer.Dispose();
        };
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        int bins = Math.Max(1, _plugin.DisplayBins);
        if (_spectrum.Length != bins)
        {
            _spectrum = new float[bins];
        }

        _plugin.GetSpectrum(_spectrum);
        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var state = new FrequencyAnalyzerState(
            FftSize: _plugin.FftSize,
            DisplayBins: _plugin.DisplayBins,
            Scale: _plugin.Scale,
            MinFrequency: _plugin.MinFrequency,
            MaxFrequency: _plugin.MaxFrequency,
            MinDb: _plugin.MinDb,
            MaxDb: _plugin.MaxDb,
            IsBypassed: _plugin.IsBypassed,
            HoveredKnob: _hoveredKnob,
            Spectrum: _spectrum
        );

        _renderer.Render(canvas, size, dpiScale, state);
    }

    private void SkiaCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        var hit = _renderer.HitTest(x, y);
        switch (hit.Area)
        {
            case AnalyzerHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;
            case AnalyzerHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;
            case AnalyzerHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;
            case AnalyzerHitArea.FftButton:
                CycleFftSize();
                e.Handled = true;
                break;
            case AnalyzerHitArea.BinsButton:
                CycleDisplayBins();
                e.Handled = true;
                break;
            case AnalyzerHitArea.ScaleButton:
                CycleScale();
                e.Handled = true;
                break;
            case AnalyzerHitArea.Knob:
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
            _hoveredKnob = hit.Area == AnalyzerHitArea.Knob ? hit.KnobIndex : -1;
        }
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _activeKnob = -1;
        SkiaCanvas.ReleaseMouseCapture();
    }

    private float GetKnobNormalizedValue(int knobIndex)
    {
        if (knobIndex < 0 || knobIndex >= KnobParams.Length)
        {
            return 0f;
        }

        float value = knobIndex switch
        {
            0 => _plugin.MinFrequency,
            1 => _plugin.MaxFrequency,
            2 => _plugin.MinDb,
            3 => _plugin.MaxDb,
            _ => 0f
        };

        var (_, min, max) = KnobParams[knobIndex];
        return (value - min) / (max - min);
    }

    private void ApplyKnobValue(int knobIndex, float normalizedValue)
    {
        if (knobIndex < 0 || knobIndex >= KnobParams.Length)
        {
            return;
        }

        normalizedValue = Math.Clamp(normalizedValue, 0f, 1f);
        var (paramIndex, min, max) = KnobParams[knobIndex];
        float value = min + normalizedValue * (max - min);
        _parameterCallback(paramIndex, value);
    }

    private void CycleFftSize()
    {
        int current = _plugin.FftSize;
        int index = Array.IndexOf(FftSizes, current);
        int next = FftSizes[(index + 1) % FftSizes.Length];
        _parameterCallback(FrequencyAnalyzerPlugin.FftSizeIndex, next);
    }

    private void CycleDisplayBins()
    {
        int current = _plugin.DisplayBins;
        int index = Array.IndexOf(DisplayBins, current);
        int next = DisplayBins[(index + 1) % DisplayBins.Length];
        _parameterCallback(FrequencyAnalyzerPlugin.DisplayBinsIndex, next);
    }

    private void CycleScale()
    {
        var current = _plugin.Scale;
        int index = Array.IndexOf(Scales, current);
        int nextIndex = (index + 1) % Scales.Length;
        _parameterCallback(FrequencyAnalyzerPlugin.ScaleIndex, (float)Scales[nextIndex]);
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
