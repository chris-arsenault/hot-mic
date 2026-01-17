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

public partial class ConsonantTransientWindow : Window
{
    private readonly ConsonantTransientRenderer _renderer = new();
    private readonly ConsonantTransientPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginPresetHelper _presetHelper;

    private float _smoothedUnvoicedGate;
    private float _smoothedFastEnvelope;
    private float _smoothedSlowEnvelope;

    public ConsonantTransientWindow(ConsonantTransientPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
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
            _parameterCallback(ConsonantTransientPlugin.AmountIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.ThresholdKnob.ValueChanged += value =>
        {
            _parameterCallback(ConsonantTransientPlugin.ThresholdIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.HighCutKnob.ValueChanged += value =>
        {
            _parameterCallback(ConsonantTransientPlugin.HighCutIndex, value);
            _presetHelper.MarkAsCustom();
        };

        var preferredSize = ConsonantTransientRenderer.GetPreferredSize();
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
        float rawGate = _plugin.GetUnvoicedGate();
        float rawFast = _plugin.GetFastEnvelope();
        float rawSlow = _plugin.GetSlowEnvelope();

        _smoothedUnvoicedGate = _smoothedUnvoicedGate * 0.7f + rawGate * 0.3f;
        _smoothedFastEnvelope = _smoothedFastEnvelope * 0.6f + rawFast * 0.4f;
        _smoothedSlowEnvelope = _smoothedSlowEnvelope * 0.6f + rawSlow * 0.4f;

        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var state = new ConsonantTransientState(
            Amount: _plugin.Amount,
            Threshold: _plugin.Threshold,
            HighCutHz: _plugin.HighCutHz,
            UnvoicedGate: _smoothedUnvoicedGate,
            FastEnvelope: _smoothedFastEnvelope,
            SlowEnvelope: _smoothedSlowEnvelope,
            TransientDetected: _plugin.GetTransientDetected(),
            LatencyMs: _plugin.SampleRate > 0 ? _plugin.LatencySamples * 1000f / _plugin.SampleRate : 0f,
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
        if (_renderer.HighCutKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.HighCutKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;

        var hit = _renderer.HitTest(x, y);
        switch (hit.Area)
        {
            case ConsonantTransientHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;
            case ConsonantTransientHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;
            case ConsonantTransientHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;
            case ConsonantTransientHitArea.PresetDropdown:
                _presetHelper.ShowPresetMenu(SkiaCanvas, _renderer.GetPresetDropdownRect());
                e.Handled = true;
                break;
            case ConsonantTransientHitArea.PresetSave:
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
        _renderer.ThresholdKnob.HandleMouseMove(x, y, leftDown);
        _renderer.HighCutKnob.HandleMouseMove(x, y, leftDown);
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _renderer.AmountKnob.HandleMouseUp(e.ChangedButton);
        _renderer.ThresholdKnob.HandleMouseUp(e.ChangedButton);
        _renderer.HighCutKnob.HandleMouseUp(e.ChangedButton);

        if (e.ChangedButton == MouseButton.Left)
            SkiaCanvas.ReleaseMouseCapture();
    }

    private void ApplyPreset(string presetName, IReadOnlyDictionary<string, float> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            int paramIndex = name switch
            {
                "Amount" => ConsonantTransientPlugin.AmountIndex,
                "Threshold" => ConsonantTransientPlugin.ThresholdIndex,
                "High Cut" or "HighCut" => ConsonantTransientPlugin.HighCutIndex,
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
            ["Threshold"] = _plugin.Threshold,
            ["High Cut"] = _plugin.HighCutHz
        };
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
