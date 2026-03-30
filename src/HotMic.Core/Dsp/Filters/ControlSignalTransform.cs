using HotMic.Core.Dsp;

namespace HotMic.Core.Dsp.Filters;

/// <summary>
/// Reconstructs lower-rate control signals into an audio-rate modulation signal.
/// Plugins should choose a mode that matches the control semantics instead of
/// multiplying audio directly by a step-held analysis value.
/// </summary>
public struct ControlSignalTransform
{
    private ControlSignalTransformMode _mode;
    private float _current;
    private float _target;
    private float _step;
    private int _rampSamples;
    private int _samplesLeft;
    private float _attackCoeff;
    private float _releaseCoeff;
    private float _onePoleCoeff;
    private float _riseLimitPerSample;
    private float _fallLimitPerSample;
    private float _decayCoeff;
    private float _lowThreshold;
    private float _highThreshold;
    private float _offValue;
    private float _onValue;
    private int _schmittState;

    public float Current => _current;

    public void Configure(int sampleRate, in ControlSignalTransformSettings settings, float initialValue = 0f)
    {
        _mode = settings.Mode;
        _current = initialValue;
        _target = initialValue;
        _step = 0f;
        _rampSamples = 0;
        _samplesLeft = 0;
        _attackCoeff = 0f;
        _releaseCoeff = 0f;
        _onePoleCoeff = 0f;
        _riseLimitPerSample = 0f;
        _fallLimitPerSample = 0f;
        _decayCoeff = 0f;
        _lowThreshold = settings.LowThreshold;
        _highThreshold = settings.HighThreshold;
        _offValue = settings.OffValue;
        _onValue = settings.OnValue;
        _schmittState = initialValue >= (_offValue + _onValue) * 0.5f ? 1 : 0;

        if (sampleRate <= 0)
        {
            return;
        }

        switch (_mode)
        {
            case ControlSignalTransformMode.LinearRamp:
                _rampSamples = Math.Max(1, (int)(MathF.Max(0.0001f, settings.RampMs * 0.001f) * sampleRate));
                break;
            case ControlSignalTransformMode.OnePole:
                _onePoleCoeff = DspUtils.TimeToCoefficient(settings.TimeMs, sampleRate);
                break;
            case ControlSignalTransformMode.AttackRelease:
                _attackCoeff = DspUtils.TimeToCoefficient(settings.AttackMs, sampleRate);
                _releaseCoeff = DspUtils.TimeToCoefficient(settings.ReleaseMs, sampleRate);
                break;
            case ControlSignalTransformMode.SlewLimiter:
                _riseLimitPerSample = MathF.Max(0f, settings.RiseUnitsPerSecond / sampleRate);
                _fallLimitPerSample = MathF.Max(0f, settings.FallUnitsPerSecond / sampleRate);
                break;
            case ControlSignalTransformMode.EventDecay:
                _decayCoeff = DspUtils.TimeToCoefficient(settings.DecayMs, sampleRate);
                break;
        }
    }

    public void Reset(float value = 0f)
    {
        _current = value;
        _target = value;
        _step = 0f;
        _rampSamples = Math.Max(0, _rampSamples);
        _samplesLeft = 0;
        _schmittState = value >= (_offValue + _onValue) * 0.5f ? 1 : 0;
    }

    public float Process(float input)
    {
        switch (_mode)
        {
            case ControlSignalTransformMode.Hold:
                _current = input;
                break;

            case ControlSignalTransformMode.LinearRamp:
                if (MathF.Abs(input - _target) > 1e-6f)
                {
                    _target = input;
                    _samplesLeft = Math.Max(1, _rampSamples);
                    _step = (_target - _current) / _samplesLeft;
                }

                if (_samplesLeft > 0)
                {
                    _current += _step;
                    _samplesLeft--;
                }
                else
                {
                    _current = _target;
                }
                break;

            case ControlSignalTransformMode.OnePole:
                _current += _onePoleCoeff * (input - _current);
                break;

            case ControlSignalTransformMode.AttackRelease:
                {
                    float coeff = input > _current ? _attackCoeff : _releaseCoeff;
                    _current += coeff * (input - _current);
                    break;
                }

            case ControlSignalTransformMode.SlewLimiter:
                {
                    float delta = input - _current;
                    if (delta > _riseLimitPerSample)
                    {
                        delta = _riseLimitPerSample;
                    }
                    else if (delta < -_fallLimitPerSample)
                    {
                        delta = -_fallLimitPerSample;
                    }

                    _current += delta;
                    break;
                }

            case ControlSignalTransformMode.EventDecay:
                if (input > _current)
                {
                    _current = input;
                }
                else
                {
                    _current += _decayCoeff * (0f - _current);
                }
                break;

            case ControlSignalTransformMode.Schmitt:
                if (_schmittState == 0 && input >= _highThreshold)
                {
                    _schmittState = 1;
                }
                else if (_schmittState != 0 && input <= _lowThreshold)
                {
                    _schmittState = 0;
                }

                _current = _schmittState != 0 ? _onValue : _offValue;
                break;
        }

        _current = DspUtils.FlushDenormal(_current);
        return _current;
    }
}

public readonly record struct ControlSignalTransformSettings
{
    public ControlSignalTransformMode Mode { get; init; }
    public float AttackMs { get; init; }
    public float ReleaseMs { get; init; }
    public float TimeMs { get; init; }
    public float RampMs { get; init; }
    public float RiseUnitsPerSecond { get; init; }
    public float FallUnitsPerSecond { get; init; }
    public float DecayMs { get; init; }
    public float LowThreshold { get; init; }
    public float HighThreshold { get; init; }
    public float OffValue { get; init; }
    public float OnValue { get; init; }

    public static ControlSignalTransformSettings Hold() =>
        new() { Mode = ControlSignalTransformMode.Hold };

    public static ControlSignalTransformSettings LinearRamp(float rampMs) =>
        new() { Mode = ControlSignalTransformMode.LinearRamp, RampMs = rampMs };

    public static ControlSignalTransformSettings OnePole(float timeMs) =>
        new() { Mode = ControlSignalTransformMode.OnePole, TimeMs = timeMs };

    public static ControlSignalTransformSettings AttackRelease(float attackMs, float releaseMs) =>
        new() { Mode = ControlSignalTransformMode.AttackRelease, AttackMs = attackMs, ReleaseMs = releaseMs };

    public static ControlSignalTransformSettings SlewLimiter(float riseUnitsPerSecond, float fallUnitsPerSecond) =>
        new()
        {
            Mode = ControlSignalTransformMode.SlewLimiter,
            RiseUnitsPerSecond = riseUnitsPerSecond,
            FallUnitsPerSecond = fallUnitsPerSecond
        };

    public static ControlSignalTransformSettings EventDecay(float decayMs) =>
        new() { Mode = ControlSignalTransformMode.EventDecay, DecayMs = decayMs };

    public static ControlSignalTransformSettings Schmitt(float lowThreshold, float highThreshold, float offValue = 0f, float onValue = 1f) =>
        new()
        {
            Mode = ControlSignalTransformMode.Schmitt,
            LowThreshold = lowThreshold,
            HighThreshold = highThreshold,
            OffValue = offValue,
            OnValue = onValue
        };
}

public enum ControlSignalTransformMode
{
    Hold = 0,
    LinearRamp = 1,
    OnePole = 2,
    AttackRelease = 3,
    SlewLimiter = 4,
    EventDecay = 5,
    Schmitt = 6
}
