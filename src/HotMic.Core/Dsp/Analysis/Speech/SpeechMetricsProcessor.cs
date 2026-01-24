using HotMic.Core.Analysis;
using HotMic.Core.Dsp.Filters;

namespace HotMic.Core.Dsp.Analysis.Speech;

/// <summary>
/// Computes speech metrics from per-frame analysis data without allocations.
/// </summary>
public sealed class SpeechMetricsProcessor
{
    private const float BandSmoothingAlpha = 0.3f;
    private const float SyllableBandLowHz = 300f;
    private const float SyllableBandHighHz = 2000f;
    private const float SyllableBandQ = 0.707f;
    private const float SyllableEnergyWindowMs = 20f;
    private const float SpeechPresenceSmoothingAlpha = 0.1f;
    private const float SpeechPresenceHysteresis = 0.02f;
    private const float SpeechPresenceBaselineAlpha = 0.01f;
    private const float SpeechPresenceBaselineGuard = 0.05f;
    private const int SpeechPresenceHangoverFrames = 8;
    private const float SpeechEnergyBaselineAlpha = 0.01f;
    private const float SpeechEnergyBaselineRiseDb = 3f;
    private const float SpeechEnergyGateThresholdDb = 3f;

    private readonly SpeechCoach _speechCoach = new();
    private readonly BiquadFilter _syllableHighPass = new();
    private readonly BiquadFilter _syllableLowPass = new();

    private float _bandLowSmooth;
    private float _bandMidSmooth;
    private float _bandPresenceSmooth;
    private float _bandHighSmooth;
    private bool _bandSmoothInitialized;
    private float _speechPresenceSmooth;
    private bool _speechPresenceInitialized;
    private float _speechPresenceBaseline;
    private bool _speechPresenceBaselineInitialized;
    private bool _speechPresenceGateOpen;
    private int _speechPresenceHangoverRemaining;
    private float _speechEnergyBaselineDb;
    private bool _speechEnergyBaselineInitialized;
    private int _syllableWindowSamples;

    public float LastEnergyDb { get; private set; }
    public float LastSyllableEnergyDb { get; private set; }
    public float LastSyllableEnergyRatio { get; private set; }
    public SyllableDetectorDebugStats SyllableDebugStats => _speechCoach.SyllableDebugStats;
    public SpeechRateDebugStats RateDebugStats => _speechCoach.RateDebugStats;
    public PauseDetectorDebugStats PauseDebugStats => _speechCoach.PauseDebugStats;

    public void Configure(int hopSize, int sampleRate)
    {
        _speechCoach.Configure(hopSize, sampleRate);
        ConfigureSyllableFilters(sampleRate);
        _syllableWindowSamples = sampleRate > 0
            ? Math.Max(1, (int)MathF.Round(sampleRate * (SyllableEnergyWindowMs / 1000f)))
            : 0;
        ResetBandSmoothing();
    }

    public void Reset()
    {
        _speechCoach.Reset();
        _syllableHighPass.Reset();
        _syllableLowPass.Reset();
        ResetBandSmoothing();
        LastEnergyDb = 0f;
        LastSyllableEnergyDb = 0f;
        LastSyllableEnergyRatio = 0f;
        _speechPresenceSmooth = 0f;
        _speechPresenceInitialized = false;
        _speechPresenceBaseline = 0f;
        _speechPresenceBaselineInitialized = false;
        _speechPresenceGateOpen = false;
        _speechPresenceHangoverRemaining = 0;
        _speechEnergyBaselineDb = 0f;
        _speechEnergyBaselineInitialized = false;
    }

