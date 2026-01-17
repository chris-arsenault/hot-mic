using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using HotMic.App.UI.PluginComponents;
using HotMic.Core.Analysis;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using WpfToolTip = System.Windows.Controls.ToolTip;

namespace HotMic.App.Views;

/// <summary>
/// Base class for visualizer windows with GPU/CPU rendering fallback.
/// All visualizers should inherit from this to maintain consistent behavior.
/// </summary>
public abstract partial class BaseVisualizerWindow : Window
{
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 0x0002;

    protected readonly AnalysisOrchestrator Orchestrator;
    protected readonly DispatcherTimer RenderTimer;

    private FrameworkElement? _skiaCanvas;
    private WindowsFormsHost? _glHost;
    private SKGLControl? _glControl;
    private bool _backendLocked;
    private bool _usingGpu;
    private bool _isClosing;

    private WpfToolTip? _tooltip;
    private string _currentTooltipText = string.Empty;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    protected BaseVisualizerWindow(AnalysisOrchestrator orchestrator)
    {
        Orchestrator = orchestrator;

        InitializeComponent();
        InitializeSkiaSurface();

        _tooltip = new WpfToolTip
        {
            Placement = PlacementMode.Relative,
            StaysOpen = true
        };
        AttachTooltip();

        RenderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        RenderTimer.Tick += OnRenderTick;

        Loaded += OnWindowLoaded;
        Closed += OnWindowClosed;
    }

    protected abstract IBaseVisualizerRenderer Renderer { get; }

