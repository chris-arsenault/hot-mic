namespace HotMic.Core.Dsp.Spectrogram;

/// <summary>
/// Adaptive noise floor estimator with soft spectral subtraction for spectrogram clarity.
/// </summary>
public sealed class SpectrogramNoiseReducer
{
    // Spec defaults (docs/technical/Cleanup.md##Noise Reduction): 50-100 frames, 10th percentile, 2.0x gate, slow/fast adaptation.
    private const int HistoryLength = 64;
    private const float Percentile = 0.1f;
    private const float GateMultiplier = 2.0f;
    private const float AdaptFast = 0.2f;
    private const float AdaptSlow = 0.02f;
    private const float OverSubtractionMin = 1.2f;
    private const float OverSubtractionMax = 2.2f;
    private const float FloorMin = 0.01f;
    private const float FloorMax = 0.02f;

    private float[] _history = Array.Empty<float>();
    private float[] _estimate = Array.Empty<float>();
    private float[] _scratch = Array.Empty<float>();
    private int _historyIndex;
    private int _historyCount;

    /// <summary>
    /// Ensure internal buffers are sized for the provided bin count.
    /// </summary>
    public void EnsureCapacity(int bins)
    {
        if (bins <= 0)
        {
            return;
        }

        int historySize = bins * HistoryLength;
        if (_history.Length != historySize)
        {
            _history = new float[historySize];
            _estimate = new float[bins];
            _scratch = new float[HistoryLength];
            Reset();
        }
        else if (_estimate.Length != bins)
        {
            _estimate = new float[bins];
        }
    }

    /// <summary>
    /// Clear the internal history and noise estimate.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_history, 0, _history.Length);
        Array.Clear(_estimate, 0, _estimate.Length);
        _historyIndex = 0;
        _historyCount = 0;
    }

    /// <summary>
    /// Apply adaptive noise reduction to the magnitude spectrum.
    /// </summary>
    public void Apply(float[] magnitudes, float amount, VoicingState voicing)
    {
        int bins = magnitudes.Length;
        if (bins == 0 || amount <= 0f)
        {
            return;
        }

        // Work in power domain to mirror the spectral-clarity spec subtraction formula.
        int historyOffset = _historyIndex * bins;
        for (int i = 0; i < bins; i++)
        {
            float value = magnitudes[i];
            _history[historyOffset + i] = value * value;
        }

        _historyIndex = (_historyIndex + 1) % HistoryLength;
        if (_historyCount < HistoryLength)
        {
            _historyCount++;
        }

        UpdateEstimate(voicing, bins);

        float alpha = OverSubtractionMin + (OverSubtractionMax - OverSubtractionMin) * amount;
        float beta = FloorMax + (FloorMin - FloorMax) * amount;

        for (int i = 0; i < bins; i++)
        {
            float value = magnitudes[i];
            float power = value * value;
            float noisePower = _estimate[i];
            float threshold = noisePower * GateMultiplier;

            float cleanPower = power - alpha * noisePower;
            float floor = beta * power;
            if (cleanPower < floor)
            {
                cleanPower = floor;
            }

            if (power < threshold && threshold > 1e-12f)
            {
                cleanPower *= power / threshold;
            }

            float processed = MathF.Sqrt(MathF.Max(cleanPower, 0f));
            magnitudes[i] = value + (processed - value) * amount;
        }
    }

    private void UpdateEstimate(VoicingState voicing, int bins)
    {
        int count = _historyCount;
        if (count <= 0 || bins <= 0)
        {
            return;
        }

        int percentileIndex = Math.Clamp((int)MathF.Floor((count - 1) * Percentile), 0, count - 1);
        float rate = voicing == VoicingState.Silence ? AdaptFast : AdaptSlow;

        for (int bin = 0; bin < bins; bin++)
        {
            for (int i = 0; i < count; i++)
            {
                _scratch[i] = _history[i * bins + bin];
            }

            Array.Sort(_scratch, 0, count);
            float target = _scratch[percentileIndex];
            float estimate = _estimate[bin];
            estimate += (target - estimate) * rate;
            _estimate[bin] = estimate;
        }
    }
}
