using System.Threading;

namespace HotMic.Core.Metering;

public sealed class MeterProcessor
{
    private const float ClipHoldSeconds = 0.5f;
    private readonly float _peakAttackCoeff;
    private readonly float _peakReleaseCoeff;
    private readonly float _rmsAttackCoeff;
    private readonly float _rmsReleaseCoeff;
    private readonly int _clipHoldSamples;
    private int _peakBits;
    private int _rmsBits;
    private int _clipHoldRemaining;
    private int _nonFiniteSampleCount;
    private float _peakSmoothed;
    private float _rmsSmoothed;

    public MeterProcessor(int sampleRate, float peakHoldSeconds = 0.5f, float peakDecayPerSecond = 2f)
    {
        // Peak smoothing: very fast attack (~1ms), moderate release (~100ms)
        // This gives responsive peaks that decay smoothly
        float peakAttackMs = 1f;
        float peakReleaseMs = 100f;
        _peakAttackCoeff = 1f - MathF.Exp(-1f / (peakAttackMs * 0.001f * sampleRate));
        _peakReleaseCoeff = 1f - MathF.Exp(-1f / (peakReleaseMs * 0.001f * sampleRate));

        // RMS smoothing: ~50ms attack, ~150ms release for responsive but smooth display
        float rmsAttackMs = 50f;
        float rmsReleaseMs = 150f;
        _rmsAttackCoeff = 1f - MathF.Exp(-1f / (rmsAttackMs * 0.001f * sampleRate));
        _rmsReleaseCoeff = 1f - MathF.Exp(-1f / (rmsReleaseMs * 0.001f * sampleRate));

        _clipHoldSamples = Math.Max(1, (int)(sampleRate * ClipHoldSeconds));
    }

    public void Process(Span<float> buffer, bool trackClip = true)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        if (!float.IsFinite(_rmsSmoothed))
        {
            _rmsSmoothed = 0f;
        }

        if (!float.IsFinite(_peakSmoothed))
        {
            _peakSmoothed = 0f;
        }

        float peak = 0f;
        bool clipped = false;
        int nonFiniteCount = 0;
        double sumSquares = 0d;
        int validSamples = 0;
        for (int i = 0; i < buffer.Length; i++)
        {
            float sample = buffer[i];
            if (!float.IsFinite(sample))
            {
                nonFiniteCount++;
                if (trackClip)
                {
                    clipped = true;
                }
                continue;
            }

            float abs = MathF.Abs(sample);
            if (abs > peak)
            {
                peak = abs;
            }
            if (trackClip && abs > 1f)
            {
                clipped = true;
            }

            sumSquares += abs * abs;
            validSamples++;
        }

        // Calculate instantaneous RMS for this buffer
        float instantRms = validSamples > 0
            ? MathF.Sqrt((float)(sumSquares / validSamples))
            : 0f;

        if (!float.IsFinite(instantRms))
        {
            instantRms = 0f;
        }

        // Smooth RMS with attack/release envelope follower
        float rmsCoeff = instantRms > _rmsSmoothed ? _rmsAttackCoeff : _rmsReleaseCoeff;
        float effectiveRmsCoeff = 1f - MathF.Pow(1f - rmsCoeff, buffer.Length);
        _rmsSmoothed += effectiveRmsCoeff * (instantRms - _rmsSmoothed);

        // Smooth peak with attack/release envelope follower (same approach as RMS)
        float peakCoeff = peak > _peakSmoothed ? _peakAttackCoeff : _peakReleaseCoeff;
        float effectivePeakCoeff = 1f - MathF.Pow(1f - peakCoeff, buffer.Length);
        _peakSmoothed += effectivePeakCoeff * (peak - _peakSmoothed);

        Interlocked.Exchange(ref _peakBits, BitConverter.SingleToInt32Bits(_peakSmoothed));
        Interlocked.Exchange(ref _rmsBits, BitConverter.SingleToInt32Bits(_rmsSmoothed));

        if (nonFiniteCount > 0)
        {
            Interlocked.Add(ref _nonFiniteSampleCount, nonFiniteCount);
        }

        if (trackClip && clipped)
        {
            Volatile.Write(ref _clipHoldRemaining, _clipHoldSamples);
        }
        else
        {
            int remaining = Volatile.Read(ref _clipHoldRemaining);
            if (remaining > 0)
            {
                remaining = Math.Max(0, remaining - buffer.Length);
                Volatile.Write(ref _clipHoldRemaining, remaining);
            }
        }
    }

    public float GetPeakLevel()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.CompareExchange(ref _peakBits, 0, 0));
    }

    public float GetRmsLevel()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.CompareExchange(ref _rmsBits, 0, 0));
    }

    public bool GetClipHoldActive()
    {
        return Volatile.Read(ref _clipHoldRemaining) > 0;
    }

    /// <summary>
    /// Returns the number of non-finite samples observed since the last call.
    /// </summary>
    public int ConsumeNonFiniteSamples()
    {
        return Interlocked.Exchange(ref _nonFiniteSampleCount, 0);
    }

    /// <summary>
    /// Resets smoothing, clip hold, and non-finite tracking.
    /// </summary>
    public void Reset()
    {
        _peakSmoothed = 0f;
        _rmsSmoothed = 0f;
        Interlocked.Exchange(ref _peakBits, BitConverter.SingleToInt32Bits(0f));
        Interlocked.Exchange(ref _rmsBits, BitConverter.SingleToInt32Bits(0f));
        Volatile.Write(ref _clipHoldRemaining, 0);
        Interlocked.Exchange(ref _nonFiniteSampleCount, 0);
    }
}
