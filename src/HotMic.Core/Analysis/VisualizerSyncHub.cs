using System.Threading;

namespace HotMic.Core.Analysis;

/// <summary>
/// Coordinates timeline view across multiple visualizer windows.
/// Enables synchronized scrolling and shared playhead position.
/// </summary>
public sealed class VisualizerSyncHub
{
    private long _viewStartFrame;
    private long _viewEndFrame;
    private float _timeWindowSeconds = 5f;
    private int _followLatest = 1;
    private long _playheadFrame = -1;

    /// <summary>
    /// Start of the current view range (in frames).
    /// </summary>
    public long ViewStartFrame
    {
        get => Volatile.Read(ref _viewStartFrame);
        private set => Volatile.Write(ref _viewStartFrame, value);
    }

    /// <summary>
    /// End of the current view range (in frames).
    /// </summary>
    public long ViewEndFrame
    {
        get => Volatile.Read(ref _viewEndFrame);
        private set => Volatile.Write(ref _viewEndFrame, value);
    }

    /// <summary>
    /// Time window in seconds for the view.
    /// </summary>
    public float TimeWindowSeconds
    {
        get => Volatile.Read(ref _timeWindowSeconds);
        set
        {
            float clamped = Math.Clamp(value, 1f, 60f);
            Volatile.Write(ref _timeWindowSeconds, clamped);
            Invalidate();
        }
    }

    /// <summary>
    /// When true, automatically scrolls to show the latest frames.
    /// </summary>
    public bool FollowLatest
    {
        get => Volatile.Read(ref _followLatest) != 0;
        set
        {
            Volatile.Write(ref _followLatest, value ? 1 : 0);
            if (value)
                Invalidate();
        }
    }

    /// <summary>
    /// Optional playhead position for synchronized playback.
    /// -1 indicates no active playhead.
    /// </summary>
    public long PlayheadFrame
    {
        get => Volatile.Read(ref _playheadFrame);
        set
        {
            Volatile.Write(ref _playheadFrame, value);
            Invalidate();
        }
    }

    /// <summary>
    /// Raised when the view range changes.
    /// </summary>
    public event Action<long, long>? ViewRangeChanged;

    /// <summary>
    /// Raised when visualizers should redraw.
    /// </summary>
    public event Action? Invalidated;

    /// <summary>
    /// Scroll the view by a frame delta.
    /// </summary>
    public void ScrollBy(int frameDelta)
    {
        if (frameDelta == 0)
            return;

        // Disable follow mode when manually scrolling backward
        if (frameDelta < 0)
            Volatile.Write(ref _followLatest, 0);

        long newStart = ViewStartFrame + frameDelta;
        long newEnd = ViewEndFrame + frameDelta;

        ViewStartFrame = Math.Max(0, newStart);
        ViewEndFrame = Math.Max(ViewStartFrame, newEnd);

        ViewRangeChanged?.Invoke(ViewStartFrame, ViewEndFrame);
        Invalidate();
    }

    /// <summary>
    /// Scroll to center on a specific frame.
    /// </summary>
    public void ScrollTo(long frameId)
    {
        Volatile.Write(ref _followLatest, 0);

        long halfWindow = (ViewEndFrame - ViewStartFrame) / 2;
        ViewStartFrame = Math.Max(0, frameId - halfWindow);
        ViewEndFrame = frameId + halfWindow;

        ViewRangeChanged?.Invoke(ViewStartFrame, ViewEndFrame);
        Invalidate();
    }

    /// <summary>
    /// Update the view range based on latest frame and time window.
    /// Called by the orchestrator when new frames are available.
    /// </summary>
    public void UpdateViewRange(long latestFrameId, int frameCapacity, int sampleRate, int hopSize)
    {
        if (!FollowLatest)
            return;

        float windowSeconds = TimeWindowSeconds;
        int framesInWindow = (int)MathF.Ceiling(windowSeconds * sampleRate / Math.Max(1, hopSize));
        framesInWindow = Math.Min(framesInWindow, frameCapacity);

        ViewEndFrame = latestFrameId;
        ViewStartFrame = Math.Max(0, latestFrameId - framesInWindow + 1);

        ViewRangeChanged?.Invoke(ViewStartFrame, ViewEndFrame);
    }

    /// <summary>
    /// Request all visualizers to redraw.
    /// </summary>
    public void Invalidate()
    {
        Invalidated?.Invoke();
    }

    /// <summary>
    /// Reset to initial state.
    /// </summary>
    public void Reset()
    {
        ViewStartFrame = 0;
        ViewEndFrame = 0;
        Volatile.Write(ref _followLatest, 1);
        Volatile.Write(ref _playheadFrame, -1);
        Invalidate();
    }
}
