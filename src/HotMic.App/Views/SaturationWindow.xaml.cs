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

public partial class SaturationWindow : Window
{
    private readonly SaturationRenderer _renderer = new();
    private readonly SaturationPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginPresetHelper _presetHelper;

    private float _smoothedInputLevel;
    private float _smoothedOutputLevel;

    public SaturationWindow(SaturationPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
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
        _renderer.WarmthKnob.ValueChanged += value =>
        {
            _parameterCallback(SaturationPlugin.WarmthIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.BlendKnob.ValueChanged += value =>
        {
            _parameterCallback(SaturationPlugin.BlendIndex, value);
            _presetHelper.MarkAsCustom();
        };

        var preferredSize = SaturationRenderer.GetPreferredSize();
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
        float rawInput = _plugin.GetAndResetInputLevel();
        float rawOutput = _plugin.GetAndResetOutputLevel();

        _smoothedInputLevel = _smoothedInputLevel * 0.7f + rawInput * 0.3f;
        _smoothedOutputLevel = _smoothedOutputLevel * 0.7f + rawOutput * 0.3f;

        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var diagnostics = _plugin.Diagnostics;
        var state = new SaturationState(
            WarmthPct: _plugin.WarmthPct,
            BlendPct: _plugin.BlendPct,
            InputLevel: _smoothedInputLevel,
            OutputLevel: _smoothedOutputLevel,
            LatencyMs: _plugin.SampleRate > 0 ? _plugin.LatencySamples * 1000f / _plugin.SampleRate : 0f,
            IsBypassed: _plugin.IsBypassed,
            PresetName: _presetHelper.CurrentPresetName,
            SampleRate: _plugin.SampleRate,
            GetTransferCurveSamples: diagnostics.GetTransferCurveSamples,
            GetScopeSamples: diagnostics.GetScopeSamples,
            GetFftSamples: diagnostics.GetFftSamples
        );

        _renderer.Render(canvas, size, dpiScale, state);
    }

    private void SkiaCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        // Let knobs handle their own input (drag, right-click edit)
        foreach (var knob in new[] { _renderer.WarmthKnob, _renderer.BlendKnob })
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
            case SaturationHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;

            case SaturationHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;

            case SaturationHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;

            case SaturationHitArea.PresetDropdown:
                _presetHelper.ShowPresetMenu(SkiaCanvas, _renderer.GetPresetDropdownRect());
                e.Handled = true;
                break;

            case SaturationHitArea.PresetSave:
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
        foreach (var knob in new[] { _renderer.WarmthKnob, _renderer.BlendKnob })
        {
            knob.HandleMouseMove(x, y, e.LeftButton == MouseButtonState.Pressed);
        }
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        // Let knobs handle mouse up
        foreach (var knob in new[] { _renderer.WarmthKnob, _renderer.BlendKnob })
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
                "Warmth" => SaturationPlugin.WarmthIndex,
                "Blend" => SaturationPlugin.BlendIndex,
                "Drive" => SaturationPlugin.WarmthIndex,
                "Mix" => SaturationPlugin.BlendIndex,
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
            ["Warmth"] = _plugin.WarmthPct,
            ["Blend"] = _plugin.BlendPct
        };
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
