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

public partial class ReverbWindow : Window
{
    private readonly ReverbRenderer _renderer = new();
    private readonly ConvolutionReverbPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginPresetHelper _presetHelper;

    public ReverbWindow(ConvolutionReverbPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
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
        _renderer.DryWetKnob.ValueChanged += value =>
        {
            _parameterCallback(ConvolutionReverbPlugin.DryWetIndex, value / 100f); // Convert from percentage
            _presetHelper.MarkAsCustom();
        };
        _renderer.DecayKnob.ValueChanged += value =>
        {
            _parameterCallback(ConvolutionReverbPlugin.DecayIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.PreDelayKnob.ValueChanged += value =>
        {
            _parameterCallback(ConvolutionReverbPlugin.PreDelayIndex, value);
            _presetHelper.MarkAsCustom();
        };

        var preferredSize = ReverbRenderer.GetPreferredSize();
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
        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var state = new ReverbState(
            DryWet: _plugin.DryWet,
            Decay: _plugin.Decay,
            PreDelayMs: _plugin.PreDelayMs,
            IrPreset: _plugin.IrPreset,
            IsIrLoaded: _plugin.IsIrLoaded,
            StatusMessage: _plugin.StatusMessage,
            LoadedIrPath: _plugin.LoadedIrPath,
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
        if (_renderer.DryWetKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.DryWetKnob.IsDragging)
                SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_renderer.DecayKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.DecayKnob.IsDragging)
                SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_renderer.PreDelayKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.PreDelayKnob.IsDragging)
                SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
            return;

        var hit = _renderer.HitTest(x, y);

        switch (hit.Area)
        {
            case ReverbHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;

            case ReverbHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;

            case ReverbHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;

            case ReverbHitArea.LoadButton:
                LoadCustomIr();
                e.Handled = true;
                break;

            case ReverbHitArea.Preset:
                _parameterCallback(ConvolutionReverbPlugin.IrPresetIndex, hit.Index);
                _presetHelper.MarkAsCustom();
                e.Handled = true;
                break;

            case ReverbHitArea.PresetDropdown:
                _presetHelper.ShowPresetMenu(SkiaCanvas, _renderer.GetPresetDropdownRect());
                e.Handled = true;
                break;

            case ReverbHitArea.PresetSave:
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
        bool isPressed = e.LeftButton == MouseButtonState.Pressed;
        _renderer.DryWetKnob.HandleMouseMove(x, y, isPressed);
        _renderer.DecayKnob.HandleMouseMove(x, y, isPressed);
        _renderer.PreDelayKnob.HandleMouseMove(x, y, isPressed);
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _renderer.DryWetKnob.HandleMouseUp(e.ChangedButton);
        _renderer.DecayKnob.HandleMouseUp(e.ChangedButton);
        _renderer.PreDelayKnob.HandleMouseUp(e.ChangedButton);

        if (e.ChangedButton == MouseButton.Left)
            SkiaCanvas.ReleaseMouseCapture();
    }

    private void LoadCustomIr()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Load Impulse Response",
            Filter = "Audio Files|*.wav;*.aif;*.aiff;*.flac|WAV Files|*.wav|All Files|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            _plugin.LoadImpulseResponse(dialog.FileName);
            SkiaCanvas.InvalidateVisual();
        }
    }

    private void ApplyPreset(string presetName, IReadOnlyDictionary<string, float> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            int paramIndex = name switch
            {
                "DryWet" => ConvolutionReverbPlugin.DryWetIndex,
                "Decay" => ConvolutionReverbPlugin.DecayIndex,
                "PreDelay" => ConvolutionReverbPlugin.PreDelayIndex,
                "IrPreset" => ConvolutionReverbPlugin.IrPresetIndex,
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
            ["DryWet"] = _plugin.DryWet,
            ["Decay"] = _plugin.Decay,
            ["PreDelay"] = _plugin.PreDelayMs,
            ["IrPreset"] = _plugin.IrPreset
        };
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
