using HotMic.Core.Dsp.Analysis;

namespace HotMic.Core.Dsp.Analysis.Speech;

/// <summary>
/// Type of pause detected.
/// </summary>
public enum PauseType : byte
{
    None = 0,
    Silent = 1,
    Filled = 2
}

/// <summary>
/// Current state of the pause detector.
/// </summary>
public enum SpeakingState : byte
{
    Speaking = 0,
    SilentPause = 1,
    FilledPause = 2
}

/// <summary>
/// Represents a completed pause event.
/// </summary>
public readonly struct PauseEvent
{
    public PauseType Type { get; }
    public long StartFrame { get; }
    public long EndFrame { get; }
    public float DurationMs { get; }

    public PauseEvent(PauseType type, long startFrame, long endFrame, float durationMs)
    {
        Type = type;
        StartFrame = startFrame;
        EndFrame = endFrame;
        DurationMs = durationMs;
    }
}

/// <summary>
/// Detects silent and filled pauses in speech.
/// Silent pauses: voicing == Silence for sustained duration.
/// Filled pauses: voiced segments with low pitch confidence and high spectral flatness ("uh", "um").
/// </summary>
public sealed class PauseDetector
{
    private const float DefaultSilentPauseThresholdMs = 150f;
    private const float DefaultFilledPauseThresholdMs = 100f;
    private const float DefaultPitchConfidenceThreshold = 0.3f;
    private const float DefaultSpectralFlatnessThreshold = 0.4f;

    private SpeakingState _currentState = SpeakingState.Speaking;
    private long _pauseStartFrame;
    private int _silenceFrameCount;
    private int _filledPauseFrameCount;
    private int _speakingFrameCount;
    private long _statsFrames;
    private long _statsSilenceFrames;
    private long _statsFilledCandidateFrames;
    private long _statsSpeakingFrames;
    private long _statsSilentPauseEvents;
    private long _statsFilledPauseEvents;
    private long _statsSilentPauseFrames;
    private long _statsFilledPauseFrames;

    /// <summary>
    /// Minimum duration in ms for a silence to count as a pause.
    /// </summary>
    public float SilentPauseThresholdMs { get; set; } = DefaultSilentPauseThresholdMs;

    /// <summary>
    /// Minimum duration in ms for a filled pause to be detected.
    /// </summary>
    public float FilledPauseThresholdMs { get; set; } = DefaultFilledPauseThresholdMs;

    /// <summary>
    /// Pitch confidence below this indicates potential filler.
    /// </summary>
    public float PitchConfidenceThreshold { get; set; } = DefaultPitchConfidenceThreshold;

    /// <summary>
    /// Spectral flatness above this (during voiced) indicates potential filler.
    /// </summary>
    public float SpectralFlatnessThreshold { get; set; } = DefaultSpectralFlatnessThreshold;

    /// <summary>
    /// Current speaking/pause state.
    /// </summary>
    public SpeakingState CurrentState => _currentState;

    /// <summary>
    /// Snapshot of pause detector debug counters.
    /// </summary>
    public PauseDetectorDebugStats DebugStats => new(
        _statsFrames,
        _statsSilenceFrames,
        _statsFilledCandidateFrames,
        _statsSpeakingFrames,
        _statsSilentPauseEvents,
        _statsFilledPauseEvents,
        _statsSilentPauseFrames,
        _statsFilledPauseFrames);

