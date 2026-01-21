using System;
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

public partial class CompressorWindow : Window, IDisposable
{
    private readonly CompressorRenderer _renderer = new();
    private readonly CompressorPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginPresetHelper _presetHelper;

    private float _smoothedInputLevel;
    private bool _disposed;

    public CompressorWindow(CompressorPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
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

        // Wire up knob value changes
        _renderer.ThresholdKnob.ValueChanged += value =>
        {
            _parameterCallback(CompressorPlugin.ThresholdIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.RatioKnob.ValueChanged += value =>
        {
            _parameterCallback(CompressorPlugin.RatioIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.AttackKnob.ValueChanged += value =>
        {
            _parameterCallback(CompressorPlugin.AttackIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.ReleaseKnob.ValueChanged += value =>
        {
            _parameterCallback(CompressorPlugin.ReleaseIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.KneeKnob.ValueChanged += value =>
        {
            _parameterCallback(CompressorPlugin.KneeIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.MakeupKnob.ValueChanged += value =>
        {
            _parameterCallback(CompressorPlugin.MakeupIndex, value);
            _presetHelper.MarkAsCustom();
        };

        var preferredSize = CompressorRenderer.GetPreferredSize();
        Width = preferredSize.Width;
        Height = preferredSize.Height;

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += OnRenderTick;
        Loaded += (_, _) => _renderTimer.Start();
        Closed += (_, _) => Dispose();
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        // Smooth the input level for display
        float rawLevel = _plugin.GetAndResetInputLevel();
        _smoothedInputLevel = _smoothedInputLevel * 0.7f + rawLevel * 0.3f;

        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var state = new CompressorState(
            ThresholdDb: _plugin.ThresholdDb,
            Ratio: _plugin.Ratio,
            AttackMs: _plugin.AttackMs,
            ReleaseMs: _plugin.ReleaseMs,
            KneeDb: _plugin.KneeDb,
            MakeupDb: _plugin.MakeupDb,
            GainReductionDb: _plugin.GetGainReductionDb(),
            InputLevel: _smoothedInputLevel,
            DetectorMode: _plugin.DetectorMode,
            SidechainHpfEnabled: _plugin.IsSidechainHpfEnabled,
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

        // Let knobs handle their own input (drag, right-click edit)
        foreach (var knob in new[] { _renderer.ThresholdKnob, _renderer.RatioKnob, _renderer.AttackKnob,
                                      _renderer.ReleaseKnob, _renderer.KneeKnob, _renderer.MakeupKnob })
        {
            if (knob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
            {
                if (knob.IsDragging)
                    SkiaCanvas.CaptureMouse();
                e.Handled = true;
                return;
            }
        }

        if (e.ChangedButton != MouseButton.Left)
            return;

        var hit = _renderer.HitTest(x, y);

        switch (hit.Area)
        {
            case CompressorHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;

            case CompressorHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;

            case CompressorHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;

            case CompressorHitArea.DetectorToggle:
                CycleDetectorMode();
                e.Handled = true;
                break;

            case CompressorHitArea.SidechainToggle:
                ToggleSidechain();
                e.Handled = true;
                break;

            case CompressorHitArea.PresetDropdown:
                _presetHelper.ShowPresetMenu(SkiaCanvas, _renderer.GetPresetDropdownRect());
                e.Handled = true;
                break;

            case CompressorHitArea.PresetSave:
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

        // Let knobs handle drag and hover
        foreach (var knob in new[] { _renderer.ThresholdKnob, _renderer.RatioKnob, _renderer.AttackKnob,
                                      _renderer.ReleaseKnob, _renderer.KneeKnob, _renderer.MakeupKnob })
        {
            knob.HandleMouseMove(x, y, e.LeftButton == MouseButtonState.Pressed);
        }
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        // Let knobs handle mouse up
        foreach (var knob in new[] { _renderer.ThresholdKnob, _renderer.RatioKnob, _renderer.AttackKnob,
                                      _renderer.ReleaseKnob, _renderer.KneeKnob, _renderer.MakeupKnob })
        {
            knob.HandleMouseUp(e.ChangedButton);
        }

        if (e.ChangedButton == MouseButton.Left)
            SkiaCanvas.ReleaseMouseCapture();
    }

    private void ApplyPreset(string presetName, IReadOnlyDictionary<string, float> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            int paramIndex = name switch
            {
                "Threshold" => CompressorPlugin.ThresholdIndex,
                "Ratio" => CompressorPlugin.RatioIndex,
                "Attack" => CompressorPlugin.AttackIndex,
                "Release" => CompressorPlugin.ReleaseIndex,
                "Knee" => CompressorPlugin.KneeIndex,
                "Makeup" => CompressorPlugin.MakeupIndex,
                "Detector" => CompressorPlugin.DetectorIndex,
                "Sidechain HPF" => CompressorPlugin.SidechainIndex,
                _ => -1
            };

            if (paramIndex >= 0)
            {
                _parameterCallback(paramIndex, value);
            }
        }
    }

    private Dictionary<string, float> GetCurrentParameters()
    {
        return new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            ["Threshold"] = _plugin.ThresholdDb,
            ["Ratio"] = _plugin.Ratio,
            ["Attack"] = _plugin.AttackMs,
            ["Release"] = _plugin.ReleaseMs,
            ["Knee"] = _plugin.KneeDb,
            ["Makeup"] = _plugin.MakeupDb,
            ["Detector"] = (float)_plugin.DetectorMode,
            ["Sidechain HPF"] = _plugin.IsSidechainHpfEnabled ? 1f : 0f
        };
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }

    private void CycleDetectorMode()
    {
        var next = _plugin.DetectorMode switch
        {
            CompressorDetectorMode.Peak => CompressorDetectorMode.Rms,
            CompressorDetectorMode.Rms => CompressorDetectorMode.Blend,
            _ => CompressorDetectorMode.Peak
        };
        _parameterCallback(CompressorPlugin.DetectorIndex, (float)next);
        _presetHelper.MarkAsCustom();
    }

    private void ToggleSidechain()
    {
        _parameterCallback(CompressorPlugin.SidechainIndex, _plugin.IsSidechainHpfEnabled ? 0f : 1f);
        _presetHelper.MarkAsCustom();
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
