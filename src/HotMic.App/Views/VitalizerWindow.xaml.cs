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

public partial class VitalizerWindow : Window, IDisposable
{
    private readonly VitalizerRenderer _renderer = new();
    private readonly VitalizerMk2TPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginPresetHelper _presetHelper;
    private bool _disposed;

    public VitalizerWindow(VitalizerMk2TPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
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
            _parameterCallback(VitalizerMk2TPlugin.DriveIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.BassKnob.ValueChanged += value =>
        {
            _parameterCallback(VitalizerMk2TPlugin.BassIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.BassCompKnob.ValueChanged += value =>
        {
            _parameterCallback(VitalizerMk2TPlugin.BassCompIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.MidHiKnob.ValueChanged += value =>
        {
            _parameterCallback(VitalizerMk2TPlugin.MidHiTuneIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.ProcessKnob.ValueChanged += value =>
        {
            _parameterCallback(VitalizerMk2TPlugin.ProcessIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.HighFreqKnob.ValueChanged += value =>
        {
            _parameterCallback(VitalizerMk2TPlugin.HighFreqIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.IntensityKnob.ValueChanged += value =>
        {
            _parameterCallback(VitalizerMk2TPlugin.IntensityIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.HighCompKnob.ValueChanged += value =>
        {
            _parameterCallback(VitalizerMk2TPlugin.HighCompIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.OutputKnob.ValueChanged += value =>
        {
            _parameterCallback(VitalizerMk2TPlugin.OutputIndex, value);
            _presetHelper.MarkAsCustom();
        };

        var preferredSize = VitalizerRenderer.GetPreferredSize();
        Width = preferredSize.Width;
        Height = preferredSize.Height;

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += (_, _) => SkiaCanvas.InvalidateVisual();
        Loaded += (_, _) => _renderTimer.Start();
        Closed += (_, _) => Dispose();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var state = new VitalizerState(
            DriveDb: _plugin.DriveDb,
            Bass: _plugin.Bass,
            BassCompRatio: _plugin.BassCompRatio,
            BassLcEnabled: _plugin.BassLcEnabled,
            MidHiTuneHz: _plugin.MidHiTuneHz,
            Process: _plugin.ProcessAmount,
            HighFreqHz: _plugin.HighFreqHz,
            Intensity: _plugin.IntensityAmount,
            HighCompRatio: _plugin.HighCompRatio,
            HighLcEnabled: _plugin.HighLcEnabled,
            TubeEnabled: _plugin.TubeEnabled,
            OutputDb: _plugin.OutputDb,
            LimitEnabled: _plugin.LimitEnabled,
            LatencyMs: _plugin.SampleRate > 0 ? _plugin.LatencySamples * 1000f / _plugin.SampleRate : 0f,
            IsBypassed: _plugin.IsBypassed,
            PresetName: _presetHelper.CurrentPresetName);

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
        if (_renderer.BassKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.BassKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_renderer.BassCompKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.BassCompKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_renderer.MidHiKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.MidHiKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_renderer.ProcessKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.ProcessKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_renderer.HighFreqKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.HighFreqKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_renderer.IntensityKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.IntensityKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_renderer.HighCompKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.HighCompKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_renderer.OutputKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.OutputKnob.IsDragging) SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;

        var hit = _renderer.HitTest(x, y);
        switch (hit.Area)
        {
            case VitalizerHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;
            case VitalizerHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;
            case VitalizerHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;
            case VitalizerHitArea.PresetDropdown:
                _presetHelper.ShowPresetMenu(SkiaCanvas, _renderer.GetPresetDropdownRect());
                e.Handled = true;
                break;
            case VitalizerHitArea.PresetSave:
                _presetHelper.ShowSaveMenu(SkiaCanvas, this);
                e.Handled = true;
                break;
            case VitalizerHitArea.BassLcToggle:
                _parameterCallback(VitalizerMk2TPlugin.BassLcIndex, _plugin.BassLcEnabled ? 0f : 1f);
                _presetHelper.MarkAsCustom();
                e.Handled = true;
                break;
            case VitalizerHitArea.HighLcToggle:
                _parameterCallback(VitalizerMk2TPlugin.HighLcIndex, _plugin.HighLcEnabled ? 0f : 1f);
                _presetHelper.MarkAsCustom();
                e.Handled = true;
                break;
            case VitalizerHitArea.TubeToggle:
                _parameterCallback(VitalizerMk2TPlugin.TubeIndex, _plugin.TubeEnabled ? 0f : 1f);
                _presetHelper.MarkAsCustom();
                e.Handled = true;
                break;
            case VitalizerHitArea.LimitToggle:
                _parameterCallback(VitalizerMk2TPlugin.LimitIndex, _plugin.LimitEnabled ? 0f : 1f);
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
        _renderer.BassKnob.HandleMouseMove(x, y, leftDown);
        _renderer.BassCompKnob.HandleMouseMove(x, y, leftDown);
        _renderer.MidHiKnob.HandleMouseMove(x, y, leftDown);
        _renderer.ProcessKnob.HandleMouseMove(x, y, leftDown);
        _renderer.HighFreqKnob.HandleMouseMove(x, y, leftDown);
        _renderer.IntensityKnob.HandleMouseMove(x, y, leftDown);
        _renderer.HighCompKnob.HandleMouseMove(x, y, leftDown);
        _renderer.OutputKnob.HandleMouseMove(x, y, leftDown);
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _renderer.DriveKnob.HandleMouseUp(e.ChangedButton);
        _renderer.BassKnob.HandleMouseUp(e.ChangedButton);
        _renderer.BassCompKnob.HandleMouseUp(e.ChangedButton);
        _renderer.MidHiKnob.HandleMouseUp(e.ChangedButton);
        _renderer.ProcessKnob.HandleMouseUp(e.ChangedButton);
        _renderer.HighFreqKnob.HandleMouseUp(e.ChangedButton);
        _renderer.IntensityKnob.HandleMouseUp(e.ChangedButton);
        _renderer.HighCompKnob.HandleMouseUp(e.ChangedButton);
        _renderer.OutputKnob.HandleMouseUp(e.ChangedButton);

        if (e.ChangedButton == MouseButton.Left)
        {
            SkiaCanvas.ReleaseMouseCapture();
        }
    }

    private void ApplyPreset(string presetName, IReadOnlyDictionary<string, float> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            int paramIndex = name switch
            {
                "Drive" => VitalizerMk2TPlugin.DriveIndex,
                "Bass" => VitalizerMk2TPlugin.BassIndex,
                "Bass Comp" => VitalizerMk2TPlugin.BassCompIndex,
                "Bass LC" => VitalizerMk2TPlugin.BassLcIndex,
                "Mid-Hi Tune" => VitalizerMk2TPlugin.MidHiTuneIndex,
                "Process" => VitalizerMk2TPlugin.ProcessIndex,
                "High Freq" => VitalizerMk2TPlugin.HighFreqIndex,
                "Intensity" => VitalizerMk2TPlugin.IntensityIndex,
                "High Comp" => VitalizerMk2TPlugin.HighCompIndex,
                "High LC" => VitalizerMk2TPlugin.HighLcIndex,
                "Tube" => VitalizerMk2TPlugin.TubeIndex,
                "Output" => VitalizerMk2TPlugin.OutputIndex,
                "Limit" => VitalizerMk2TPlugin.LimitIndex,
                _ => -1
            };
            if (paramIndex >= 0) _parameterCallback(paramIndex, value);
        }
    }

    private Dictionary<string, float> GetCurrentParameters()
    {
        return new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            ["Drive"] = _plugin.DriveDb,
            ["Bass"] = _plugin.Bass,
            ["Bass Comp"] = _plugin.BassCompRatio,
            ["Bass LC"] = _plugin.BassLcEnabled ? 1f : 0f,
            ["Mid-Hi Tune"] = _plugin.MidHiTuneHz,
            ["Process"] = _plugin.ProcessAmount,
            ["High Freq"] = _plugin.HighFreqHz,
            ["Intensity"] = _plugin.IntensityAmount,
            ["High Comp"] = _plugin.HighCompRatio,
            ["High LC"] = _plugin.HighLcEnabled ? 1f : 0f,
            ["Tube"] = _plugin.TubeEnabled ? 1f : 0f,
            ["Output"] = _plugin.OutputDb,
            ["Limit"] = _plugin.LimitEnabled ? 1f : 0f
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
