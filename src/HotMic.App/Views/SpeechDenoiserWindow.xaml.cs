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

public partial class SpeechDenoiserWindow : Window
{
    private readonly SpeechDenoiserRenderer _renderer = new();
    private readonly SpeechDenoiserPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginPresetHelper _presetHelper;

    public SpeechDenoiserWindow(SpeechDenoiserPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
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

        var preferredSize = SpeechDenoiserRenderer.GetPreferredSize();
        Width = preferredSize.Width;
        Height = preferredSize.Height;

        // Wire up knob ValueChanged events
        _renderer.MixKnob.ValueChanged += value =>
        {
            _parameterCallback(SpeechDenoiserPlugin.DryWetIndex, value);
            _presetHelper.MarkAsCustom();
        };
        _renderer.AttenLimitKnob.ValueChanged += value =>
        {
            _parameterCallback(SpeechDenoiserPlugin.AttenLimitIndex, value);
            _presetHelper.MarkAsCustom();
        };

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

        float sampleRate = 48000f;
        float latencyMs = _plugin.LatencySamples > 0 ? _plugin.LatencySamples * 1000f / sampleRate : 0f;

        var state = new SpeechDenoiserState(
            MixPercent: GetParameterValue(SpeechDenoiserPlugin.DryWetIndex),
            AttenLimitDb: GetParameterValue(SpeechDenoiserPlugin.AttenLimitIndex),
            AttenEnabled: GetParameterValue(SpeechDenoiserPlugin.AttenEnableIndex) >= 0.5f,
            LatencyMs: latencyMs,
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

        // Check if any knob handles the mouse first
        if (_renderer.MixKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.MixKnob.IsDragging)
            {
                SkiaCanvas.CaptureMouse();
            }
            e.Handled = true;
            return;
        }
        if (_renderer.AttenLimitKnob.HandleMouseDown(x, y, e.ChangedButton, SkiaCanvas))
        {
            if (_renderer.AttenLimitKnob.IsDragging)
            {
                SkiaCanvas.CaptureMouse();
            }
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var hit = _renderer.HitTest(x, y);

        switch (hit.Area)
        {
            case SpeechDenoiserHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;

            case SpeechDenoiserHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;

            case SpeechDenoiserHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;

            case SpeechDenoiserHitArea.AttenLimitToggle:
                float currentValue = GetParameterValue(SpeechDenoiserPlugin.AttenEnableIndex);
                _parameterCallback(SpeechDenoiserPlugin.AttenEnableIndex, currentValue >= 0.5f ? 0f : 1f);
                _presetHelper.MarkAsCustom();
                e.Handled = true;
                break;

            case SpeechDenoiserHitArea.PresetDropdown:
                _presetHelper.ShowPresetMenu(SkiaCanvas, _renderer.GetPresetDropdownRect());
                e.Handled = true;
                break;

            case SpeechDenoiserHitArea.PresetSave:
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

        // Forward to all knobs
        _renderer.MixKnob.HandleMouseMove(x, y, leftDown);
        _renderer.AttenLimitKnob.HandleMouseMove(x, y, leftDown);
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        // Forward to all knobs
        _renderer.MixKnob.HandleMouseUp(e.ChangedButton);
        _renderer.AttenLimitKnob.HandleMouseUp(e.ChangedButton);

        if (e.ChangedButton == MouseButton.Left)
        {
            SkiaCanvas.ReleaseMouseCapture();
        }
    }

    private float GetParameterValue(int index)
    {
        var state = _plugin.GetState();
        if (state.Length >= (index + 1) * sizeof(float))
        {
            return BitConverter.ToSingle(state, index * sizeof(float));
        }
        return _plugin.Parameters[index].DefaultValue;
    }

    private void ApplyPreset(string presetName, IReadOnlyDictionary<string, float> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            int paramIndex = name switch
            {
                "DryWet" => SpeechDenoiserPlugin.DryWetIndex,
                "AttenLimit" => SpeechDenoiserPlugin.AttenLimitIndex,
                "AttenEnabled" => SpeechDenoiserPlugin.AttenEnableIndex,
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
            ["DryWet"] = GetParameterValue(SpeechDenoiserPlugin.DryWetIndex),
            ["AttenLimit"] = GetParameterValue(SpeechDenoiserPlugin.AttenLimitIndex),
            ["AttenEnabled"] = GetParameterValue(SpeechDenoiserPlugin.AttenEnableIndex) >= 0.5f ? 1f : 0f
        };
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
