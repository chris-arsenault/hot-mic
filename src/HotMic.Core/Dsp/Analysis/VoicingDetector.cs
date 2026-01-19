using HotMic.Core.Dsp;

namespace HotMic.Core.Dsp.Analysis;

/// <summary>
/// Voicing classification for speech analysis.
/// </summary>
public enum VoicingState : byte
{
    Silence = 0,
    Unvoiced = 1,
    Voiced = 2
}

public readonly record struct VoicingResult(VoicingState State, float Score);

public readonly record struct VoicingDetectorSettings
{
    public VoicingDetectorSettings()
    {
    }

    public float InitialNoiseFloorDb { get; init; } = -80f;
    public float NoiseFloorAttackMs { get; init; } = 60f;
    public float NoiseFloorReleaseMs { get; init; } = 2000f;
    public float NoiseFloorMarginDb { get; init; } = 6f;
    public float SilenceMarginDb { get; init; } = 0f;
    public float EnergyRangeDb { get; init; } = 18f;

    public float ZcrLow { get; init; } = 0.02f;
    public float ZcrHigh { get; init; } = 0.18f;
    public float FlatnessLow { get; init; } = 0.15f;
    public float FlatnessHigh { get; init; } = 0.6f;
    public float CppLow { get; init; } = 4f;
    public float CppHigh { get; init; } = 16f;

    public float VoicedThreshold { get; init; } = 0.6f;
    public float UnvoicedThreshold { get; init; } = 0.4f;
    public float AttackMs { get; init; } = 10f;
    public float ReleaseMs { get; init; } = 80f;
    public float HangoverMs { get; init; } = 60f;

    public float BandMinHz { get; init; } = 80f;
    public float BandMaxHz { get; init; } = 4000f;

    public float PeriodicityWeight { get; init; } = 0.45f;
    public float FlatnessWeight { get; init; } = 0.2f;
    public float ZcrWeight { get; init; } = 0.2f;
    public float EnergyWeight { get; init; } = 0.15f;
}

/// <summary>
/// Voicing detector with adaptive noise floor, band-limited flatness, and temporal smoothing.
/// </summary>
public sealed class VoicingDetector
{
    private int _sampleRate;
    private int _hopSize;
    private float _binResolution;
    private int _flatnessStartBin;
    private int _flatnessEndBin;
    private VoicingDetectorSettings _settings;

    private float _noiseFloorDb;
    private float _smoothedScore;
    private int _hangoverFramesLeft;
    private float _noiseAttackCoeff;
    private float _noiseReleaseCoeff;
    private float _attackCoeff;
    private float _releaseCoeff;
    private int _hangoverFrames;
    private float _weightSum;
    private VoicingState _state;

    public void Configure(int sampleRate, int hopSize, float binResolution, VoicingDetectorSettings settings)
    {
        _sampleRate = Math.Max(1, sampleRate);
        _hopSize = Math.Max(1, hopSize);
        _binResolution = MathF.Max(1e-6f, binResolution);
        _settings = settings;

        float framesPerSecond = _sampleRate / (float)_hopSize;
        _noiseAttackCoeff = TimeToFrameCoefficient(settings.NoiseFloorAttackMs, framesPerSecond);
        _noiseReleaseCoeff = TimeToFrameCoefficient(settings.NoiseFloorReleaseMs, framesPerSecond);
        _attackCoeff = TimeToFrameCoefficient(settings.AttackMs, framesPerSecond);
        _releaseCoeff = TimeToFrameCoefficient(settings.ReleaseMs, framesPerSecond);
        _hangoverFrames = Math.Max(0, (int)MathF.Round(settings.HangoverMs * framesPerSecond / 1000f));

        _weightSum = settings.PeriodicityWeight +
                     settings.FlatnessWeight +
                     settings.ZcrWeight +
                     settings.EnergyWeight;
        if (_weightSum <= 1e-6f)
        {
            _weightSum = 1f;
        }

        UpdateFlatnessBins(settings.BandMinHz, settings.BandMaxHz);
        Reset();
    }

    public void Reset()
    {
        _noiseFloorDb = _settings.InitialNoiseFloorDb;
        _smoothedScore = 0f;
        _hangoverFramesLeft = 0;
        _state = VoicingState.Silence;
    }

