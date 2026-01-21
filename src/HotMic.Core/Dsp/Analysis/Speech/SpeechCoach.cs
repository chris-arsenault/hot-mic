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
    public float PauseRatio { get; set; }          // fraction of time in pauses (0-1)
    public float MeanPauseDurationMs { get; set; } // average pause duration
    public float PausesPerMinute { get; set; }     // pause frequency
    public float FilledPauseRatio { get; set; }    // ratio of filled to total pauses

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

    // Per-frame state
    public SpeakingState CurrentState { get; set; }     // Speaking/SilentPause/FilledPause
    public bool SyllableDetected { get; set; }          // True if syllable at this frame
    public StressLevel LastStressLevel { get; set; }    // Stress of most recent syllable
    public int FillerCount { get; set; }                // Total fillers detected in window
}

/// <summary>
/// Coordinates all speech analysis components for real-time feedback.
/// </summary>
public sealed class SpeechCoach
{
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
    /// <param name="pitchHz">Detected pitch in Hz (0 if unvoiced).</param>
    /// <param name="pitchConfidence">Pitch detection confidence (0-1).</param>
    /// <param name="voicing">Voicing state.</param>
    /// <param name="spectralFlatness">Spectral flatness (0-1).</param>
    /// <param name="spectralFlux">Spectral flux.</param>
    /// <param name="spectralSlope">Spectral slope (dB/kHz).</param>
    /// <param name="hnr">Harmonic-to-noise ratio.</param>
    /// <param name="f1Hz">First formant frequency (0 if not detected).</param>
    /// <param name="f2Hz">Second formant frequency (0 if not detected).</param>
    /// <param name="frameId">Current frame ID.</param>
    /// <returns>Updated speech metrics.</returns>
    public SpeechMetrics Process(
        float energyDb,
        float pitchHz,
        float pitchConfidence,
        VoicingState voicing,
        float spectralFlatness,
        float spectralFlux,
        float spectralSlope,
        float hnr,
        float f1Hz,
        float f2Hz,
        long frameId)
    {
        // Process syllable detection
        bool syllableDetected = _syllableDetector.Process(energyDb, voicing, frameId, _hopSize, _sampleRate);
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
        _pitchAnalyzer.RecordPitch(pitchHz, voicing, frameId);

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
            out var filledPauseRatio);

        _cachedMetrics.SyllableRate = syllableRate;
        _cachedMetrics.ArticulationRate = articulationRate;
        _cachedMetrics.PauseRatio = pauseRatio;
        _cachedMetrics.MeanPauseDurationMs = meanPauseDurationMs;
        _cachedMetrics.PausesPerMinute = pausesPerMinute;
        _cachedMetrics.FilledPauseRatio = filledPauseRatio;

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
    }

    /// <summary>
    /// Get the current cached metrics without processing.
    /// </summary>
    public SpeechMetrics CurrentMetrics => _cachedMetrics;

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
    }
}
