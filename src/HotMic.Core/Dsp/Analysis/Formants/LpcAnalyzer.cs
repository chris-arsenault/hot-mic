namespace HotMic.Core.Dsp.Analysis.Formants;

/// <summary>
/// Computes LPC coefficients using Burg recursion.
/// </summary>
public sealed class LpcAnalyzer
{
    private int _order;
    private double[] _coefficients = Array.Empty<double>();
    private double[] _coefficientsTemp = Array.Empty<double>();
    private float[] _forwardErrors = Array.Empty<float>();
    private float[] _backwardErrors = Array.Empty<float>();

    public LpcAnalyzer(int order)
    {
        Configure(order);
    }

    public int Order => _order;

    public void Configure(int order)
    {
        _order = Math.Clamp(order, 4, 32);
        int size = _order + 1;
        if (_coefficients.Length != size)
        {
            _coefficients = new double[size];
            _coefficientsTemp = new double[size];
        }
    }

    /// <summary>
    /// Compute LPC coefficients for the provided frame.
    /// </summary>
    public bool Compute(ReadOnlySpan<float> frame, Span<float> outputCoefficients)
    {
        if (outputCoefficients.Length < _order + 1)
        {
            return false;
        }

        int n = frame.Length;
        if (n <= _order)
        {
            return false;
        }

        EnsureErrorBuffers(n);
        for (int i = 0; i < n; i++)
        {
            float sample = frame[i];
            _forwardErrors[i] = sample;
            _backwardErrors[i] = sample;
        }

        Array.Clear(_coefficients);
        _coefficients[0] = 1.0;

        // Burg recursion (Childers 1978; Press et al. 1992) for A(z)=1+Σa_k z^-k.
        for (int m = 1; m <= _order; m++)
        {
            double numerator = 0.0;
            double denominator = 0.0;
            for (int i = m; i < n; i++)
            {
                double ef = _forwardErrors[i];
                double eb = _backwardErrors[i - 1];
                numerator += ef * eb;
                denominator += ef * ef + eb * eb;
            }

            if (denominator <= 1e-12)
            {
                return false;
            }

            double reflection = -2.0 * numerator / denominator;
            if (reflection <= -1.0 || reflection >= 1.0)
            {
                return false;
            }

            _coefficientsTemp[m] = reflection;
            for (int i = 1; i < m; i++)
            {
                _coefficientsTemp[i] = _coefficients[i] + reflection * _coefficients[m - i];
            }

            for (int i = 1; i <= m; i++)
            {
                _coefficients[i] = _coefficientsTemp[i];
            }

            for (int i = m; i < n; i++)
            {
                double ef = _forwardErrors[i];
                double eb = _backwardErrors[i - 1];
                _forwardErrors[i] = (float)(ef + reflection * eb);
                _backwardErrors[i - 1] = (float)(eb + reflection * ef);
            }
        }

        for (int i = 0; i <= _order; i++)
        {
            outputCoefficients[i] = (float)_coefficients[i];
        }

        return true;
    }

    private void EnsureErrorBuffers(int length)
    {
        if (_forwardErrors.Length < length)
        {
            _forwardErrors = new float[length];
            _backwardErrors = new float[length];
        }
    }
}
