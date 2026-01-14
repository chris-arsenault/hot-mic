namespace HotMic.Core.Dsp.Analysis.Formants;

/// <summary>
/// Computes LPC coefficients using Levinson-Durbin recursion.
/// </summary>
public sealed class LpcAnalyzer
{
    private int _order;
    private double[] _autoCorrelation = Array.Empty<double>();
    private double[] _coefficients = Array.Empty<double>();

    public LpcAnalyzer(int order)
    {
        Configure(order);
    }

    public int Order => _order;

    public void Configure(int order)
    {
        _order = Math.Clamp(order, 4, 32);
        int size = _order + 1;
        if (_autoCorrelation.Length != size)
        {
            _autoCorrelation = new double[size];
            _coefficients = new double[size];
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

        // Autocorrelation
        for (int lag = 0; lag <= _order; lag++)
        {
            double sum = 0.0;
            for (int i = 0; i < n - lag; i++)
            {
                sum += (double)frame[i] * frame[i + lag];
            }
            _autoCorrelation[lag] = sum;
        }

        double error = _autoCorrelation[0];
        if (error <= 1e-12)
        {
            return false;
        }

        _coefficients[0] = 1f;
        for (int i = 1; i <= _order; i++)
        {
            double acc = _autoCorrelation[i];
            for (int j = 1; j < i; j++)
            {
                acc -= _coefficients[j] * _autoCorrelation[i - j];
            }

            double reflection = acc / error;
            _coefficients[i] = reflection;

            for (int j = 1; j < i; j++)
            {
                _coefficients[j] -= reflection * _coefficients[i - j];
            }

            error *= 1f - reflection * reflection;
            if (error <= 1e-12)
            {
                break;
            }
        }

        for (int i = 0; i <= _order; i++)
        {
            outputCoefficients[i] = (float)_coefficients[i];
        }

        return true;
    }
}
