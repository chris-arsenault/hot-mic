namespace HotMic.Core.Dsp.Analysis.Pitch;

/// <summary>
/// Result of pitch detection.
/// </summary>
public readonly record struct PitchResult(float? FrequencyHz, float Confidence, bool IsVoiced);

/// <summary>
/// YIN pitch detector (de Cheveigné & Kawahara, 2002).
/// Uses pre-allocated buffers and is safe for real-time analysis threads.
/// </summary>
public sealed class YinPitchDetector
{
    private int _sampleRate;
    private int _frameSize;
    private int _minTau;
    private int _maxTau;
    private float _threshold;

    private float[] _difference = Array.Empty<float>();
    private float[] _cmnd = Array.Empty<float>();

    public YinPitchDetector(int sampleRate, int frameSize, float minFrequency, float maxFrequency, float threshold = 0.15f)
    {
        Configure(sampleRate, frameSize, minFrequency, maxFrequency, threshold);
    }

    public void Configure(int sampleRate, int frameSize, float minFrequency, float maxFrequency, float threshold)
    {
        _sampleRate = Math.Max(1, sampleRate);
        _frameSize = Math.Max(64, frameSize);
        _threshold = Math.Clamp(threshold, 0.01f, 0.5f);

        float maxFreq = Math.Clamp(maxFrequency, 40f, _sampleRate * 0.45f);
        float minFreq = Math.Clamp(minFrequency, 20f, maxFreq - 1f);
        _minTau = Math.Max(2, (int)(_sampleRate / maxFreq));
        _maxTau = Math.Min(_frameSize - 2, (int)(_sampleRate / minFreq));

        int required = _maxTau + 1;
        if (_difference.Length < required)
        {
            _difference = new float[required];
            _cmnd = new float[required];
        }
    }

    /// <summary>
    /// Detect pitch for the provided frame (length must be >= configured frame size).
    /// </summary>
    public PitchResult Detect(ReadOnlySpan<float> frame)
    {
        if (frame.Length < _frameSize || _maxTau <= _minTau)
        {
            return new PitchResult(null, 0f, false);
        }

        int size = _frameSize;
        int maxTau = _maxTau;

        // Difference function
        for (int tau = 0; tau <= maxTau; tau++)
        {
            _difference[tau] = 0f;
        }

        for (int tau = 1; tau <= maxTau; tau++)
        {
            float sum = 0f;
            int limit = size - tau;
            for (int i = 0; i < limit; i++)
            {
                float delta = frame[i] - frame[i + tau];
                sum += delta * delta;
            }
            _difference[tau] = sum;
        }

        // Cumulative mean normalized difference (CMND)
        float runningSum = 0f;
        _cmnd[0] = 1f;
        for (int tau = 1; tau <= maxTau; tau++)
        {
            runningSum += _difference[tau];
            _cmnd[tau] = runningSum > 1e-12f ? (_difference[tau] * tau / runningSum) : 1f;
        }

        // Absolute threshold
        int tauEstimate = -1;
        for (int tau = _minTau; tau <= maxTau; tau++)
        {
            if (_cmnd[tau] < _threshold && _cmnd[tau] < _cmnd[tau - 1])
            {
                while (tau + 1 <= maxTau && _cmnd[tau + 1] < _cmnd[tau])
                {
                    tau++;
                }
                tauEstimate = tau;
                break;
            }
        }

        if (tauEstimate < 0)
        {
            return new PitchResult(null, 0f, false);
        }

        float refinedTau = tauEstimate;
        if (tauEstimate > 1 && tauEstimate < maxTau - 1)
        {
            float s0 = _cmnd[tauEstimate - 1];
            float s1 = _cmnd[tauEstimate];
            float s2 = _cmnd[tauEstimate + 1];
            float denom = 2f * (2f * s1 - s2 - s0);
            if (MathF.Abs(denom) > 1e-6f)
            {
                refinedTau = tauEstimate + (s2 - s0) / denom;
            }
        }

        float frequency = _sampleRate / refinedTau;
        float confidence = Math.Clamp(1f - _cmnd[tauEstimate], 0f, 1f);
        return new PitchResult(frequency, confidence, true);
    }
}
