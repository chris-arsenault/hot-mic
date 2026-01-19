using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HotMic.App.Diagnostics;
using HotMic.App.UI.PluginComponents;
using HotMic.Core.Plugins.BuiltIn;
using HotMic.Core.Presets;
using SkiaSharp;
using SkiaSharp.Views.WPF;

namespace HotMic.App.Views;

public partial class UpwardExpanderWindow : Window, IDisposable
{
    private readonly UpwardExpanderRenderer _renderer = new();
    private readonly UpwardExpanderPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginPresetHelper _presetHelper;

    private float _smoothedLowLevel;
    private float _smoothedMidLevel;
    private float _smoothedHighLevel;
    private float _smoothedLowGain;
    private float _smoothedMidGain;
    private float _smoothedHighGain;
    private float _smoothedSpeech;
    private long _lastDebugTick;
    private bool _disposed;

    public UpwardExpanderWindow(UpwardExpanderPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
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

        _renderer.AmountKnob.ValueChanged += value =>
        {
            _parameterCallback(UpwardExpanderPlugin.AmountIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.ThresholdKnob.ValueChanged += value =>
        {
            _parameterCallback(UpwardExpanderPlugin.ThresholdIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.LowSplitKnob.ValueChanged += value =>
        {
            _parameterCallback(UpwardExpanderPlugin.LowSplitIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.HighSplitKnob.ValueChanged += value =>
        {
            _parameterCallback(UpwardExpanderPlugin.HighSplitIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.AttackKnob.ValueChanged += value =>
        {
            _parameterCallback(UpwardExpanderPlugin.AttackIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.ReleaseKnob.ValueChanged += value =>
        {
            _parameterCallback(UpwardExpanderPlugin.ReleaseIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.GateKnob.ValueChanged += value =>
        {
            _parameterCallback(UpwardExpanderPlugin.GateStrengthIndex, value);
            _presetHelper.MarkAsCustom();
        };

        var preferredSize = UpwardExpanderRenderer.GetPreferredSize();
        Width = preferredSize.Width;
        Height = preferredSize.Height;

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += OnRenderTick;
        Loaded += (_, _) => _renderTimer.Start();
        Closed += (_, _) => Dispose();
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        float rawLowLvl = _plugin.GetLowLevel();
        float rawMidLvl = _plugin.GetMidLevel();
        float rawHighLvl = _plugin.GetHighLevel();
        float rawLowGain = _plugin.GetLowGainDb();
        float rawMidGain = _plugin.GetMidGainDb();
        float rawHighGain = _plugin.GetHighGainDb();
        float rawSpeech = _plugin.GetSpeechPresence();

        _smoothedLowLevel = _smoothedLowLevel * 0.7f + rawLowLvl * 0.3f;
        _smoothedMidLevel = _smoothedMidLevel * 0.7f + rawMidLvl * 0.3f;
        _smoothedHighLevel = _smoothedHighLevel * 0.7f + rawHighLvl * 0.3f;
        _smoothedLowGain = _smoothedLowGain * 0.8f + rawLowGain * 0.2f;
        _smoothedMidGain = _smoothedMidGain * 0.8f + rawMidGain * 0.2f;
        _smoothedHighGain = _smoothedHighGain * 0.8f + rawHighGain * 0.2f;
        _smoothedSpeech = _smoothedSpeech * 0.7f + rawSpeech * 0.3f;

        if (EnhanceDebug.ShouldLog(ref _lastDebugTick))
        {
            float scale = EnhanceDebug.ScaleFactor(_plugin.AmountScaleIndex);
            EnhanceDebug.Log("UpwardExpander",
                $"scale=x{scale:0} idx={_plugin.AmountScaleIndex} amount={_plugin.AmountPct:0} thresh={_plugin.ThresholdDb:0} " +
                $"lowSplit={_plugin.LowSplitHz:0} highSplit={_plugin.HighSplitHz:0} atk={_plugin.AttackMs:0} rel={_plugin.ReleaseMs:0} gate={_plugin.GateStrength:0.00} " +
                $"speech={rawSpeech:0.000} lowLvl={rawLowLvl:0.000} midLvl={rawMidLvl:0.000} highLvl={rawHighLvl:0.000} " +
                $"lowGain={rawLowGain:0.00} midGain={rawMidGain:0.00} highGain={rawHighGain:0.00} status=\"{_plugin.StatusMessage}\"");
        }

        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var state = new UpwardExpanderState(
            AmountPct: _plugin.AmountPct,
            ThresholdDb: _plugin.ThresholdDb,
            LowSplitHz: _plugin.LowSplitHz,
            HighSplitHz: _plugin.HighSplitHz,
            AttackMs: _plugin.AttackMs,
            ReleaseMs: _plugin.ReleaseMs,
            GateStrength: _plugin.GateStrength,
            LowLevel: _smoothedLowLevel,
            MidLevel: _smoothedMidLevel,
            HighLevel: _smoothedHighLevel,
            LowGainDb: _smoothedLowGain,
            MidGainDb: _smoothedMidGain,
            HighGainDb: _smoothedHighGain,
            SpeechPresence: _smoothedSpeech,
            ScaleIndex: _plugin.AmountScaleIndex,
            LatencyMs: _plugin.SampleRate > 0 ? _plugin.LatencySamples * 1000f / _plugin.SampleRate : 0f,
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

        // Check all 7 knobs
        if (_renderer.AmountKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.AmountKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_renderer.ThresholdKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.ThresholdKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_renderer.LowSplitKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.LowSplitKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_renderer.HighSplitKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.HighSplitKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_renderer.AttackKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.AttackKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_renderer.ReleaseKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.ReleaseKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_renderer.GateKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.GateKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;

        var hit = _renderer.HitTest(x, y);
        switch (hit.Area)
        {
            case UpwardExpanderHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;
            case UpwardExpanderHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;
            case UpwardExpanderHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;
            case UpwardExpanderHitArea.PresetDropdown:
                _presetHelper.ShowPresetMenu(SkiaCanvas, _renderer.GetPresetDropdownRect());
                e.Handled = true;
                break;
            case UpwardExpanderHitArea.PresetSave:
                _presetHelper.ShowSaveMenu(SkiaCanvas, this);
                e.Handled = true;
                break;
            case UpwardExpanderHitArea.ScaleToggle:
                _parameterCallback(UpwardExpanderPlugin.ScaleIndex, hit.KnobIndex);
                _presetHelper.MarkAsCustom();
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

        _renderer.AmountKnob.HandleMouseMove(x, y, leftDown);
        _renderer.ThresholdKnob.HandleMouseMove(x, y, leftDown);
        _renderer.LowSplitKnob.HandleMouseMove(x, y, leftDown);
        _renderer.HighSplitKnob.HandleMouseMove(x, y, leftDown);
        _renderer.AttackKnob.HandleMouseMove(x, y, leftDown);
        _renderer.ReleaseKnob.HandleMouseMove(x, y, leftDown);
        _renderer.GateKnob.HandleMouseMove(x, y, leftDown);
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _renderer.AmountKnob.HandleMouseUp(e.ChangedButton);
        _renderer.ThresholdKnob.HandleMouseUp(e.ChangedButton);
        _renderer.LowSplitKnob.HandleMouseUp(e.ChangedButton);
        _renderer.HighSplitKnob.HandleMouseUp(e.ChangedButton);
        _renderer.AttackKnob.HandleMouseUp(e.ChangedButton);
        _renderer.ReleaseKnob.HandleMouseUp(e.ChangedButton);
        _renderer.GateKnob.HandleMouseUp(e.ChangedButton);

        if (e.ChangedButton == MouseButton.Left)
            SkiaCanvas.ReleaseMouseCapture();
    }

    private void ApplyPreset(string presetName, IReadOnlyDictionary<string, float> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            int paramIndex = name switch
            {
                "Amount" => UpwardExpanderPlugin.AmountIndex,
                "Scale" => UpwardExpanderPlugin.ScaleIndex,
                "Threshold" => UpwardExpanderPlugin.ThresholdIndex,
                "Low Split" or "LowSplit" => UpwardExpanderPlugin.LowSplitIndex,
                "High Split" or "HighSplit" => UpwardExpanderPlugin.HighSplitIndex,
                "Attack" => UpwardExpanderPlugin.AttackIndex,
                "Release" => UpwardExpanderPlugin.ReleaseIndex,
                "Gate Strength" or "GateStrength" => UpwardExpanderPlugin.GateStrengthIndex,
                _ => -1
            };
            if (paramIndex >= 0) _parameterCallback(paramIndex, value);
        }
    }

    private Dictionary<string, float> GetCurrentParameters()
    {
        return new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            ["Amount"] = _plugin.AmountPct,
            ["Scale"] = _plugin.AmountScaleIndex,
            ["Threshold"] = _plugin.ThresholdDb,
            ["Low Split"] = _plugin.LowSplitHz,
            ["High Split"] = _plugin.HighSplitHz,
            ["Attack"] = _plugin.AttackMs,
            ["Release"] = _plugin.ReleaseMs,
            ["Gate Strength"] = _plugin.GateStrength
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
