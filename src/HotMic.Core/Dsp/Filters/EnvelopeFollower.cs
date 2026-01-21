namespace HotMic.Core.Dsp.Filters;

public sealed class EnvelopeFollower
{
    private float _attackCoeff;
    private float _releaseCoeff;
    private float _envelope;

    public void Configure(float attackMs, float releaseMs, int sampleRate)
    {
        _attackCoeff = DspUtils.TimeToCoefficient(attackMs, sampleRate);
        _releaseCoeff = DspUtils.TimeToCoefficient(releaseMs, sampleRate);
    }

    public float Process(float input)
    {
        float abs = MathF.Abs(input);
        float coeff = abs > _envelope ? _attackCoeff : _releaseCoeff;
        _envelope += coeff * (abs - _envelope);
        _envelope = DspUtils.FlushDenormal(_envelope);
        return _envelope;
    }

    public void Reset()
    {
        _envelope = 0f;
    }
}
