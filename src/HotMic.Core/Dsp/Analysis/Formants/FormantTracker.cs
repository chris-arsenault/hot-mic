using System.Numerics;

namespace HotMic.Core.Dsp.Analysis.Formants;

/// <summary>
/// Tracks vocal formants from LPC coefficients via polynomial root finding.
/// </summary>
public sealed class FormantTracker
{
    private const int MaxIterations = 32;
    private const float RootEpsilon = 1e-6f;

    private int _order;
    private Complex[] _roots = Array.Empty<Complex>();
    private Complex[] _polyCoefficients = Array.Empty<Complex>();
    private float[] _freqScratch = Array.Empty<float>();
    private float[] _bwScratch = Array.Empty<float>();

    public FormantTracker(int order)
    {
        Configure(order);
    }

    public int Order => _order;

    public void Configure(int order)
    {
        _order = Math.Clamp(order, 4, 32);
        int size = _order + 1;
        if (_polyCoefficients.Length != size)
        {
            _polyCoefficients = new Complex[size];
            _roots = new Complex[_order];
        }

        if (_freqScratch.Length != _order)
        {
            _freqScratch = new float[_order];
            _bwScratch = new float[_order];
        }
    }

    /// <summary>
    /// Extract formant frequencies and bandwidths. Returns count of formants written.
    /// </summary>
    public int Track(ReadOnlySpan<float> lpcCoefficients, int sampleRate,
        Span<float> formantFrequencies, Span<float> formantBandwidths,
        float minFrequency, float maxFrequency, int maxFormants)
    {
        if (lpcCoefficients.Length < _order + 1 || formantFrequencies.IsEmpty || formantBandwidths.IsEmpty)
        {
            return 0;
        }

        maxFormants = Math.Min(maxFormants, Math.Min(formantFrequencies.Length, formantBandwidths.Length));
        if (maxFormants <= 0)
        {
            return 0;
        }

        // Build polynomial: z^p + a1 z^(p-1) + ... + ap
        _polyCoefficients[0] = Complex.One;
        for (int i = 1; i <= _order; i++)
        {
            _polyCoefficients[i] = new Complex(lpcCoefficients[i], 0f);
        }

        InitializeRoots();
        SolveRoots();

        int count = 0;
        float nyquist = sampleRate * 0.5f;
        float minHz = Math.Clamp(minFrequency, 20f, nyquist - 1f);
        float maxHz = Math.Clamp(maxFrequency, minHz + 1f, nyquist);

        for (int i = 0; i < _roots.Length; i++)
        {
            Complex root = _roots[i];
            if (root.Imaginary <= 0f)
            {
                continue;
            }

            float magnitude = (float)root.Magnitude;
            if (magnitude <= 1e-4f)
            {
                continue;
            }

            float angle = MathF.Atan2((float)root.Imaginary, (float)root.Real);
            float freq = angle * sampleRate / (2f * MathF.PI);
            float bandwidth = -sampleRate / MathF.PI * MathF.Log(magnitude);

            if (freq < minHz || freq > maxHz || bandwidth <= 0f)
            {
                continue;
            }

            _freqScratch[count] = freq;
            _bwScratch[count] = bandwidth;
            count++;
        }

        if (count == 0)
        {
            return 0;
        }

        // Sort by frequency (small N, insertion sort).
        for (int i = 1; i < count; i++)
        {
            float f = _freqScratch[i];
            float b = _bwScratch[i];
            int j = i - 1;
            while (j >= 0 && _freqScratch[j] > f)
            {
                _freqScratch[j + 1] = _freqScratch[j];
                _bwScratch[j + 1] = _bwScratch[j];
                j--;
            }
            _freqScratch[j + 1] = f;
            _bwScratch[j + 1] = b;
        }

        int outputCount = Math.Min(count, maxFormants);
        for (int i = 0; i < outputCount; i++)
        {
            formantFrequencies[i] = _freqScratch[i];
            formantBandwidths[i] = _bwScratch[i];
        }

        return outputCount;
    }

    private void InitializeRoots()
    {
        float radius = 0.9f;
        for (int i = 0; i < _roots.Length; i++)
        {
            float angle = 2f * MathF.PI * i / _roots.Length;
            _roots[i] = new Complex(radius * MathF.Cos(angle), radius * MathF.Sin(angle));
        }
    }

    private void SolveRoots()
    {
        for (int iter = 0; iter < MaxIterations; iter++)
        {
            bool converged = true;
            for (int i = 0; i < _roots.Length; i++)
            {
                Complex z = _roots[i];
                Complex numerator = EvaluatePolynomial(z);
                Complex denominator = Complex.One;

                for (int j = 0; j < _roots.Length; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }
                    denominator *= (z - _roots[j]);
                }

                if (denominator.Magnitude < 1e-9f)
                {
                    continue;
                }

                Complex delta = numerator / denominator;
                z -= delta;
                _roots[i] = z;

                if (delta.Magnitude > RootEpsilon)
                {
                    converged = false;
                }
            }

            if (converged)
            {
                break;
            }
        }
    }

    private Complex EvaluatePolynomial(Complex z)
    {
        Complex result = _polyCoefficients[0];
        for (int i = 1; i < _polyCoefficients.Length; i++)
        {
            result = result * z + _polyCoefficients[i];
        }
        return result;
    }
}
