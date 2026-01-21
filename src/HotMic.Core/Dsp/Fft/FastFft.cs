namespace HotMic.Core.Dsp.Fft;

/// <summary>
/// In-place radix-2 FFT with precomputed tables (no allocations at runtime).
/// </summary>
public sealed class FastFft
{
    private readonly int _size;
    private readonly int[] _bitReverse;
    private readonly float[] _cos;
    private readonly float[] _sin;

    public FastFft(int size)
    {
        if (size <= 0 || (size & (size - 1)) != 0)
        {
            throw new ArgumentException("FFT size must be a power of two.", nameof(size));
        }

        _size = size;
        _bitReverse = BuildBitReverse(size);
        _cos = new float[size / 2];
        _sin = new float[size / 2];
        for (int i = 0; i < _cos.Length; i++)
        {
            float angle = -2f * MathF.PI * i / size;
            _cos[i] = MathF.Cos(angle);
            _sin[i] = MathF.Sin(angle);
        }
    }

    public int Size => _size;

    public void Forward(float[] real, float[] imag)
    {
        Transform(real, imag, inverse: false);
    }

    public void Inverse(float[] real, float[] imag)
    {
        Transform(real, imag, inverse: true);
    }

    private void Transform(float[] real, float[] imag, bool inverse)
    {
        int n = _size;
        BitReversePermute(real, imag);

        for (int len = 2; len <= n; len <<= 1)
        {
            int half = len >> 1;
            int step = n / len;

            for (int i = 0; i < n; i += len)
            {
                int k = 0;
                for (int j = 0; j < half; j++)
                {
                    int even = i + j;
                    int odd = even + half;

                    float cos = _cos[k];
                    float sin = inverse ? -_sin[k] : _sin[k];

                    float tre = real[odd] * cos - imag[odd] * sin;
                    float tim = real[odd] * sin + imag[odd] * cos;

                    real[odd] = real[even] - tre;
                    imag[odd] = imag[even] - tim;
                    real[even] += tre;
                    imag[even] += tim;

                    k += step;
                }
            }
        }

        if (inverse)
        {
            float inv = 1f / n;
            for (int i = 0; i < n; i++)
            {
                real[i] *= inv;
                imag[i] *= inv;
            }
        }
    }

    private void BitReversePermute(float[] real, float[] imag)
    {
        int n = _size;
        for (int i = 0; i < n; i++)
        {
            int j = _bitReverse[i];
            if (j <= i)
            {
                continue;
            }

            (real[i], real[j]) = (real[j], real[i]);
            (imag[i], imag[j]) = (imag[j], imag[i]);
        }
    }

    private static int[] BuildBitReverse(int size)
    {
        int bits = 0;
        int temp = size;
        while (temp > 1)
        {
            bits++;
            temp >>= 1;
        }

        var table = new int[size];
        for (int i = 0; i < size; i++)
        {
            int value = i;
            int reversed = 0;
            for (int bit = 0; bit < bits; bit++)
            {
                reversed = (reversed << 1) | (value & 1);
                value >>= 1;
            }
            table[i] = reversed;
        }

        return table;
    }
}
