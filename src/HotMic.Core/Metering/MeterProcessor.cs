using System.Threading;

namespace HotMic.Core.Metering;

public sealed class MeterProcessor
{
    private readonly int _holdSamples;
    private readonly float _peakDecayPerSample;
    private readonly float _rmsAttackCoeff;
    private readonly float _rmsReleaseCoeff;
    private int _peakBits;
    private int _rmsBits;
    private float _peakHold;
    private float _rmsSmoothed;
    private int _holdSamplesLeft;

    public MeterProcessor(int sampleRate, float peakHoldSeconds = 0.5f, float peakDecayPerSecond = 2f)
    {
        _holdSamples = (int)(sampleRate * peakHoldSeconds);
        _peakDecayPerSample = peakDecayPerSecond / sampleRate;

        // RMS smoothing: ~50ms attack, ~150ms release for responsive but smooth display
        float rmsAttackMs = 50f;
        float rmsReleaseMs = 150f;
        _rmsAttackCoeff = 1f - MathF.Exp(-1f / (rmsAttackMs * 0.001f * sampleRate));
        _rmsReleaseCoeff = 1f - MathF.Exp(-1f / (rmsReleaseMs * 0.001f * sampleRate));
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

        // Calculate instantaneous RMS for this buffer
        float instantRms = MathF.Sqrt((float)(sumSquares / buffer.Length));

        // Smooth RMS with attack/release envelope follower
        float rmsCoeff = instantRms > _rmsSmoothed ? _rmsAttackCoeff : _rmsReleaseCoeff;
        // Apply coefficient for the number of samples processed (approximate)
        float effectiveCoeff = 1f - MathF.Pow(1f - rmsCoeff, buffer.Length);
        _rmsSmoothed += effectiveCoeff * (instantRms - _rmsSmoothed);

        UpdatePeakHold(peak, buffer.Length);

        Interlocked.Exchange(ref _peakBits, BitConverter.SingleToInt32Bits(_peakHold));
        Interlocked.Exchange(ref _rmsBits, BitConverter.SingleToInt32Bits(_rmsSmoothed));
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

        _peakHold = MathF.Max(0f, _peakHold - _peakDecayPerSample * samples);
    }
}
