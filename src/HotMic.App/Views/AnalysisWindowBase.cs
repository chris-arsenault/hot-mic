using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using HotMic.Core.Analysis;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace HotMic.App.Views;

/// <summary>
/// Base class for analysis windows providing shared Skia surface initialization,
/// render timer management, and analysis subscription handling.
/// </summary>
public abstract class AnalysisWindowBase : Window, IDisposable
{
    private readonly AnalysisOrchestrator _orchestrator;
    private readonly IAnalysisResultStore _store;
    private readonly DispatcherTimer _renderTimer;
    private IDisposable? _subscription;
    private bool _isDisposed;

    private FrameworkElement? _skiaCanvas;
    private WindowsFormsHost? _glHost;
    private SKGLControl? _glControl;
    private bool _backendLocked;
    private bool _usingGpu;

    private Grid? _hostGrid;

    protected AnalysisOrchestrator Orchestrator => _orchestrator;
    protected IAnalysisResultStore Store => _store;
    protected FrameworkElement? SkiaCanvas => _skiaCanvas;
    protected DispatcherTimer RenderTimer => _renderTimer;
    protected bool UsingGpu => _usingGpu;
    protected bool BackendLocked => _backendLocked;
    protected Grid? HostGrid => _hostGrid;
    protected SKGLControl? GlControl => _glControl;
    protected WindowsFormsHost? GlHost => _glHost;
    protected IDisposable? Subscription => _subscription;
    protected bool IsDisposed => _isDisposed;

    protected AnalysisWindowBase(AnalysisOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
        _store = orchestrator.Results;

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += OnRenderTick;

        Loaded += (_, _) =>
        {
            UpdateSubscription();
            _renderTimer.Start();
        };
        Closed += (_, _) => Dispose();
    }

    /// <summary>
    /// Initialize the Skia rendering surface with GPU fallback to CPU.
    /// Call this after InitializeComponent() in derived classes.
    /// </summary>
    protected void InitializeSkiaSurface(Grid hostGrid)
    {
        _hostGrid = hostGrid;

        if (TryCreateGpuCanvas())
        {
            return;
        }

        CreateCpuCanvas();
    }

    private bool TryCreateGpuCanvas()
    {
        if (_hostGrid is null) return false;

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
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
                Child = glControl
            };

            _usingGpu = true;
            _backendLocked = false;
            _skiaCanvas = host;
            _glControl = glControl;
            _glHost = host;
            glControl.PaintSurface += OnPaintSurfaceGpu;
            AttachWinFormsInputHandlers(glControl);
            _hostGrid.Children.Clear();
            _hostGrid.Children.Add(host);
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
        if (_hostGrid is null) return;

