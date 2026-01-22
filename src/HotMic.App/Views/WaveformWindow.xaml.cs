using System;
using System.Windows.Input;
using HotMic.App.UI.PluginComponents;
using HotMic.Core.Analysis;
using SkiaSharp;

namespace HotMic.App.Views;

public partial class WaveformWindow : AnalysisWindowBase
{
    private readonly WaveformWindowRenderer _renderer = new(PluginComponentTheme.BlueOnBlack);

    // Data buffers
    private float[] _waveformMin = Array.Empty<float>();
    private float[] _waveformMax = Array.Empty<float>();
    private int _bufferFrameCount;
    private long _lastCopiedFrameId = -1;

    // Display settings
    private float _minDb = -60f;
    private float _maxDb;

    public WaveformWindow(AnalysisOrchestrator orchestrator)
        : base(orchestrator)
    {
        InitializeComponent();
        InitializeSkiaSurface(SkiaHost);

        WireKnobHandlers();
        SyncKnobsFromSettings();
    }

    protected override AnalysisCapabilities ComputeRequiredCapabilities() => AnalysisCapabilities.Waveform;

    protected override void OnRenderTick(object? sender, EventArgs e)
    {
        EnsureBuffers();
        CopyWaveformData();
        InvalidateRenderSurface();
    }

    protected override void OnRender(SKCanvas canvas, int width, int height)
    {
        _renderer.Render(
            canvas,
            width,
            height,
            _waveformMin,
            _waveformMax,
            Store.AvailableFrames,
            _minDb,
            _maxDb);
    }

    private void WireKnobHandlers()
    {
        _renderer.MinDbKnob.ValueChanged += v => _minDb = v;
        _renderer.MaxDbKnob.ValueChanged += v => _maxDb = v;
        _renderer.TimeKnob.ValueChanged += v => Orchestrator.Config.TimeWindow = v;
    }

    private void SyncKnobsFromSettings()
    {
        _renderer.MinDbKnob.Value = _minDb;
        _renderer.MaxDbKnob.Value = _maxDb;
        _renderer.TimeKnob.Value = Orchestrator.Config.TimeWindow;
    }

    private void EnsureBuffers()
    {
        int frameCapacity = Store.FrameCapacity;
        if (_bufferFrameCount != frameCapacity)
        {
            _bufferFrameCount = frameCapacity;
            _waveformMin = new float[frameCapacity];
            _waveformMax = new float[frameCapacity];
            _lastCopiedFrameId = -1;
        }
    }

    private void CopyWaveformData()
    {
        Store.TryGetWaveformRange(
            _lastCopiedFrameId,
            _waveformMin,
            _waveformMax,
            out long latestFrameId,
            out _,
            out _);

        _lastCopiedFrameId = latestFrameId;
    }

    #region WPF Mouse Handlers (CPU mode)

    protected override void OnSkiaMouseDown(object sender, MouseButtonEventArgs e)
    {
        var element = sender as System.Windows.FrameworkElement;
        if (element is null) return;

        var pos = e.GetPosition(element);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        // Let knobs handle their own input (drag, right-click edit)
        foreach (var knob in _renderer.AllKnobs)
        {
            if (knob.HandleMouseDown(x, y, e.ChangedButton, element))
            {
                if (knob.IsDragging)
                    element.CaptureMouse();
                e.Handled = true;
                return;
            }
        }

        if (e.ChangedButton != MouseButton.Left)
            return;

        // Close button (check before title bar drag since it's inside the title bar)
        if (_renderer.CloseButtonRect.Contains(x, y))
        {
            Close();
            e.Handled = true;
            return;
        }

        // Title bar drag
        if (y < 36)
        {
            DragMove();
            e.Handled = true;
        }
    }

    protected override void OnSkiaMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var element = sender as System.Windows.FrameworkElement;
        if (element is null) return;

        var pos = e.GetPosition(element);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        foreach (var knob in _renderer.AllKnobs)
        {
            knob.HandleMouseMove(x, y, e.LeftButton == MouseButtonState.Pressed);
        }
    }

    protected override void OnSkiaMouseUp(object sender, MouseButtonEventArgs e)
    {
        var element = sender as System.Windows.FrameworkElement;
        if (element is null) return;

        foreach (var knob in _renderer.AllKnobs)
        {
            knob.HandleMouseUp(e.ChangedButton);
        }

        if (e.ChangedButton == MouseButton.Left)
            element.ReleaseMouseCapture();
    }

    #endregion

    #region WinForms Mouse Handlers (GPU mode)

    protected override void OnSkiaMouseDownWinForms(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        float x = e.X;
        float y = e.Y;

        var wpfButton = ToWpfMouseButton(e.Button);

        // Let knobs handle their own input (drag, right-click edit)
        foreach (var knob in _renderer.AllKnobs)
        {
            if (wpfButton.HasValue && knob.HandleMouseDown(x, y, wpfButton.Value, SkiaCanvas))
            {
                return;
            }
        }

        if (e.Button != System.Windows.Forms.MouseButtons.Left)
            return;

        // Close button (check before title bar drag since it's inside the title bar)
        if (_renderer.CloseButtonRect.Contains(x, y))
        {
            Close();
            return;
        }

        // Title bar drag
        if (y < 36)
        {
            DragWindow();
        }
    }

    protected override void OnSkiaMouseMoveWinForms(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        float x = e.X;
        float y = e.Y;
        bool leftPressed = (e.Button & System.Windows.Forms.MouseButtons.Left) != 0;

        foreach (var knob in _renderer.AllKnobs)
        {
            knob.HandleMouseMove(x, y, leftPressed);
        }
    }

    protected override void OnSkiaMouseUpWinForms(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        var wpfButton = ToWpfMouseButton(e.Button);
        if (wpfButton.HasValue)
        {
            foreach (var knob in _renderer.AllKnobs)
            {
                knob.HandleMouseUp(wpfButton.Value);
            }
        }
    }

    #endregion

    public override void Dispose()
    {
        if (IsDisposed) return;
        _renderer.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
