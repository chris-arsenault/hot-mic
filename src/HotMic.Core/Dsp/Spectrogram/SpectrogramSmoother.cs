namespace HotMic.Core.Dsp.Spectrogram;

/// <summary>
/// Spectrogram smoothing utilities (EMA or bilateral).
/// </summary>
public sealed class SpectrogramSmoother
{
    // Spec defaults (docs/technical/Cleanup.md##Temporal Smoothing): 2-3 frame/bin radius, sigma spatial ~1.5, intensity 6-10 dB.
    private const int DefaultTimeRadius = 2;
    private const int DefaultFreqRadius = 2;
    private const float DefaultSigmaSpatial = 1.5f;
    private const float DefaultSigmaIntensityDb = 8f;

    private readonly int _timeRadius;
    private readonly int _freqRadius;
    private readonly float _sigmaSpatial;
    private readonly float _sigmaIntensityDb;

    private float[] _history = Array.Empty<float>();
    private int _historyIndex;
    private int _historyCount;
    private float[] _timeWeights = Array.Empty<float>();
    private float[] _freqWeights = Array.Empty<float>();

    public SpectrogramSmoother()
        : this(DefaultTimeRadius, DefaultFreqRadius, DefaultSigmaSpatial, DefaultSigmaIntensityDb)
    {
    }

    public SpectrogramSmoother(int timeRadius, int freqRadius, float sigmaSpatial, float sigmaIntensityDb)
    {
        _timeRadius = Math.Max(0, timeRadius);
        _freqRadius = Math.Max(0, freqRadius);
        _sigmaSpatial = MathF.Max(1e-3f, sigmaSpatial);
        _sigmaIntensityDb = MathF.Max(1e-3f, sigmaIntensityDb);
    }

    /// <summary>
    /// Ensure internal buffers are sized for the provided bin count.
    /// </summary>
    public void EnsureCapacity(int bins)
    {
        if (bins <= 0)
        {
            return;
        }

        int historyLength = _timeRadius + 1;
        int required = bins * historyLength;
        if (_history.Length != required)
        {
            _history = new float[required];
            _historyIndex = 0;
            _historyCount = 0;
        }

        if (_timeWeights.Length != historyLength || _freqWeights.Length != _freqRadius * 2 + 1)
        {
            _timeWeights = new float[historyLength];
            _freqWeights = new float[_freqRadius * 2 + 1];
            UpdateWeights();
        }
    }

    /// <summary>
    /// Clear the internal history.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_history, 0, _history.Length);
        _historyIndex = 0;
        _historyCount = 0;
    }

    /// <summary>
    /// Apply exponential smoothing from input into output.
    /// </summary>
    public void ApplyEma(float[] input, float[] output, float amount)
    {
        float blend = 1f - amount;
        for (int i = 0; i < input.Length; i++)
        {
            float current = output[i];
            output[i] = current + (input[i] - current) * blend;
        }
    }

    /// <summary>
    /// Apply bilateral time/frequency smoothing from input into output.
    /// </summary>
    public void ApplyBilateral(float[] input, float[] output, float amount)
    {
        int bins = input.Length;
        if (bins == 0)
        {
            return;
        }

        int historyLength = _timeRadius + 1;
        int writeIndex = _historyIndex;
        Array.Copy(input, 0, _history, writeIndex * bins, bins);
        _historyIndex = (writeIndex + 1) % historyLength;
        if (_historyCount < historyLength)
        {
            _historyCount++;
        }

        int currentIndex = writeIndex;
        int available = _historyCount;
        float intensityCoeff = -0.5f / (_sigmaIntensityDb * _sigmaIntensityDb);

        for (int bin = 0; bin < bins; bin++)
        {
            float center = input[bin];
            float centerDb = DspUtils.LinearToDb(center);
            float sum = 0f;
            float sumW = 0f;

            int timeCount = Math.Min(available, _timeRadius + 1);
            for (int dt = 0; dt < timeCount; dt++)
            {
                int frameIndex = (currentIndex - dt + historyLength) % historyLength;
                float timeWeight = _timeWeights[dt];
                int baseOffset = frameIndex * bins;

                for (int df = -_freqRadius; df <= _freqRadius; df++)
                {
                    int freqIndex = bin + df;
                    if (freqIndex < 0 || freqIndex >= bins)
                    {
                        continue;
                    }

                    float neighbor = _history[baseOffset + freqIndex];
                    float neighborDb = DspUtils.LinearToDb(neighbor);
                    float deltaDb = neighborDb - centerDb;
                    // Bilateral weighting in dB keeps edges between loud/quiet regions intact.
                    float intensityWeight = MathF.Exp(deltaDb * deltaDb * intensityCoeff);
                    float weight = timeWeight * _freqWeights[df + _freqRadius] * intensityWeight;
                    sum += neighbor * weight;
                    sumW += weight;
                }
            }

            float filtered = sumW > 1e-6f ? sum / sumW : center;
            output[bin] = center + (filtered - center) * amount;
        }
    }

    private void UpdateWeights()
    {
        float spatialCoeff = -0.5f / (_sigmaSpatial * _sigmaSpatial);
        for (int dt = 0; dt <= _timeRadius; dt++)
        {
            float dist = dt;
            _timeWeights[dt] = MathF.Exp(dist * dist * spatialCoeff);
        }

        for (int df = -_freqRadius; df <= _freqRadius; df++)
        {
            float dist = df;
            _freqWeights[df + _freqRadius] = MathF.Exp(dist * dist * spatialCoeff);
        }
    }
}