    public SpeechMetricsFrame Process(
        float waveformMin,
        float waveformMax,
        ReadOnlySpan<float> analysisRaw,
        ReadOnlySpan<float> speechMagnitudes,
        ReadOnlySpan<float> binCentersHz,
        float binResolutionHz,
        float speechPresence,
        float pitchHz,
        float pitchConfidence,
        VoicingState voicing,
        float spectralFlux,
        float spectralSlope,
        float hnr,
        long frameId)
    {
        float energyDb = ComputePeakDb(waveformMin, waveformMax);
        float syllableEnergyDb = ComputeSyllableEnergyDb(analysisRaw);
        float spectralFlatness = ComputeSpectralFlatness(speechMagnitudes);

        if (!float.IsFinite(speechPresence))
        {
            speechPresence = 0f;
        }

        float presenceValue = Math.Clamp(speechPresence, 0f, 1f);
        if (!_speechPresenceBaselineInitialized)
        {
            _speechPresenceBaseline = presenceValue;
            _speechPresenceBaselineInitialized = true;
        }
        else if (presenceValue <= _speechPresenceBaseline + SpeechPresenceBaselineGuard)
        {
            _speechPresenceBaseline = SpeechPresenceBaselineAlpha * presenceValue
                + (1f - SpeechPresenceBaselineAlpha) * _speechPresenceBaseline;
        }

        float normalizedPresence = (presenceValue - _speechPresenceBaseline)
            / MathF.Max(1e-4f, 1f - _speechPresenceBaseline);
        normalizedPresence = Math.Clamp(normalizedPresence, 0f, 1f);

        if (!_speechPresenceInitialized)
        {
            _speechPresenceSmooth = normalizedPresence;
            _speechPresenceInitialized = true;
        }
        else
        {
            _speechPresenceSmooth = SpeechPresenceSmoothingAlpha * normalizedPresence
                + (1f - SpeechPresenceSmoothingAlpha) * _speechPresenceSmooth;
        }

        float gateThreshold = AnalysisSignalProcessor.SpeechPresenceGateThreshold;
        float onThreshold = MathF.Min(1f, gateThreshold + SpeechPresenceHysteresis);
        float offThreshold = MathF.Max(0f, gateThreshold - SpeechPresenceHysteresis);

        if (_speechPresenceGateOpen)
        {
            if (_speechPresenceSmooth < offThreshold)
            {
                _speechPresenceGateOpen = false;
            }
        }
        else if (_speechPresenceSmooth > onThreshold)
        {
            _speechPresenceGateOpen = true;
        }

        if (!_speechEnergyBaselineInitialized)
        {
            _speechEnergyBaselineDb = syllableEnergyDb;
            _speechEnergyBaselineInitialized = true;
        }
        else if (syllableEnergyDb < _speechEnergyBaselineDb)
        {
            _speechEnergyBaselineDb = syllableEnergyDb;
        }
        else if (syllableEnergyDb <= _speechEnergyBaselineDb + SpeechEnergyBaselineRiseDb)
        {
            _speechEnergyBaselineDb = SpeechEnergyBaselineAlpha * syllableEnergyDb
                + (1f - SpeechEnergyBaselineAlpha) * _speechEnergyBaselineDb;
        }

        bool energyGateOpen = (syllableEnergyDb - _speechEnergyBaselineDb) >= SpeechEnergyGateThresholdDb;
        bool combinedGateOpen = _speechPresenceGateOpen || energyGateOpen;

        if (combinedGateOpen)
        {
            _speechPresenceHangoverRemaining = SpeechPresenceHangoverFrames;
        }
        else if (_speechPresenceHangoverRemaining > 0)
        {
            _speechPresenceHangoverRemaining--;
        }

        bool hasSpeech = combinedGateOpen || _speechPresenceHangoverRemaining > 0;
        if (!hasSpeech)
        {
            pitchHz = 0f;
            pitchConfidence = 0f;
            voicing = VoicingState.Silence;
        }
        else if (voicing == VoicingState.Silence)
        {
            voicing = VoicingState.Unvoiced;
        }

        float bandLowRatio = 0f;
        float bandMidRatio = 0f;
        float bandPresenceRatio = 0f;
        float bandHighRatio = 0f;
        float clarityRatio = 0f;
        float syllableEnergyRatio = 0f;

        if (!speechMagnitudes.IsEmpty && (binCentersHz.Length > 0 || binResolutionHz > 0f))
        {
            ComputeSpeechBandMetrics(
                speechMagnitudes,
                binCentersHz,
                binResolutionHz,
                out bandLowRatio,
                out bandMidRatio,
                out bandPresenceRatio,
                out bandHighRatio,
                out clarityRatio,
                out syllableEnergyRatio);
        }

        LastEnergyDb = energyDb;
        LastSyllableEnergyDb = syllableEnergyDb;
        LastSyllableEnergyRatio = syllableEnergyRatio;

        var metrics = _speechCoach.Process(
            energyDb,
            syllableEnergyDb,
            pitchHz,
            pitchConfidence,
            voicing,
            spectralFlatness,
            spectralFlux,
            spectralSlope,
            hnr,
            f1Hz: 0f,
            f2Hz: 0f,
            bandLowRatio,
            bandMidRatio,
            bandPresenceRatio,
            bandHighRatio,
            clarityRatio,
            frameId);

        return new SpeechMetricsFrame
        {
            SyllableRate = metrics.SyllableRate,
            ArticulationRate = metrics.ArticulationRate,
            WordsPerMinute = metrics.WordsPerMinute,
            ArticulationWpm = metrics.ArticulationWpm,
            PauseRatio = metrics.PauseRatio,
            MeanPauseDurationMs = metrics.MeanPauseDurationMs,
            PausesPerMinute = metrics.PausesPerMinute,
            FilledPauseRatio = metrics.FilledPauseRatio,
            PauseMicroCount = metrics.PauseMicroCount,
            PauseShortCount = metrics.PauseShortCount,
            PauseMediumCount = metrics.PauseMediumCount,
            PauseLongCount = metrics.PauseLongCount,
            MonotoneScore = metrics.MonotoneScore,
            ClarityScore = metrics.OverallClarity,
            IntelligibilityScore = metrics.IntelligibilityScore,
            BandLowRatio = metrics.BandLowRatio,
            BandMidRatio = metrics.BandMidRatio,
            BandPresenceRatio = metrics.BandPresenceRatio,
            BandHighRatio = metrics.BandHighRatio,
            ClarityRatio = metrics.ClarityRatio,
            SpeakingState = (byte)metrics.CurrentState,
            SyllableDetected = metrics.SyllableDetected,
            EmphasisDetected = metrics.EmphasisDetected
        };
    }

