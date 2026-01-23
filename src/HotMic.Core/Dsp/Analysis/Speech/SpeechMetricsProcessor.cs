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

    private readonly SpeechCoach _speechCoach = new();
    private readonly BiquadFilter _syllableHighPass = new();
    private readonly BiquadFilter _syllableLowPass = new();

    private float _bandLowSmooth;
    private float _bandMidSmooth;
    private float _bandPresenceSmooth;
    private float _bandHighSmooth;
    private bool _bandSmoothInitialized;

    public float LastEnergyDb { get; private set; }
    public float LastSyllableEnergyDb { get; private set; }
    public float LastSyllableEnergyRatio { get; private set; }

    public void Configure(int hopSize, int sampleRate)
    {
        _speechCoach.Configure(hopSize, sampleRate);
        ConfigureSyllableFilters(sampleRate);
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
    }

    public SpeechMetricsFrame Process(
        float waveformMin,
        float waveformMax,
        ReadOnlySpan<float> hopRaw,
        ReadOnlySpan<float> speechMagnitudes,
        ReadOnlySpan<float> binCentersHz,
        float binResolutionHz,
        float pitchHz,
        float pitchConfidence,
        VoicingState voicing,
        float spectralFlux,
        float spectralSlope,
        float hnr,
        long frameId)
    {
        float energyDb = ComputePeakDb(waveformMin, waveformMax);
        float syllableEnergyDb = ComputeSyllableEnergyDb(hopRaw);
        float spectralFlatness = ComputeSpectralFlatness(speechMagnitudes);

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

        double sum = 0.0;
        for (int i = 0; i < hopRaw.Length; i++)
        {
            float filtered = _syllableLowPass.Process(_syllableHighPass.Process(hopRaw[i]));
            sum += filtered * filtered;
        }

        float rms = sum > 0.0 ? MathF.Sqrt((float)(sum / hopRaw.Length)) : 0f;
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
