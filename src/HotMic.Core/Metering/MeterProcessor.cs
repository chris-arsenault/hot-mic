using System.Threading;

namespace HotMic.Core.Metering;

public sealed class MeterProcessor
{
    private readonly int _holdSamples;
    private readonly float _decayPerSample;
    private int _peakBits;
    private int _rmsBits;
    private float _peakHold;
    private int _holdSamplesLeft;

    public MeterProcessor(int sampleRate, float peakHoldSeconds = 1.5f, float decayPerSecond = 0.5f)
    {
        _holdSamples = (int)(sampleRate * peakHoldSeconds);
        _decayPerSample = decayPerSecond / sampleRate;
    }

    public void Process(Span<float> buffer)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        float peak = 0f;
        double sumSquares = 0d;
        for (int i = 0; i < buffer.Length; i++)
        {
            float sample = buffer[i];
            float abs = MathF.Abs(sample);
            if (abs > peak)
            {
                peak = abs;
            }

            sumSquares += abs * abs;
        }

        float rms = MathF.Sqrt((float)(sumSquares / buffer.Length));
        UpdatePeakHold(peak, buffer.Length);

        Interlocked.Exchange(ref _peakBits, BitConverter.SingleToInt32Bits(_peakHold));
        Interlocked.Exchange(ref _rmsBits, BitConverter.SingleToInt32Bits(rms));
    }

    public float GetPeakLevel()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.CompareExchange(ref _peakBits, 0, 0));
    }

    public float GetRmsLevel()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.CompareExchange(ref _rmsBits, 0, 0));
    }

    private void UpdatePeakHold(float peak, int samples)
    {
        if (peak >= _peakHold)
        {
            _peakHold = peak;
            _holdSamplesLeft = _holdSamples;
            return;
        }

        if (_holdSamplesLeft > 0)
        {
            _holdSamplesLeft -= samples;
            if (_holdSamplesLeft < 0)
            {
                _holdSamplesLeft = 0;
            }
            return;
        }

        _peakHold = MathF.Max(0f, _peakHold - _decayPerSample * samples);
    }
}
