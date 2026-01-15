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

public partial class VoiceGateWindow : Window
{
    private readonly VoiceGateRenderer _renderer = new();
    private readonly SileroVoiceGatePlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginPresetHelper _presetHelper;

    public VoiceGateWindow(SileroVoiceGatePlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
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
            _parameterCallback(SileroVoiceGatePlugin.ThresholdIndex, value / 100f); // Convert from percentage
            _presetHelper.MarkAsCustom();
        };
        _renderer.AttackKnob.ValueChanged += value =>
        {
            _parameterCallback(SileroVoiceGatePlugin.AttackIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.ReleaseKnob.ValueChanged += value =>
        {
            _parameterCallback(SileroVoiceGatePlugin.ReleaseIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.HoldKnob.ValueChanged += value =>
        {
            _parameterCallback(SileroVoiceGatePlugin.HoldIndex, value);
            _presetHelper.MarkAsCustom();
        };

        var preferredSize = VoiceGateRenderer.GetPreferredSize();
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
        float vad = _plugin.VadProbability;
        float threshold = GetParameterValue(SileroVoiceGatePlugin.ThresholdIndex);
        bool gateOpen = vad >= threshold;

        float inputLevel = _plugin.GetAndResetInputLevel();
        _renderer.WaveformBuffer.Push(inputLevel, gateOpen);
        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        float threshold = GetParameterValue(SileroVoiceGatePlugin.ThresholdIndex);
        float vad = _plugin.VadProbability;

        var state = new VoiceGateState(
            Threshold: threshold,
            AttackMs: GetParameterValue(SileroVoiceGatePlugin.AttackIndex),
            ReleaseMs: GetParameterValue(SileroVoiceGatePlugin.ReleaseIndex),
            HoldMs: GetParameterValue(SileroVoiceGatePlugin.HoldIndex),
            VadProbability: vad,
            IsGateOpen: vad >= threshold,
            IsBypassed: _plugin.IsBypassed,
            StatusMessage: _plugin.StatusMessage,
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
        foreach (var knob in new[] { _renderer.ThresholdKnob, _renderer.AttackKnob,
                                      _renderer.ReleaseKnob, _renderer.HoldKnob })
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
            case VoiceGateHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;

            case VoiceGateHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;

            case VoiceGateHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;

            case VoiceGateHitArea.PresetDropdown:
                _presetHelper.ShowPresetMenu(SkiaCanvas, _renderer.GetPresetDropdownRect());
                e.Handled = true;
                break;

            case VoiceGateHitArea.PresetSave:
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
        foreach (var knob in new[] { _renderer.ThresholdKnob, _renderer.AttackKnob,
                                      _renderer.ReleaseKnob, _renderer.HoldKnob })
        {
            knob.HandleMouseMove(x, y, e.LeftButton == MouseButtonState.Pressed);
        }
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        // Let knobs handle mouse up
        foreach (var knob in new[] { _renderer.ThresholdKnob, _renderer.AttackKnob,
                                      _renderer.ReleaseKnob, _renderer.HoldKnob })
        {
            knob.HandleMouseUp(e.ChangedButton);
        }

        if (e.ChangedButton == MouseButton.Left)
            SkiaCanvas.ReleaseMouseCapture();
    }

    private float GetParameterValue(int index)
    {
        var param = _plugin.Parameters[index];
        return index switch
        {
            SileroVoiceGatePlugin.ThresholdIndex => GetStoredValue(0, param.DefaultValue),
            SileroVoiceGatePlugin.AttackIndex => GetStoredValue(1, param.DefaultValue),
            SileroVoiceGatePlugin.ReleaseIndex => GetStoredValue(2, param.DefaultValue),
            SileroVoiceGatePlugin.HoldIndex => GetStoredValue(3, param.DefaultValue),
            _ => param.DefaultValue
        };
    }

    private float GetStoredValue(int stateIndex, float defaultValue)
    {
        var state = _plugin.GetState();
        if (state.Length >= (stateIndex + 1) * sizeof(float))
        {
            return BitConverter.ToSingle(state, stateIndex * sizeof(float));
        }
        return defaultValue;
    }

    private void ApplyPreset(string presetName, IReadOnlyDictionary<string, float> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            int paramIndex = name switch
            {
                "Threshold" => SileroVoiceGatePlugin.ThresholdIndex,
                "Attack" => SileroVoiceGatePlugin.AttackIndex,
                "Release" => SileroVoiceGatePlugin.ReleaseIndex,
                "Hold" => SileroVoiceGatePlugin.HoldIndex,
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
            ["Threshold"] = GetParameterValue(SileroVoiceGatePlugin.ThresholdIndex),
            ["Attack"] = GetParameterValue(SileroVoiceGatePlugin.AttackIndex),
            ["Release"] = GetParameterValue(SileroVoiceGatePlugin.ReleaseIndex),
            ["Hold"] = GetParameterValue(SileroVoiceGatePlugin.HoldIndex)
        };
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
