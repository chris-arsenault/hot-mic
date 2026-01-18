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

public partial class FormantEnhancerWindow : Window, IDisposable
{
    private readonly FormantEnhancerRenderer _renderer = new();
    private readonly FormantEnhancerPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginPresetHelper _presetHelper;

    private float _smoothedF1;
    private float _smoothedF2;
    private float _smoothedF3;
    private float _smoothedSpeech;
    private bool _disposed;

    public FormantEnhancerWindow(FormantEnhancerPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
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
            _parameterCallback(FormantEnhancerPlugin.AmountIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.BoostKnob.ValueChanged += value =>
        {
            _parameterCallback(FormantEnhancerPlugin.BoostIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.SmoothingKnob.ValueChanged += value =>
        {
            _parameterCallback(FormantEnhancerPlugin.SmoothingIndex, value);
            _presetHelper.MarkAsCustom();
        };

        var preferredSize = FormantEnhancerRenderer.GetPreferredSize();
        Width = preferredSize.Width;
        Height = preferredSize.Height;

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += OnRenderTick;
        Loaded += (_, _) => _renderTimer.Start();
        Closed += (_, _) => Dispose();
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        float rawF1 = _plugin.GetF1Hz();
        float rawF2 = _plugin.GetF2Hz();
        float rawF3 = _plugin.GetF3Hz();
        float rawSpeech = _plugin.GetSpeechPresence();

        _smoothedF1 = _smoothedF1 * 0.8f + rawF1 * 0.2f;
        _smoothedF2 = _smoothedF2 * 0.8f + rawF2 * 0.2f;
        _smoothedF3 = _smoothedF3 * 0.8f + rawF3 * 0.2f;
        _smoothedSpeech = _smoothedSpeech * 0.7f + rawSpeech * 0.3f;

        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var state = new FormantEnhancerState(
            Amount: _plugin.Amount,
            BoostDb: _plugin.BoostDb,
            SmoothingMs: _plugin.SmoothingMs,
            F1Hz: _smoothedF1,
            F2Hz: _smoothedF2,
            F3Hz: _smoothedF3,
            SpeechPresence: _smoothedSpeech,
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
        if (_renderer.BoostKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.BoostKnob.IsDragging) SkiaCanvas.CaptureMouse();
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
            case FormantEnhancerHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;
            case FormantEnhancerHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;
            case FormantEnhancerHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;
            case FormantEnhancerHitArea.PresetDropdown:
                _presetHelper.ShowPresetMenu(SkiaCanvas, _renderer.GetPresetDropdownRect());
                e.Handled = true;
                break;
            case FormantEnhancerHitArea.PresetSave:
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
        _renderer.BoostKnob.HandleMouseMove(x, y, leftDown);
        _renderer.SmoothingKnob.HandleMouseMove(x, y, leftDown);
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _renderer.AmountKnob.HandleMouseUp(e.ChangedButton);
        _renderer.BoostKnob.HandleMouseUp(e.ChangedButton);
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
                "Amount" => FormantEnhancerPlugin.AmountIndex,
                "Boost" => FormantEnhancerPlugin.BoostIndex,
                "Smoothing" => FormantEnhancerPlugin.SmoothingIndex,
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
            ["Boost"] = _plugin.BoostDb,
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
