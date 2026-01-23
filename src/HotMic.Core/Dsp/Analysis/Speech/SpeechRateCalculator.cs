namespace HotMic.Core.Dsp.Analysis.Speech;

/// <summary>
/// Computes speech rate metrics over a sliding time window.
/// </summary>
public sealed class SpeechRateCalculator
{
    private const int MaxEvents = 1024;

    private readonly long[] _syllableFrames = new long[MaxEvents];
    private readonly long[] _pauseStartFrames = new long[MaxEvents];
    private readonly float[] _pauseDurationsMs = new float[MaxEvents];
    private readonly PauseType[] _pauseTypes = new PauseType[MaxEvents];

    private int _syllableHead;
    private int _syllableCount;
    private int _pauseHead;
    private int _pauseCount;
    private long _debugSyllablesRecorded;
    private long _debugPausesRecorded;

    private float _windowSeconds = 10f;

    /// <summary>
    /// Analysis window duration in seconds.
    /// </summary>
    public float WindowSeconds
    {
        get => _windowSeconds;
        set => _windowSeconds = Math.Clamp(value, 1f, 60f);
    }

    /// <summary>
    /// Record a detected syllable.
    /// </summary>
    public void RecordSyllable(long frameId)
    {
        _syllableFrames[_syllableHead] = frameId;
        _syllableHead = (_syllableHead + 1) % MaxEvents;
        _syllableCount = Math.Min(_syllableCount + 1, MaxEvents);
        _debugSyllablesRecorded++;
    }

    /// <summary>
    /// Record a completed pause event.
    /// </summary>
    public void RecordPause(PauseEvent evt)
    {
        _pauseStartFrames[_pauseHead] = evt.StartFrame;
        _pauseDurationsMs[_pauseHead] = evt.DurationMs;
        _pauseTypes[_pauseHead] = evt.Type;
        _pauseHead = (_pauseHead + 1) % MaxEvents;
        _pauseCount = Math.Min(_pauseCount + 1, MaxEvents);
        _debugPausesRecorded++;
    }

    /// <summary>
    /// Compute speech metrics for the current window.
    /// </summary>
    /// <param name="currentFrameId">Current frame ID.</param>
    /// <param name="hopSize">Hop size in samples.</param>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <param name="syllableRate">Output: syllables per minute (including pauses).</param>
    /// <param name="articulationRate">Output: syllables per minute (excluding pauses).</param>
    /// <param name="pauseRatio">Output: fraction of time spent in pauses (0-1).</param>
    /// <param name="meanPauseDurationMs">Output: average pause duration in ms.</param>
    /// <param name="pausesPerMinute">Output: number of pauses per minute.</param>
    /// <param name="filledPauseRatio">Output: ratio of filled pauses to total pauses (0-1).</param>
    /// <param name="pauseMicroCount">Output: number of micro pauses (150-300ms).</param>
    /// <param name="pauseShortCount">Output: number of short pauses (300-700ms).</param>
    /// <param name="pauseMediumCount">Output: number of medium pauses (700-1500ms).</param>
    /// <param name="pauseLongCount">Output: number of long pauses (>1500ms).</param>
    public void Compute(
        long currentFrameId,
        int hopSize,
        int sampleRate,
        out float syllableRate,
        out float articulationRate,
        out float pauseRatio,
        out float meanPauseDurationMs,
        out float pausesPerMinute,
        out float filledPauseRatio,
        out int pauseMicroCount,
        out int pauseShortCount,
        out int pauseMediumCount,
        out int pauseLongCount)
    {
        float frameDurationMs = 1000f * hopSize / sampleRate;
        float windowMs = _windowSeconds * 1000f;
        long windowFrames = (long)(_windowSeconds * sampleRate / hopSize);
        long windowStartFrame = currentFrameId - windowFrames;

        // Count syllables in window
        int syllablesInWindow = 0;
        for (int i = 0; i < _syllableCount; i++)
        {
            int idx = (_syllableHead - 1 - i + MaxEvents) % MaxEvents;
            long frame = _syllableFrames[idx];
            if (frame < windowStartFrame)
            {
                break;
            }
            syllablesInWindow++;
        }

        // Sum pause durations in window
        float totalPauseDurationMs = 0f;
        float totalFilledPauseDurationMs = 0f;
        int pausesInWindow = 0;
        int filledPausesInWindow = 0;
        pauseMicroCount = 0;
        pauseShortCount = 0;
        pauseMediumCount = 0;
        pauseLongCount = 0;

        for (int i = 0; i < _pauseCount; i++)
        {
            int idx = (_pauseHead - 1 - i + MaxEvents) % MaxEvents;
            long frame = _pauseStartFrames[idx];
            if (frame < windowStartFrame)
            {
                break;
            }

            float duration = _pauseDurationsMs[idx];
            totalPauseDurationMs += duration;
            pausesInWindow++;

            if (_pauseTypes[idx] == PauseType.Filled)
            {
                totalFilledPauseDurationMs += duration;
                filledPausesInWindow++;
            }

            // Pause categories per SPEECH.md 7.2
            if (duration >= 150f && duration < 300f)
            {
                pauseMicroCount++;
            }
            else if (duration < 700f)
            {
                pauseShortCount++;
            }
            else if (duration < 1500f)
            {
                pauseMediumCount++;
            }
            else
            {
                pauseLongCount++;
            }
        }

        // Compute metrics
        float speakingTimeMs = windowMs - totalPauseDurationMs;
        speakingTimeMs = MathF.Max(speakingTimeMs, 1f); // Avoid division by zero

        // Syllables per minute (total time)
        syllableRate = syllablesInWindow / (_windowSeconds / 60f);

        // Syllables per minute (speaking time only)
        float speakingMinutes = speakingTimeMs / 60000f;
        articulationRate = speakingMinutes > 0.001f ? syllablesInWindow / speakingMinutes : 0f;

        // Pause metrics
        pauseRatio = totalPauseDurationMs / windowMs;
        meanPauseDurationMs = pausesInWindow > 0 ? totalPauseDurationMs / pausesInWindow : 0f;
        pausesPerMinute = pausesInWindow / (_windowSeconds / 60f);
        filledPauseRatio = pausesInWindow > 0 ? (float)filledPausesInWindow / pausesInWindow : 0f;
    }

    /// <summary>
    /// Reset all recorded events.
    /// </summary>
    public void Reset()
    {
        _syllableHead = 0;
        _syllableCount = 0;
        _pauseHead = 0;
        _pauseCount = 0;
        _debugSyllablesRecorded = 0;
        _debugPausesRecorded = 0;
    }

    /// <summary>
    /// Snapshot of debug counters for diagnostics.
    /// </summary>
    public SpeechRateDebugStats DebugStats => new(_debugSyllablesRecorded, _debugPausesRecorded, _windowSeconds);
}

public readonly record struct SpeechRateDebugStats(
    long SyllablesRecorded,
    long PausesRecorded,
    float WindowSeconds);
