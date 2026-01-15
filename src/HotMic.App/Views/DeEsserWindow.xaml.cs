using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HotMic.App.UI.PluginComponents;
using HotMic.Core.Plugins.BuiltIn;
using HotMic.Core.Presets;
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
    private readonly PluginPresetHelper _presetHelper;

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

        _presetHelper = new PluginPresetHelper(
            plugin.Id,
            PluginPresetManager.Default,
            ApplyPreset,
            GetCurrentParameters);

        // Wire up knob value changes
        _renderer.CenterFreqKnob.ValueChanged += value =>
        {
            _parameterCallback(DeEsserPlugin.CenterFreqIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.BandwidthKnob.ValueChanged += value =>
        {
            _parameterCallback(DeEsserPlugin.BandwidthIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.ThresholdKnob.ValueChanged += value =>
        {
            _parameterCallback(DeEsserPlugin.ThresholdIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.ReductionKnob.ValueChanged += value =>
        {
            _parameterCallback(DeEsserPlugin.ReductionIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.MaxRangeKnob.ValueChanged += value =>
        {
            _parameterCallback(DeEsserPlugin.MaxRangeIndex, value);
            _presetHelper.MarkAsCustom();
        };

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
            Spectrum: _spectrum,
            PresetName: _presetHelper.CurrentPresetName
        );

        _renderer.Render(canvas, size, dpiScale, state);
    }

    private void SkiaCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        // Let knobs handle their own input (drag, right-click edit)
        if (_renderer.CenterFreqKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas) ||
            _renderer.BandwidthKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas) ||
            _renderer.ThresholdKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas) ||
            _renderer.ReductionKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas) ||
            _renderer.MaxRangeKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.CenterFreqKnob.IsDragging || _renderer.BandwidthKnob.IsDragging ||
                _renderer.ThresholdKnob.IsDragging || _renderer.ReductionKnob.IsDragging ||
                _renderer.MaxRangeKnob.IsDragging)
                SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
            return;

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

            case DeEsserHitArea.PresetDropdown:
                _presetHelper.ShowPresetMenu(SkiaCanvas, _renderer.GetPresetDropdownRect());
                e.Handled = true;
                break;

            case DeEsserHitArea.PresetSave:
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
        bool leftDown = e.LeftButton == MouseButtonState.Pressed;

        // Let knobs handle drag and hover
        _renderer.CenterFreqKnob.HandleMouseMove(x, y, leftDown);
        _renderer.BandwidthKnob.HandleMouseMove(x, y, leftDown);
        _renderer.ThresholdKnob.HandleMouseMove(x, y, leftDown);
        _renderer.ReductionKnob.HandleMouseMove(x, y, leftDown);
        _renderer.MaxRangeKnob.HandleMouseMove(x, y, leftDown);
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _renderer.CenterFreqKnob.HandleMouseUp(e.ChangedButton);
        _renderer.BandwidthKnob.HandleMouseUp(e.ChangedButton);
        _renderer.ThresholdKnob.HandleMouseUp(e.ChangedButton);
        _renderer.ReductionKnob.HandleMouseUp(e.ChangedButton);
        _renderer.MaxRangeKnob.HandleMouseUp(e.ChangedButton);

        if (e.ChangedButton == MouseButton.Left)
            SkiaCanvas.ReleaseMouseCapture();
    }

    private void ApplyPreset(string presetName, IReadOnlyDictionary<string, float> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            int paramIndex = name switch
            {
                "Center" => DeEsserPlugin.CenterFreqIndex,
                "Bandwidth" => DeEsserPlugin.BandwidthIndex,
                "Threshold" => DeEsserPlugin.ThresholdIndex,
                "Reduction" => DeEsserPlugin.ReductionIndex,
                "MaxRange" => DeEsserPlugin.MaxRangeIndex,
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
            ["Center"] = _plugin.CenterHz,
            ["Bandwidth"] = _plugin.BandwidthHz,
            ["Threshold"] = _plugin.ThresholdDb,
            ["Reduction"] = _plugin.ReductionDb,
            ["MaxRange"] = _plugin.MaxRangeDb
        };
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
