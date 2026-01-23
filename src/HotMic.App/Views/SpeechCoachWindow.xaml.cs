using System;
using System.Windows.Input;
using HotMic.App.UI.PluginComponents;
using HotMic.Core.Analysis;
using HotMic.Core.Dsp.Analysis;
using SkiaSharp;

namespace HotMic.App.Views;

public partial class SpeechCoachWindow : AnalysisWindowBase
{
    private readonly SpeechCoachRenderer _renderer = new();

    // Data buffers
    private byte[] _speakingStateTrack = Array.Empty<byte>();
    private byte[] _syllableMarkers = Array.Empty<byte>();
    private byte[] _emphasisMarkers = Array.Empty<byte>();
    private float[] _syllableRateTrack = Array.Empty<float>();
    private float[] _articulationRateTrack = Array.Empty<float>();
    private float[] _wordsPerMinuteTrack = Array.Empty<float>();
    private float[] _articulationWpmTrack = Array.Empty<float>();
    private float[] _pauseRatioTrack = Array.Empty<float>();
    private float[] _meanPauseDurationTrack = Array.Empty<float>();
    private float[] _pausesPerMinuteTrack = Array.Empty<float>();
    private float[] _filledPauseRatioTrack = Array.Empty<float>();
    private float[] _pauseMicroCountTrack = Array.Empty<float>();
    private float[] _pauseShortCountTrack = Array.Empty<float>();
    private float[] _pauseMediumCountTrack = Array.Empty<float>();
    private float[] _pauseLongCountTrack = Array.Empty<float>();
    private float[] _monotoneScoreTrack = Array.Empty<float>();
    private float[] _clarityScoreTrack = Array.Empty<float>();
    private float[] _intelligibilityTrack = Array.Empty<float>();
    private float[] _bandLowRatioTrack = Array.Empty<float>();
    private float[] _bandMidRatioTrack = Array.Empty<float>();
    private float[] _bandPresenceRatioTrack = Array.Empty<float>();
    private float[] _bandHighRatioTrack = Array.Empty<float>();
    private float[] _clarityRatioTrack = Array.Empty<float>();
    private float[] _pitchTrack = Array.Empty<float>();
    private float[] _pitchConfidence = Array.Empty<float>();
    private byte[] _voicingStates = Array.Empty<byte>();
    private float[] _waveformMin = Array.Empty<float>();
    private float[] _waveformMax = Array.Empty<float>();
    private float[] _pitchScratch = Array.Empty<float>();
    private int _bufferFrameCount;
    private long _lastCopiedFrameId = -1;
    private long _latestFrameId = -1;
    private int _availableFrames;
    private int _pitchMedianCounter;
    private float _pitchMedianHz;

    // Latest metrics (from most recent frame)
    private float _wordsPerMinute;
    private float _articulationWpm;
    private float _pauseRatio;
    private float _meanPauseDurationMs;
    private float _monotoneScore;
    private float _clarityRatio;
    private float _intelligibilityScore;
    private float _bandLowRatio;
    private float _bandMidRatio;
    private float _bandPresenceRatio;
    private float _bandHighRatio;
    private float _pauseMicroCount;
    private float _pauseShortCount;
    private float _pauseMediumCount;
    private float _pauseLongCount;

    public SpeechCoachWindow(AnalysisOrchestrator orchestrator)
        : base(orchestrator)
    {
        InitializeComponent();
        InitializeSkiaSurface(SkiaHost);
    }

    protected override AnalysisCapabilities ComputeRequiredCapabilities() =>
        AnalysisCapabilities.SpeechMetrics |
        AnalysisCapabilities.Pitch |
        AnalysisCapabilities.VoicingState |
        AnalysisCapabilities.Waveform;

    protected override void OnRenderTick(object? sender, EventArgs e)
    {
        EnsureBuffers();
        CopySpeechData();
        InvalidateRenderSurface();
    }

