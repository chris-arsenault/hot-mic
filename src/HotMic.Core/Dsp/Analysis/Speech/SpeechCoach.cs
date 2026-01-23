using System;
using System.Diagnostics;
using System.Globalization;
using HotMic.Core.Dsp.Analysis;

namespace HotMic.Core.Dsp.Analysis.Speech;

/// <summary>
/// Aggregated speech metrics for a single frame.
/// </summary>
public struct SpeechMetrics
{
    // Rate metrics (updated periodically, not every frame)
    public float SyllableRate { get; set; }        // syllables/min (including pauses)
    public float ArticulationRate { get; set; }    // syllables/min (excluding pauses)
    public float WordsPerMinute { get; set; }      // words/min (estimated)
    public float ArticulationWpm { get; set; }     // words/min (excluding pauses)
    public float PauseRatio { get; set; }          // fraction of time in pauses (0-1)
    public float MeanPauseDurationMs { get; set; } // average pause duration
    public float PausesPerMinute { get; set; }     // pause frequency
    public float FilledPauseRatio { get; set; }    // ratio of filled to total pauses
    public int PauseMicroCount { get; set; }       // pauses 150-300ms
    public int PauseShortCount { get; set; }       // pauses 300-700ms
    public int PauseMediumCount { get; set; }      // pauses 700-1500ms
    public int PauseLongCount { get; set; }        // pauses >1500ms

    // Prosody metrics
    public float PitchRangeSemitones { get; set; }      // max - min pitch
    public float PitchVariationSemitones { get; set; }  // stddev of pitch
    public float PitchSlopeSemitones { get; set; }      // rising/falling tendency
    public float MonotoneScore { get; set; }            // 0-1, 1 = highly monotone
    public float MeanPitchHz { get; set; }              // average pitch

    // Clarity metrics
    public float VowelClarity { get; set; }        // 0-100
    public float ConsonantClarity { get; set; }    // 0-100
    public float TransitionSharpness { get; set; } // 0-100
    public float OverallClarity { get; set; }      // 0-100
    public float IntelligibilityScore { get; set; }// 0-100
    public float BandLowRatio { get; set; }        // 0-1 (80-300Hz)
    public float BandMidRatio { get; set; }        // 0-1 (300-1000Hz)
    public float BandPresenceRatio { get; set; }   // 0-1 (1-4kHz)
    public float BandHighRatio { get; set; }       // 0-1 (4-8kHz)
    public float ClarityRatio { get; set; }        // presence / (low + mid)

    // Per-frame state
    public SpeakingState CurrentState { get; set; }     // Speaking/SilentPause/FilledPause
    public bool SyllableDetected { get; set; }          // True if syllable at this frame
    public StressLevel LastStressLevel { get; set; }    // Stress of most recent syllable
    public int FillerCount { get; set; }                // Total fillers detected in window
    public bool EmphasisDetected { get; set; }          // Energy-based emphasis marker
}

/// <summary>
/// Coordinates all speech analysis components for real-time feedback.
/// </summary>
public sealed class SpeechCoach
{
    private const float EmphasisWindowMs = 500f;
    private const float EmphasisThresholdDb = 6f;
    private const float SyllablesPerWord = 1.5f; // SPEECH.md 6.5 (avg syllables per word)
    private const string DebugEnvVar = "HOTMIC_SPEECH_DEBUG";
    private static readonly bool DebugLoggingEnabled = GetDebugLoggingEnabled();
    private static readonly long DebugIntervalTicks = Stopwatch.Frequency; // ~1s

    private readonly SyllableDetector _syllableDetector = new();
    private readonly PauseDetector _pauseDetector = new();
    private readonly SpeechRateCalculator _rateCalculator = new();
    private readonly PitchContourAnalyzer _pitchAnalyzer = new();
    private readonly StressDetector _stressDetector = new();
    private readonly FillerDetector _fillerDetector = new();
    private readonly ClarityAnalyzer _clarityAnalyzer = new();
    private readonly IntelligibilityAnalyzer _intelligibilityAnalyzer = new();

