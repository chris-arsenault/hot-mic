using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using HotMic.App.UI.PluginComponents;
using HotMic.Core.Analysis;
using HotMic.Core.Dsp.Analysis.Speech;
using SkiaSharp;

namespace HotMic.App.Views;

public partial class SpeechCoachWindow : AnalysisWindowBase
{
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 0x0002;

    private readonly SpeechCoachRenderer _renderer;

    // Data buffers
    private byte[] _speakingStateTrack = Array.Empty<byte>();
    private byte[] _syllableMarkers = Array.Empty<byte>();
    private float[] _syllableRateTrack = Array.Empty<float>();
    private float[] _articulationRateTrack = Array.Empty<float>();
    private float[] _pauseRatioTrack = Array.Empty<float>();
    private float[] _monotoneScoreTrack = Array.Empty<float>();
    private float[] _clarityScoreTrack = Array.Empty<float>();
    private float[] _intelligibilityTrack = Array.Empty<float>();
    private int _bufferFrameCount;
    private long _lastCopiedFrameId = -1;

    // Latest metrics (from most recent frame)
    private float _syllableRate;
    private float _articulationRate;
    private float _pauseRatio;
    private float _monotoneScore;
    private float _clarityScore;
    private float _intelligibilityScore;

    public SpeechCoachWindow(AnalysisOrchestrator orchestrator)
        : base(orchestrator)
    {
        InitializeComponent();
        _renderer = new SpeechCoachRenderer(PluginComponentTheme.BlueOnBlack);
        InitializeSkiaSurface(SkiaHost);
    }

    protected override AnalysisCapabilities ComputeRequiredCapabilities()
    {
        return AnalysisCapabilities.SpeechMetrics | AnalysisCapabilities.Pitch | AnalysisCapabilities.VoicingState;
    }

    protected override void OnRenderTick(object? sender, EventArgs e)
    {
        EnsureBuffers();
        CopySpeechData();
        InvalidateRenderSurface();
    }

    private void EnsureBuffers()
    {
        int frameCapacity = Store.FrameCapacity;
        if (_bufferFrameCount != frameCapacity)
        {
            _bufferFrameCount = frameCapacity;
            _speakingStateTrack = new byte[frameCapacity];
            _syllableMarkers = new byte[frameCapacity];
            _syllableRateTrack = new float[frameCapacity];
            _articulationRateTrack = new float[frameCapacity];
            _pauseRatioTrack = new float[frameCapacity];
            _monotoneScoreTrack = new float[frameCapacity];
            _clarityScoreTrack = new float[frameCapacity];
            _intelligibilityTrack = new float[frameCapacity];
            _lastCopiedFrameId = -1;
        }
    }

    private void CopySpeechData()
    {
        // Try to get speech metrics from the store
        bool copied = Store.TryGetSpeechMetrics(
            _lastCopiedFrameId,
            _syllableRateTrack,
            _articulationRateTrack,
            _pauseRatioTrack,
            _monotoneScoreTrack,
            _clarityScoreTrack,
            _intelligibilityTrack,
            _speakingStateTrack,
            _syllableMarkers,
            out long latestFrameId,
            out int availableFrames,
            out _);

        if (copied && availableFrames > 0)
        {
            _lastCopiedFrameId = latestFrameId;

            // Get latest metric values from the most recent frame
            int latestIndex = availableFrames - 1;
            _syllableRate = _syllableRateTrack[latestIndex];
            _articulationRate = _articulationRateTrack[latestIndex];
            _pauseRatio = _pauseRatioTrack[latestIndex];
            _monotoneScore = _monotoneScoreTrack[latestIndex];
            _clarityScore = _clarityScoreTrack[latestIndex];
            _intelligibilityScore = _intelligibilityTrack[latestIndex];
        }
    }

    protected override void OnRender(SKCanvas canvas, int width, int height)
    {
        _renderer.Render(
            canvas,
            width,
            height,
            _speakingStateTrack,
            _syllableMarkers,
            Store.AvailableFrames,
            _syllableRate,
            _articulationRate,
            _pauseRatio,
            _monotoneScore,
            _clarityScore,
            _intelligibilityScore);
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
