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

public partial class DynamicEqWindow : Window, IDisposable
{
    private readonly DynamicEqRenderer _renderer = new();
    private readonly DynamicEqPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginPresetHelper _presetHelper;

    private float _smoothedVoicingLevel;
    private float _smoothedFricativeLevel;
    private float _smoothedLowGain;
    private float _smoothedEdgeGain;
    private float _smoothedAirGain;
    private long _lastDebugTick;
    private bool _disposed;

    public DynamicEqWindow(DynamicEqPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
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

        _renderer.LowBoostKnob.ValueChanged += value =>
        {
            _parameterCallback(DynamicEqPlugin.LowBoostIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.HighBoostKnob.ValueChanged += value =>
        {
            _parameterCallback(DynamicEqPlugin.HighBoostIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.SmoothingKnob.ValueChanged += value =>
        {
            _parameterCallback(DynamicEqPlugin.SmoothingIndex, value);
            _presetHelper.MarkAsCustom();
        };

        var preferredSize = DynamicEqRenderer.GetPreferredSize();
        Width = preferredSize.Width;
        Height = preferredSize.Height;

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += OnRenderTick;
        Loaded += (_, _) => _renderTimer.Start();
        Closed += (_, _) => Dispose();
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        float rawVoicing = _plugin.GetVoicingLevel();
        float rawFricative = _plugin.GetFricativeLevel();
        float rawLow = _plugin.GetLowGainDb();
        float rawEdge = _plugin.GetEdgeGainDb();
        float rawAir = _plugin.GetAirGainDb();

        _smoothedVoicingLevel = _smoothedVoicingLevel * 0.7f + rawVoicing * 0.3f;
        _smoothedFricativeLevel = _smoothedFricativeLevel * 0.7f + rawFricative * 0.3f;
        _smoothedLowGain = _smoothedLowGain * 0.8f + rawLow * 0.2f;
        _smoothedEdgeGain = _smoothedEdgeGain * 0.8f + rawEdge * 0.2f;
        _smoothedAirGain = _smoothedAirGain * 0.8f + rawAir * 0.2f;

        if (EnhanceDebug.ShouldLog(ref _lastDebugTick))
        {
            float scale = EnhanceDebug.ScaleFactor(_plugin.BoostScaleIndex);
            EnhanceDebug.Log("DynamicEq",
                $"scale=x{scale:0} idx={_plugin.BoostScaleIndex} lowBoost={_plugin.LowBoostDb:0.0} highBoost={_plugin.HighBoostDb:0.0} smooth={_plugin.SmoothingMs:0} " +
                $"voicing={rawVoicing:0.000} fricative={rawFricative:0.000} lowGain={rawLow:0.00} edgeGain={rawEdge:0.00} airGain={rawAir:0.00} status=\"{_plugin.StatusMessage}\"");
        }

        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var state = new DynamicEqState(
            LowBoostDb: _plugin.LowBoostDb,
            HighBoostDb: _plugin.HighBoostDb,
            SmoothingMs: _plugin.SmoothingMs,
            ScaleIndex: _plugin.BoostScaleIndex,
            VoicingLevel: _smoothedVoicingLevel,
            FricativeLevel: _smoothedFricativeLevel,
            LowGainDb: _smoothedLowGain,
            EdgeGainDb: _smoothedEdgeGain,
            AirGainDb: _smoothedAirGain,
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

        if (_renderer.LowBoostKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.LowBoostKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_renderer.HighBoostKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.HighBoostKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_renderer.SmoothingKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.SmoothingKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;

        var hit = _renderer.HitTest(x, y);
        switch (hit.Area)
        {
            case DynamicEqHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;
            case DynamicEqHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;
            case DynamicEqHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;
            case DynamicEqHitArea.PresetDropdown:
                _presetHelper.ShowPresetMenu(SkiaCanvas, _renderer.GetPresetDropdownRect());
                e.Handled = true;
                break;
            case DynamicEqHitArea.PresetSave:
                _presetHelper.ShowSaveMenu(SkiaCanvas, this);
                e.Handled = true;
                break;
            case DynamicEqHitArea.ScaleToggle:
                _parameterCallback(DynamicEqPlugin.ScaleIndex, hit.KnobIndex);
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

        _renderer.LowBoostKnob.HandleMouseMove(x, y, leftDown);
        _renderer.HighBoostKnob.HandleMouseMove(x, y, leftDown);
        _renderer.SmoothingKnob.HandleMouseMove(x, y, leftDown);
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _renderer.LowBoostKnob.HandleMouseUp(e.ChangedButton);
        _renderer.HighBoostKnob.HandleMouseUp(e.ChangedButton);
        _renderer.SmoothingKnob.HandleMouseUp(e.ChangedButton);

        if (e.ChangedButton == MouseButton.Left)
            SkiaCanvas.ReleaseMouseCapture();
    }

    private void ApplyPreset(string presetName, IReadOnlyDictionary<string, float> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            int paramIndex = name switch
            {
                "Low Boost" or "LowBoost" => DynamicEqPlugin.LowBoostIndex,
                "Scale" => DynamicEqPlugin.ScaleIndex,
                "High Boost" or "HighBoost" => DynamicEqPlugin.HighBoostIndex,
                "Smoothing" => DynamicEqPlugin.SmoothingIndex,
                _ => -1
            };
            if (paramIndex >= 0) _parameterCallback(paramIndex, value);
        }
    }

    private Dictionary<string, float> GetCurrentParameters()
    {
        return new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            ["Low Boost"] = _plugin.LowBoostDb,
            ["Scale"] = _plugin.BoostScaleIndex,
            ["High Boost"] = _plugin.HighBoostDb,
            ["Smoothing"] = _plugin.SmoothingMs
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
