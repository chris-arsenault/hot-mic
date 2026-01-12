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

    private SaturationKnob _activeKnob = SaturationKnob.None;
    private float _dragStartY;
    private float _dragStartValue;
    private SaturationKnob _hoveredKnob = SaturationKnob.None;
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

        var state = new SaturationState(
            WarmthPct: _plugin.WarmthPct,
            BlendPct: _plugin.BlendPct,
            InputLevel: _smoothedInputLevel,
            OutputLevel: _smoothedOutputLevel,
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

            case SaturationHitArea.Knob:
                _activeKnob = hit.Knob;
                _dragStartY = y;
                _dragStartValue = GetKnobNormalizedValue(hit.Knob);
                SkiaCanvas.CaptureMouse();
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

        if (_activeKnob != SaturationKnob.None && e.LeftButton == MouseButtonState.Pressed)
        {
            float deltaY = _dragStartY - y;
            float newNormalized = RotaryKnob.CalculateValueFromDrag(_dragStartValue, -deltaY, 0.004f);
            ApplyKnobValue(_activeKnob, newNormalized);
            e.Handled = true;
        }
        else
        {
            var hit = _renderer.HitTest(x, y);
            _hoveredKnob = hit.Area == SaturationHitArea.Knob ? hit.Knob : SaturationKnob.None;
        }
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        _activeKnob = SaturationKnob.None;
        SkiaCanvas.ReleaseMouseCapture();
    }

    private float GetKnobNormalizedValue(SaturationKnob knob) => knob switch
    {
        SaturationKnob.Warmth => _plugin.WarmthPct / 100f,
        SaturationKnob.Blend => _plugin.BlendPct / 100f,
        _ => 0f
    };

    private void ApplyKnobValue(SaturationKnob knob, float normalizedValue)
    {
        switch (knob)
        {
            case SaturationKnob.Warmth:
                float warmthPct = normalizedValue * 100f;
                _parameterCallback(SaturationPlugin.WarmthIndex, warmthPct);
                break;

            case SaturationKnob.Blend:
                float blendPct = normalizedValue * 100f;
                _parameterCallback(SaturationPlugin.BlendIndex, blendPct);
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
