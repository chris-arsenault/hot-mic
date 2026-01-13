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

public partial class VoiceGateWindow : Window
{
    private readonly VoiceGateRenderer _renderer = new();
    private readonly SileroVoiceGatePlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginPresetHelper _presetHelper;

    private int _activeKnob = -1;
    private float _dragStartY;
    private float _dragStartValue;
    private int _hoveredKnob = -1;

    public VoiceGateWindow(SileroVoiceGatePlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
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

        var preferredSize = VoiceGateRenderer.GetPreferredSize();
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
        float vad = _plugin.VadProbability;
        float threshold = GetParameterValue(SileroVoiceGatePlugin.ThresholdIndex);
        bool gateOpen = vad >= threshold;

        float inputLevel = _plugin.GetAndResetInputLevel();
        _renderer.WaveformBuffer.Push(inputLevel, gateOpen);
        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        float threshold = GetParameterValue(SileroVoiceGatePlugin.ThresholdIndex);
        float vad = _plugin.VadProbability;

        var state = new VoiceGateState(
            Threshold: threshold,
            AttackMs: GetParameterValue(SileroVoiceGatePlugin.AttackIndex),
            ReleaseMs: GetParameterValue(SileroVoiceGatePlugin.ReleaseIndex),
            HoldMs: GetParameterValue(SileroVoiceGatePlugin.HoldIndex),
            VadProbability: vad,
            IsGateOpen: vad >= threshold,
            IsBypassed: _plugin.IsBypassed,
            StatusMessage: _plugin.StatusMessage,
            HoveredKnob: _hoveredKnob,
            PresetName: _presetHelper.CurrentPresetName
        );

        _renderer.Render(canvas, size, dpiScale, state);
    }

    private void SkiaCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        var hit = _renderer.HitTest(x, y);

        switch (hit.Area)
        {
            case VoiceGateHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;

            case VoiceGateHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;

            case VoiceGateHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;

            case VoiceGateHitArea.Knob:
                _activeKnob = hit.KnobIndex;
                _dragStartY = y;
                _dragStartValue = GetKnobNormalizedValue(hit.KnobIndex);
                SkiaCanvas.CaptureMouse();
                e.Handled = true;
                break;

            case VoiceGateHitArea.PresetDropdown:
                _presetHelper.ShowPresetMenu(SkiaCanvas, _renderer.GetPresetDropdownRect());
                e.Handled = true;
                break;

            case VoiceGateHitArea.PresetSave:
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

        if (_activeKnob >= 0 && e.LeftButton == MouseButtonState.Pressed)
        {
            float deltaY = _dragStartY - y;
            float newNormalized = RotaryKnob.CalculateValueFromDrag(_dragStartValue, -deltaY, 0.003f);
            ApplyKnobValue(_activeKnob, newNormalized);
            e.Handled = true;
        }
        else
        {
            var hit = _renderer.HitTest(x, y);
            _hoveredKnob = hit.Area == VoiceGateHitArea.Knob ? hit.KnobIndex : -1;
        }
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        _activeKnob = -1;
        SkiaCanvas.ReleaseMouseCapture();
    }

    private float GetParameterValue(int index)
    {
        var param = _plugin.Parameters[index];
        return index switch
        {
            SileroVoiceGatePlugin.ThresholdIndex => GetStoredValue(0, param.DefaultValue),
            SileroVoiceGatePlugin.AttackIndex => GetStoredValue(1, param.DefaultValue),
            SileroVoiceGatePlugin.ReleaseIndex => GetStoredValue(2, param.DefaultValue),
            SileroVoiceGatePlugin.HoldIndex => GetStoredValue(3, param.DefaultValue),
            _ => param.DefaultValue
        };
    }

    private float GetStoredValue(int stateIndex, float defaultValue)
    {
        var state = _plugin.GetState();
        if (state.Length >= (stateIndex + 1) * sizeof(float))
        {
            return BitConverter.ToSingle(state, stateIndex * sizeof(float));
        }
        return defaultValue;
    }

    private float GetKnobNormalizedValue(int knobIndex)
    {
        return knobIndex switch
        {
            0 => (GetParameterValue(SileroVoiceGatePlugin.ThresholdIndex) - 0.05f) / (0.95f - 0.05f),
            1 => (GetParameterValue(SileroVoiceGatePlugin.AttackIndex) - 1f) / (50f - 1f),
            2 => (GetParameterValue(SileroVoiceGatePlugin.ReleaseIndex) - 20f) / (500f - 20f),
            3 => GetParameterValue(SileroVoiceGatePlugin.HoldIndex) / 300f,
            _ => 0f
        };
    }

    private void ApplyKnobValue(int knobIndex, float normalizedValue)
    {
        float value = knobIndex switch
        {
            0 => 0.05f + normalizedValue * 0.90f,        // Threshold: 0.05 to 0.95
            1 => 1f + normalizedValue * 49f,             // Attack: 1 to 50 ms
            2 => 20f + normalizedValue * 480f,           // Release: 20 to 500 ms
            3 => normalizedValue * 300f,                 // Hold: 0 to 300 ms
            _ => 0f
        };

        int paramIndex = knobIndex switch
        {
            0 => SileroVoiceGatePlugin.ThresholdIndex,
            1 => SileroVoiceGatePlugin.AttackIndex,
            2 => SileroVoiceGatePlugin.ReleaseIndex,
            3 => SileroVoiceGatePlugin.HoldIndex,
            _ => -1
        };

        if (paramIndex >= 0)
        {
            _parameterCallback(paramIndex, value);
            _presetHelper.MarkAsCustom();
        }
    }

    private void ApplyPreset(string presetName, IReadOnlyDictionary<string, float> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            int paramIndex = name switch
            {
                "Threshold" => SileroVoiceGatePlugin.ThresholdIndex,
                "Attack" => SileroVoiceGatePlugin.AttackIndex,
                "Release" => SileroVoiceGatePlugin.ReleaseIndex,
                "Hold" => SileroVoiceGatePlugin.HoldIndex,
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
            ["Threshold"] = GetParameterValue(SileroVoiceGatePlugin.ThresholdIndex),
            ["Attack"] = GetParameterValue(SileroVoiceGatePlugin.AttackIndex),
            ["Release"] = GetParameterValue(SileroVoiceGatePlugin.ReleaseIndex),
            ["Hold"] = GetParameterValue(SileroVoiceGatePlugin.HoldIndex)
        };
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
