using HotMic.Core.Dsp;

namespace HotMic.Core.Dsp.Filters;

/// <summary>
/// First-order all-pass filter for phase manipulation without magnitude changes.
/// </summary>
public sealed class AllPassFilter
{
    private float _a;
    private float _z1;

    public void SetFrequency(int sampleRate, float frequency)
    {
        if (sampleRate <= 0)
        {
            _a = 0f;
            return;
        }

        float freq = Math.Clamp(frequency, 10f, sampleRate * 0.45f);
        float t = MathF.Tan(MathF.PI * freq / sampleRate);
        _a = (1f - t) / (1f + t);
    }

    public float Process(float input)
    {
        float output = -_a * input + _z1;
        _z1 = input + _a * output;
        _z1 = DspUtils.FlushDenormal(_z1);
        return DspUtils.FlushDenormal(output);
    }

    public void Reset()
    {
        _z1 = 0f;
    }
}