    public VoicingResult Process(ReadOnlySpan<float> frame, ReadOnlySpan<float> magnitudes, float pitchConfidence, float cppDb = 0f)
    {
        if (frame.IsEmpty)
        {
            return new VoicingResult(VoicingState.Silence, 0f);
        }

        float rms = ComputeRms(frame);
        float energyDb = DspUtils.LinearToDb(rms);

        float noiseCoeff = energyDb < _noiseFloorDb ? _noiseAttackCoeff : _noiseReleaseCoeff;
        _noiseFloorDb += (energyDb - _noiseFloorDb) * noiseCoeff;

        float snrDb = energyDb - _noiseFloorDb;
        if (snrDb < _settings.SilenceMarginDb)
        {
            if (_state == VoicingState.Voiced && _hangoverFramesLeft > 0)
            {
                _hangoverFramesLeft--;
                return new VoicingResult(_state, _smoothedScore);
            }

            _state = VoicingState.Silence;
            _smoothedScore = 0f;
            _hangoverFramesLeft = 0;
            return new VoicingResult(_state, 0f);
        }

        float energyScore = Normalize(snrDb, _settings.NoiseFloorMarginDb,
            _settings.NoiseFloorMarginDb + MathF.Max(1f, _settings.EnergyRangeDb));

        float zcr = ComputeZeroCrossingRate(frame);
        float zcrScore = 1f - Normalize(zcr, _settings.ZcrLow, _settings.ZcrHigh);

        float flatness = ComputeSpectralFlatness(magnitudes);
        float flatnessScore = 1f - Normalize(flatness, _settings.FlatnessLow, _settings.FlatnessHigh);

        float periodicity = Clamp01(pitchConfidence);
        if (cppDb > 0f)
        {
            float cppScore = Normalize(cppDb, _settings.CppLow, _settings.CppHigh);
            periodicity = MathF.Max(periodicity, cppScore);
        }

        float rawScore = periodicity * _settings.PeriodicityWeight +
                         flatnessScore * _settings.FlatnessWeight +
                         zcrScore * _settings.ZcrWeight +
                         energyScore * _settings.EnergyWeight;
        float score = Clamp01(rawScore / _weightSum);

        float coeff = score > _smoothedScore ? _attackCoeff : _releaseCoeff;
        _smoothedScore += (score - _smoothedScore) * coeff;

        if (_state == VoicingState.Voiced)
        {
            if (_smoothedScore <= _settings.UnvoicedThreshold)
            {
                if (_hangoverFramesLeft <= 0)
                {
                    _state = VoicingState.Unvoiced;
                }
                else
                {
                    _hangoverFramesLeft--;
                }
            }
            else
            {
                _hangoverFramesLeft = _hangoverFrames;
            }
        }
        else
        {
            if (_smoothedScore >= _settings.VoicedThreshold)
            {
                _state = VoicingState.Voiced;
                _hangoverFramesLeft = _hangoverFrames;
            }
            else
            {
                _state = VoicingState.Unvoiced;
            }
        }

        return new VoicingResult(_state, _smoothedScore);
    }

    public VoicingState Detect(ReadOnlySpan<float> frame, ReadOnlySpan<float> magnitudes, float pitchConfidence)
    {
        return Process(frame, magnitudes, pitchConfidence).State;
    }

    private void UpdateFlatnessBins(float minHz, float maxHz)
    {
        float clampedMin = MathF.Max(20f, MathF.Min(minHz, maxHz));
        float clampedMax = MathF.Max(clampedMin + 1f, maxHz);
        _flatnessStartBin = (int)MathF.Floor(clampedMin / _binResolution);
        _flatnessEndBin = (int)MathF.Ceiling(clampedMax / _binResolution);
    }

    private float ComputeSpectralFlatness(ReadOnlySpan<float> magnitudes)
    {
        if (magnitudes.IsEmpty)
        {
            return 1f;
        }

        int start = Math.Clamp(_flatnessStartBin, 0, magnitudes.Length - 1);
        int end = Math.Clamp(_flatnessEndBin, start, magnitudes.Length - 1);
        int count = end - start + 1;
        if (count <= 0)
        {
            return 1f;
        }

        double logSum = 0.0;
        double linSum = 0.0;
        for (int i = start; i <= end; i++)
        {
            float mag = magnitudes[i];
            float power = mag * mag + 1e-12f;
            logSum += Math.Log(power);
            linSum += power;
        }

        double geometric = Math.Exp(logSum / count);
        double arithmetic = linSum / count;
        return arithmetic > 1e-12 ? (float)(geometric / arithmetic) : 1f;
    }

    private static float ComputeRms(ReadOnlySpan<float> frame)
    {
        double sum = 0.0;
        for (int i = 0; i < frame.Length; i++)
        {
            float v = frame[i];
            sum += v * v;
        }

        return (float)Math.Sqrt(sum / Math.Max(1, frame.Length));
    }

    private static float ComputeZeroCrossingRate(ReadOnlySpan<float> frame)
    {
        int crossings = 0;
        float prev = frame[0];
        for (int i = 1; i < frame.Length; i++)
        {
            float current = frame[i];
            if ((prev >= 0f && current < 0f) || (prev < 0f && current >= 0f))
            {
                crossings++;
            }
            prev = current;
        }

        return crossings / MathF.Max(1f, frame.Length - 1);
    }

    private static float Normalize(float value, float min, float max)
    {
        if (max <= min)
        {
            return 0f;
        }

        return Clamp01((value - min) / (max - min));
    }

    private static float TimeToFrameCoefficient(float timeMs, float framesPerSecond)
    {
        float timeSeconds = MathF.Max(0.0001f, timeMs * 0.001f);
        float rate = MathF.Max(1f, framesPerSecond);
        return 1f - MathF.Exp(-1f / (timeSeconds * rate));
    }

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
}
