namespace HotMic.Core.Dsp.Filters;

/// <summary>
/// In-place biquad filter (transposed DF-II) with coefficient updates and no allocations.
/// </summary>
public sealed class BiquadFilter
{
    private float _b0;
    private float _b1;
    private float _b2;
    private float _a1;
    private float _a2;
    private float _z1;
    private float _z2;

    public void SetLowShelf(float sampleRate, float freq, float gainDb, float q)
    {
        SetCoefficients(FilterType.LowShelf, sampleRate, freq, gainDb, q);
    }

    public void SetHighShelf(float sampleRate, float freq, float gainDb, float q)
    {
        SetCoefficients(FilterType.HighShelf, sampleRate, freq, gainDb, q);
    }

    public void SetPeaking(float sampleRate, float freq, float gainDb, float q)
    {
        SetCoefficients(FilterType.Peaking, sampleRate, freq, gainDb, q);
    }

    public void SetHighPass(float sampleRate, float freq, float q)
    {
        SetCoefficients(FilterType.HighPass, sampleRate, freq, 0f, q);
    }

    public void SetLowPass(float sampleRate, float freq, float q)
    {
        SetCoefficients(FilterType.LowPass, sampleRate, freq, 0f, q);
    }

    public void SetBandPass(float sampleRate, float freq, float q)
    {
        SetCoefficients(FilterType.BandPass, sampleRate, freq, 0f, q);
    }

    public float Process(float input)
    {
        float output = _b0 * input + _z1;
        _z1 = _b1 * input - _a1 * output + _z2;
        _z2 = _b2 * input - _a2 * output;
        return output;
    }

    public void Reset()
    {
        _z1 = 0f;
        _z2 = 0f;
    }

    private void SetCoefficients(FilterType type, float sampleRate, float freq, float gainDb, float q)
    {
        float clampedFreq = Math.Clamp(freq, 10f, sampleRate * 0.45f);
        float clampedQ = MathF.Max(0.1f, q);
        float a = MathF.Pow(10f, gainDb / 40f);
        float omega = 2f * MathF.PI * clampedFreq / sampleRate;
        float sin = MathF.Sin(omega);
        float cos = MathF.Cos(omega);
        float alpha = sin / (2f * clampedQ);

        float b0;
        float b1;
        float b2;
        float a0;
        float a1;
        float a2;

        switch (type)
        {
            case FilterType.LowPass:
            {
                b0 = (1f - cos) * 0.5f;
                b1 = 1f - cos;
                b2 = (1f - cos) * 0.5f;
                a0 = 1f + alpha;
                a1 = -2f * cos;
                a2 = 1f - alpha;
                break;
            }
            case FilterType.HighPass:
            {
                b0 = (1f + cos) * 0.5f;
                b1 = -(1f + cos);
                b2 = (1f + cos) * 0.5f;
                a0 = 1f + alpha;
                a1 = -2f * cos;
                a2 = 1f - alpha;
                break;
            }
            case FilterType.BandPass:
            {
                b0 = alpha;
                b1 = 0f;
                b2 = -alpha;
                a0 = 1f + alpha;
                a1 = -2f * cos;
                a2 = 1f - alpha;
                break;
            }
            case FilterType.LowShelf:
            {
                float sqrtA = MathF.Sqrt(a);
                float twoSqrtAAlpha = 2f * sqrtA * alpha;
                b0 = a * ((a + 1f) - (a - 1f) * cos + twoSqrtAAlpha);
                b1 = 2f * a * ((a - 1f) - (a + 1f) * cos);
                b2 = a * ((a + 1f) - (a - 1f) * cos - twoSqrtAAlpha);
                a0 = (a + 1f) + (a - 1f) * cos + twoSqrtAAlpha;
                a1 = -2f * ((a - 1f) + (a + 1f) * cos);
                a2 = (a + 1f) + (a - 1f) * cos - twoSqrtAAlpha;
                break;
            }
            case FilterType.HighShelf:
            {
                float sqrtA = MathF.Sqrt(a);
                float twoSqrtAAlpha = 2f * sqrtA * alpha;
                b0 = a * ((a + 1f) + (a - 1f) * cos + twoSqrtAAlpha);
                b1 = -2f * a * ((a - 1f) + (a + 1f) * cos);
                b2 = a * ((a + 1f) + (a - 1f) * cos - twoSqrtAAlpha);
                a0 = (a + 1f) - (a - 1f) * cos + twoSqrtAAlpha;
                a1 = 2f * ((a - 1f) - (a + 1f) * cos);
                a2 = (a + 1f) - (a - 1f) * cos - twoSqrtAAlpha;
                break;
            }
            default:
            {
                b0 = 1f + alpha * a;
                b1 = -2f * cos;
                b2 = 1f - alpha * a;
                a0 = 1f + alpha / a;
                a1 = -2f * cos;
                a2 = 1f - alpha / a;
                break;
            }
        }

        float invA0 = 1f / a0;
        _b0 = b0 * invA0;
        _b1 = b1 * invA0;
        _b2 = b2 * invA0;
        _a1 = a1 * invA0;
        _a2 = a2 * invA0;
    }

    private enum FilterType
    {
        LowShelf,
        HighShelf,
        Peaking,
        LowPass,
        HighPass,
        BandPass
    }
}