    private int _hopSize;
    private int _sampleRate;
    private int _metricsUpdateCounter;
    private int _fillerCountInWindow;
    private SpeechMetrics _cachedMetrics;
    private StressLevel _lastStressLevel;
    private float _energyMeanDb;
    private bool _energyInitialized;
    private long _lastDebugTicks;
    private long _lastFrameId;
    private float _lastEnergyDb;
    private float _lastSyllableEnergyDb;
    private float _lastPitchHz;
    private float _lastPitchConfidence;
    private VoicingState _lastVoicing;
    private float _lastSpectralFlatness;
    private float _lastSpectralFlux;
    private float _lastHnr;

    /// <summary>
    /// How often to recompute aggregate metrics (in frames).
    /// </summary>
    public int MetricsUpdateInterval { get; set; } = 10;

    /// <summary>
    /// Analysis window for rate calculations (seconds).
    /// </summary>
    public float WindowSeconds
    {
        get => _rateCalculator.WindowSeconds;
        set
        {
            _rateCalculator.WindowSeconds = value;
            _pitchAnalyzer.WindowSeconds = value;
        }
    }

    /// <summary>
    /// Configure the speech coach for the given audio parameters.
    /// </summary>
    public void Configure(int hopSize, int sampleRate)
    {
        _hopSize = hopSize;
        _sampleRate = sampleRate;
        _intelligibilityAnalyzer.Configure(hopSize, sampleRate);
    }

    /// <summary>
    /// Process a single analysis frame.
    /// </summary>
    /// <param name="energyDb">Frame energy in dB.</param>
    /// <param name="syllableEnergyDb">Band-limited energy for syllable detection (300-2000 Hz) in dB.</param>
    /// <param name="pitchHz">Detected pitch in Hz (0 if unvoiced).</param>
    /// <param name="pitchConfidence">Pitch detection confidence (0-1).</param>
    /// <param name="voicing">Voicing state.</param>
    /// <param name="spectralFlatness">Spectral flatness (0-1).</param>
    /// <param name="spectralFlux">Spectral flux.</param>
    /// <param name="spectralSlope">Spectral slope (dB/kHz).</param>
    /// <param name="hnr">Harmonic-to-noise ratio.</param>
    /// <param name="f1Hz">First formant frequency (0 if not detected).</param>
    /// <param name="f2Hz">Second formant frequency (0 if not detected).</param>
    /// <param name="bandLowRatio">Low band energy ratio (80-300 Hz).</param>
    /// <param name="bandMidRatio">Mid band energy ratio (300-1000 Hz).</param>
    /// <param name="bandPresenceRatio">Presence band energy ratio (1-4 kHz).</param>
    /// <param name="bandHighRatio">High band energy ratio (4-8 kHz).</param>
    /// <param name="clarityRatio">Presence / (low + mid).</param>
    /// <param name="frameId">Current frame ID.</param>
    /// <returns>Updated speech metrics.</returns>
    public SpeechMetrics Process(
        float energyDb,
        float syllableEnergyDb,
        float pitchHz,
        float pitchConfidence,
        VoicingState voicing,
        float spectralFlatness,
        float spectralFlux,
        float spectralSlope,
        float hnr,
        float f1Hz,
        float f2Hz,
        float bandLowRatio,
        float bandMidRatio,
        float bandPresenceRatio,
        float bandHighRatio,
        float clarityRatio,
        long frameId)
    {
        _lastFrameId = frameId;
        _lastEnergyDb = energyDb;
        _lastSyllableEnergyDb = syllableEnergyDb;
        _lastPitchHz = pitchHz;
        _lastPitchConfidence = pitchConfidence;
        _lastVoicing = voicing;
        _lastSpectralFlatness = spectralFlatness;
        _lastSpectralFlux = spectralFlux;
        _lastHnr = hnr;

        // Process syllable detection
        bool syllableDetected = _syllableDetector.Process(syllableEnergyDb, voicing, frameId, _hopSize, _sampleRate);
        if (syllableDetected)
        {
            _rateCalculator.RecordSyllable(frameId);

            // Classify stress for detected syllable
            _lastStressLevel = _stressDetector.ClassifyStress(energyDb, pitchHz);
        }

        // Update stress detector statistics (for all voiced frames)
        if (voicing == VoicingState.Voiced)
        {
            _stressDetector.UpdateStatistics(energyDb, pitchHz);
        }

        // Process pause detection
        bool pauseEnded = _pauseDetector.Process(
            voicing, pitchConfidence, spectralFlatness,
            frameId, _hopSize, _sampleRate,
            out var pauseEvent);

        if (pauseEnded)
        {
            _rateCalculator.RecordPause(pauseEvent);
        }

        // Process pitch contour
        _pitchAnalyzer.RecordPitch(pitchHz, pitchConfidence, voicing, frameId);

        // Process filler detection
        bool fillerDetected = _fillerDetector.Process(
            pitchHz, spectralFlux, f1Hz, f2Hz, voicing,
            frameId, _hopSize, _sampleRate,
            out var fillerEvent);

        if (fillerDetected)
        {
            _fillerCountInWindow++;
            // Also record as a filled pause
            var asPause = new PauseEvent(PauseType.Filled, fillerEvent.StartFrame, fillerEvent.EndFrame, fillerEvent.DurationMs);
            _rateCalculator.RecordPause(asPause);
        }

        // Process clarity
        _clarityAnalyzer.Process(hnr, spectralSlope, voicing);

        // Process intelligibility
        _intelligibilityAnalyzer.Process(energyDb);

        // Update per-frame metrics
        _cachedMetrics.CurrentState = _pauseDetector.CurrentState;
        _cachedMetrics.SyllableDetected = syllableDetected;
        _cachedMetrics.LastStressLevel = _lastStressLevel;
        _cachedMetrics.BandLowRatio = bandLowRatio;
        _cachedMetrics.BandMidRatio = bandMidRatio;
        _cachedMetrics.BandPresenceRatio = bandPresenceRatio;
        _cachedMetrics.BandHighRatio = bandHighRatio;
        _cachedMetrics.ClarityRatio = clarityRatio;

        // Emphasis detection based on local energy mean (SPEECH.md 5.4)
        float frameDurationMs = _sampleRate > 0 ? 1000f * _hopSize / _sampleRate : 10f;
        float alpha = frameDurationMs / (EmphasisWindowMs + frameDurationMs);
        if (!_energyInitialized)
        {
            _energyMeanDb = energyDb;
            _energyInitialized = true;
        }
        else
        {
            _energyMeanDb = alpha * energyDb + (1f - alpha) * _energyMeanDb;
        }

        _cachedMetrics.EmphasisDetected = voicing == VoicingState.Voiced && energyDb > _energyMeanDb + EmphasisThresholdDb;

        // Periodically recompute aggregate metrics
        _metricsUpdateCounter++;
        if (_metricsUpdateCounter >= MetricsUpdateInterval)
        {
            _metricsUpdateCounter = 0;
            UpdateAggregateMetrics(frameId);
        }

        return _cachedMetrics;
    }

