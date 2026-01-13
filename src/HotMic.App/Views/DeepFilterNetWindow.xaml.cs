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

public partial class DeepFilterNetWindow : Window
{
    private readonly DeepFilterNetRenderer _renderer = new();
    private readonly DeepFilterNetPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginPresetHelper _presetHelper;

    private int _activeKnob = -1;
    private float _dragStartY;
    private float _dragStartValue;
    private int _hoveredKnob = -1;

    public DeepFilterNetWindow(DeepFilterNetPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
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

        var preferredSize = DeepFilterNetRenderer.GetPreferredSize();
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

        float sampleRate = 48000f; // DeepFilterNet requires 48kHz
        float latencyMs = _plugin.LatencySamples > 0 ? _plugin.LatencySamples * 1000f / sampleRate : 0f;

        string statusMessage = _plugin.StatusMessage;
        if (string.IsNullOrEmpty(statusMessage))
        {
            statusMessage = $"Buffers: dropped {_plugin.InputDropSamples} / short {_plugin.OutputUnderrunSamples}";
        }

        var state = new DeepFilterNetState(
            ReductionPercent: GetParameterValue(DeepFilterNetPlugin.ReductionIndex),
            AttenuationLimitDb: GetParameterValue(DeepFilterNetPlugin.AttenuationIndex),
            PostFilterEnabled: GetParameterValue(DeepFilterNetPlugin.PostFilterIndex) >= 0.5f,
            GainReductionDb: _plugin.GainReductionDb,
            LatencyMs: latencyMs,
            IsBypassed: _plugin.IsBypassed,
            StatusMessage: statusMessage,
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
            case DeepFilterNetHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;

            case DeepFilterNetHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;

            case DeepFilterNetHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;

            case DeepFilterNetHitArea.PostFilterToggle:
                float currentValue = GetParameterValue(DeepFilterNetPlugin.PostFilterIndex);
                _parameterCallback(DeepFilterNetPlugin.PostFilterIndex, currentValue >= 0.5f ? 0f : 1f);
                _presetHelper.MarkAsCustom();
                e.Handled = true;
                break;

            case DeepFilterNetHitArea.Knob:
                _activeKnob = hit.KnobIndex;
                _dragStartY = y;
                _dragStartValue = GetKnobNormalizedValue(hit.KnobIndex);
                SkiaCanvas.CaptureMouse();
                e.Handled = true;
                break;

            case DeepFilterNetHitArea.PresetDropdown:
                _presetHelper.ShowPresetMenu(SkiaCanvas, _renderer.GetPresetDropdownRect());
                e.Handled = true;
                break;

            case DeepFilterNetHitArea.PresetSave:
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
            _hoveredKnob = hit.Area == DeepFilterNetHitArea.Knob ? hit.KnobIndex : -1;
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
        var state = _plugin.GetState();
        if (state.Length >= (index + 1) * sizeof(float))
        {
            return BitConverter.ToSingle(state, index * sizeof(float));
        }
        return _plugin.Parameters[index].DefaultValue;
    }

    private float GetKnobNormalizedValue(int knobIndex)
    {
        return knobIndex switch
        {
            0 => GetParameterValue(DeepFilterNetPlugin.ReductionIndex) / 100f,
            1 => GetParameterValue(DeepFilterNetPlugin.AttenuationIndex) / 100f,
            _ => 0f
        };
    }

    private void ApplyKnobValue(int knobIndex, float normalizedValue)
    {
        float value = knobIndex switch
        {
            0 => normalizedValue * 100f,           // Reduction: 0 to 100%
            1 => normalizedValue * 100f,           // Attenuation: 0 to 100 dB
            _ => 0f
        };

        int paramIndex = knobIndex switch
        {
            0 => DeepFilterNetPlugin.ReductionIndex,
            1 => DeepFilterNetPlugin.AttenuationIndex,
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
                "Reduction" => DeepFilterNetPlugin.ReductionIndex,
                "Attenuation" => DeepFilterNetPlugin.AttenuationIndex,
                "PostFilter" => DeepFilterNetPlugin.PostFilterIndex,
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
            ["Reduction"] = GetParameterValue(DeepFilterNetPlugin.ReductionIndex),
            ["Attenuation"] = GetParameterValue(DeepFilterNetPlugin.AttenuationIndex),
            ["PostFilter"] = GetParameterValue(DeepFilterNetPlugin.PostFilterIndex) >= 0.5f ? 1f : 0f
        };
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
