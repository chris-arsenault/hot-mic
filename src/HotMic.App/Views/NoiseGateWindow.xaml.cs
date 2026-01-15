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

public partial class NoiseGateWindow : Window
{
    private readonly NoiseGateRenderer _renderer = new();
    private readonly NoiseGatePlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginPresetHelper _presetHelper;

    public NoiseGateWindow(NoiseGatePlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
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
        _renderer.ThresholdKnob.ValueChanged += value =>
        {
            _parameterCallback(NoiseGatePlugin.ThresholdIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.HysteresisKnob.ValueChanged += value =>
        {
            _parameterCallback(NoiseGatePlugin.HysteresisIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.AttackKnob.ValueChanged += value =>
        {
            _parameterCallback(NoiseGatePlugin.AttackIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.HoldKnob.ValueChanged += value =>
        {
            _parameterCallback(NoiseGatePlugin.HoldIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.ReleaseKnob.ValueChanged += value =>
        {
            _parameterCallback(NoiseGatePlugin.ReleaseIndex, value);
            _presetHelper.MarkAsCustom();
        };

        var preferredSize = NoiseGateRenderer.GetPreferredSize();
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
        // Update waveform buffer with current input level
        float inputLevel = _plugin.GetAndResetInputLevel();
        bool gateOpen = _plugin.IsGateOpen();
        _renderer.WaveformBuffer.Push(inputLevel, gateOpen);

        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var state = new NoiseGateState(
            ThresholdDb: _plugin.ThresholdDb,
            HysteresisDb: _plugin.HysteresisDb,
            AttackMs: _plugin.AttackMs,
            HoldMs: _plugin.HoldMs,
            ReleaseMs: _plugin.ReleaseMs,
            LatencyMs: _plugin.SampleRate > 0 ? _plugin.LatencySamples * 1000f / _plugin.SampleRate : 0f,
            IsGateOpen: _plugin.IsGateOpen(),
            IsBypassed: _plugin.IsBypassed,
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
        foreach (var knob in new[] { _renderer.ThresholdKnob, _renderer.HysteresisKnob, _renderer.AttackKnob,
                                      _renderer.HoldKnob, _renderer.ReleaseKnob })
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

            case NoiseGateHitArea.PresetDropdown:
                _presetHelper.ShowPresetMenu(SkiaCanvas, _renderer.GetPresetDropdownRect());
                e.Handled = true;
                break;

            case NoiseGateHitArea.PresetSave:
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
        foreach (var knob in new[] { _renderer.ThresholdKnob, _renderer.HysteresisKnob, _renderer.AttackKnob,
                                      _renderer.HoldKnob, _renderer.ReleaseKnob })
        {
            knob.HandleMouseMove(x, y, e.LeftButton == MouseButtonState.Pressed);
        }
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        // Let knobs handle mouse up
        foreach (var knob in new[] { _renderer.ThresholdKnob, _renderer.HysteresisKnob, _renderer.AttackKnob,
                                      _renderer.HoldKnob, _renderer.ReleaseKnob })
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
                "Threshold" => NoiseGatePlugin.ThresholdIndex,
                "Hysteresis" => NoiseGatePlugin.HysteresisIndex,
                "Attack" => NoiseGatePlugin.AttackIndex,
                "Hold" => NoiseGatePlugin.HoldIndex,
                "Release" => NoiseGatePlugin.ReleaseIndex,
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
            ["Threshold"] = _plugin.ThresholdDb,
            ["Hysteresis"] = _plugin.HysteresisDb,
            ["Attack"] = _plugin.AttackMs,
            ["Hold"] = _plugin.HoldMs,
            ["Release"] = _plugin.ReleaseMs
        };
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
