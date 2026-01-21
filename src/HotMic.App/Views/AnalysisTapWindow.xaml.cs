using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HotMic.App.UI.PluginComponents;
using HotMic.Core.Analysis;
using HotMic.Core.Plugins;
using HotMic.Core.Plugins.BuiltIn;
using SkiaSharp;

namespace HotMic.App.Views;

public partial class AnalysisTapWindow : Window, IDisposable
{
    private readonly AnalysisTapRenderer _renderer = new();
    private readonly AnalysisTapPlugin _plugin;
    private readonly int _channelIndex;
    private readonly int _pluginInstanceId;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly Func<AnalysisSignalMask> _usedLaterProvider;
    private readonly Action<AnalysisSignalMask> _requestSignalsCallback;
    private readonly DispatcherTimer _renderTimer;
    private readonly AnalysisTapSignalState[] _signalStates;
    private readonly float[] _smoothedValues;
    private readonly long[] _lastNonFiniteLogTicks;
    private AnalysisSignalMask _usedLaterMask;
    private AnalysisSignalMask _requestedMask;
    private bool _disposed;
    private static readonly long NonFiniteLogIntervalTicks = Math.Max(1, Stopwatch.Frequency * 2);

    public AnalysisTapWindow(
        AnalysisTapPlugin plugin,
        int channelIndex,
        int pluginInstanceId,
        Action<int, float> parameterCallback,
        Action<bool> bypassCallback,
        Func<AnalysisSignalMask> usedLaterProvider,
        Action<AnalysisSignalMask> requestSignalsCallback)
    {
        InitializeComponent();
        _plugin = plugin;
        _channelIndex = channelIndex;
        _pluginInstanceId = pluginInstanceId;
        _parameterCallback = parameterCallback;
        _bypassCallback = bypassCallback;
        _usedLaterProvider = usedLaterProvider;
        _requestSignalsCallback = requestSignalsCallback;

        int signalCount = (int)AnalysisSignalId.Count;
        _signalStates = new AnalysisTapSignalState[signalCount];
        _smoothedValues = new float[signalCount];
        _lastNonFiniteLogTicks = new long[signalCount];

        var preferredSize = AnalysisTapRenderer.GetPreferredSize(signalCount);
        Width = preferredSize.Width;
        Height = preferredSize.Height;

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += OnRenderTick;
        Loaded += (_, _) => _renderTimer.Start();
        Closed += (_, _) => Dispose();
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        _usedLaterMask = _usedLaterProvider?.Invoke() ?? AnalysisSignalMask.None;
        int nonFiniteMask = _plugin.ConsumeNonFiniteSignalMask();
        if (nonFiniteMask != 0)
        {
            long now = Stopwatch.GetTimestamp();
            for (int i = 0; i < _signalStates.Length; i++)
            {
                if ((nonFiniteMask & (1 << i)) == 0)
                {
                    continue;
                }

                if (now - _lastNonFiniteLogTicks[i] < NonFiniteLogIntervalTicks)
                {
                    continue;
                }

                _lastNonFiniteLogTicks[i] = now;
                var signal = (AnalysisSignalId)i;
                string message = $"[AnalysisTap] non-finite signal ch{_channelIndex + 1} slot={_pluginInstanceId} signal={signal}";
                Console.WriteLine(message);
            }
        }

        int count = _signalStates.Length;
        for (int i = 0; i < count; i++)
        {
            var signal = (AnalysisSignalId)i;
            float raw = _plugin.GetValue(signal);
            if (!float.IsFinite(raw))
            {
                raw = 0f;
            }

            if (!float.IsFinite(_smoothedValues[i]))
            {
                _smoothedValues[i] = 0f;
            }
            _smoothedValues[i] = _smoothedValues[i] * 0.7f + raw * 0.3f;

            _signalStates[i] = new AnalysisTapSignalState(
                Signal: signal,
                Value: _smoothedValues[i],
                Mode: _plugin.GetMode(signal),
                HasUpstream: _plugin.HasSource(signal),
                UsedLater: (_usedLaterMask & (AnalysisSignalMask)(1 << i)) != 0);
        }

        UpdateRequestedSignals();
        SkiaCanvas.InvalidateVisual();
    }

    private void UpdateRequestedSignals()
    {
        AnalysisSignalMask requested = AnalysisSignalMask.None;
        if (!_plugin.IsBypassed)
        {
            for (int i = 0; i < _signalStates.Length; i++)
            {
                if (_signalStates[i].Mode != AnalysisTapMode.Disabled)
                {
                    requested |= (AnalysisSignalMask)(1 << i);
                }
            }
        }

        requested = AnalysisSignalDependencies.Expand(requested);
        if (requested != _requestedMask)
        {
            _requestedMask = requested;
            _requestSignalsCallback?.Invoke(requested);
        }
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var state = new AnalysisTapState(
            Signals: _signalStates,
            LatencyMs: _plugin.SampleRate > 0 ? _plugin.LatencySamples * 1000f / _plugin.SampleRate : 0f,
            IsBypassed: _plugin.IsBypassed);

        _renderer.Render(canvas, size, dpiScale, state);
    }

    private void SkiaCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var hit = _renderer.HitTest(x, y);
        switch (hit.Area)
        {
            case AnalysisTapHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;
            case AnalysisTapHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;
            case AnalysisTapHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;
            case AnalysisTapHitArea.ModeToggle:
                if (hit.SignalIndex >= 0 && hit.SignalIndex < _signalStates.Length)
                {
                    var signal = (AnalysisSignalId)hit.SignalIndex;
                    var currentMode = _plugin.GetMode(signal);
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
        {
            SkiaCanvas.ReleaseMouseCapture();
        }
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
        _requestSignalsCallback?.Invoke(AnalysisSignalMask.None);
        GC.SuppressFinalize(this);
    }
}
