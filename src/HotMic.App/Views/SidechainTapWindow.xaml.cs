using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HotMic.App.UI.PluginComponents;
using HotMic.Core.Plugins;
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
    private readonly Func<SidechainSignalMask> _usedLaterProvider;
    private readonly DispatcherTimer _renderTimer;

    private float _smoothedSpeech;
    private float _smoothedVoiced;
    private float _smoothedUnvoiced;
    private float _smoothedSibilance;
    private SidechainSignalMask _usedLaterMask;
    private bool _disposed;

    public SidechainTapWindow(SidechainTapPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback, Func<SidechainSignalMask> usedLaterProvider)
    {
        InitializeComponent();
        _plugin = plugin;
        _parameterCallback = parameterCallback;
        _bypassCallback = bypassCallback;
        _usedLaterProvider = usedLaterProvider;

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

        _usedLaterMask = _usedLaterProvider?.Invoke() ?? SidechainSignalMask.None;

        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var state = new SidechainTapState(
            Speech: new SidechainTapSignalState(
                Value: _smoothedSpeech,
                Mode: _plugin.SpeechPresenceMode,
                HasUpstream: _plugin.SpeechPresenceHasSource,
                UsedLater: (_usedLaterMask & SidechainSignalMask.SpeechPresence) != 0),
            Voiced: new SidechainTapSignalState(
                Value: _smoothedVoiced,
                Mode: _plugin.VoicedProbabilityMode,
                HasUpstream: _plugin.VoicedProbabilityHasSource,
                UsedLater: (_usedLaterMask & SidechainSignalMask.VoicedProbability) != 0),
            Unvoiced: new SidechainTapSignalState(
                Value: _smoothedUnvoiced,
                Mode: _plugin.UnvoicedEnergyMode,
                HasUpstream: _plugin.UnvoicedEnergyHasSource,
                UsedLater: (_usedLaterMask & SidechainSignalMask.UnvoicedEnergy) != 0),
            Sibilance: new SidechainTapSignalState(
                Value: _smoothedSibilance,
                Mode: _plugin.SibilanceEnergyMode,
                HasUpstream: _plugin.SibilanceEnergyHasSource,
                UsedLater: (_usedLaterMask & SidechainSignalMask.SibilanceEnergy) != 0),
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
            case SidechainTapHitArea.ModeToggle:
                if (hit.SignalIndex >= 0 && hit.SignalIndex < (int)SidechainSignalId.Count)
                {
                    var currentMode = hit.SignalIndex switch
                    {
                        SidechainTapPlugin.SpeechPresenceIndex => _plugin.SpeechPresenceMode,
                        SidechainTapPlugin.VoicedProbabilityIndex => _plugin.VoicedProbabilityMode,
                        SidechainTapPlugin.UnvoicedEnergyIndex => _plugin.UnvoicedEnergyMode,
                        SidechainTapPlugin.SibilanceEnergyIndex => _plugin.SibilanceEnergyMode,
                        _ => SidechainTapMode.Generate
                    };

                    if (currentMode != hit.Mode)
                    {
                        _parameterCallback(hit.SignalIndex, (float)hit.Mode);
                    }
                }
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
