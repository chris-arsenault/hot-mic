using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using HotMic.App.UI.PluginComponents;
using HotMic.Core.Analysis;
using SkiaSharp;

namespace HotMic.App.Views;

public partial class WaveformWindow : AnalysisWindowBase
{
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 0x0002;

    private readonly WaveformWindowRenderer _renderer;

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
        _renderer = new WaveformWindowRenderer(PluginComponentTheme.BlueOnBlack);
        InitializeSkiaSurface(SkiaHost);
        WireKnobHandlers();
        SyncKnobsFromSettings();
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

    protected override AnalysisCapabilities ComputeRequiredCapabilities()
    {
        return AnalysisCapabilities.Waveform;
    }

    protected override void OnRenderTick(object? sender, EventArgs e)
    {
        EnsureBuffers();
        CopyWaveformData();
        InvalidateRenderSurface();
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

    #region Mouse Handling

    protected override void OnSkiaMouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        // Close button (check before title bar drag since it's inside the title bar)
        if (_renderer.CloseButtonRect.Contains(x, y))
        {
            Close();
            return;
        }

        // Title bar drag
        if (y < 36 && e.LeftButton == MouseButtonState.Pressed)
        {
            DragWindow();
            return;
        }

        // Knob interaction
        TryHandleKnobMouseDown(x, y, e.ChangedButton);
    }

    protected override void OnSkiaMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;
        bool leftDown = e.LeftButton == MouseButtonState.Pressed;
        bool shiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        HandleKnobMouseMove(x, y, leftDown, shiftHeld);
    }

    protected override void OnSkiaMouseUp(object sender, MouseButtonEventArgs e)
    {
        HandleKnobMouseUp(e.ChangedButton);
    }

    protected override void OnSkiaMouseDownWinForms(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        float x = e.X;
        float y = e.Y;

        // Close button (check before title bar drag since it's inside the title bar)
        if (_renderer.CloseButtonRect.Contains(x, y))
        {
            Close();
            return;
        }

        if (y < 36 && e.Button == System.Windows.Forms.MouseButtons.Left)
        {
            DragWindow();
            return;
        }

        var button = ToWpfMouseButton(e.Button);
        if (button.HasValue)
        {
            TryHandleKnobMouseDown(x, y, button.Value);
        }
    }

    protected override void OnSkiaMouseMoveWinForms(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        bool leftDown = (e.Button & System.Windows.Forms.MouseButtons.Left) != 0;
        bool shiftHeld = (System.Windows.Forms.Control.ModifierKeys & System.Windows.Forms.Keys.Shift) != 0;
        HandleKnobMouseMove(e.X, e.Y, leftDown, shiftHeld);
    }

    protected override void OnSkiaMouseUpWinForms(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        var button = ToWpfMouseButton(e.Button);
        if (button.HasValue)
        {
            HandleKnobMouseUp(button.Value);
        }
    }

    private bool TryHandleKnobMouseDown(float x, float y, MouseButton button)
    {
        if (SkiaCanvas is null) return false;

        foreach (var knob in _renderer.AllKnobs)
        {
            if (knob.HandleMouseDown(x, y, button, SkiaCanvas))
            {
                return true;
            }
        }
        return false;
    }

    private void HandleKnobMouseMove(float x, float y, bool leftDown, bool shiftHeld)
    {
        foreach (var knob in _renderer.AllKnobs)
        {
            knob.HandleMouseMove(x, y, leftDown, shiftHeld);
        }
    }

    private void HandleKnobMouseUp(MouseButton button)
    {
        foreach (var knob in _renderer.AllKnobs)
        {
            knob.HandleMouseUp(button);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private void DragWindow()
    {
        ReleaseCapture();
        var helper = new System.Windows.Interop.WindowInteropHelper(this);
        SendMessage(helper.Handle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
    }

    #endregion

    public override void Dispose()
    {
        _renderer.Dispose();
        base.Dispose();
    }
}
