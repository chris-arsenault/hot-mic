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

public partial class LimiterWindow : Window
{
    private readonly LimiterRenderer _renderer = new();
    private readonly LimiterPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginPresetHelper _presetHelper;

    private LimiterKnob _activeKnob = LimiterKnob.None;
    private float _dragStartY;
    private float _dragStartValue;
    private LimiterKnob _hoveredKnob = LimiterKnob.None;
    private float _smoothedInputLevel;
    private float _smoothedOutputLevel;
    private float _smoothedGainReduction;

    public LimiterWindow(LimiterPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
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

        var preferredSize = LimiterRenderer.GetPreferredSize();
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
        float rawGr = _plugin.GetGainReductionDb();

        _smoothedInputLevel = _smoothedInputLevel * 0.7f + rawInput * 0.3f;
        _smoothedOutputLevel = _smoothedOutputLevel * 0.7f + rawOutput * 0.3f;
        _smoothedGainReduction = _smoothedGainReduction * 0.8f + rawGr * 0.2f;

        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var state = new LimiterState(
            CeilingDb: _plugin.CeilingDb,
            ReleaseMs: _plugin.ReleaseMs,
            InputLevel: _smoothedInputLevel,
            OutputLevel: _smoothedOutputLevel,
            GainReductionDb: _smoothedGainReduction,
            LatencyMs: _plugin.SampleRate > 0 ? _plugin.LatencySamples * 1000f / _plugin.SampleRate : 0f,
            IsBypassed: _plugin.IsBypassed,
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
            case LimiterHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;

            case LimiterHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;

            case LimiterHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;

            case LimiterHitArea.Knob:
                _activeKnob = hit.Knob;
                _dragStartY = y;
                _dragStartValue = GetKnobNormalizedValue(hit.Knob);
                SkiaCanvas.CaptureMouse();
                e.Handled = true;
                break;

            case LimiterHitArea.PresetDropdown:
                _presetHelper.ShowPresetMenu(SkiaCanvas, _renderer.GetPresetDropdownRect());
                e.Handled = true;
                break;

            case LimiterHitArea.PresetSave:
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

        if (_activeKnob != LimiterKnob.None && e.LeftButton == MouseButtonState.Pressed)
        {
            float deltaY = _dragStartY - y;
            float newNormalized = RotaryKnob.CalculateValueFromDrag(_dragStartValue, -deltaY, 0.004f);
            ApplyKnobValue(_activeKnob, newNormalized);
            e.Handled = true;
        }
        else
        {
            var hit = _renderer.HitTest(x, y);
            _hoveredKnob = hit.Area == LimiterHitArea.Knob ? hit.Knob : LimiterKnob.None;
        }
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        _activeKnob = LimiterKnob.None;
        SkiaCanvas.ReleaseMouseCapture();
    }

    private float GetKnobNormalizedValue(LimiterKnob knob) => knob switch
    {
        LimiterKnob.Ceiling => (_plugin.CeilingDb + 3f) / 3f,
        LimiterKnob.Release => (_plugin.ReleaseMs - 10f) / 190f,
        _ => 0f
    };

    private void ApplyKnobValue(LimiterKnob knob, float normalizedValue)
    {
        switch (knob)
        {
            case LimiterKnob.Ceiling:
                float ceilingDb = -3f + normalizedValue * 3f;
                _parameterCallback(LimiterPlugin.CeilingIndex, ceilingDb);
                break;

            case LimiterKnob.Release:
                float releaseMs = 10f + normalizedValue * 190f;
                _parameterCallback(LimiterPlugin.ReleaseIndex, releaseMs);
                break;
        }
        _presetHelper.MarkAsCustom();
    }

    private void ApplyPreset(string presetName, IReadOnlyDictionary<string, float> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            int paramIndex = name switch
            {
                "Ceiling" => LimiterPlugin.CeilingIndex,
                "Release" => LimiterPlugin.ReleaseIndex,
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
            ["Ceiling"] = _plugin.CeilingDb,
            ["Release"] = _plugin.ReleaseMs
        };
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