    private static float ComputeSpectralFlatness(ReadOnlySpan<float> magnitudes)
    {
        if (magnitudes.IsEmpty)
        {
            return 1f;
        }

        float logSum = 0f;
        float linSum = 0f;
        int count = magnitudes.Length;
        for (int i = 0; i < count; i++)
        {
            float mag = MathF.Max(magnitudes[i], 1e-12f);
            logSum += MathF.Log(mag);
            linSum += mag;
        }

        float geometric = MathF.Exp(logSum / count);
        float arithmetic = linSum / count;
        return arithmetic > 1e-12f ? geometric / arithmetic : 1f;
    }

    private void ComputeSpeechBandMetrics(
        ReadOnlySpan<float> magnitudes,
        ReadOnlySpan<float> binCentersHz,
        float binResolutionHz,
        out float bandLowRatio,
        out float bandMidRatio,
        out float bandPresenceRatio,
        out float bandHighRatio,
        out float clarityRatio,
        out float syllableEnergyRatio)
    {
        float lowPower = 0f;
        float midPower = 0f;
        float presencePower = 0f;
        float highPower = 0f;
        float syllablePower = 0f;

        bool useCenters = !binCentersHz.IsEmpty && binCentersHz.Length >= magnitudes.Length;
        bool useResolution = binResolutionHz > 0f;
        if (!useCenters && !useResolution)
        {
            bandLowRatio = 0f;
            bandMidRatio = 0f;
            bandPresenceRatio = 0f;
            bandHighRatio = 0f;
            clarityRatio = 0f;
            syllableEnergyRatio = 0f;
            return;
        }

        int count = magnitudes.Length;
        if (useCenters && binCentersHz.Length < count)
        {
            count = binCentersHz.Length;
        }

        for (int i = 0; i < count; i++)
        {
            float freq = useCenters ? binCentersHz[i] : i * binResolutionHz;
            float mag = magnitudes[i];
            float power = mag * mag;

            if (freq >= 80f && freq < 300f)
            {
                lowPower += power;
            }
            else if (freq >= 300f && freq < 1000f)
            {
                midPower += power;
            }
            else if (freq >= 1000f && freq < 4000f)
            {
                presencePower += power;
            }
            else if (freq >= 4000f && freq < 8000f)
            {
                highPower += power;
            }

            if (freq >= 300f && freq <= 2000f)
            {
                syllablePower += power;
            }
        }

        float totalPower = lowPower + midPower + presencePower + highPower;
        float invTotal = totalPower > 1e-12f ? 1f / totalPower : 0f;

        float lowRatio = lowPower * invTotal;
        float midRatio = midPower * invTotal;
        float presenceRatio = presencePower * invTotal;
        float highRatio = highPower * invTotal;

        if (!_bandSmoothInitialized)
        {
            _bandLowSmooth = lowRatio;
            _bandMidSmooth = midRatio;
            _bandPresenceSmooth = presenceRatio;
            _bandHighSmooth = highRatio;
            _bandSmoothInitialized = true;
        }
        else
        {
            _bandLowSmooth = BandSmoothingAlpha * lowRatio + (1f - BandSmoothingAlpha) * _bandLowSmooth;
            _bandMidSmooth = BandSmoothingAlpha * midRatio + (1f - BandSmoothingAlpha) * _bandMidSmooth;
            _bandPresenceSmooth = BandSmoothingAlpha * presenceRatio + (1f - BandSmoothingAlpha) * _bandPresenceSmooth;
            _bandHighSmooth = BandSmoothingAlpha * highRatio + (1f - BandSmoothingAlpha) * _bandHighSmooth;
        }

        bandLowRatio = _bandLowSmooth;
        bandMidRatio = _bandMidSmooth;
        bandPresenceRatio = _bandPresenceSmooth;
        bandHighRatio = _bandHighSmooth;

        float clarityDenom = bandLowRatio + bandMidRatio;
        clarityRatio = clarityDenom > 1e-6f ? bandPresenceRatio / clarityDenom : 0f;

        syllableEnergyRatio = totalPower > 1e-12f ? syllablePower / totalPower : 0f;
    }

