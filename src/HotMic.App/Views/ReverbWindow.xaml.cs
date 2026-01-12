using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HotMic.App.UI.PluginComponents;
using HotMic.Core.Plugins.BuiltIn;
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

    private int _activeKnob = -1;
    private float _dragStartY;
    private float _dragStartValue;
    private int _hoveredKnob = -1;

    public ReverbWindow(ConvolutionReverbPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
    {
        InitializeComponent();
        _plugin = plugin;
        _parameterCallback = parameterCallback;
        _bypassCallback = bypassCallback;

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
            HoveredKnob: _hoveredKnob
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
                e.Handled = true;
                break;

            case ReverbHitArea.Knob:
                _activeKnob = hit.Index;
                _dragStartY = y;
                _dragStartValue = GetKnobNormalizedValue(hit.Index);
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
            _hoveredKnob = hit.Area == ReverbHitArea.Knob ? hit.Index : -1;
        }
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        _activeKnob = -1;
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

    private float GetKnobNormalizedValue(int knobIndex)
    {
        return knobIndex switch
        {
            0 => _plugin.DryWet,
            1 => (_plugin.Decay - 0.1f) / (2f - 0.1f),
            2 => _plugin.PreDelayMs / 100f,
            _ => 0f
        };
    }

    private void ApplyKnobValue(int knobIndex, float normalizedValue)
    {
        float value = knobIndex switch
        {
            0 => normalizedValue,                          // Dry/Wet: 0 to 1
            1 => 0.1f + normalizedValue * 1.9f,            // Decay: 0.1 to 2.0
            2 => normalizedValue * 100f,                   // Pre-delay: 0 to 100 ms
            _ => 0f
        };

        int paramIndex = knobIndex switch
        {
            0 => ConvolutionReverbPlugin.DryWetIndex,
            1 => ConvolutionReverbPlugin.DecayIndex,
            2 => ConvolutionReverbPlugin.PreDelayIndex,
            _ => -1
        };

        if (paramIndex >= 0)
        {
            _parameterCallback(paramIndex, value);
        }
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
