namespace HotMic.Core.Dsp.Spectrogram;

/// <summary>
/// Median-filter based harmonic/percussive separation for spectrogram cleanup.
/// </summary>
public sealed class SpectrogramHpssProcessor
{
    // Spec defaults (docs/technical/Cleanup.md##HPSS (Median Filtering)): 17-31 frame/bin kernels, mask power 2.0.
    private const int TimeKernel = 17;
    private const int FreqKernel = 17;
    private const int TimeDownsample = 2;
    private const int FreqDownsample = 2;
    private const float MaskPower = 2.0f;

    private readonly int _timeKernel;
    private readonly int _freqKernel;

    private float[] _history = Array.Empty<float>();
    private int _historyIndex;
    private int _historyCount;
    private float[] _timeScratch = Array.Empty<float>();
    private float[] _freqScratch = Array.Empty<float>();
    private float[] _downsampled = Array.Empty<float>();
    private float[] _mask = Array.Empty<float>();
    private float[] _timeAccum = Array.Empty<float>();
    private int _timeAccumCount;
    private int _reducedBins;

    public SpectrogramHpssProcessor()
    {
        _timeKernel = Math.Max(3, (TimeKernel + TimeDownsample - 1) / TimeDownsample);
        _freqKernel = Math.Max(3, (FreqKernel + FreqDownsample - 1) / FreqDownsample);
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

        int reducedBins = (bins + FreqDownsample - 1) / FreqDownsample;
        int historySize = reducedBins * _timeKernel;
        if (_history.Length != historySize || _reducedBins != reducedBins)
        {
            _history = new float[historySize];
            _timeScratch = new float[_timeKernel + 1];
            _freqScratch = new float[_freqKernel];
            _downsampled = new float[reducedBins];
            _mask = new float[reducedBins];
            _timeAccum = new float[reducedBins];
            _reducedBins = reducedBins;
            Reset();
        }
    }

    /// <summary>
    /// Clear the internal history.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_history, 0, _history.Length);
        Array.Clear(_downsampled, 0, _downsampled.Length);
        Array.Clear(_mask, 0, _mask.Length);
        Array.Clear(_timeAccum, 0, _timeAccum.Length);
        _historyIndex = 0;
        _historyCount = 0;
        _timeAccumCount = 0;
    }

    /// <summary>
    /// Apply harmonic/percussive separation and write into the output buffer.
    /// </summary>
    public void Apply(float[] input, float[] output, float amount)
    {
        Apply(input, output, amount, input.Length);
    }

    /// <summary>
    /// Apply harmonic/percussive separation with explicit bin count.
    /// </summary>
    public void Apply(float[] input, float[] output, float amount, int bins)
    {
        if (bins <= 0 || bins > input.Length)
        {
            return;
        }

        if (amount <= 0f)
        {
            Array.Copy(input, output, bins);
            return;
        }

        int reducedBins = _reducedBins;
        int halfFreq = _freqKernel / 2;
        bool maskPowIsTwo = MathF.Abs(MaskPower - 2f) < 1e-3f;

        // Downsample frequency before HPSS to reduce median-filter cost.
        for (int bin = 0; bin < reducedBins; bin++)
        {
            int start = bin * FreqDownsample;
            int end = Math.Min(bins, start + FreqDownsample);
            float sum = 0f;
            for (int i = start; i < end; i++)
            {
                sum += input[i];
            }
            _downsampled[bin] = sum / MathF.Max(1, end - start);
        }

        for (int i = 0; i < reducedBins; i++)
        {
            _timeAccum[i] += _downsampled[i];
        }
        _timeAccumCount++;
        if (_timeAccumCount >= TimeDownsample)
        {
            float inv = 1f / _timeAccumCount;
            int historyOffset = _historyIndex * reducedBins;
            for (int i = 0; i < reducedBins; i++)
            {
                _history[historyOffset + i] = _timeAccum[i] * inv;
                _timeAccum[i] = 0f;
            }

            _historyIndex = (_historyIndex + 1) % _timeKernel;
            if (_historyCount < _timeKernel)
            {
                _historyCount++;
            }
            _timeAccumCount = 0;
        }

        int timeCount = _historyCount;
        for (int bin = 0; bin < reducedBins; bin++)
        {
            // Horizontal (time) median preserves harmonics; vertical (freq) median captures percussive energy.
            _timeScratch[0] = _downsampled[bin];
            int samples = Math.Min(timeCount + 1, _timeScratch.Length);
            for (int k = 1; k < samples; k++)
            {
                int index = (_historyIndex - k + _timeKernel) % _timeKernel;
                _timeScratch[k] = _history[index * reducedBins + bin];
            }
            float harmonic = Median(_timeScratch, samples);

            int start = Math.Max(0, bin - halfFreq);
            int end = Math.Min(reducedBins - 1, bin + halfFreq);
            int count = end - start + 1;
            for (int i = 0; i < count; i++)
            {
                _freqScratch[i] = _downsampled[start + i];
            }
            float percussive = Median(_freqScratch, count);

            float hPow = maskPowIsTwo ? harmonic * harmonic : MathF.Pow(harmonic, MaskPower);
            float pPow = maskPowIsTwo ? percussive * percussive : MathF.Pow(percussive, MaskPower);
            float mask = hPow / (hPow + pPow + 1e-12f);
            _mask[bin] = mask;
        }

        for (int bin = 0; bin < bins; bin++)
        {
            int reduced = Math.Min(reducedBins - 1, bin / FreqDownsample);
            float mask = _mask[reduced];
            float gain = (1f - amount) + mask * amount;
            output[bin] = input[bin] * gain;
        }
    }

    private static float Median(float[] values, int count)
    {
        if (count <= 0)
        {
            return 0f;
        }

        for (int i = 1; i < count; i++)
        {
            float key = values[i];
            int j = i - 1;
            while (j >= 0 && values[j] > key)
            {
                values[j + 1] = values[j];
                j--;
            }
            values[j + 1] = key;
        }

        return values[count / 2];
    }
}
