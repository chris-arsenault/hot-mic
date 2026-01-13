namespace HotMic.Core.Dsp;

/// <summary>
/// Computes LPC coefficients using Levinson-Durbin recursion.
/// </summary>
public sealed class LpcAnalyzer
{
    private int _order;
    private float[] _autoCorrelation = Array.Empty<float>();
    private float[] _coefficients = Array.Empty<float>();

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
            _autoCorrelation = new float[size];
            _coefficients = new float[size];
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
            float sum = 0f;
            for (int i = 0; i < n - lag; i++)
            {
                sum += frame[i] * frame[i + lag];
            }
            _autoCorrelation[lag] = sum;
        }

        float error = _autoCorrelation[0];
        if (error <= 1e-12f)
        {
            return false;
        }

        _coefficients[0] = 1f;
        for (int i = 1; i <= _order; i++)
        {
            float acc = _autoCorrelation[i];
            for (int j = 1; j < i; j++)
            {
                acc -= _coefficients[j] * _autoCorrelation[i - j];
            }

            float reflection = acc / error;
            _coefficients[i] = reflection;

            for (int j = 1; j < i; j++)
            {
                _coefficients[j] -= reflection * _coefficients[i - j];
            }

            error *= 1f - reflection * reflection;
            if (error <= 1e-12f)
            {
                break;
            }
        }

        for (int i = 0; i <= _order; i++)
        {
            outputCoefficients[i] = _coefficients[i];
        }

        return true;
    }
}
