namespace HotMic.Core.Dsp.Spectrogram;

/// <summary>
/// Median-filter based harmonic/percussive separation for spectrogram cleanup.
/// </summary>
public sealed class SpectrogramHpssProcessor
{
    // Spec defaults (docs/technical/Cleanup.md##HPSS (Median Filtering)): 17-31 frame/bin kernels, mask power 2.0.
    private const int TimeKernel = 17;
    private const int FreqKernel = 17;
    private const float MaskPower = 2.0f;

    private float[] _history = Array.Empty<float>();
    private int _historyIndex;
    private int _historyCount;
    private float[] _timeScratch = Array.Empty<float>();
    private float[] _freqScratch = Array.Empty<float>();

    /// <summary>
    /// Ensure internal buffers are sized for the provided bin count.
    /// </summary>
    public void EnsureCapacity(int bins)
    {
        if (bins <= 0)
        {
            return;
        }

        int historySize = bins * TimeKernel;
        if (_history.Length != historySize)
        {
            _history = new float[historySize];
            _timeScratch = new float[TimeKernel];
            _freqScratch = new float[FreqKernel];
            Reset();
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
    /// Apply harmonic/percussive separation and write into the output buffer.
    /// </summary>
    public void Apply(float[] input, float[] output, float amount)
    {
        int bins = input.Length;
        if (bins == 0)
        {
            return;
        }

        if (amount <= 0f)
        {
            Array.Copy(input, output, bins);
            return;
        }

        int historyOffset = _historyIndex * bins;
        Array.Copy(input, 0, _history, historyOffset, bins);
        _historyIndex = (_historyIndex + 1) % TimeKernel;
        if (_historyCount < TimeKernel)
        {
            _historyCount++;
        }

        int timeCount = _historyCount;
        int halfFreq = FreqKernel / 2;
        bool maskPowIsTwo = MathF.Abs(MaskPower - 2f) < 1e-3f;

        for (int bin = 0; bin < bins; bin++)
        {
            // Horizontal (time) median preserves harmonics; vertical (freq) median captures percussive energy.
            for (int k = 0; k < timeCount; k++)
            {
                int index = (_historyIndex - 1 - k + TimeKernel) % TimeKernel;
                _timeScratch[k] = _history[index * bins + bin];
            }
            float harmonic = Median(_timeScratch, timeCount);

            int start = Math.Max(0, bin - halfFreq);
            int end = Math.Min(bins - 1, bin + halfFreq);
            int count = end - start + 1;
            for (int i = 0; i < count; i++)
            {
                _freqScratch[i] = input[start + i];
            }
            float percussive = Median(_freqScratch, count);

            float hPow = maskPowIsTwo ? harmonic * harmonic : MathF.Pow(harmonic, MaskPower);
            float pPow = maskPowIsTwo ? percussive * percussive : MathF.Pow(percussive, MaskPower);
            float mask = hPow / (hPow + pPow + 1e-12f);
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
