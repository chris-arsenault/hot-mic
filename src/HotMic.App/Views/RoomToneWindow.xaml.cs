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

public partial class RoomToneWindow : Window, IDisposable
{
    private readonly RoomToneRenderer _renderer = new();
    private readonly RoomTonePlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginPresetHelper _presetHelper;

    private float _smoothedSpeechPresence;
    private float _smoothedDuckAmount;
    private float _smoothedNoiseLevel;
    private long _lastDebugTick;
    private bool _disposed;

    public RoomToneWindow(RoomTonePlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
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

        _renderer.LevelKnob.ValueChanged += value =>
        {
            _parameterCallback(RoomTonePlugin.LevelIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.DuckKnob.ValueChanged += value =>
        {
            _parameterCallback(RoomTonePlugin.DuckIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.ToneKnob.ValueChanged += value =>
        {
            _parameterCallback(RoomTonePlugin.ToneIndex, value);
            _presetHelper.MarkAsCustom();
        };

        var preferredSize = RoomToneRenderer.GetPreferredSize();
        Width = preferredSize.Width;
        Height = preferredSize.Height;

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += OnRenderTick;
        Loaded += (_, _) => _renderTimer.Start();
        Closed += (_, _) => Dispose();
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        float rawSpeech = _plugin.GetSpeechPresence();
        float rawDuck = _plugin.GetDuckAmount();
        float rawNoise = _plugin.GetNoiseLevel();

        _smoothedSpeechPresence = _smoothedSpeechPresence * 0.7f + rawSpeech * 0.3f;
        _smoothedDuckAmount = _smoothedDuckAmount * 0.7f + rawDuck * 0.3f;
        _smoothedNoiseLevel = _smoothedNoiseLevel * 0.8f + rawNoise * 0.2f;

        if (EnhanceDebug.ShouldLog(ref _lastDebugTick))
        {
            float scale = EnhanceDebug.ScaleFactor(_plugin.LevelScaleIndex);
            EnhanceDebug.Log("RoomTone",
                $"scale=x{scale:0} idx={_plugin.LevelScaleIndex} levelDb={_plugin.LevelDb:0.0} duck={_plugin.DuckStrength:0.00} tone={_plugin.ToneHz:0} " +
                $"speech={rawSpeech:0.000} duckAmt={rawDuck:0.000} noise={rawNoise:0.000} status=\"{_plugin.StatusMessage}\"");
        }

        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var state = new RoomToneState(
            LevelDb: _plugin.LevelDb,
            DuckStrength: _plugin.DuckStrength,
            ToneHz: _plugin.ToneHz,
            ScaleIndex: _plugin.LevelScaleIndex,
            SpeechPresence: _smoothedSpeechPresence,
            DuckAmount: _smoothedDuckAmount,
            NoiseLevel: _smoothedNoiseLevel,
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

        if (_renderer.LevelKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.LevelKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_renderer.DuckKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.DuckKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_renderer.ToneKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.ToneKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;

        var hit = _renderer.HitTest(x, y);
        switch (hit.Area)
        {
            case RoomToneHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;
            case RoomToneHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;
            case RoomToneHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;
            case RoomToneHitArea.PresetDropdown:
                _presetHelper.ShowPresetMenu(SkiaCanvas, _renderer.GetPresetDropdownRect());
                e.Handled = true;
                break;
            case RoomToneHitArea.PresetSave:
                _presetHelper.ShowSaveMenu(SkiaCanvas, this);
                e.Handled = true;
                break;
            case RoomToneHitArea.ScaleToggle:
                _parameterCallback(RoomTonePlugin.ScaleIndex, hit.KnobIndex);
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

        _renderer.LevelKnob.HandleMouseMove(x, y, leftDown);
        _renderer.DuckKnob.HandleMouseMove(x, y, leftDown);
        _renderer.ToneKnob.HandleMouseMove(x, y, leftDown);
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _renderer.LevelKnob.HandleMouseUp(e.ChangedButton);
        _renderer.DuckKnob.HandleMouseUp(e.ChangedButton);
        _renderer.ToneKnob.HandleMouseUp(e.ChangedButton);

        if (e.ChangedButton == MouseButton.Left)
            SkiaCanvas.ReleaseMouseCapture();
    }

    private void ApplyPreset(string presetName, IReadOnlyDictionary<string, float> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            int paramIndex = name switch
            {
                "Level" => RoomTonePlugin.LevelIndex,
                "Scale" => RoomTonePlugin.ScaleIndex,
                "Duck" => RoomTonePlugin.DuckIndex,
                "Tone" => RoomTonePlugin.ToneIndex,
                _ => -1
            };
            if (paramIndex >= 0) _parameterCallback(paramIndex, value);
        }
    }

    private Dictionary<string, float> GetCurrentParameters()
    {
        return new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            ["Level"] = _plugin.LevelDb,
            ["Scale"] = _plugin.LevelScaleIndex,
            ["Duck"] = _plugin.DuckStrength,
            ["Tone"] = _plugin.ToneHz
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
