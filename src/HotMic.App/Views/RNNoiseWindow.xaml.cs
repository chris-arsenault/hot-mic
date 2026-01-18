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

public partial class RNNoiseWindow : Window, IDisposable
{
    private readonly RNNoiseRenderer _renderer = new();
    private readonly RNNoisePlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginPresetHelper _presetHelper;
    private bool _disposed;

    public RNNoiseWindow(RNNoisePlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
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

        // Wire up knob ValueChanged events
        _renderer.ReductionKnob.ValueChanged += value =>
        {
            _parameterCallback(RNNoisePlugin.ReductionIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.VadThresholdKnob.ValueChanged += value =>
        {
            _parameterCallback(RNNoisePlugin.VadThresholdIndex, value);
            _presetHelper.MarkAsCustom();
        };

        var preferredSize = RNNoiseRenderer.GetPreferredSize();
        Width = preferredSize.Width;
        Height = preferredSize.Height;

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += OnRenderTick;
        Loaded += (_, _) => _renderTimer.Start();
        Closed += (_, _) => Dispose();
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        float sampleRate = 48000f; // RNNoise requires 48kHz
        float latencyMs = _plugin.LatencySamples > 0 ? _plugin.LatencySamples * 1000f / sampleRate : 0f;

        var state = new RNNoiseState(
            ReductionPercent: GetParameterValue(RNNoisePlugin.ReductionIndex),
            VadThreshold: GetParameterValue(RNNoisePlugin.VadThresholdIndex),
            VadProbability: _plugin.VadProbability,
            GainReductionDb: _plugin.GainReductionDb,
            LatencyMs: latencyMs,
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

        // Check if any knob handles the mouse first
        if (_renderer.ReductionKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (e.ChangedButton == MouseButton.Left)
                SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_renderer.VadThresholdKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (e.ChangedButton == MouseButton.Left)
                SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
            return;

        var hit = _renderer.HitTest(x, y);

        switch (hit.Area)
        {
            case RNNoiseHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;

            case RNNoiseHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;

            case RNNoiseHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;

            case RNNoiseHitArea.PresetDropdown:
                _presetHelper.ShowPresetMenu(SkiaCanvas, _renderer.GetPresetDropdownRect());
                e.Handled = true;
                break;

            case RNNoiseHitArea.PresetSave:
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
        bool isLeftDown = e.LeftButton == MouseButtonState.Pressed;

        // Forward to all knobs
        _renderer.ReductionKnob.HandleMouseMove(x, y, isLeftDown);
        _renderer.VadThresholdKnob.HandleMouseMove(x, y, isLeftDown);
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        // Forward to all knobs
        _renderer.ReductionKnob.HandleMouseUp(e.ChangedButton);
        _renderer.VadThresholdKnob.HandleMouseUp(e.ChangedButton);

        if (e.ChangedButton == MouseButton.Left)
            SkiaCanvas.ReleaseMouseCapture();
    }

    private float GetParameterValue(int index)
    {
        var state = _plugin.GetState();
        if (state.Length >= (index + 1) * sizeof(float))
        {
            return BitConverter.ToSingle(state, index * sizeof(float));
        }
        return _plugin.Parameters[index].DefaultValue;
    }

    private void ApplyPreset(string presetName, IReadOnlyDictionary<string, float> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            int paramIndex = name switch
            {
                "Reduction" => RNNoisePlugin.ReductionIndex,
                "VadThreshold" => RNNoisePlugin.VadThresholdIndex,
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
            ["Reduction"] = GetParameterValue(RNNoisePlugin.ReductionIndex),
            ["VadThreshold"] = GetParameterValue(RNNoisePlugin.VadThresholdIndex)
        };
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _renderTimer.Stop();
        _renderer.Dispose();
        GC.SuppressFinalize(this);
    }
}