    /// <summary>
    /// Process a frame and detect pause transitions.
    /// </summary>
    /// <param name="voicing">Voicing state.</param>
    /// <param name="pitchConfidence">Pitch detection confidence (0-1).</param>
    /// <param name="spectralFlatness">Spectral flatness (0-1).</param>
    /// <param name="frameId">Current frame ID.</param>
    /// <param name="hopSize">Hop size in samples.</param>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <param name="pauseEvent">Output pause event if a pause just ended.</param>
    /// <returns>True if a pause event was completed this frame.</returns>
    public bool Process(
        VoicingState voicing,
        float pitchConfidence,
        float spectralFlatness,
        long frameId,
        int hopSize,
        int sampleRate,
        out PauseEvent pauseEvent)
    {
        pauseEvent = default;
        float frameDurationMs = 1000f * hopSize / sampleRate;

        // Classify current frame
        bool isSilence = voicing == VoicingState.Silence;
        bool isFilledPauseCandidate = voicing == VoicingState.Voiced
            && pitchConfidence < PitchConfidenceThreshold
            && spectralFlatness > SpectralFlatnessThreshold;
        bool isSpeaking = !isSilence && !isFilledPauseCandidate;

        _statsFrames++;
        if (isSilence)
        {
            _statsSilenceFrames++;
        }
        else if (isFilledPauseCandidate)
        {
            _statsFilledCandidateFrames++;
        }
        else
        {
            _statsSpeakingFrames++;
        }

        switch (_currentState)
        {
            case SpeakingState.Speaking:
                if (isSilence)
                {
                    _silenceFrameCount++;
                    float silenceDuration = _silenceFrameCount * frameDurationMs;
                    if (silenceDuration >= SilentPauseThresholdMs)
                    {
                        _currentState = SpeakingState.SilentPause;
                        _pauseStartFrame = frameId - _silenceFrameCount + 1;
                    }
                }
                else if (isFilledPauseCandidate)
                {
                    _filledPauseFrameCount++;
                    float filledDuration = _filledPauseFrameCount * frameDurationMs;
                    if (filledDuration >= FilledPauseThresholdMs)
                    {
                        _currentState = SpeakingState.FilledPause;
                        _pauseStartFrame = frameId - _filledPauseFrameCount + 1;
                    }
                    _silenceFrameCount = 0;
                }
                else
                {
                    _silenceFrameCount = 0;
                    _filledPauseFrameCount = 0;
                    _speakingFrameCount++;
                }
                break;

            case SpeakingState.SilentPause:
                if (isSilence)
                {
                    // Continue silent pause
                    _silenceFrameCount++;
                }
                else
                {
                    // End of silent pause
                    float duration = _silenceFrameCount * frameDurationMs;
                    pauseEvent = new PauseEvent(PauseType.Silent, _pauseStartFrame, frameId - 1, duration);
                    _currentState = SpeakingState.Speaking;
                    _silenceFrameCount = 0;
                    _filledPauseFrameCount = isFilledPauseCandidate ? 1 : 0;
                    _speakingFrameCount = isSpeaking ? 1 : 0;
                    _statsSilentPauseEvents++;
                    return true;
                }
                break;

            case SpeakingState.FilledPause:
                if (isFilledPauseCandidate)
                {
                    // Continue filled pause
                    _filledPauseFrameCount++;
                }
                else
                {
                    // End of filled pause
                    float duration = _filledPauseFrameCount * frameDurationMs;
                    pauseEvent = new PauseEvent(PauseType.Filled, _pauseStartFrame, frameId - 1, duration);
                    _currentState = SpeakingState.Speaking;
                    _filledPauseFrameCount = 0;
                    _silenceFrameCount = isSilence ? 1 : 0;
                    _speakingFrameCount = isSpeaking ? 1 : 0;
                    _statsFilledPauseEvents++;
                    return true;
                }
                break;
        }

        switch (_currentState)
        {
            case SpeakingState.SilentPause:
                _statsSilentPauseFrames++;
                break;
            case SpeakingState.FilledPause:
                _statsFilledPauseFrames++;
                break;
        }

        return false;
    }

    /// <summary>
    /// Get the duration of the current pause in progress (if any).
    /// </summary>
    public float GetCurrentPauseDurationMs(int hopSize, int sampleRate)
    {
        float frameDurationMs = 1000f * hopSize / sampleRate;
        return _currentState switch
        {
            SpeakingState.SilentPause => _silenceFrameCount * frameDurationMs,
            SpeakingState.FilledPause => _filledPauseFrameCount * frameDurationMs,
            _ => 0f
        };
    }

    /// <summary>
    /// Reset the detector state.
    /// </summary>
    public void Reset()
    {
        _currentState = SpeakingState.Speaking;
        _pauseStartFrame = 0;
        _silenceFrameCount = 0;
        _filledPauseFrameCount = 0;
        _speakingFrameCount = 0;
        _statsFrames = 0;
        _statsSilenceFrames = 0;
        _statsFilledCandidateFrames = 0;
        _statsSpeakingFrames = 0;
        _statsSilentPauseEvents = 0;
        _statsFilledPauseEvents = 0;
        _statsSilentPauseFrames = 0;
        _statsFilledPauseFrames = 0;
    }
}

public readonly record struct PauseDetectorDebugStats(
    long Frames,
    long SilenceFrames,
    long FilledCandidateFrames,
    long SpeakingFrames,
    long SilentPauseEvents,
    long FilledPauseEvents,
    long SilentPauseFrames,
    long FilledPauseFrames);
