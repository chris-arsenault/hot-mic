namespace HotMic.Core.Dsp.Filters;

/// <summary>
/// Low-pass filter with decimation for downsampling audio.
/// Used for LPC analysis at reduced sample rates.
/// </summary>
public sealed class DecimatingFilter
{
    private const int DefaultOrder = 8;
    private const float DefaultCutoff = 0.4f; // Relative to Nyquist

    private readonly float[] _coefficients;
    private readonly float[] _state;
    private int _decimationFactor = 2;

    public DecimatingFilter(int order = DefaultOrder)
    {
        _coefficients = new float[order];
        _state = new float[order];

        // Simple moving average as placeholder
        float sum = 0f;
        for (int i = 0; i < order; i++)
        {
            _coefficients[i] = 1f / order;
            sum += _coefficients[i];
        }

        // Normalize
        for (int i = 0; i < order; i++)
        {
            _coefficients[i] /= sum;
        }
    }

    public void Reset()
    {
        Array.Clear(_state);
    }

    /// <summary>
    /// Process and downsample the input signal.
    /// </summary>
    /// <param name="input">Input samples.</param>
    /// <param name="output">Output buffer for decimated samples.</param>
    /// <returns>Number of output samples written.</returns>
    public int ProcessDownsample(ReadOnlySpan<float> input, Span<float> output)
    {
        int outputIndex = 0;
        int maxOutput = output.Length;
        int inputLength = input.Length;

        for (int i = 0; i < inputLength && outputIndex < maxOutput; i++)
        {
            // Shift state
            for (int j = _state.Length - 1; j > 0; j--)
            {
                _state[j] = _state[j - 1];
            }
            _state[0] = input[i];

            // Only output every _decimationFactor samples
            if (i % _decimationFactor == 0)
            {
                float sum = 0f;
                for (int j = 0; j < _coefficients.Length && j < _state.Length; j++)
                {
                    sum += _coefficients[j] * _state[j];
                }
                output[outputIndex++] = sum;
            }
        }

        return outputIndex;
    }

    public void SetDecimationFactor(int factor)
    {
        _decimationFactor = Math.Max(1, factor);
    }
}