    protected override void OnRender(SKCanvas canvas, int width, int height)
    {
        var summary = new SpeechCoachSummary(
            _wordsPerMinute,
            _articulationWpm,
            _pauseRatio,
            _meanPauseDurationMs,
            _clarityRatio,
            _monotoneScore,
            _intelligibilityScore,
            _bandLowRatio,
            _bandMidRatio,
            _bandPresenceRatio,
            _bandHighRatio,
            _pauseMicroCount,
            _pauseShortCount,
            _pauseMediumCount,
            _pauseLongCount);

        var state = new SpeechCoachState(
            _latestFrameId,
            _availableFrames,
            _bufferFrameCount,
            _pitchTrack,
            _pitchConfidence,
            _voicingStates,
            _waveformMin,
            _waveformMax,
            _speakingStateTrack,
            _syllableMarkers,
            _emphasisMarkers,
            _wordsPerMinuteTrack,
            _articulationWpmTrack,
            _pauseRatioTrack,
            _pitchMedianHz,
            summary);

        _renderer.Render(canvas, width, height, state);
    }

    private void EnsureBuffers()
    {
        int frameCapacity = Store.FrameCapacity;
        if (_bufferFrameCount != frameCapacity)
        {
            _bufferFrameCount = frameCapacity;
            _speakingStateTrack = new byte[frameCapacity];
            _syllableMarkers = new byte[frameCapacity];
            _emphasisMarkers = new byte[frameCapacity];
            _syllableRateTrack = new float[frameCapacity];
            _articulationRateTrack = new float[frameCapacity];
            _wordsPerMinuteTrack = new float[frameCapacity];
            _articulationWpmTrack = new float[frameCapacity];
            _pauseRatioTrack = new float[frameCapacity];
            _meanPauseDurationTrack = new float[frameCapacity];
            _pausesPerMinuteTrack = new float[frameCapacity];
            _filledPauseRatioTrack = new float[frameCapacity];
            _pauseMicroCountTrack = new float[frameCapacity];
            _pauseShortCountTrack = new float[frameCapacity];
            _pauseMediumCountTrack = new float[frameCapacity];
            _pauseLongCountTrack = new float[frameCapacity];
            _monotoneScoreTrack = new float[frameCapacity];
            _clarityScoreTrack = new float[frameCapacity];
            _intelligibilityTrack = new float[frameCapacity];
            _bandLowRatioTrack = new float[frameCapacity];
            _bandMidRatioTrack = new float[frameCapacity];
            _bandPresenceRatioTrack = new float[frameCapacity];
            _bandHighRatioTrack = new float[frameCapacity];
            _clarityRatioTrack = new float[frameCapacity];
            _pitchTrack = new float[frameCapacity];
            _pitchConfidence = new float[frameCapacity];
            _voicingStates = new byte[frameCapacity];
            _waveformMin = new float[frameCapacity];
            _waveformMax = new float[frameCapacity];
            _pitchScratch = new float[frameCapacity];
            _lastCopiedFrameId = -1;
            _latestFrameId = -1;
            _availableFrames = 0;
            _pitchMedianCounter = 0;
            _pitchMedianHz = 0f;
        }
    }

