using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using HotMic.App.UI.PluginComponents;
using HotMic.Core.Plugins.BuiltIn;
using HotMic.Core.Presets;
using SkiaSharp;
using SkiaSharp.Views.WPF;

namespace HotMic.App.Views;

public partial class FFTNoiseWindow : Window
{
    private readonly FFTNoiseRenderer _renderer = new();
    private readonly FFTNoiseRemovalPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly Action _learnToggleCallback;
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginPresetHelper _presetHelper;

    // Pre-allocated spectrum buffers
    private readonly float[] _inputSpectrum = new float[FFTNoiseRemovalPlugin.DisplayBins];
    private readonly float[] _inputPeaks = new float[FFTNoiseRemovalPlugin.DisplayBins];
    private readonly float[] _outputSpectrum = new float[FFTNoiseRemovalPlugin.DisplayBins];
    private readonly float[] _outputPeaks = new float[FFTNoiseRemovalPlugin.DisplayBins];
    private readonly float[] _noiseProfile = new float[FFTNoiseRemovalPlugin.DisplayBins];

    public FFTNoiseWindow(FFTNoiseRemovalPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback, Action learnToggleCallback)
    {
        InitializeComponent();
        _plugin = plugin;
        _parameterCallback = parameterCallback;
        _bypassCallback = bypassCallback;
        _learnToggleCallback = learnToggleCallback;

        _presetHelper = new PluginPresetHelper(
            plugin.Id,
            PluginPresetManager.Default,
            ApplyPreset,
            GetCurrentParameters);

        // Wire up knob value changes
        _renderer.ReductionKnob.ValueChanged += value =>
        {
            _parameterCallback(FFTNoiseRemovalPlugin.ReductionIndex, value);
            _presetHelper.MarkAsCustom();
        };

        var preferredSize = FFTNoiseRenderer.GetPreferredSize();
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
        // Get spectrum data
        _plugin.GetSpectrumData(_inputSpectrum, _inputPeaks, _outputSpectrum, _outputPeaks, _noiseProfile);

        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var state = new FFTNoiseState(
            Reduction: _plugin.Reduction,
            IsLearning: _plugin.IsLearning,
            LearningProgress: _plugin.LearningProgress,
            LearningTotal: _plugin.LearningTotal,
            HasNoiseProfile: _plugin.HasNoiseProfile,
            IsBypassed: _plugin.IsBypassed,
            SampleRate: _plugin.SampleRate,
            LatencyMs: _plugin.SampleRate > 0 ? _plugin.LatencySamples * 1000f / _plugin.SampleRate : 0f,
            InputSpectrum: _inputSpectrum,
            InputPeaks: _inputPeaks,
            OutputSpectrum: _outputSpectrum,
            OutputPeaks: _outputPeaks,
            NoiseProfile: _noiseProfile,
            PresetName: _presetHelper.CurrentPresetName
        );

        _renderer.Render(canvas, size, dpiScale, state);
    }

    private void SkiaCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        // Let the knob handle its own input (drag, right-click edit)
        if (_renderer.ReductionKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.ReductionKnob.IsDragging)
                SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
            return;

        var hit = _renderer.HitTest(x, y);

        switch (hit.Area)
        {
            case FFTNoiseHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;

            case FFTNoiseHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;

            case FFTNoiseHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;

            case FFTNoiseHitArea.LearnButton:
                _learnToggleCallback();
                e.Handled = true;
                break;

            case FFTNoiseHitArea.PresetDropdown:
                _presetHelper.ShowPresetMenu(SkiaCanvas, _renderer.GetPresetDropdownRect());
                e.Handled = true;
                break;

            case FFTNoiseHitArea.PresetSave:
                _presetHelper.ShowSaveMenu(SkiaCanvas, this);
                e.Handled = true;
                break;
        }
    }

    private void SkiaCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        // Let the knob handle drag and hover
        _renderer.ReductionKnob.HandleMouseMove(x, y, e.LeftButton == MouseButtonState.Pressed);
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _renderer.ReductionKnob.HandleMouseUp(e.ChangedButton);

        if (e.ChangedButton == MouseButton.Left)
            SkiaCanvas.ReleaseMouseCapture();
    }

    private void ApplyPreset(string presetName, IReadOnlyDictionary<string, float> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            int paramIndex = name switch
            {
                "Reduction" => FFTNoiseRemovalPlugin.ReductionIndex,
                _ => -1
            };

            if (paramIndex >= 0)
            {
                _parameterCallback(paramIndex, value);
            }
        }
    }

    private Dictionary<string, float> GetCurrentParameters()
    {
        return new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            ["Reduction"] = _plugin.Reduction
        };
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
