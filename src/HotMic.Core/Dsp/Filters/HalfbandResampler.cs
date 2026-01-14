namespace HotMic.Core.Dsp.Filters;

// 2x half-band FIR resampler with precomputed taps and no allocations.
internal sealed class HalfbandResampler
{
    private const int TapCount = 15;
    private const int CenterTap = (TapCount - 1) / 2;

    // Windowed-sinc half-band taps (cutoff = 0.5 of Nyquist at the upsampled rate).
    private static readonly float[] Taps =
    [
        -0.003651454f, 0f, 0.016179254f, 0f, -0.068411775f, 0f, 0.304947525f, 0.5018729f,
        0.304947525f, 0f, -0.068411775f, 0f, 0.016179254f, 0f, -0.003651454f
    ];

    private static readonly float[] EvenTaps =
    [
        -0.003651454f, 0.016179254f, -0.068411775f, 0.304947525f,
        0.304947525f, -0.068411775f, 0.016179254f, -0.003651454f
    ];

    private static readonly float[] OddTaps =
    [
        0f, 0f, 0f, 0.5018729f, 0f, 0f, 0f
    ];

    private readonly float[] _delay = new float[TapCount];
    private int _index;
    private int _phase;

    public int FilterDelaySamples => CenterTap;

    public void Reset()
    {
        Array.Clear(_delay);
        _index = 0;
        _phase = 0;
    }

    public void ProcessUpsample(ReadOnlySpan<float> input, Span<float> output)
    {
        int outIndex = 0;

        for (int i = 0; i < input.Length; i++)
        {
            _delay[_index] = input[i];

            float even = 0f;
            float odd = 0f;

            int delayIndex = _index;
            for (int tap = 0; tap < EvenTaps.Length; tap++)
            {
                even += EvenTaps[tap] * _delay[delayIndex];
                if (--delayIndex < 0)
                {
                    delayIndex = TapCount - 1;
                }
            }

            delayIndex = _index;
            for (int tap = 0; tap < OddTaps.Length; tap++)
            {
                odd += OddTaps[tap] * _delay[delayIndex];
                if (--delayIndex < 0)
                {
                    delayIndex = TapCount - 1;
                }
            }

            output[outIndex++] = even * 2f;
            output[outIndex++] = odd * 2f;

            if (++_index == TapCount)
            {
                _index = 0;
            }
        }
    }

    public void ProcessDownsample(ReadOnlySpan<float> input, Span<float> output)
    {
        int outIndex = 0;

        for (int i = 0; i < input.Length; i++)
        {
            _delay[_index] = input[i];

            float filtered = 0f;
            int delayIndex = _index;
            for (int tap = 0; tap < TapCount; tap++)
            {
                filtered += Taps[tap] * _delay[delayIndex];
                if (--delayIndex < 0)
                {
                    delayIndex = TapCount - 1;
                }
            }

            if (++_index == TapCount)
            {
                _index = 0;
            }

            if ((_phase & 1) == 0)
            {
                output[outIndex++] = filtered;
            }
            _phase ^= 1;
        }
    }
}
