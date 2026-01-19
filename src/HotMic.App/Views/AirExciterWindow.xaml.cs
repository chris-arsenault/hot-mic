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

public partial class AirExciterWindow : Window, IDisposable
{
    private readonly AirExciterRenderer _renderer = new();
    private readonly AirExciterPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginPresetHelper _presetHelper;

    private float _smoothedGateLevel;
    private float _smoothedHfEnergy;
    private float _smoothedSaturation;
    private long _lastDebugTick;
    private bool _disposed;

    public AirExciterWindow(AirExciterPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
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

        _renderer.DriveKnob.ValueChanged += value =>
        {
            _parameterCallback(AirExciterPlugin.DriveIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.MixKnob.ValueChanged += value =>
        {
            _parameterCallback(AirExciterPlugin.MixIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.CutoffKnob.ValueChanged += value =>
        {
            _parameterCallback(AirExciterPlugin.CutoffIndex, value);
            _presetHelper.MarkAsCustom();
        };

        var preferredSize = AirExciterRenderer.GetPreferredSize();
        Width = preferredSize.Width;
        Height = preferredSize.Height;

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += OnRenderTick;
        Loaded += (_, _) => _renderTimer.Start();
        Closed += (_, _) => Dispose();
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        float rawGate = _plugin.GetGateLevel();
        float rawHf = _plugin.GetHfEnergy();
        float rawSat = _plugin.GetSaturationAmount();

        _smoothedGateLevel = _smoothedGateLevel * 0.7f + rawGate * 0.3f;
        _smoothedHfEnergy = _smoothedHfEnergy * 0.7f + rawHf * 0.3f;
        _smoothedSaturation = _smoothedSaturation * 0.8f + rawSat * 0.2f;

        if (EnhanceDebug.ShouldLog(ref _lastDebugTick))
        {
            float scale = EnhanceDebug.ScaleFactor(_plugin.AmountScaleIndex);
            EnhanceDebug.Log("AirExciter",
                $"scale=x{scale:0} idx={_plugin.AmountScaleIndex} drive={_plugin.Drive:0.00} mix={_plugin.Mix:0.00} cutoff={_plugin.CutoffHz:0} " +
                $"gate={rawGate:0.000} hf={rawHf:0.000} sat={rawSat:0.000} status=\"{_plugin.StatusMessage}\"");
        }

        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var state = new AirExciterState(
            Drive: _plugin.Drive,
            Mix: _plugin.Mix,
            CutoffHz: _plugin.CutoffHz,
            ScaleIndex: _plugin.AmountScaleIndex,
            GateLevel: _smoothedGateLevel,
            HfEnergy: _smoothedHfEnergy,
            SaturationAmount: _smoothedSaturation,
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

        if (_renderer.DriveKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.DriveKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_renderer.MixKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.MixKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_renderer.CutoffKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.CutoffKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;

        var hit = _renderer.HitTest(x, y);
        switch (hit.Area)
        {
            case AirExciterHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;
            case AirExciterHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;
            case AirExciterHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;
            case AirExciterHitArea.PresetDropdown:
                _presetHelper.ShowPresetMenu(SkiaCanvas, _renderer.GetPresetDropdownRect());
                e.Handled = true;
                break;
            case AirExciterHitArea.PresetSave:
                _presetHelper.ShowSaveMenu(SkiaCanvas, this);
                e.Handled = true;
                break;
            case AirExciterHitArea.ScaleToggle:
                _parameterCallback(AirExciterPlugin.ScaleIndex, hit.KnobIndex);
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

        _renderer.DriveKnob.HandleMouseMove(x, y, leftDown);
        _renderer.MixKnob.HandleMouseMove(x, y, leftDown);
        _renderer.CutoffKnob.HandleMouseMove(x, y, leftDown);
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _renderer.DriveKnob.HandleMouseUp(e.ChangedButton);
        _renderer.MixKnob.HandleMouseUp(e.ChangedButton);
        _renderer.CutoffKnob.HandleMouseUp(e.ChangedButton);

        if (e.ChangedButton == MouseButton.Left)
            SkiaCanvas.ReleaseMouseCapture();
    }

    private void ApplyPreset(string presetName, IReadOnlyDictionary<string, float> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            int paramIndex = name switch
            {
                "Drive" => AirExciterPlugin.DriveIndex,
                "Scale" => AirExciterPlugin.ScaleIndex,
                "Mix" => AirExciterPlugin.MixIndex,
                "Cutoff" => AirExciterPlugin.CutoffIndex,
                _ => -1
            };
            if (paramIndex >= 0) _parameterCallback(paramIndex, value);
        }
    }

    private Dictionary<string, float> GetCurrentParameters()
    {
        return new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            ["Drive"] = _plugin.Drive,
            ["Scale"] = _plugin.AmountScaleIndex,
            ["Mix"] = _plugin.Mix,
            ["Cutoff"] = _plugin.CutoffHz
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
