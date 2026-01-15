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

    public FrequencyAnalyzerWindow(FrequencyAnalyzerPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
    {
        InitializeComponent();
        _plugin = plugin;
        _parameterCallback = parameterCallback;
        _bypassCallback = bypassCallback;

        // Wire up KnobWidget ValueChanged events
        _renderer.MinFreqKnob.ValueChanged += v => _parameterCallback(FrequencyAnalyzerPlugin.MinFrequencyIndex, v);
        _renderer.MaxFreqKnob.ValueChanged += v => _parameterCallback(FrequencyAnalyzerPlugin.MaxFrequencyIndex, v);
        _renderer.MinDbKnob.ValueChanged += v => _parameterCallback(FrequencyAnalyzerPlugin.MinDbIndex, v);
        _renderer.MaxDbKnob.ValueChanged += v => _parameterCallback(FrequencyAnalyzerPlugin.MaxDbIndex, v);

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
            Spectrum: _spectrum
        );

        _renderer.Render(canvas, size, dpiScale, state);
    }

    private void SkiaCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        // Check if any knob handles the mouse first
        if (_renderer.MinFreqKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas) ||
            _renderer.MaxFreqKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas) ||
            _renderer.MinDbKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas) ||
            _renderer.MaxDbKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

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
        }
    }

    private void SkiaCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;
        bool leftDown = e.LeftButton == MouseButtonState.Pressed;

        // Forward to all knobs
        _renderer.MinFreqKnob.HandleMouseMove(x, y, leftDown);
        _renderer.MaxFreqKnob.HandleMouseMove(x, y, leftDown);
        _renderer.MinDbKnob.HandleMouseMove(x, y, leftDown);
        _renderer.MaxDbKnob.HandleMouseMove(x, y, leftDown);
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        // Forward to all knobs
        _renderer.MinFreqKnob.HandleMouseUp(e.ChangedButton);
        _renderer.MaxFreqKnob.HandleMouseUp(e.ChangedButton);
        _renderer.MinDbKnob.HandleMouseUp(e.ChangedButton);
        _renderer.MaxDbKnob.HandleMouseUp(e.ChangedButton);

        SkiaCanvas.ReleaseMouseCapture();
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
