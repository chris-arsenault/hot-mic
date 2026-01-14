namespace HotMic.Core.Dsp.Analysis.Pitch;

/// <summary>
/// Autocorrelation-based pitch detector for vocal signals.
/// </summary>
public sealed class AutocorrelationPitchDetector
{
    private int _sampleRate;
    private int _frameSize;
    private int _minLag;
    private int _maxLag;
    private float _confidenceThreshold;

    private float[] _autocorr = Array.Empty<float>();
    private double[] _energyPrefix = Array.Empty<double>();

    public AutocorrelationPitchDetector(int sampleRate, int frameSize, float minFrequency, float maxFrequency, float confidenceThreshold = 0.3f)
    {
        Configure(sampleRate, frameSize, minFrequency, maxFrequency, confidenceThreshold);
    }

    /// <summary>
    /// Updates the detector configuration for the current sample rate and analysis window.
    /// </summary>
    public void Configure(int sampleRate, int frameSize, float minFrequency, float maxFrequency, float confidenceThreshold)
    {
        _sampleRate = Math.Max(1, sampleRate);
        _frameSize = Math.Max(64, frameSize);
        _confidenceThreshold = Math.Clamp(confidenceThreshold, 0.05f, 0.95f);

        float maxFreq = Math.Clamp(maxFrequency, 40f, _sampleRate * 0.45f);
        float minFreq = Math.Clamp(minFrequency, 20f, maxFreq - 1f);
        _minLag = Math.Max(2, (int)(_sampleRate / maxFreq));
        _maxLag = Math.Min(_frameSize - 2, (int)(_sampleRate / minFreq));

        int required = _maxLag + 1;
        if (_autocorr.Length < required)
        {
            _autocorr = new float[required];
        }

        if (_energyPrefix.Length < _frameSize + 1)
        {
            _energyPrefix = new double[_frameSize + 1];
        }
    }

    /// <summary>
    /// Detects pitch for the provided frame (length must be >= configured frame size).
    /// </summary>
    public PitchResult Detect(ReadOnlySpan<float> frame)
    {
        if (frame.Length < _frameSize || _maxLag <= _minLag)
        {
            return new PitchResult(null, 0f, false);
        }

        int size = _frameSize;
        _energyPrefix[0] = 0.0;
        for (int i = 0; i < size; i++)
        {
            double sample = frame[i];
            _energyPrefix[i + 1] = _energyPrefix[i] + sample * sample;
        }

        double bestCorr = 0.0;
        int bestLag = -1;

        for (int lag = _minLag; lag <= _maxLag; lag++)
        {
            int limit = size - lag;
            double sum = 0.0;
            for (int i = 0; i < limit; i++)
            {
                sum += (double)frame[i] * frame[i + lag];
            }

            double energy0 = _energyPrefix[limit];
            double energyLag = _energyPrefix[size] - _energyPrefix[lag];
            double denom = Math.Sqrt(energy0 * energyLag + 1e-12);
            double corr = denom > 0.0 ? sum / denom : 0.0;
            _autocorr[lag] = (float)corr;

            if (corr > bestCorr)
            {
                bestCorr = corr;
                bestLag = lag;
            }
        }

        if (bestLag <= 0 || bestCorr < _confidenceThreshold)
        {
            return new PitchResult(null, Math.Clamp((float)bestCorr, 0f, 1f), false);
        }

        float refinedLag = bestLag;
        if (bestLag > _minLag && bestLag < _maxLag)
        {
            float s0 = _autocorr[bestLag - 1];
            float s1 = _autocorr[bestLag];
            float s2 = _autocorr[bestLag + 1];
            float denom = 2f * (2f * s1 - s2 - s0);
            if (MathF.Abs(denom) > 1e-6f)
            {
                refinedLag = bestLag + (s2 - s0) / denom;
            }
        }

        float frequency = _sampleRate / refinedLag;
        float confidence = Math.Clamp((float)bestCorr, 0f, 1f);
        return new PitchResult(frequency, confidence, true);
    }
}