    private void UpdateAggregateMetrics(long frameId)
    {
        // Rate metrics
        _rateCalculator.Compute(
            frameId, _hopSize, _sampleRate,
            out var syllableRate,
            out var articulationRate,
            out var pauseRatio,
            out var meanPauseDurationMs,
            out var pausesPerMinute,
            out var filledPauseRatio,
            out var pauseMicroCount,
            out var pauseShortCount,
            out var pauseMediumCount,
            out var pauseLongCount);

        _cachedMetrics.SyllableRate = syllableRate;
        _cachedMetrics.ArticulationRate = articulationRate;
        _cachedMetrics.WordsPerMinute = SyllablesPerWord > 0f ? syllableRate / SyllablesPerWord : 0f;
        _cachedMetrics.ArticulationWpm = SyllablesPerWord > 0f ? articulationRate / SyllablesPerWord : 0f;
        _cachedMetrics.PauseRatio = pauseRatio;
        _cachedMetrics.MeanPauseDurationMs = meanPauseDurationMs;
        _cachedMetrics.PausesPerMinute = pausesPerMinute;
        _cachedMetrics.FilledPauseRatio = filledPauseRatio;
        _cachedMetrics.PauseMicroCount = pauseMicroCount;
        _cachedMetrics.PauseShortCount = pauseShortCount;
        _cachedMetrics.PauseMediumCount = pauseMediumCount;
        _cachedMetrics.PauseLongCount = pauseLongCount;

        // Prosody metrics
        _pitchAnalyzer.Compute(
            frameId, _hopSize, _sampleRate,
            out var pitchRangeSemitones,
            out var pitchVariationSemitones,
            out var pitchSlopeSemitones,
            out var monotoneScore,
            out var meanPitchHz);

        _cachedMetrics.PitchRangeSemitones = pitchRangeSemitones;
        _cachedMetrics.PitchVariationSemitones = pitchVariationSemitones;
        _cachedMetrics.PitchSlopeSemitones = pitchSlopeSemitones;
        _cachedMetrics.MonotoneScore = monotoneScore;
        _cachedMetrics.MeanPitchHz = meanPitchHz;

        // Clarity metrics
        _clarityAnalyzer.Compute(out var vowelClarity,
            out var consonantClarity,
            out var transitionSharpness,
            out var overallClarity);

        _cachedMetrics.VowelClarity = vowelClarity;
        _cachedMetrics.ConsonantClarity = consonantClarity;
        _cachedMetrics.TransitionSharpness = transitionSharpness;
        _cachedMetrics.OverallClarity = overallClarity;

        // Intelligibility metrics
        _intelligibilityAnalyzer.Compute(out _, out _, out var intelligibilityScore);
        _cachedMetrics.IntelligibilityScore = intelligibilityScore;

        // Filler count
        _cachedMetrics.FillerCount = _fillerCountInWindow;

        MaybeLogDebug();
    }

