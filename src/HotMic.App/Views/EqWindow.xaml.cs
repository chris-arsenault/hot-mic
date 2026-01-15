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

public partial class EqWindow : Window
{
    private readonly EqRenderer _renderer = new();
    private readonly FiveBandEqPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginPresetHelper _presetHelper;

    private float _smoothedInputLevel;
    private float _smoothedOutputLevel;

    // Pre-allocated spectrum buffers to avoid allocations during rendering
    private readonly float[] _spectrumLevels = new float[FiveBandEqPlugin.SpectrumBins];
    private readonly float[] _spectrumPeaks = new float[FiveBandEqPlugin.SpectrumBins];

    public EqWindow(FiveBandEqPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
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
        _renderer.HpfFreqKnob.ValueChanged += value =>
        {
            _parameterCallback(FiveBandEqPlugin.HpfFreqIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.LowGainKnob.ValueChanged += value =>
        {
            _parameterCallback(FiveBandEqPlugin.LowShelfGainIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.LowFreqKnob.ValueChanged += value =>
        {
            _parameterCallback(FiveBandEqPlugin.LowShelfFreqIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.Mid1GainKnob.ValueChanged += value =>
        {
            _parameterCallback(FiveBandEqPlugin.Mid1GainIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.Mid1FreqKnob.ValueChanged += value =>
        {
            _parameterCallback(FiveBandEqPlugin.Mid1FreqIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.Mid2GainKnob.ValueChanged += value =>
        {
            _parameterCallback(FiveBandEqPlugin.Mid2GainIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.Mid2FreqKnob.ValueChanged += value =>
        {
            _parameterCallback(FiveBandEqPlugin.Mid2FreqIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.HighGainKnob.ValueChanged += value =>
        {
            _parameterCallback(FiveBandEqPlugin.HighShelfGainIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.HighFreqKnob.ValueChanged += value =>
        {
            _parameterCallback(FiveBandEqPlugin.HighShelfFreqIndex, value);
            _presetHelper.MarkAsCustom();
        };

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
        foreach (var knob in new[] { _renderer.HpfFreqKnob, _renderer.LowGainKnob, _renderer.LowFreqKnob,
                                      _renderer.Mid1GainKnob, _renderer.Mid1FreqKnob, _renderer.Mid2GainKnob,
                                      _renderer.Mid2FreqKnob, _renderer.HighGainKnob, _renderer.HighFreqKnob })
        {
            if (knob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
            {
                if (knob.IsDragging)
                    SkiaCanvas.CaptureMouse();
                e.Handled = true;
                return;
            }
        }

        if (e.ChangedButton != MouseButton.Left)
            return;

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

            case EqHitArea.PresetDropdown:
                _presetHelper.ShowPresetMenu(SkiaCanvas, _renderer.GetPresetDropdownRect());
                e.Handled = true;
                break;

            case EqHitArea.PresetSave:
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

        // Let knobs handle drag and hover
        foreach (var knob in new[] { _renderer.HpfFreqKnob, _renderer.LowGainKnob, _renderer.LowFreqKnob,
                                      _renderer.Mid1GainKnob, _renderer.Mid1FreqKnob, _renderer.Mid2GainKnob,
                                      _renderer.Mid2FreqKnob, _renderer.HighGainKnob, _renderer.HighFreqKnob })
        {
            knob.HandleMouseMove(x, y, e.LeftButton == MouseButtonState.Pressed);
        }
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        // Let knobs handle mouse up
        foreach (var knob in new[] { _renderer.HpfFreqKnob, _renderer.LowGainKnob, _renderer.LowFreqKnob,
                                      _renderer.Mid1GainKnob, _renderer.Mid1FreqKnob, _renderer.Mid2GainKnob,
                                      _renderer.Mid2FreqKnob, _renderer.HighGainKnob, _renderer.HighFreqKnob })
        {
            knob.HandleMouseUp(e.ChangedButton);
        }

        if (e.ChangedButton == MouseButton.Left)
            SkiaCanvas.ReleaseMouseCapture();
    }

    private void ApplyPreset(string presetName, IReadOnlyDictionary<string, float> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            int paramIndex = name switch
            {
                "HpfFreq" => FiveBandEqPlugin.HpfFreqIndex,
                "LowShelfGain" => FiveBandEqPlugin.LowShelfGainIndex,
                "LowShelfFreq" => FiveBandEqPlugin.LowShelfFreqIndex,
                "Mid1Gain" => FiveBandEqPlugin.Mid1GainIndex,
                "Mid1Freq" => FiveBandEqPlugin.Mid1FreqIndex,
                "Mid1Q" => FiveBandEqPlugin.Mid1QIndex,
                "Mid2Gain" => FiveBandEqPlugin.Mid2GainIndex,
                "Mid2Freq" => FiveBandEqPlugin.Mid2FreqIndex,
                "Mid2Q" => FiveBandEqPlugin.Mid2QIndex,
                "HighShelfGain" => FiveBandEqPlugin.HighShelfGainIndex,
                "HighShelfFreq" => FiveBandEqPlugin.HighShelfFreqIndex,
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
            ["HpfFreq"] = _plugin.HpfFreq,
            ["LowShelfGain"] = _plugin.LowShelfGainDb,
            ["LowShelfFreq"] = _plugin.LowShelfFreq,
            ["Mid1Gain"] = _plugin.Mid1GainDb,
            ["Mid1Freq"] = _plugin.Mid1Freq,
            ["Mid1Q"] = _plugin.Mid1Q,
            ["Mid2Gain"] = _plugin.Mid2GainDb,
            ["Mid2Freq"] = _plugin.Mid2Freq,
            ["Mid2Q"] = _plugin.Mid2Q,
            ["HighShelfGain"] = _plugin.HighShelfGainDb,
            ["HighShelfFreq"] = _plugin.HighShelfFreq
        };
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
