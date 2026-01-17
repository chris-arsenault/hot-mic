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

public partial class BassEnhancerWindow : Window
{
    private readonly BassEnhancerRenderer _renderer = new();
    private readonly BassEnhancerPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginPresetHelper _presetHelper;

    private float _smoothedVoicedGate;
    private float _smoothedBassEnergy;
    private float _smoothedHarmonicAmount;

    public BassEnhancerWindow(BassEnhancerPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
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
            _parameterCallback(BassEnhancerPlugin.AmountIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.DriveKnob.ValueChanged += value =>
        {
            _parameterCallback(BassEnhancerPlugin.DriveIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.MixKnob.ValueChanged += value =>
        {
            _parameterCallback(BassEnhancerPlugin.MixIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.CenterKnob.ValueChanged += value =>
        {
            _parameterCallback(BassEnhancerPlugin.CenterIndex, value);
            _presetHelper.MarkAsCustom();
        };

        var preferredSize = BassEnhancerRenderer.GetPreferredSize();
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
        float rawGate = _plugin.GetVoicedGate();
        float rawBass = _plugin.GetBassEnergy();
        float rawHarmonic = _plugin.GetHarmonicAmount();

        _smoothedVoicedGate = _smoothedVoicedGate * 0.7f + rawGate * 0.3f;
        _smoothedBassEnergy = _smoothedBassEnergy * 0.7f + rawBass * 0.3f;
        _smoothedHarmonicAmount = _smoothedHarmonicAmount * 0.8f + rawHarmonic * 0.2f;

        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var state = new BassEnhancerState(
            Amount: _plugin.Amount,
            Drive: _plugin.Drive,
            Mix: _plugin.Mix,
            CenterHz: _plugin.CenterHz,
            VoicedGate: _smoothedVoicedGate,
            BassEnergy: _smoothedBassEnergy,
            HarmonicAmount: _smoothedHarmonicAmount,
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

        if (_renderer.AmountKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.AmountKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
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
        if (_renderer.CenterKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.CenterKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;

        var hit = _renderer.HitTest(x, y);
        switch (hit.Area)
        {
            case BassEnhancerHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;
            case BassEnhancerHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;
            case BassEnhancerHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;
            case BassEnhancerHitArea.PresetDropdown:
                _presetHelper.ShowPresetMenu(SkiaCanvas, _renderer.GetPresetDropdownRect());
                e.Handled = true;
                break;
            case BassEnhancerHitArea.PresetSave:
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

        _renderer.AmountKnob.HandleMouseMove(x, y, leftDown);
        _renderer.DriveKnob.HandleMouseMove(x, y, leftDown);
        _renderer.MixKnob.HandleMouseMove(x, y, leftDown);
        _renderer.CenterKnob.HandleMouseMove(x, y, leftDown);
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _renderer.AmountKnob.HandleMouseUp(e.ChangedButton);
        _renderer.DriveKnob.HandleMouseUp(e.ChangedButton);
        _renderer.MixKnob.HandleMouseUp(e.ChangedButton);
        _renderer.CenterKnob.HandleMouseUp(e.ChangedButton);

        if (e.ChangedButton == MouseButton.Left)
            SkiaCanvas.ReleaseMouseCapture();
    }

    private void ApplyPreset(string presetName, IReadOnlyDictionary<string, float> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            int paramIndex = name switch
            {
                "Amount" => BassEnhancerPlugin.AmountIndex,
                "Drive" => BassEnhancerPlugin.DriveIndex,
                "Mix" => BassEnhancerPlugin.MixIndex,
                "Center" => BassEnhancerPlugin.CenterIndex,
                _ => -1
            };
            if (paramIndex >= 0) _parameterCallback(paramIndex, value);
        }
    }

    private Dictionary<string, float> GetCurrentParameters()
    {
        return new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            ["Amount"] = _plugin.Amount,
            ["Drive"] = _plugin.Drive,
            ["Mix"] = _plugin.Mix,
            ["Center"] = _plugin.CenterHz
        };
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