        var element = new SKElement
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch
        };

        _usingGpu = false;
        _backendLocked = true;
        _glControl = null;
        _glHost = null;
        _skiaCanvas = element;
        element.PaintSurface += OnPaintSurfaceCpu;
        AttachInputHandlers(element);
        _hostGrid.Children.Clear();
        _hostGrid.Children.Add(element);
    }

    protected void FallbackToCpu()
    {
        if (_backendLocked || !_usingGpu || _hostGrid is null)
        {
            return;
        }

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
            _hostGrid.Children.Clear();
            _glHost.Dispose();
            _glHost = null;
        }

        CreateCpuCanvas();
    }

    protected void InvalidateRenderSurface()
    {
        if (_usingGpu)
        {
            _glControl?.Invalidate();
        }
        else
        {
            (_skiaCanvas as SKElement)?.InvalidateVisual();
        }
    }

    private void OnPaintSurfaceGpu(object? sender, SKPaintGLSurfaceEventArgs e)
    {
        OnRender(e.Surface.Canvas, e.Surface.Canvas.DeviceClipBounds.Width, e.Surface.Canvas.DeviceClipBounds.Height);
    }

    private void OnPaintSurfaceCpu(object? sender, SKPaintSurfaceEventArgs e)
    {
        OnRender(e.Surface.Canvas, e.Info.Width, e.Info.Height);
    }

    /// <summary>
    /// Called each render tick to update data and invalidate the surface.
    /// </summary>
    protected abstract void OnRenderTick(object? sender, EventArgs e);

    /// <summary>
    /// Called to render the window content.
    /// </summary>
    protected abstract void OnRender(SKCanvas canvas, int width, int height);

    /// <summary>
    /// Compute the required analysis capabilities for this window.
    /// </summary>
    protected abstract AnalysisCapabilities ComputeRequiredCapabilities();

    /// <summary>
    /// Update the analysis subscription based on current requirements.
    /// </summary>
    protected void UpdateSubscription()
    {
        var requiredCaps = ComputeRequiredCapabilities();
        _subscription?.Dispose();
        _subscription = _orchestrator.Subscribe(requiredCaps);
    }

    #region Input Handling

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HT_CAPTION = 0x2;

    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    /// <summary>
    /// Initiates window drag from WinForms control (GPU mode).
    /// Use DragMove() for CPU mode (WPF controls).
    /// </summary>
    protected void DragWindow()
    {
        ReleaseCapture();
        var hwnd = new WindowInteropHelper(this).Handle;
        _ = SendMessage(hwnd, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
    }

    protected virtual void AttachInputHandlers(UIElement element)
    {
        element.MouseDown += OnSkiaMouseDown;
        element.MouseMove += OnSkiaMouseMove;
        element.MouseUp += OnSkiaMouseUp;
        element.MouseLeave += OnSkiaMouseLeave;
    }

    protected virtual void DetachInputHandlers(UIElement element)
    {
        element.MouseDown -= OnSkiaMouseDown;
        element.MouseMove -= OnSkiaMouseMove;
        element.MouseUp -= OnSkiaMouseUp;
        element.MouseLeave -= OnSkiaMouseLeave;
    }

    protected virtual void AttachWinFormsInputHandlers(SKGLControl control)
    {
        control.MouseDown += OnSkiaMouseDownWinForms;
        control.MouseMove += OnSkiaMouseMoveWinForms;
        control.MouseUp += OnSkiaMouseUpWinForms;
        control.MouseLeave += OnSkiaMouseLeaveWinForms;
    }

    protected virtual void DetachWinFormsInputHandlers(SKGLControl control)
    {
        control.MouseDown -= OnSkiaMouseDownWinForms;
        control.MouseMove -= OnSkiaMouseMoveWinForms;
        control.MouseUp -= OnSkiaMouseUpWinForms;
        control.MouseLeave -= OnSkiaMouseLeaveWinForms;
    }

    protected virtual void OnSkiaMouseDown(object sender, MouseButtonEventArgs e) { }
    protected virtual void OnSkiaMouseMove(object sender, System.Windows.Input.MouseEventArgs e) { }
    protected virtual void OnSkiaMouseUp(object sender, MouseButtonEventArgs e) { }
    protected virtual void OnSkiaMouseLeave(object sender, System.Windows.Input.MouseEventArgs e) { }

    protected virtual void OnSkiaMouseDownWinForms(object? sender, System.Windows.Forms.MouseEventArgs e) { }
    protected virtual void OnSkiaMouseMoveWinForms(object? sender, System.Windows.Forms.MouseEventArgs e) { }
    protected virtual void OnSkiaMouseUpWinForms(object? sender, System.Windows.Forms.MouseEventArgs e) { }
    protected virtual void OnSkiaMouseLeaveWinForms(object? sender, EventArgs e) { }

    protected static MouseButton? ToWpfMouseButton(System.Windows.Forms.MouseButtons button)
    {
        if ((button & System.Windows.Forms.MouseButtons.Left) != 0)
            return MouseButton.Left;
        if ((button & System.Windows.Forms.MouseButtons.Right) != 0)
            return MouseButton.Right;
        if ((button & System.Windows.Forms.MouseButtons.Middle) != 0)
            return MouseButton.Middle;
        return null;
    }

    #endregion

    public virtual void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _renderTimer.Stop();
        _subscription?.Dispose();
        _subscription = null;

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
        GC.SuppressFinalize(this);
    }
}
