namespace HotMic.Core.Dsp;

public sealed class DelayLine
{
    private float[] _buffer = Array.Empty<float>();
    private int _mask;
    private int _writeIndex;

    public DelayLine(int maxDelaySamples)
    {
        EnsureCapacity(maxDelaySamples);
    }

    public int Capacity => _buffer.Length;

    public void EnsureCapacity(int maxDelaySamples)
    {
        int required = Math.Max(1, maxDelaySamples + 1);
        int size = NextPowerOfTwo(required);
        if (_buffer.Length >= size)
        {
            return;
        }

        _buffer = new float[size];
        _mask = size - 1;
        _writeIndex = 0;
    }

    public void Process(ReadOnlySpan<float> input, Span<float> output, int delaySamples)
    {
        if (input.IsEmpty)
        {
            return;
        }

        if (delaySamples < 0)
        {
            delaySamples = 0;
        }

        if (delaySamples >= _buffer.Length)
        {
            delaySamples = _buffer.Length - 1;
        }

        for (int i = 0; i < input.Length; i++)
        {
            float sample = input[i];
            _buffer[_writeIndex] = sample;
            int readIndex = (_writeIndex - delaySamples) & _mask;
            output[i] = delaySamples == 0 ? sample : _buffer[readIndex];
            _writeIndex = (_writeIndex + 1) & _mask;
        }
    }

    private static int NextPowerOfTwo(int value)
    {
        int power = 1;
        while (power < value)
        {
            power <<= 1;
        }
        return power;
    }
}