    private void CopySpeechData()
    {
        bool speechCopied = Store.TryGetSpeechMetrics(
            _lastCopiedFrameId,
            _syllableRateTrack,
            _articulationRateTrack,
            _wordsPerMinuteTrack,
            _articulationWpmTrack,
            _pauseRatioTrack,
            _meanPauseDurationTrack,
            _pausesPerMinuteTrack,
            _filledPauseRatioTrack,
            _pauseMicroCountTrack,
            _pauseShortCountTrack,
            _pauseMediumCountTrack,
            _pauseLongCountTrack,
            _monotoneScoreTrack,
            _clarityScoreTrack,
            _intelligibilityTrack,
            _bandLowRatioTrack,
            _bandMidRatioTrack,
            _bandPresenceRatioTrack,
            _bandHighRatioTrack,
            _clarityRatioTrack,
            _speakingStateTrack,
            _syllableMarkers,
            _emphasisMarkers,
            out long latestFrameId,
            out int availableFrames,
            out _);

        Store.TryGetPitchRange(
            _lastCopiedFrameId,
            _pitchTrack,
            _pitchConfidence,
            _voicingStates,
            out _,
            out _,
            out _);

        Store.TryGetWaveformRange(
            _lastCopiedFrameId,
            _waveformMin,
            _waveformMax,
            out _,
            out _,
            out _);

        if (speechCopied)
        {
            _lastCopiedFrameId = latestFrameId;
            _latestFrameId = latestFrameId;
            _availableFrames = availableFrames;

            if (availableFrames > 0 && _bufferFrameCount > 0 && latestFrameId >= 0)
            {
                int latestIndex = (int)(latestFrameId % _bufferFrameCount);
                if (latestIndex < 0)
                {
                    latestIndex += _bufferFrameCount;
                }

                _wordsPerMinute = _wordsPerMinuteTrack[latestIndex];
                _articulationWpm = _articulationWpmTrack[latestIndex];
                _pauseRatio = _pauseRatioTrack[latestIndex];
                _meanPauseDurationMs = _meanPauseDurationTrack[latestIndex];
                _monotoneScore = _monotoneScoreTrack[latestIndex];
                _clarityRatio = _clarityRatioTrack[latestIndex];
                _intelligibilityScore = _intelligibilityTrack[latestIndex];
                _bandLowRatio = _bandLowRatioTrack[latestIndex];
                _bandMidRatio = _bandMidRatioTrack[latestIndex];
                _bandPresenceRatio = _bandPresenceRatioTrack[latestIndex];
                _bandHighRatio = _bandHighRatioTrack[latestIndex];
                _pauseMicroCount = _pauseMicroCountTrack[latestIndex];
                _pauseShortCount = _pauseShortCountTrack[latestIndex];
                _pauseMediumCount = _pauseMediumCountTrack[latestIndex];
                _pauseLongCount = _pauseLongCountTrack[latestIndex];
            }
        }
        else
        {
            _latestFrameId = Store.LatestFrameId;
            _availableFrames = Store.AvailableFrames;
        }

        if (_availableFrames <= 0)
        {
            _wordsPerMinute = 0f;
            _articulationWpm = 0f;
            _pauseRatio = 0f;
            _meanPauseDurationMs = 0f;
            _monotoneScore = 0f;
            _clarityRatio = 0f;
            _intelligibilityScore = 0f;
            _bandLowRatio = 0f;
            _bandMidRatio = 0f;
            _bandPresenceRatio = 0f;
            _bandHighRatio = 0f;
            _pauseMicroCount = 0f;
            _pauseShortCount = 0f;
            _pauseMediumCount = 0f;
            _pauseLongCount = 0f;
        }

        if (_availableFrames > 0 && _bufferFrameCount > 0)
        {
            _pitchMedianCounter++;
            if (_pitchMedianCounter >= 15)
            {
                _pitchMedianCounter = 0;
                UpdatePitchMedian();
            }
            else if (_pitchMedianHz <= 0f)
            {
                UpdatePitchMedian();
            }
        }
    }

    private void UpdatePitchMedian()
    {
        if (_availableFrames <= 0 || _bufferFrameCount <= 0 || _latestFrameId < 0)
        {
            _pitchMedianHz = 0f;
            return;
        }

        long oldestFrameId = _latestFrameId - _availableFrames + 1;
        int count = 0;
        for (int i = 0; i < _availableFrames; i++)
        {
            long frameId = oldestFrameId + i;
            int index = (int)(frameId % _bufferFrameCount);
            if (index < 0)
            {
                index += _bufferFrameCount;
            }

            if (_voicingStates[index] == (byte)VoicingState.Voiced)
            {
                float pitch = _pitchTrack[index];
                if (pitch > 0f)
                {
                    _pitchScratch[count++] = pitch;
                }
            }
        }

        if (count == 0)
        {
            _pitchMedianHz = 0f;
            return;
        }

        Array.Sort(_pitchScratch, 0, count);
        _pitchMedianHz = _pitchScratch[count / 2];
    }

    #region WPF Mouse Handlers (CPU mode)

    protected override void OnSkiaMouseDown(object sender, MouseButtonEventArgs e)
    {
        var element = sender as System.Windows.FrameworkElement;
        if (element is null) return;

        var pos = e.GetPosition(element);
        float x = (float)pos.X;
        float y = (float)pos.Y;

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

    #endregion

    #region WinForms Mouse Handlers (GPU mode)

    protected override void OnSkiaMouseDownWinForms(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        float x = e.X;
        float y = e.Y;

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

    #endregion

    public override void Dispose()
    {
        if (IsDisposed) return;
        _renderer.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
