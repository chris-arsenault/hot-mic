using HotMic.Core.Dsp.Analysis;

namespace HotMic.Core.Dsp.Analysis.Speech;

/// <summary>
/// Detects syllable nuclei using energy peaks filtered by voicing state.
/// Based on de Jong &amp; Wempe (2009) intensity peak detection method.
/// </summary>
public sealed class SyllableDetector
{
    private const int MaxHistoryFrames = 8;
    private const float DefaultProminenceDb = 3f;
    private const float DefaultMinIntervalMs = 50f;

    private readonly float[] _energyHistory = new float[MaxHistoryFrames];
    private readonly bool[] _voicedHistory = new bool[MaxHistoryFrames];
    private int _historyIndex;
    private int _historyCount;
    private long _lastSyllableFrame = -1000;
    private float _smoothedEnergyDb;
    private bool _initialized;

    /// <summary>
    /// Minimum prominence in dB for a peak to be considered a syllable nucleus.
    /// </summary>
    public float ProminenceThresholdDb { get; set; } = DefaultProminenceDb;

    /// <summary>
    /// Minimum interval between syllables in milliseconds.
    /// </summary>
    public float MinIntervalMs { get; set; } = DefaultMinIntervalMs;

    /// <summary>
    /// Smoothing factor for energy envelope (0-1, higher = more smoothing).
    /// </summary>
    public float SmoothingAlpha { get; set; } = 0.15f;

    /// <summary>
    /// Process a frame and detect if it contains a syllable nucleus.
    /// </summary>
    /// <param name="energyDb">Frame energy in dB.</param>
    /// <param name="voicing">Voicing state for this frame.</param>
    /// <param name="frameId">Current frame ID for timing.</param>
    /// <param name="hopSize">Hop size in samples.</param>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <returns>True if a syllable nucleus was detected at this frame.</returns>
    public bool Process(float energyDb, VoicingState voicing, long frameId, int hopSize, int sampleRate)
    {
        // Update smoothed energy
        if (!_initialized)
        {
            _smoothedEnergyDb = energyDb;
            _initialized = true;
        }
        else
        {
            _smoothedEnergyDb = SmoothingAlpha * energyDb + (1f - SmoothingAlpha) * _smoothedEnergyDb;
        }

        // Store in history buffer
        int prevIndex = _historyIndex;
        _energyHistory[_historyIndex] = _smoothedEnergyDb;
        _voicedHistory[_historyIndex] = voicing == VoicingState.Voiced;
        _historyIndex = (_historyIndex + 1) % MaxHistoryFrames;
        _historyCount = Math.Min(_historyCount + 1, MaxHistoryFrames);

        // Need at least 3 frames to detect a peak
        if (_historyCount < 3)
        {
            return false;
        }

        // Check the frame 1 position back (center of 3-frame window)
        int centerIdx = (prevIndex - 1 + MaxHistoryFrames) % MaxHistoryFrames;
        int prevIdx = (prevIndex - 2 + MaxHistoryFrames) % MaxHistoryFrames;

        // Peak detection: center > prev AND center > current
        float centerEnergy = _energyHistory[centerIdx];
        float prevEnergy = _energyHistory[prevIdx];
        float currentEnergy = _smoothedEnergyDb;

        bool isPeak = centerEnergy > prevEnergy && centerEnergy > currentEnergy;
        if (!isPeak)
        {
            return false;
        }

        // Check voicing at center frame
        if (!_voicedHistory[centerIdx])
        {
            return false;
        }

        // Check prominence (dip before and after)
        float minDip = MathF.Min(prevEnergy, currentEnergy);
        float prominence = centerEnergy - minDip;
        if (prominence < ProminenceThresholdDb)
        {
            return false;
        }

        // Check minimum interval
        float frameDurationMs = 1000f * hopSize / sampleRate;
        float minIntervalFrames = MinIntervalMs / frameDurationMs;
        long centerFrame = frameId - 1; // The peak is at the previous frame

        if (centerFrame - _lastSyllableFrame < minIntervalFrames)
        {
            return false;
        }

        // Syllable detected
        _lastSyllableFrame = centerFrame;
        return true;
    }

    /// <summary>
    /// Reset the detector state.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_energyHistory, 0, _energyHistory.Length);
        Array.Clear(_voicedHistory, 0, _voicedHistory.Length);
        _historyIndex = 0;
        _historyCount = 0;
        _lastSyllableFrame = -1000;
        _smoothedEnergyDb = 0f;
        _initialized = false;
    }
}
