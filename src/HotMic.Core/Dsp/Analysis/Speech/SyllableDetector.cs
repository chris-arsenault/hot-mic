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
    private const int MaxHistoryFrames = 512;
    private const float DefaultProminenceDb = 3f;
    private const float DefaultUnvoicedProminencePenaltyDb = 1.5f;
    private const float DefaultMinIntervalMs = 50f;
    private const float DefaultPeakSpanMs = 60f;
    private const float DefaultMicroDipClampDb = 3f;
    private const float DefaultEnergyFloorDb = -60f;
    private const string DebugEnvVar = "HOTMIC_SPEECH_DEBUG";
    private static readonly bool DebugLoggingEnabled = GetDebugLoggingEnabled();
    private static readonly long DebugIntervalTicks = Stopwatch.Frequency; // ~1s

    private readonly float[] _energyHistory = new float[MaxHistoryFrames];
    private readonly VoicingState[] _voicingHistory = new VoicingState[MaxHistoryFrames];
    private int _historyIndex;
    private int _historyCount;
    private long _lastSyllableFrame = -1000;
    private float _smoothedEnergyDb;
    private float _baselineEnergyDb;
    private bool _initialized;
    private bool _baselineInitialized;
    private long _lastDebugTicks;
    private int _debugPeakCount;
    private int _debugVoicedPeakCount;
    private float _debugMaxProminence;
    private float _debugMinEnergy = float.PositiveInfinity;
    private float _debugMaxEnergy = float.NegativeInfinity;
    private long _debugLastDetectedFrame = -1;
    private long _statsFrames;
    private long _statsWarmupFrames;
    private long _statsPeaks;
    private long _statsVoicedPeaks;
    private long _statsDetected;
    private long _statsDetectedVoiced;
    private long _statsDetectedUnvoiced;
    private long _statsRejectNotPeak;
    private long _statsRejectUnvoiced;
    private long _statsRejectLowProminence;
    private long _statsRejectLowProminenceInstant;
    private long _statsRejectLowProminenceMean;
    private long _statsRejectMinInterval;
    private long _statsClampApplied;
    private long _statsMeanPenaltyApplied;
    private long _statsBaselineUpdates;
    private long _statsBaselineSkips;
    private float _statsMinEnergyDb = float.PositiveInfinity;
    private float _statsMaxEnergyDb = float.NegativeInfinity;
    private float _statsMinBaselineDb = float.PositiveInfinity;
    private float _statsMaxBaselineDb = float.NegativeInfinity;
    private float _statsMaxProminenceClampDb;
    private float _statsMaxProminenceDb;
    private float _statsMaxProminenceInstantDb;
    private float _statsMaxProminenceMeanDb;
    private long _statsLastDetectedFrame = -1;

    /// <summary>
    /// Snapshot of debug counters for diagnostics.
    /// </summary>
    public SyllableDetectorDebugStats DebugStats => new(
        _statsFrames,
        _statsWarmupFrames,
        _statsPeaks,
        _statsVoicedPeaks,
        _statsDetected,
        _statsDetectedVoiced,
        _statsDetectedUnvoiced,
        _statsRejectNotPeak,
        _statsRejectUnvoiced,
        _statsRejectLowProminence,
        _statsRejectLowProminenceInstant,
        _statsRejectLowProminenceMean,
        _statsRejectMinInterval,
        _statsClampApplied,
        _statsMeanPenaltyApplied,
        _statsBaselineUpdates,
        _statsBaselineSkips,
        float.IsPositiveInfinity(_statsMinEnergyDb) ? 0f : _statsMinEnergyDb,
        float.IsNegativeInfinity(_statsMaxEnergyDb) ? 0f : _statsMaxEnergyDb,
        float.IsPositiveInfinity(_statsMinBaselineDb) ? 0f : _statsMinBaselineDb,
        float.IsNegativeInfinity(_statsMaxBaselineDb) ? 0f : _statsMaxBaselineDb,
        _statsMaxProminenceClampDb,
        _statsMaxProminenceDb,
        _statsMaxProminenceInstantDb,
        _statsMaxProminenceMeanDb,
        _statsLastDetectedFrame);

    /// <summary>
    /// Minimum prominence in dB for a peak to be considered a syllable nucleus.
    /// </summary>
    public float ProminenceThresholdDb { get; set; } = DefaultProminenceDb;

    /// <summary>
    /// Additional prominence required for unvoiced syllable candidates.
    /// </summary>
    public float UnvoicedProminencePenaltyDb { get; set; } = DefaultUnvoicedProminencePenaltyDb;

    /// <summary>
    /// Minimum interval between syllables in milliseconds.
    /// </summary>
    public float MinIntervalMs { get; set; } = DefaultMinIntervalMs;

    /// <summary>
    /// Smoothing factor for energy envelope (0-1).
    /// </summary>
    public float SmoothingAlpha { get; set; } = 0.15f;

    /// <summary>
    /// Time span used for peak comparison on each side of the center frame.
    /// </summary>
    public float PeakSpanMs { get; set; } = DefaultPeakSpanMs;

    /// <summary>
    /// Clamp depth for short-lived dips relative to local mean (dB).
    /// </summary>
    public float MicroDipClampDb { get; set; } = DefaultMicroDipClampDb;

    /// <summary>
    /// Slow baseline smoothing for energy normalization (0-1).
    /// </summary>
    public float BaselineAlpha { get; set; } = 0.01f;

    /// <summary>
    /// Minimum energy floor (dB) to reduce extreme dips from noise reduction.
    /// </summary>
    public float EnergyFloorDb { get; set; } = DefaultEnergyFloorDb;

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
        _statsFrames++;

        float frameDurationMs = sampleRate > 0 ? 1000f * hopSize / sampleRate : 0f;

        energyDb = MathF.Max(energyDb, EnergyFloorDb);

        bool updateBaseline = voicing != VoicingState.Silence;
        if (!_baselineInitialized)
        {
            _baselineEnergyDb = energyDb;
            _baselineInitialized = true;
            _statsBaselineUpdates++;
        }
        else if (updateBaseline)
        {
            _baselineEnergyDb = BaselineAlpha * energyDb + (1f - BaselineAlpha) * _baselineEnergyDb;
            _statsBaselineUpdates++;
        }
        else
        {
            _statsBaselineSkips++;
        }

        if (_baselineEnergyDb < _statsMinBaselineDb)
        {
            _statsMinBaselineDb = _baselineEnergyDb;
        }
        if (_baselineEnergyDb > _statsMaxBaselineDb)
        {
            _statsMaxBaselineDb = _baselineEnergyDb;
        }

        float normalizedEnergyDb = energyDb - _baselineEnergyDb;

        // Update smoothed energy
        if (!_initialized)
        {
            _smoothedEnergyDb = normalizedEnergyDb;
            _initialized = true;
        }
        else
        {
            _smoothedEnergyDb = SmoothingAlpha * normalizedEnergyDb + (1f - SmoothingAlpha) * _smoothedEnergyDb;
        }

        if (_smoothedEnergyDb < _debugMinEnergy)
        {
            _debugMinEnergy = _smoothedEnergyDb;
        }
        if (_smoothedEnergyDb > _debugMaxEnergy)
        {
            _debugMaxEnergy = _smoothedEnergyDb;
        }
        if (_smoothedEnergyDb < _statsMinEnergyDb)
        {
            _statsMinEnergyDb = _smoothedEnergyDb;
        }
        if (_smoothedEnergyDb > _statsMaxEnergyDb)
        {
            _statsMaxEnergyDb = _smoothedEnergyDb;
        }

        // Store in history buffer
        int prevIndex = _historyIndex;
        _energyHistory[_historyIndex] = _smoothedEnergyDb;
        _voicingHistory[_historyIndex] = voicing;
        _historyIndex = (_historyIndex + 1) % MaxHistoryFrames;
        _historyCount = Math.Min(_historyCount + 1, MaxHistoryFrames);

        // Compare the center frame to samples ~PeakSpanMs apart to avoid hop-size aliasing.
        int peakSpanFrames = frameDurationMs > 0f
            ? Math.Max(1, (int)MathF.Round(PeakSpanMs / frameDurationMs))
            : 1;
        int requiredHistory = 2 * peakSpanFrames + 1;

        // Need enough frames to detect a peak with the configured span.
        if (_historyCount < requiredHistory)
        {
            _statsWarmupFrames++;
            MaybeLogDebug(frameId, hopSize, sampleRate, isPeak: false, prominence: 0f, prominenceInstant: 0f, prominenceMean: 0f,
                voicedCenter: false, centerEnergy: 0f, prevEnergy: 0f, currentEnergy: 0f,
                leftMean: 0f, rightMean: 0f, leftMin: 0f, rightMin: 0f, detected: false);
            return false;
        }

        // Check the frame peakSpanFrames back (center of comparison window)
        int centerIdx = (prevIndex - peakSpanFrames + MaxHistoryFrames) % MaxHistoryFrames;
        int prevIdx = (prevIndex - 2 * peakSpanFrames + MaxHistoryFrames) % MaxHistoryFrames;

        // Peak detection: center > prev AND center > current
        float centerEnergy = _energyHistory[centerIdx];
        float prevEnergy = _energyHistory[prevIdx];
        float currentEnergy = _smoothedEnergyDb;

        bool isPeak = centerEnergy > prevEnergy && centerEnergy > currentEnergy;
        if (!isPeak)
        {
            _statsRejectNotPeak++;
            MaybeLogDebug(frameId, hopSize, sampleRate, isPeak, prominence: 0f, prominenceInstant: 0f, prominenceMean: 0f,
                voicedCenter: _voicingHistory[centerIdx] == VoicingState.Voiced, centerEnergy, prevEnergy, currentEnergy,
                leftMean: 0f, rightMean: 0f, leftMin: 0f, rightMin: 0f, detected: false);
            return false;
        }

        _statsPeaks++;
        _debugPeakCount++;

        // Check voicing at center frame
        VoicingState centerVoicing = _voicingHistory[centerIdx];
        bool speechCenter = centerVoicing != VoicingState.Silence;
        bool voicedCenter = centerVoicing == VoicingState.Voiced;
        if (!speechCenter)
        {
            _statsRejectUnvoiced++;
            MaybeLogDebug(frameId, hopSize, sampleRate, isPeak, prominence: 0f, prominenceInstant: 0f, prominenceMean: 0f, voicedCenter,
                centerEnergy, prevEnergy, currentEnergy, leftMean: 0f, rightMean: 0f, leftMin: 0f, rightMin: 0f,
                detected: false);
            return false;
        }

        _statsVoicedPeaks++;
        _debugVoicedPeakCount++;

        // Check prominence (dip before and after)
        float leftSum = 0f;
        float rightSum = 0f;
        float leftMin = float.PositiveInfinity;
        float rightMin = float.PositiveInfinity;
        for (int offset = 1; offset <= peakSpanFrames; offset++)
        {
            int leftIdx = (centerIdx - offset + MaxHistoryFrames) % MaxHistoryFrames;
            float leftValue = _energyHistory[leftIdx];
            leftSum += leftValue;
            if (leftValue < leftMin)
            {
                leftMin = leftValue;
            }

            int rightIdx = (centerIdx + offset) % MaxHistoryFrames;
            float rightValue = _energyHistory[rightIdx];
            rightSum += rightValue;
            if (rightValue < rightMin)
            {
                rightMin = rightValue;
            }
        }

        float leftMean = leftSum / peakSpanFrames;
        float rightMean = rightSum / peakSpanFrames;

        float meanBaseline = MathF.Min(leftMean, rightMean);
        float minDipInstant = MathF.Min(prevEnergy, currentEnergy);
        float minDip = MathF.Max(minDipInstant, meanBaseline - MicroDipClampDb);
        float prominenceClamp = centerEnergy - minDip;
        float prominenceMean = centerEnergy - MathF.Max(leftMean, rightMean);
        bool clampActive = minDip > minDipInstant;
        bool meanPenalty = prominenceMean < prominenceClamp;
        float prominence = meanPenalty ? prominenceMean : prominenceClamp;
        float prominenceInstant = centerEnergy - minDipInstant;
        if (clampActive)
        {
            _statsClampApplied++;
        }
        if (meanPenalty)
        {
            _statsMeanPenaltyApplied++;
        }
        if (prominence > _debugMaxProminence)
        {
            _debugMaxProminence = prominence;
        }
        if (prominenceClamp > _statsMaxProminenceClampDb)
        {
            _statsMaxProminenceClampDb = prominenceClamp;
        }
        if (prominence > _statsMaxProminenceDb)
        {
            _statsMaxProminenceDb = prominence;
        }
        if (prominenceInstant > _statsMaxProminenceInstantDb)
        {
            _statsMaxProminenceInstantDb = prominenceInstant;
        }
        if (prominenceMean > _statsMaxProminenceMeanDb)
        {
            _statsMaxProminenceMeanDb = prominenceMean;
        }
        // Treat voicing as confidence: allow unvoiced peaks but require extra prominence to avoid noise sensitivity.
        float requiredProminence = ProminenceThresholdDb + (voicedCenter ? 0f : UnvoicedProminencePenaltyDb);
        if (prominence < requiredProminence)
        {
            _statsRejectLowProminence++;
            if (prominenceInstant >= requiredProminence)
            {
                _statsRejectLowProminenceInstant++;
            }
            if (prominenceMean < requiredProminence)
            {
                _statsRejectLowProminenceMean++;
            }
            MaybeLogDebug(frameId, hopSize, sampleRate, isPeak, prominence, prominenceInstant, prominenceMean, voicedCenter,
                centerEnergy, prevEnergy, currentEnergy, leftMean, rightMean, leftMin, rightMin, detected: false);
            return false;
        }

        // Check minimum interval
        float minIntervalFrames = MinIntervalMs / frameDurationMs;
        long centerFrame = frameId - peakSpanFrames; // The peak is at the center frame

        if (centerFrame - _lastSyllableFrame < minIntervalFrames)
        {
            _statsRejectMinInterval++;
            MaybeLogDebug(frameId, hopSize, sampleRate, isPeak, prominence, prominenceInstant, prominenceMean, voicedCenter,
                centerEnergy, prevEnergy, currentEnergy, leftMean, rightMean, leftMin, rightMin, detected: false);
            return false;
        }

        // Syllable detected
        _lastSyllableFrame = centerFrame;
        _debugLastDetectedFrame = centerFrame;
        _statsDetected++;
        if (voicedCenter)
        {
            _statsDetectedVoiced++;
        }
        else
        {
            _statsDetectedUnvoiced++;
        }
        _statsLastDetectedFrame = centerFrame;
        MaybeLogDebug(frameId, hopSize, sampleRate, isPeak, prominence, prominenceInstant, prominenceMean, voicedCenter,
            centerEnergy, prevEnergy, currentEnergy, leftMean, rightMean, leftMin, rightMin, detected: true);
        return true;
    }

    /// <summary>
    /// Reset the detector state.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_energyHistory, 0, _energyHistory.Length);
        Array.Clear(_voicingHistory, 0, _voicingHistory.Length);
        _historyIndex = 0;
        _historyCount = 0;
        _lastSyllableFrame = -1000;
        _smoothedEnergyDb = 0f;
        _baselineEnergyDb = 0f;
        _initialized = false;
        _baselineInitialized = false;
        _lastDebugTicks = 0;
        _debugPeakCount = 0;
        _debugVoicedPeakCount = 0;
        _debugMaxProminence = 0f;
        _debugMinEnergy = float.PositiveInfinity;
        _debugMaxEnergy = float.NegativeInfinity;
        _debugLastDetectedFrame = -1;
        _statsFrames = 0;
        _statsWarmupFrames = 0;
        _statsPeaks = 0;
        _statsVoicedPeaks = 0;
        _statsDetected = 0;
        _statsDetectedVoiced = 0;
        _statsDetectedUnvoiced = 0;
        _statsRejectNotPeak = 0;
        _statsRejectUnvoiced = 0;
        _statsRejectLowProminence = 0;
        _statsRejectLowProminenceInstant = 0;
        _statsRejectLowProminenceMean = 0;
        _statsRejectMinInterval = 0;
        _statsClampApplied = 0;
        _statsMeanPenaltyApplied = 0;
        _statsBaselineUpdates = 0;
        _statsBaselineSkips = 0;
        _statsMinEnergyDb = float.PositiveInfinity;
        _statsMaxEnergyDb = float.NegativeInfinity;
        _statsMinBaselineDb = float.PositiveInfinity;
        _statsMaxBaselineDb = float.NegativeInfinity;
        _statsMaxProminenceClampDb = 0f;
        _statsMaxProminenceDb = 0f;
        _statsMaxProminenceInstantDb = 0f;
        _statsMaxProminenceMeanDb = 0f;
        _statsLastDetectedFrame = -1;
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

    private void MaybeLogDebug(long frameId, int hopSize, int sampleRate, bool isPeak, float prominence, float prominenceInstant,
        float prominenceMean,
        bool voicedCenter, float centerEnergy, float prevEnergy, float currentEnergy, float leftMean, float rightMean,
        float leftMin, float rightMin, bool detected)
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
            "SyllableDebug frame={0} smoothDb={1:0.00} center={2:0.00} prev={3:0.00} curr={4:0.00} isPeak={5} voiced={6} prom={7:0.00} promInst={8:0.00} promMean={9:0.00} leftMean={10:0.00} rightMean={11:0.00} leftMin={12:0.00} rightMin={13:0.00} minIntFrames={14:0.0} sinceLast={15} detected={16} peaks={17} voicedPeaks={18} maxProm={19:0.00} minDb={20:0.00} maxDb={21:0.00} lastDet={22}",
            frameId,
            _smoothedEnergyDb,
            centerEnergy,
            prevEnergy,
            currentEnergy,
            isPeak ? 1 : 0,
            voicedCenter ? 1 : 0,
            prominence,
            prominenceInstant,
            prominenceMean,
            leftMean,
            rightMean,
            leftMin,
            rightMin,
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

public readonly record struct SyllableDetectorDebugStats(
    long Frames,
    long WarmupFrames,
    long Peaks,
    long VoicedPeaks,
    long Detected,
    long DetectedVoiced,
    long DetectedUnvoiced,
    long RejectNotPeak,
    long RejectUnvoiced,
    long RejectLowProminence,
    long RejectLowProminenceInstant,
    long RejectLowProminenceMean,
    long RejectMinInterval,
    long ClampApplied,
    long MeanPenaltyApplied,
    long BaselineUpdates,
    long BaselineSkips,
    float MinEnergyDb,
    float MaxEnergyDb,
    float MinBaselineDb,
    float MaxBaselineDb,
    float MaxProminenceClampDb,
    float MaxProminenceDb,
    float MaxProminenceInstantDb,
    float MaxProminenceMeanDb,
    long LastDetectedFrame);
