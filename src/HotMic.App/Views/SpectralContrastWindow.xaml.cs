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

public partial class SpectralContrastWindow : Window, IDisposable
{
    private readonly SpectralContrastRenderer _renderer = new();
    private readonly SpectralContrastPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginPresetHelper _presetHelper;

    private float _smoothedSpeechGate;
    private float _smoothedContrastStrength;
    private readonly float[] _smoothedMagnitudes = new float[256];
    private long _lastDebugTick;
    private bool _disposed;

    public SpectralContrastWindow(SpectralContrastPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
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

        _renderer.StrengthKnob.ValueChanged += value =>
        {
            _parameterCallback(SpectralContrastPlugin.StrengthIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.MixKnob.ValueChanged += value =>
        {
            _parameterCallback(SpectralContrastPlugin.MixIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.GateStrengthKnob.ValueChanged += value =>
        {
            _parameterCallback(SpectralContrastPlugin.GateStrengthIndex, value);
            _presetHelper.MarkAsCustom();
        };

        var preferredSize = SpectralContrastRenderer.GetPreferredSize();
        Width = preferredSize.Width;
        Height = preferredSize.Height;

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += OnRenderTick;
        Loaded += (_, _) => _renderTimer.Start();
        Closed += (_, _) => Dispose();
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        float rawSpeech = _plugin.GetSpeechGate();
        float rawGateApplied = _plugin.GetAppliedGate();
        float rawStrength = _plugin.GetContrastStrength();
        float rawContrastMean = _plugin.GetContrastMeanAbs();
        float rawContrastPeak = _plugin.GetContrastPeakAbs();
        float rawGainMean = _plugin.GetGainMean();
        float rawGainPeak = _plugin.GetGainPeak();

        _smoothedSpeechGate = _smoothedSpeechGate * 0.7f + rawSpeech * 0.3f;
        _smoothedContrastStrength = _smoothedContrastStrength * 0.8f + rawStrength * 0.2f;

        // Get and smooth magnitudes
        int binCount = _plugin.MagnitudeBinCount;
        Span<float> mags = stackalloc float[binCount];
        _plugin.GetMagnitudeSpectrum(mags);
        int count = Math.Min(binCount, _smoothedMagnitudes.Length);
        for (int i = 0; i < count; i++)
        {
            _smoothedMagnitudes[i] = _smoothedMagnitudes[i] * 0.7f + mags[i] * 0.3f;
        }

        if (EnhanceDebug.ShouldLog(ref _lastDebugTick))
        {
            float scale = EnhanceDebug.ScaleFactor(_plugin.StrengthScaleIndex);
            EnhanceDebug.Log("SpectralContrast",
                $"scale=x{scale:0} idx={_plugin.StrengthScaleIndex} strength={_plugin.StrengthPct:0} mix={_plugin.MixPct:0} " +
                $"gateStrength={_plugin.GateStrength:0.00} speech={rawSpeech:0.000} gateApplied={rawGateApplied:0.000} " +
                $"strengthApplied={rawStrength:0.000} contrastAbs={rawContrastMean:0.000} contrastPeak={rawContrastPeak:0.000} " +
                $"gainMean={rawGainMean:0.000} gainPeak={rawGainPeak:0.000} bins={_plugin.MagnitudeBinCount} status=\"{_plugin.StatusMessage}\"");
        }

        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var state = new SpectralContrastState(
            StrengthPct: _plugin.StrengthPct,
            MixPct: _plugin.MixPct,
            GateStrength: _plugin.GateStrength,
            ScaleIndex: _plugin.StrengthScaleIndex,
            SpeechGate: _smoothedSpeechGate,
            ContrastStrength: _smoothedContrastStrength,
            Magnitudes: _smoothedMagnitudes,
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

        if (_renderer.StrengthKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.StrengthKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_renderer.MixKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.MixKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_renderer.GateStrengthKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.GateStrengthKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;

        var hit = _renderer.HitTest(x, y);
        switch (hit.Area)
        {
            case SpectralContrastHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;
            case SpectralContrastHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;
            case SpectralContrastHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;
            case SpectralContrastHitArea.PresetDropdown:
                _presetHelper.ShowPresetMenu(SkiaCanvas, _renderer.GetPresetDropdownRect());
                e.Handled = true;
                break;
            case SpectralContrastHitArea.PresetSave:
                _presetHelper.ShowSaveMenu(SkiaCanvas, this);
                e.Handled = true;
                break;
            case SpectralContrastHitArea.ScaleToggle:
                _parameterCallback(SpectralContrastPlugin.ScaleIndex, hit.KnobIndex);
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

        _renderer.StrengthKnob.HandleMouseMove(x, y, leftDown);
        _renderer.MixKnob.HandleMouseMove(x, y, leftDown);
        _renderer.GateStrengthKnob.HandleMouseMove(x, y, leftDown);
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _renderer.StrengthKnob.HandleMouseUp(e.ChangedButton);
        _renderer.MixKnob.HandleMouseUp(e.ChangedButton);
        _renderer.GateStrengthKnob.HandleMouseUp(e.ChangedButton);

        if (e.ChangedButton == MouseButton.Left)
            SkiaCanvas.ReleaseMouseCapture();
    }

    private void ApplyPreset(string presetName, IReadOnlyDictionary<string, float> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            int paramIndex = name switch
            {
                "Strength" => SpectralContrastPlugin.StrengthIndex,
                "Scale" => SpectralContrastPlugin.ScaleIndex,
                "Mix" => SpectralContrastPlugin.MixIndex,
                "Gate Strength" or "GateStrength" => SpectralContrastPlugin.GateStrengthIndex,
                _ => -1
            };
            if (paramIndex >= 0) _parameterCallback(paramIndex, value);
        }
    }

    private Dictionary<string, float> GetCurrentParameters()
    {
        return new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            ["Strength"] = _plugin.StrengthPct,
            ["Scale"] = _plugin.StrengthScaleIndex,
            ["Mix"] = _plugin.MixPct,
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