    protected virtual void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        RenderTimer.Start();
    }

    protected virtual void OnWindowClosed(object? sender, EventArgs e)
    {
        _isClosing = true;
        RenderTimer.Stop();

        if (_glControl is not null)
        {
            DetachWinFormsInputHandlers(_glControl);
            _glControl.PaintSurface -= OnPaintSurfaceGpu;
            _glControl.Dispose();
            _glControl = null;
        }

        if (_glHost is not null)
        {
            _glHost.Child = null;
            _glHost.Dispose();
            _glHost = null;
        }

        Renderer.Dispose();
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        if (_isClosing) return;

        if (_usingGpu && _glControl is not null)
        {
            _glControl.Invalidate();
        }
        else if (_skiaCanvas is SKElement element)
        {
            element.InvalidateVisual();
        }
    }

    private void InitializeSkiaSurface()
    {
        if (TryCreateGpuCanvas())
        {
            return;
        }

        CreateCpuCanvas();
    }

    private bool TryCreateGpuCanvas()
    {
        SKGLControl? glControl = null;
        WindowsFormsHost? host = null;
        try
        {
            glControl = new SKGLControl
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                BackColor = System.Drawing.Color.FromArgb(10, 13, 18)
            };
            glControl.CreateControl();
            host = new WindowsFormsHost
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Child = glControl
            };

            _usingGpu = true;
            _backendLocked = false;
            _skiaCanvas = host;
            _glControl = glControl;
            _glHost = host;
            glControl.PaintSurface += OnPaintSurfaceGpu;
            AttachWinFormsInputHandlers(glControl);
            SkiaHost.Children.Clear();
            SkiaHost.Children.Add(host);
            AttachTooltip();
            return true;
        }
        catch (Exception)
        {
            glControl?.Dispose();
            host?.Dispose();
            _usingGpu = false;
            _glControl = null;
            _glHost = null;
            return false;
        }
    }

    private void CreateCpuCanvas()
    {
        var element = new SKElement
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        _usingGpu = false;
        _backendLocked = true;
        _glControl = null;
        _glHost = null;
        _skiaCanvas = element;
        element.PaintSurface += OnPaintSurfaceCpu;
        AttachWpfInputHandlers(element);
        SkiaHost.Children.Clear();
        SkiaHost.Children.Add(element);
        AttachTooltip();
    }

    protected void FallbackToCpu()
    {
        if (_backendLocked || !_usingGpu) return;

        _backendLocked = true;
        if (_glControl is not null)
        {
            _glControl.PaintSurface -= OnPaintSurfaceGpu;
            DetachWinFormsInputHandlers(_glControl);
            _glControl.Dispose();
            _glControl = null;
        }

        if (_glHost is not null)
        {
            _glHost.Child = null;
            SkiaHost.Children.Clear();
            _glHost.Dispose();
            _glHost = null;
        }

        CreateCpuCanvas();
    }

    private void OnPaintSurfaceGpu(object? sender, SKPaintGLSurfaceEventArgs e)
    {
        if (_isClosing) return;
        OnRender(e.Surface.Canvas, new SKSize(e.BackendRenderTarget.Width, e.BackendRenderTarget.Height));
    }

    private void OnPaintSurfaceCpu(object? sender, SKPaintSurfaceEventArgs e)
    {
        if (_isClosing) return;
        OnRender(e.Surface.Canvas, new SKSize(e.Info.Width, e.Info.Height));
    }

    protected abstract void OnRender(SKCanvas canvas, SKSize size);

    #region Input Handlers - WPF (SKElement)

    private void AttachWpfInputHandlers(UIElement element)
    {
        element.MouseDown += OnMouseDownWpf;
        element.MouseMove += OnMouseMoveWpf;
        element.MouseUp += OnMouseUpWpf;
        element.MouseLeave += OnMouseLeaveWpf;
    }

    private void OnMouseDownWpf(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition((IInputElement)sender);
        float x = (float)pos.X;
        float y = (float)pos.Y;
        HandleMouseDown(x, y, e.ChangedButton, e.ClickCount);
    }

    private void OnMouseMoveWpf(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition((IInputElement)sender);
        float x = (float)pos.X;
        float y = (float)pos.Y;
        bool leftDown = e.LeftButton == MouseButtonState.Pressed;
        bool shiftHeld = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
        HandleMouseMove(x, y, leftDown, shiftHeld);
    }

    private void OnMouseUpWpf(object sender, MouseButtonEventArgs e)
    {
        HandleMouseUp(e.ChangedButton);
    }

    private void OnMouseLeaveWpf(object sender, MouseEventArgs e)
    {
        HandleMouseLeave();
    }

    #endregion

    #region Input Handlers - WinForms (SKGLControl)

    private void AttachWinFormsInputHandlers(SKGLControl control)
    {
        control.MouseDown += OnMouseDownWinForms;
        control.MouseMove += OnMouseMoveWinForms;
        control.MouseUp += OnMouseUpWinForms;
        control.MouseLeave += OnMouseLeaveWinForms;
    }

    private void DetachWinFormsInputHandlers(SKGLControl control)
    {
        control.MouseDown -= OnMouseDownWinForms;
        control.MouseMove -= OnMouseMoveWinForms;
        control.MouseUp -= OnMouseUpWinForms;
        control.MouseLeave -= OnMouseLeaveWinForms;
    }

    private void OnMouseDownWinForms(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        var button = ToWpfMouseButton(e.Button);
        if (button.HasValue)
        {
            int clickCount = e.Clicks;
            HandleMouseDown(e.X, e.Y, button.Value, clickCount);
        }
    }

    private void OnMouseMoveWinForms(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        bool leftDown = (e.Button & System.Windows.Forms.MouseButtons.Left) != 0;
        bool shiftHeld = (System.Windows.Forms.Control.ModifierKeys & System.Windows.Forms.Keys.Shift) != 0;
        HandleMouseMove(e.X, e.Y, leftDown, shiftHeld);
    }

    private void OnMouseUpWinForms(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        var button = ToWpfMouseButton(e.Button);
        if (button.HasValue)
        {
            HandleMouseUp(button.Value);
        }
    }

    private void OnMouseLeaveWinForms(object? sender, EventArgs e)
    {
        HandleMouseLeave();
    }

    private static MouseButton? ToWpfMouseButton(System.Windows.Forms.MouseButtons button)
    {
        if ((button & System.Windows.Forms.MouseButtons.Left) != 0) return MouseButton.Left;
        if ((button & System.Windows.Forms.MouseButtons.Right) != 0) return MouseButton.Right;
        if ((button & System.Windows.Forms.MouseButtons.Middle) != 0) return MouseButton.Middle;
        return null;
    }

    #endregion

    #region Common Input Handling

    private void HandleMouseDown(float x, float y, MouseButton button, int clickCount)
    {
        // Double-click handling
        if (clickCount >= 2 && button == MouseButton.Left)
        {
            if (TryHandleKnobDoubleClick(x, y)) return;
        }

        // Check close button
        if (Renderer.HitTestCloseButton(x, y) && button == MouseButton.Left)
        {
            Close();
            return;
        }

        // Check title bar for dragging
        if (Renderer.HitTestTitleBar(x, y) && button == MouseButton.Left)
        {
            StartWindowDrag();
            return;
        }

        // Try knob interaction
        if (TryHandleKnobMouseDown(x, y, button)) return;

        // Let subclass handle other interactions
        OnMouseDownOverride(x, y, button);
    }

    private void HandleMouseMove(float x, float y, bool leftDown, bool shiftHeld)
    {
        // Handle knob interaction
        HandleKnobMouseMove(x, y, leftDown, shiftHeld);

        // Update tooltip
        var tooltipText = Renderer.GetTooltipText(x, y);
        SetTooltip(tooltipText);

        // Let subclass handle other interactions
        OnMouseMoveOverride(x, y, leftDown, shiftHeld);
    }

    private void HandleMouseUp(MouseButton button)
    {
        HandleKnobMouseUp(button);
        OnMouseUpOverride(button);
    }

    private void HandleMouseLeave()
    {
        ResetKnobHover();
        SetTooltip(null);
        OnMouseLeaveOverride();
    }

    protected virtual void OnMouseDownOverride(float x, float y, MouseButton button) { }
    protected virtual void OnMouseMoveOverride(float x, float y, bool leftDown, bool shiftHeld) { }
    protected virtual void OnMouseUpOverride(MouseButton button) { }
    protected virtual void OnMouseLeaveOverride() { }

    #endregion

    #region Knob Handling

    private bool TryHandleKnobMouseDown(float x, float y, MouseButton button)
    {
        if (_skiaCanvas is null) return false;

        foreach (var knob in Renderer.AllKnobs)
        {
            if (knob.HandleMouseDown(x, y, button, _skiaCanvas))
            {
                SetTooltip(null);
                return true;
            }
        }

        return false;
    }

    private void HandleKnobMouseMove(float x, float y, bool leftDown, bool shiftHeld)
    {
        foreach (var knob in Renderer.AllKnobs)
        {
            knob.HandleMouseMove(x, y, leftDown, shiftHeld);
        }
    }

    private bool TryHandleKnobDoubleClick(float x, float y)
    {
        foreach (var knob in Renderer.AllKnobs)
        {
            if (knob.HandleDoubleClick(x, y)) return true;
        }

        return false;
    }

    private void HandleKnobMouseUp(MouseButton button)
    {
        foreach (var knob in Renderer.AllKnobs)
        {
            knob.HandleMouseUp(button);
        }
    }

    private void ResetKnobHover()
    {
        foreach (var knob in Renderer.AllKnobs)
        {
            knob.UpdateHover(float.NegativeInfinity, float.NegativeInfinity);
        }
    }

    #endregion

    #region Window Chrome

    private void StartWindowDrag()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        SendMessage(hwnd, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
    }

    #endregion

    #region Tooltip

    private void AttachTooltip()
    {
        if (_tooltip is null || _skiaCanvas is null) return;

        _tooltip.PlacementTarget = _skiaCanvas;
        ToolTipService.SetInitialShowDelay(_skiaCanvas, 0);
        ToolTipService.SetShowDuration(_skiaCanvas, int.MaxValue);
        _skiaCanvas.ToolTip = _tooltip;
    }

    protected void SetTooltip(string? text)
    {
        if (_tooltip is null) return;

        if (string.IsNullOrEmpty(text))
        {
            if (_tooltip.IsOpen)
            {
                _tooltip.IsOpen = false;
            }
            _currentTooltipText = string.Empty;
            return;
        }

        if (text == _currentTooltipText) return;

        _currentTooltipText = text;
        _tooltip.Content = text;
        _tooltip.IsOpen = true;
    }

    #endregion
}

/// <summary>
/// Interface for visualizer renderers that work with BaseVisualizerWindow.
/// </summary>
public interface IBaseVisualizerRenderer : IDisposable
{
    IReadOnlyList<KnobWidget> AllKnobs { get; }
    bool HitTestCloseButton(float x, float y);
    bool HitTestTitleBar(float x, float y);
    string? GetTooltipText(float x, float y);
}
