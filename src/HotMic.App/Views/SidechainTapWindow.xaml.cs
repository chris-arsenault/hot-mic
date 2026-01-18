using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HotMic.App.UI.PluginComponents;
using HotMic.Core.Plugins.BuiltIn;
using SkiaSharp;
using SkiaSharp.Views.WPF;

namespace HotMic.App.Views;

public partial class SidechainTapWindow : Window, IDisposable
{
    private readonly SidechainTapRenderer _renderer = new();
    private readonly SidechainTapPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;

    private float _smoothedSpeech;
    private float _smoothedVoiced;
    private float _smoothedUnvoiced;
    private float _smoothedSibilance;
    private bool _disposed;

    public SidechainTapWindow(SidechainTapPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
    {
        InitializeComponent();
        _plugin = plugin;
        _parameterCallback = parameterCallback;
        _bypassCallback = bypassCallback;

        var preferredSize = SidechainTapRenderer.GetPreferredSize();
        Width = preferredSize.Width;
        Height = preferredSize.Height;

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += OnRenderTick;
        Loaded += (_, _) => _renderTimer.Start();
        Closed += (_, _) => Dispose();
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        float rawSpeech = _plugin.GetSpeechPresence();
        float rawVoiced = _plugin.GetVoicedProbability();
        float rawUnvoiced = _plugin.GetUnvoicedEnergy();
        float rawSibilance = _plugin.GetSibilanceEnergy();

        _smoothedSpeech = _smoothedSpeech * 0.7f + rawSpeech * 0.3f;
        _smoothedVoiced = _smoothedVoiced * 0.7f + rawVoiced * 0.3f;
        _smoothedUnvoiced = _smoothedUnvoiced * 0.7f + rawUnvoiced * 0.3f;
        _smoothedSibilance = _smoothedSibilance * 0.7f + rawSibilance * 0.3f;

        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var state = new SidechainTapState(
            SpeechPresence: _smoothedSpeech,
            VoicedProbability: _smoothedVoiced,
            UnvoicedEnergy: _smoothedUnvoiced,
            SibilanceEnergy: _smoothedSibilance,
            SpeechEnabled: _plugin.SpeechPresenceEnabled,
            VoicedEnabled: _plugin.VoicedProbabilityEnabled,
            UnvoicedEnabled: _plugin.UnvoicedEnergyEnabled,
            SibilanceEnabled: _plugin.SibilanceEnergyEnabled,
            LatencyMs: _plugin.SampleRate > 0 ? _plugin.LatencySamples * 1000f / _plugin.SampleRate : 0f,
            IsBypassed: _plugin.IsBypassed
        );

        _renderer.Render(canvas, size, dpiScale, state);
    }

    private void SkiaCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        if (e.ChangedButton != MouseButton.Left) return;

        var hit = _renderer.HitTest(x, y);
        switch (hit.Area)
        {
            case SidechainTapHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;
            case SidechainTapHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;
            case SidechainTapHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;
            case SidechainTapHitArea.Toggle:
                // Toggle the enabled state for each signal
                float newValue = hit.ToggleIndex switch
                {
                    0 => _plugin.SpeechPresenceEnabled ? 0f : 1f,
                    1 => _plugin.VoicedProbabilityEnabled ? 0f : 1f,
                    2 => _plugin.UnvoicedEnergyEnabled ? 0f : 1f,
                    3 => _plugin.SibilanceEnergyEnabled ? 0f : 1f,
                    _ => 1f
                };
                _parameterCallback(hit.ToggleIndex, newValue);
                e.Handled = true;
                break;
        }
    }

    private void SkiaCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // No knobs to handle
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            SkiaCanvas.ReleaseMouseCapture();
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
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