    /// <summary>
    /// Get the current cached metrics without processing.
    /// </summary>
    public SpeechMetrics CurrentMetrics => _cachedMetrics;

    /// <summary>
    /// Debug counters from the syllable detector.
    /// </summary>
    public SyllableDetectorDebugStats SyllableDebugStats => _syllableDetector.DebugStats;

    /// <summary>
    /// Debug counters from the rate calculator.
    /// </summary>
    public SpeechRateDebugStats RateDebugStats => _rateCalculator.DebugStats;

    /// <summary>
    /// Reset all analyzers.
    /// </summary>
    public void Reset()
    {
        _syllableDetector.Reset();
        _pauseDetector.Reset();
        _rateCalculator.Reset();
        _pitchAnalyzer.Reset();
        _stressDetector.Reset();
        _fillerDetector.Reset();
        _clarityAnalyzer.Reset();
        _intelligibilityAnalyzer.Reset();
        _metricsUpdateCounter = 0;
        _fillerCountInWindow = 0;
        _cachedMetrics = default;
        _lastStressLevel = StressLevel.Unstressed;
        _energyMeanDb = 0f;
        _energyInitialized = false;
        _lastDebugTicks = 0;
        _lastFrameId = 0;
        _lastEnergyDb = 0f;
        _lastSyllableEnergyDb = 0f;
        _lastPitchHz = 0f;
        _lastPitchConfidence = 0f;
        _lastVoicing = VoicingState.Silence;
        _lastSpectralFlatness = 0f;
        _lastSpectralFlux = 0f;
        _lastHnr = 0f;
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

    private void MaybeLogDebug()
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
        string message = string.Format(
            CultureInfo.InvariantCulture,
            "SpeechDebug frame={0} energyDb={1:0.0} syllDb={2:0.0} voicing={3} pitchHz={4:0.0} conf={5:0.00} flat={6:0.00} flux={7:0.00} hnr={8:0.0} syllDet={9} syllRate={10:0.0} artRate={11:0.0} wpm={12:0.0} artWpm={13:0.0} pauseRatio={14:0.00} monotone={15:0.00}",
            _lastFrameId,
            _lastEnergyDb,
            _lastSyllableEnergyDb,
            (byte)_lastVoicing,
            _lastPitchHz,
            _lastPitchConfidence,
            _lastSpectralFlatness,
            _lastSpectralFlux,
            _lastHnr,
            _cachedMetrics.SyllableDetected,
            _cachedMetrics.SyllableRate,
            _cachedMetrics.ArticulationRate,
            _cachedMetrics.WordsPerMinute,
            _cachedMetrics.ArticulationWpm,
            _cachedMetrics.PauseRatio,
            _cachedMetrics.MonotoneScore);

        Console.WriteLine(message);
        Debug.WriteLine(message);
    }
}