    private float ComputeSyllableEnergyDb(ReadOnlySpan<float> hopRaw)
    {
        if (hopRaw.IsEmpty)
        {
            return -80f;
        }

        // Use a short trailing window to reduce pitch-period peaks in the envelope.
        if (_syllableWindowSamples > 0 && hopRaw.Length > _syllableWindowSamples)
        {
            hopRaw = hopRaw.Slice(hopRaw.Length - _syllableWindowSamples, _syllableWindowSamples);
        }

        float sumSquares = 0f;
        for (int i = 0; i < hopRaw.Length; i++)
        {
            float filtered = _syllableLowPass.Process(_syllableHighPass.Process(hopRaw[i]));
            sumSquares += filtered * filtered;
        }

        if (sumSquares <= 1e-12f)
        {
            return -80f;
        }

        float rms = MathF.Sqrt(sumSquares / hopRaw.Length);
        return rms > 1e-8f ? 20f * MathF.Log10(rms) : -80f;
    }

    private void ConfigureSyllableFilters(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            return;
        }

        _syllableHighPass.SetHighPass(sampleRate, SyllableBandLowHz, SyllableBandQ);
        _syllableLowPass.SetLowPass(sampleRate, SyllableBandHighHz, SyllableBandQ);
        _syllableHighPass.Reset();
        _syllableLowPass.Reset();
    }

    private void ResetBandSmoothing()
    {
        _bandLowSmooth = 0f;
        _bandMidSmooth = 0f;
        _bandPresenceSmooth = 0f;
        _bandHighSmooth = 0f;
        _bandSmoothInitialized = false;
    }

    private static float ComputePeakDb(float waveformMin, float waveformMax)
    {
        float peak = MathF.Max(MathF.Abs(waveformMin), MathF.Abs(waveformMax));
        return peak > 1e-8f ? 20f * MathF.Log10(peak) : -80f;
    }
}
