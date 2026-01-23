using System;
using System.Diagnostics;
using System.Globalization;
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
    private const string DebugEnvVar = "HOTMIC_SPEECH_DEBUG";
    private static readonly bool DebugLoggingEnabled = GetDebugLoggingEnabled();
    private static readonly long DebugIntervalTicks = Stopwatch.Frequency; // ~1s

    private readonly float[] _energyHistory = new float[MaxHistoryFrames];
    private readonly bool[] _voicedHistory = new bool[MaxHistoryFrames];
    private int _historyIndex;
    private int _historyCount;
    private long _lastSyllableFrame = -1000;
    private float _smoothedEnergyDb;
    private bool _initialized;
    private long _lastDebugTicks;
    private int _debugPeakCount;
    private int _debugVoicedPeakCount;
    private float _debugMaxProminence;
    private float _debugMinEnergy = float.PositiveInfinity;
    private float _debugMaxEnergy = float.NegativeInfinity;
    private long _debugLastDetectedFrame = -1;

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

        if (_smoothedEnergyDb < _debugMinEnergy)
        {
            _debugMinEnergy = _smoothedEnergyDb;
        }
        if (_smoothedEnergyDb > _debugMaxEnergy)
        {
            _debugMaxEnergy = _smoothedEnergyDb;
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
            MaybeLogDebug(frameId, hopSize, sampleRate, isPeak: false, prominence: 0f, voicedCenter: false,
                centerEnergy: 0f, prevEnergy: 0f, currentEnergy: 0f, detected: false);
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
            MaybeLogDebug(frameId, hopSize, sampleRate, isPeak, prominence: 0f, voicedCenter: _voicedHistory[centerIdx],
                centerEnergy, prevEnergy, currentEnergy, detected: false);
            return false;
        }

        _debugPeakCount++;

        // Check voicing at center frame
        bool voicedCenter = _voicedHistory[centerIdx];
        if (!voicedCenter)
        {
            MaybeLogDebug(frameId, hopSize, sampleRate, isPeak, prominence: 0f, voicedCenter,
                centerEnergy, prevEnergy, currentEnergy, detected: false);
            return false;
        }

        _debugVoicedPeakCount++;

        // Check prominence (dip before and after)
        float minDip = MathF.Min(prevEnergy, currentEnergy);
        float prominence = centerEnergy - minDip;
        if (prominence > _debugMaxProminence)
        {
            _debugMaxProminence = prominence;
        }
        if (prominence < ProminenceThresholdDb)
        {
            MaybeLogDebug(frameId, hopSize, sampleRate, isPeak, prominence, voicedCenter,
                centerEnergy, prevEnergy, currentEnergy, detected: false);
            return false;
        }

        // Check minimum interval
        float frameDurationMs = 1000f * hopSize / sampleRate;
        float minIntervalFrames = MinIntervalMs / frameDurationMs;
        long centerFrame = frameId - 1; // The peak is at the previous frame

        if (centerFrame - _lastSyllableFrame < minIntervalFrames)
        {
            MaybeLogDebug(frameId, hopSize, sampleRate, isPeak, prominence, voicedCenter,
                centerEnergy, prevEnergy, currentEnergy, detected: false);
            return false;
        }

        // Syllable detected
        _lastSyllableFrame = centerFrame;
        _debugLastDetectedFrame = centerFrame;
        MaybeLogDebug(frameId, hopSize, sampleRate, isPeak, prominence, voicedCenter,
            centerEnergy, prevEnergy, currentEnergy, detected: true);
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
        _lastDebugTicks = 0;
        _debugPeakCount = 0;
        _debugVoicedPeakCount = 0;
        _debugMaxProminence = 0f;
        _debugMinEnergy = float.PositiveInfinity;
        _debugMaxEnergy = float.NegativeInfinity;
        _debugLastDetectedFrame = -1;
    }

    private static bool GetDebugLoggingEnabled()
    {
        string? value = Environment.GetEnvironmentVariable(DebugEnvVar);
        if (string.IsNullOrWhiteSpace(value))
        {
            return Debugger.IsAttached;
        }

        return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private void MaybeLogDebug(long frameId, int hopSize, int sampleRate, bool isPeak, float prominence, bool voicedCenter,
        float centerEnergy, float prevEnergy, float currentEnergy, bool detected)
    {
        if (!DebugLoggingEnabled)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        if (now - _lastDebugTicks < DebugIntervalTicks)
        {
            return;
        }

        _lastDebugTicks = now;
        float frameDurationMs = sampleRate > 0 ? 1000f * hopSize / sampleRate : 0f;
        float minIntervalFrames = frameDurationMs > 0f ? MinIntervalMs / frameDurationMs : 0f;
        long centerFrame = frameId - 1;
        long framesSinceLast = centerFrame - _lastSyllableFrame;

        string message = string.Format(
            CultureInfo.InvariantCulture,
            "SyllableDebug frame={0} smoothDb={1:0.00} center={2:0.00} prev={3:0.00} curr={4:0.00} isPeak={5} voiced={6} prom={7:0.00} minIntFrames={8:0.0} sinceLast={9} detected={10} peaks={11} voicedPeaks={12} maxProm={13:0.00} minDb={14:0.00} maxDb={15:0.00} lastDet={16}",
            frameId,
            _smoothedEnergyDb,
            centerEnergy,
            prevEnergy,
            currentEnergy,
            isPeak ? 1 : 0,
            voicedCenter ? 1 : 0,
            prominence,
            minIntervalFrames,
            framesSinceLast,
            detected ? 1 : 0,
            _debugPeakCount,
            _debugVoicedPeakCount,
            _debugMaxProminence,
            float.IsPositiveInfinity(_debugMinEnergy) ? 0f : _debugMinEnergy,
            float.IsNegativeInfinity(_debugMaxEnergy) ? 0f : _debugMaxEnergy,
            _debugLastDetectedFrame);

        Console.WriteLine(message);
        Debug.WriteLine(message);

        _debugPeakCount = 0;
        _debugVoicedPeakCount = 0;
        _debugMaxProminence = 0f;
        _debugMinEnergy = float.PositiveInfinity;
        _debugMaxEnergy = float.NegativeInfinity;
    }
}
