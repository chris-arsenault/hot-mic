using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HotMic.App.UI.PluginComponents;
using HotMic.Core.Plugins.BuiltIn;
using SkiaSharp;
using SkiaSharp.Views.WPF;

namespace HotMic.App.Views;

public partial class GainWindow : Window
{
    private readonly GainRenderer _renderer = new();
    private readonly GainPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly DispatcherTimer _renderTimer;

    private bool _isKnobActive;
    private float _dragStartY;
    private float _dragStartValue;
    private bool _isKnobHovered;
    private float _smoothedInputLevel;
    private float _smoothedOutputLevel;

    public GainWindow(GainPlugin plugin, Action<int, float> parameterCallback)
    {
        InitializeComponent();
        _plugin = plugin;
        _parameterCallback = parameterCallback;

        var preferredSize = GainRenderer.GetPreferredSize();
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
        // Smooth the levels for display
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

        var state = new GainState(
            GainDb: _plugin.GainDb,
            InputLevel: _smoothedInputLevel,
            OutputLevel: _smoothedOutputLevel,
            IsPhaseInverted: _plugin.IsPhaseInverted,
            IsBypassed: _plugin.IsBypassed,
            IsKnobHovered: _isKnobHovered
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
            case GainHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;

            case GainHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;

            case GainHitArea.BypassButton:
                _plugin.IsBypassed = !_plugin.IsBypassed;
                e.Handled = true;
                break;

            case GainHitArea.PhaseButton:
                float newPhase = _plugin.IsPhaseInverted ? 0f : 1f;
                _parameterCallback(GainPlugin.PhaseInvertIndex, newPhase);
                e.Handled = true;
                break;

            case GainHitArea.Knob:
                _isKnobActive = true;
                _dragStartY = y;
                _dragStartValue = GetKnobNormalizedValue();
                SkiaCanvas.CaptureMouse();
                e.Handled = true;
                break;
        }
    }

    private void SkiaCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        if (_isKnobActive && e.LeftButton == MouseButtonState.Pressed)
        {
            float deltaY = _dragStartY - y;
            float newNormalized = RotaryKnob.CalculateValueFromDrag(_dragStartValue, -deltaY, 0.004f);
            ApplyKnobValue(newNormalized);
            e.Handled = true;
        }
        else
        {
            var hit = _renderer.HitTest(x, y);
            _isKnobHovered = hit.Area == GainHitArea.Knob;
        }
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        _isKnobActive = false;
        SkiaCanvas.ReleaseMouseCapture();
    }

    private float GetKnobNormalizedValue()
    {
        // -24 to +24 dB maps to 0 to 1
        return (_plugin.GainDb + 24f) / 48f;
    }

    private void ApplyKnobValue(float normalizedValue)
    {
        // 0 to 1 maps to -24 to +24 dB
        float gainDb = -24f + normalizedValue * 48f;
        _parameterCallback(GainPlugin.GainIndex, gainDb);
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
