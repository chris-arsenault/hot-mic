using System.Threading;
using HotMic.Core.Dsp;

namespace HotMic.Core.Metering;

/// <summary>
/// Computes K-weighted LUFS loudness with momentary (400ms) and short-term (3s) windows.
/// </summary>
public sealed class LufsMeterProcessor
{
    // ITU-R BS.1770 K-weighting filter: 2nd order HPF @ 60Hz (Q=0.5) + high-shelf @ 4kHz (+4dB, Q=0.707)
    private const float KHighPassFreqHz = 60f;
    private const float KHighPassQ = 0.5f;
    private const float KHighShelfFreqHz = 4000f;
    private const float KHighShelfGainDb = 4f;
    private const float KHighShelfQ = 0.70710678f;
    private const float LufsOffsetDb = -0.691f;
    private const float MinLufs = -70f;

    private readonly BiquadFilter _highPass = new();
    private readonly BiquadFilter _highShelf = new();
    private readonly float[] _momentaryBuffer;
    private readonly float[] _shortTermBuffer;
    private int _momentaryIndex;
    private int _shortTermIndex;
    private int _momentaryFilled;
    private int _shortTermFilled;
    private double _momentarySum;
    private double _shortTermSum;
    private int _momentaryBits;
    private int _shortTermBits;

    public LufsMeterProcessor(int sampleRate, float momentarySeconds = 0.4f, float shortTermSeconds = 3.0f)
    {
        int safeRate = Math.Max(1, sampleRate);
        int momentarySamples = Math.Max(1, (int)MathF.Round(safeRate * momentarySeconds));
        int shortTermSamples = Math.Max(1, (int)MathF.Round(safeRate * shortTermSeconds));

        _momentaryBuffer = new float[momentarySamples];
        _shortTermBuffer = new float[shortTermSamples];

        _highPass.SetHighPass(safeRate, KHighPassFreqHz, KHighPassQ);
        _highShelf.SetHighShelf(safeRate, KHighShelfFreqHz, KHighShelfGainDb, KHighShelfQ);
        _momentaryBits = BitConverter.SingleToInt32Bits(MinLufs);
        _shortTermBits = BitConverter.SingleToInt32Bits(MinLufs);
    }

    /// <summary>
    /// Process a mono buffer and update LUFS windows (thread-safe for UI reads).
    /// </summary>
    public void Process(ReadOnlySpan<float> buffer)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        for (int i = 0; i < buffer.Length; i++)
        {
            float sample = buffer[i];
            if (!float.IsFinite(sample))
            {
                sample = 0f;
            }

            float weighted = _highShelf.Process(_highPass.Process(sample));
            float squared = weighted * weighted;

            UpdateWindow(squared, _momentaryBuffer, ref _momentaryIndex, ref _momentaryFilled, ref _momentarySum);
            UpdateWindow(squared, _shortTermBuffer, ref _shortTermIndex, ref _shortTermFilled, ref _shortTermSum);
        }

        Publish();
    }

    /// <summary>
    /// Process one channel from interleaved audio without allocations.
    /// </summary>
    public void ProcessInterleaved(ReadOnlySpan<float> interleaved, int channelCount, int channelIndex)
    {
        if (interleaved.IsEmpty || channelCount <= 0 || (uint)channelIndex >= (uint)channelCount)
        {
            return;
        }

        for (int i = channelIndex; i < interleaved.Length; i += channelCount)
        {
            float sample = interleaved[i];
            if (!float.IsFinite(sample))
            {
                sample = 0f;
            }

            float weighted = _highShelf.Process(_highPass.Process(sample));
            float squared = weighted * weighted;

            UpdateWindow(squared, _momentaryBuffer, ref _momentaryIndex, ref _momentaryFilled, ref _momentarySum);
            UpdateWindow(squared, _shortTermBuffer, ref _shortTermIndex, ref _shortTermFilled, ref _shortTermSum);
        }

        Publish();
    }

    /// <summary>
    /// Reset filter state and accumulated windows.
    /// </summary>
    public void Reset()
    {
        _highPass.Reset();
        _highShelf.Reset();
        Array.Clear(_momentaryBuffer, 0, _momentaryBuffer.Length);
        Array.Clear(_shortTermBuffer, 0, _shortTermBuffer.Length);
        _momentaryIndex = 0;
        _shortTermIndex = 0;
        _momentaryFilled = 0;
        _shortTermFilled = 0;
        _momentarySum = 0d;
        _shortTermSum = 0d;
        Interlocked.Exchange(ref _momentaryBits, BitConverter.SingleToInt32Bits(MinLufs));
        Interlocked.Exchange(ref _shortTermBits, BitConverter.SingleToInt32Bits(MinLufs));
    }

    /// <summary>
    /// Read the momentary LUFS value (thread-safe).
    /// </summary>
    public float GetMomentaryLufs()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.CompareExchange(ref _momentaryBits, 0, 0));
    }

    /// <summary>
    /// Read the short-term LUFS value (thread-safe).
    /// </summary>
    public float GetShortTermLufs()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.CompareExchange(ref _shortTermBits, 0, 0));
    }

    private static void UpdateWindow(float value, float[] buffer, ref int index, ref int filled, ref double sum)
    {
        if (filled < buffer.Length)
        {
            buffer[index] = value;
            sum += value;
            filled++;
        }
        else
        {
            sum -= buffer[index];
            buffer[index] = value;
            sum += value;
        }

        index++;
        if (index >= buffer.Length)
        {
            index = 0;
        }
    }

    private static float ComputeLufs(double sum, int count)
    {
        if (count <= 0)
        {
            return MinLufs;
        }

        double meanSquare = sum / count;
        if (meanSquare <= 0d)
        {
            return MinLufs;
        }

        float lufs = LufsOffsetDb + 10f * MathF.Log10((float)meanSquare + 1e-12f);
        if (!float.IsFinite(lufs))
        {
            return MinLufs;
        }

        return MathF.Max(MinLufs, lufs);
    }

    private void Publish()
    {
        float momentary = ComputeLufs(_momentarySum, _momentaryFilled);
        float shortTerm = ComputeLufs(_shortTermSum, _shortTermFilled);
        Interlocked.Exchange(ref _momentaryBits, BitConverter.SingleToInt32Bits(momentary));
        Interlocked.Exchange(ref _shortTermBits, BitConverter.SingleToInt32Bits(shortTerm));
    }
}
