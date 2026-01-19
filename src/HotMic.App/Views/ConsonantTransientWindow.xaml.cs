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

public partial class ConsonantTransientWindow : Window, IDisposable
{
    private readonly ConsonantTransientRenderer _renderer = new();
    private readonly ConsonantTransientPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginPresetHelper _presetHelper;
    private bool _disposed;

    private float _smoothedOnsetGate;
    private float _smoothedFlux;
    private float _smoothedFluxBaseline;

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
        Closed += (_, _) => Dispose();
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        float rawGate = _plugin.GetOnsetGate();
        float rawFlux = _plugin.GetFluxDb();
        float rawBaseline = _plugin.GetFluxBaselineDb();

        _smoothedOnsetGate = _smoothedOnsetGate * 0.7f + rawGate * 0.3f;
        _smoothedFlux = _smoothedFlux * 0.9f + rawFlux * 0.1f;
        _smoothedFluxBaseline = _smoothedFluxBaseline * 0.9f + rawBaseline * 0.1f;

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
            OnsetGate: _smoothedOnsetGate,
            FluxDb: _smoothedFlux,
            FluxBaselineDb: _smoothedFluxBaseline,
            TransientDetected: _plugin.GetTransientDetected(),
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
