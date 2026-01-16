using HotMic.Core.Dsp.Analysis;

namespace HotMic.Core.Dsp.Analysis.Speech;

/// <summary>
/// Analyzes pitch contour for prosody metrics.
/// Computes pitch range, variation, slope, and monotone detection.
/// </summary>
public sealed class PitchContourAnalyzer
{
    private const int MaxHistorySize = 512;
    private const float ReferenceFrequency = 100f; // Reference for semitone conversion

    private readonly float[] _pitchHistory = new float[MaxHistorySize]; // In semitones
    private readonly long[] _frameHistory = new long[MaxHistorySize];
    private int _historyHead;
    private int _historyCount;

    private float _windowSeconds = 5f;

    /// <summary>
    /// Analysis window duration in seconds.
    /// </summary>
    public float WindowSeconds
    {
        get => _windowSeconds;
        set => _windowSeconds = Math.Clamp(value, 1f, 30f);
    }

    /// <summary>
    /// Threshold in semitones below which speech is considered monotone.
    /// </summary>
    public float MonotoneThresholdSemitones { get; set; } = 2f;

    /// <summary>
    /// Record a pitch value for a voiced frame.
    /// </summary>
    /// <param name="pitchHz">Pitch frequency in Hz (0 if unvoiced).</param>
    /// <param name="voicing">Voicing state.</param>
    /// <param name="frameId">Current frame ID.</param>
    public void RecordPitch(float pitchHz, VoicingState voicing, long frameId)
    {
        // Only record voiced frames with valid pitch
        if (voicing != VoicingState.Voiced || pitchHz <= 0f)
        {
            return;
        }

        // Convert to semitones relative to reference
        float semitones = 12f * MathF.Log2(pitchHz / ReferenceFrequency);

        _pitchHistory[_historyHead] = semitones;
        _frameHistory[_historyHead] = frameId;
        _historyHead = (_historyHead + 1) % MaxHistorySize;
        _historyCount = Math.Min(_historyCount + 1, MaxHistorySize);
    }

    /// <summary>
    /// Compute prosody metrics for the current window.
    /// </summary>
    /// <param name="currentFrameId">Current frame ID.</param>
    /// <param name="hopSize">Hop size in samples.</param>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <param name="pitchRangeSemitones">Output: max - min pitch in semitones.</param>
    /// <param name="pitchVariationSemitones">Output: standard deviation of pitch in semitones.</param>
    /// <param name="pitchSlopeSemitones">Output: linear regression slope (rising/falling tendency).</param>
    /// <param name="monotoneScore">Output: 0-1 score where 1 = highly monotone.</param>
    /// <param name="meanPitchHz">Output: mean pitch in Hz.</param>
    public void Compute(
        long currentFrameId,
        int hopSize,
        int sampleRate,
        out float pitchRangeSemitones,
        out float pitchVariationSemitones,
        out float pitchSlopeSemitones,
        out float monotoneScore,
        out float meanPitchHz)
    {
        pitchRangeSemitones = 0f;
        pitchVariationSemitones = 0f;
        pitchSlopeSemitones = 0f;
        monotoneScore = 1f;
        meanPitchHz = 0f;

        if (_historyCount == 0)
        {
            return;
        }

        long windowFrames = (long)(_windowSeconds * sampleRate / hopSize);
        long windowStartFrame = currentFrameId - windowFrames;

        // Collect pitched frames in window
        float sum = 0f;
        float sumSq = 0f;
        float minPitch = float.MaxValue;
        float maxPitch = float.MinValue;
        int count = 0;

        // For linear regression
        double sumX = 0;
        double sumY = 0;
        double sumXY = 0;
        double sumX2 = 0;

        for (int i = 0; i < _historyCount; i++)
        {
            int idx = (_historyHead - 1 - i + MaxHistorySize) % MaxHistorySize;
            long frame = _frameHistory[idx];

            if (frame < windowStartFrame)
            {
                break;
            }

            float pitch = _pitchHistory[idx];
            sum += pitch;
            sumSq += pitch * pitch;
            minPitch = MathF.Min(minPitch, pitch);
            maxPitch = MathF.Max(maxPitch, pitch);

            // Use relative frame position for regression
            double x = frame - windowStartFrame;
            sumX += x;
            sumY += pitch;
            sumXY += x * pitch;
            sumX2 += x * x;

            count++;
        }

        if (count < 2)
        {
            if (count == 1)
            {
                meanPitchHz = ReferenceFrequency * MathF.Pow(2f, sum / 12f);
            }
            return;
        }

        // Mean and variance
        float mean = sum / count;
        float variance = (sumSq / count) - (mean * mean);
        float stdDev = variance > 0f ? MathF.Sqrt(variance) : 0f;

        // Range
        pitchRangeSemitones = maxPitch - minPitch;
        pitchVariationSemitones = stdDev;

        // Linear regression slope
        double denom = count * sumX2 - sumX * sumX;
        if (MathF.Abs((float)denom) > 1e-6f)
        {
            pitchSlopeSemitones = (float)((count * sumXY - sumX * sumY) / denom);
            // Scale to semitones per second
            float framesPerSecond = sampleRate / (float)hopSize;
            pitchSlopeSemitones *= framesPerSecond;
        }

        // Monotone score: inverse of variation normalized by threshold
        // Score = 1 means highly monotone, 0 means varied
        monotoneScore = 1f - MathF.Min(pitchVariationSemitones / MonotoneThresholdSemitones, 1f);
        monotoneScore = MathF.Max(0f, monotoneScore);

        // Convert mean back to Hz
        meanPitchHz = ReferenceFrequency * MathF.Pow(2f, mean / 12f);
    }

    /// <summary>
    /// Get the pitch direction over recent frames.
    /// </summary>
    /// <param name="framesBack">Number of frames to look back.</param>
    /// <returns>Positive = rising, negative = falling, 0 = flat or insufficient data.</returns>
    public float GetRecentPitchDirection(int framesBack = 10)
    {
        if (_historyCount < 2)
        {
            return 0f;
        }

        int samplesToUse = Math.Min(framesBack, _historyCount);
        int newestIdx = (_historyHead - 1 + MaxHistorySize) % MaxHistorySize;
        int oldestIdx = (_historyHead - samplesToUse + MaxHistorySize) % MaxHistorySize;

        return _pitchHistory[newestIdx] - _pitchHistory[oldestIdx];
    }

    /// <summary>
    /// Reset the analyzer state.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_pitchHistory, 0, _pitchHistory.Length);
        Array.Clear(_frameHistory, 0, _frameHistory.Length);
        _historyHead = 0;
        _historyCount = 0;
    }
}
