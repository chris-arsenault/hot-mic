namespace HotMic.Core.Dsp.Filters;

/// <summary>
/// Simple pre-emphasis filter (y[n] = x[n] - alpha * x[n-1]).
/// </summary>
public struct PreEmphasisFilter
{
    private float _alpha;
    private float _prev;

    public void Configure(float alpha)
    {
        _alpha = Math.Clamp(alpha, 0f, 1f);
    }

    public float Process(float input)
    {
        float output = input - _alpha * _prev;
        _prev = DspUtils.FlushDenormal(input);
        return DspUtils.FlushDenormal(output);
    }

    public void Reset()
    {
        _prev = 0f;
    }
}
