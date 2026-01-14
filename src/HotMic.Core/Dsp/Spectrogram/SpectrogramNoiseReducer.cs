namespace HotMic.Core.Dsp.Spectrogram;

/// <summary>
/// Adaptive noise floor estimator with soft spectral subtraction for spectrogram clarity.
/// </summary>
public sealed class SpectrogramNoiseReducer
{
    // Spec defaults (docs/technical/Cleanup.md##Noise Reduction): 10th percentile, 2.0x gate, slow/fast adaptation.
    private const int InitSamples = 5;
    private const int MarkerCount = 5;
    private const float Percentile = 0.1f;
    private const float GateMultiplier = 2.0f;
    private const float AdaptFast = 0.2f;
    private const float AdaptSlow = 0.02f;
    private const float OverSubtractionMin = 1.2f;
    private const float OverSubtractionMax = 2.2f;
    private const float FloorMin = 0.01f;
    private const float FloorMax = 0.02f;

    private float[] _estimate = Array.Empty<float>();
    private float[] _initHistory = Array.Empty<float>();
    private float[] _markers = Array.Empty<float>();
    private int[] _markerPositions = Array.Empty<int>();
    private float[] _desiredPositions = Array.Empty<float>();
    private float[] _scratch = Array.Empty<float>();
    private int _initCount;

    private static readonly float[] MarkerDeltas =
    [
        0f,
        Percentile * 0.5f,
        Percentile,
        (1f + Percentile) * 0.5f,
        1f
    ];

    /// <summary>
    /// Ensure internal buffers are sized for the provided bin count.
    /// </summary>
    public void EnsureCapacity(int bins)
    {
        if (bins <= 0)
        {
            return;
        }

        if (_estimate.Length != bins)
        {
            _estimate = new float[bins];
        }

        int initSize = bins * InitSamples;
        if (_initHistory.Length != initSize)
        {
            _initHistory = new float[initSize];
        }

        int markerSize = bins * MarkerCount;
        if (_markers.Length != markerSize)
        {
            _markers = new float[markerSize];
            _markerPositions = new int[markerSize];
            _desiredPositions = new float[markerSize];
        }

        if (_scratch.Length != InitSamples)
        {
            _scratch = new float[InitSamples];
        }

        Reset();
    }

    /// <summary>
    /// Clear the internal history and noise estimate.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_estimate, 0, _estimate.Length);
        Array.Clear(_initHistory, 0, _initHistory.Length);
        Array.Clear(_markers, 0, _markers.Length);
        Array.Clear(_markerPositions, 0, _markerPositions.Length);
        Array.Clear(_desiredPositions, 0, _desiredPositions.Length);
        _initCount = 0;
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

        float rate = voicing == VoicingState.Silence ? AdaptFast : AdaptSlow;
        if (!UpdateEstimate(magnitudes, bins, rate))
        {
            return;
        }

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

    private bool UpdateEstimate(ReadOnlySpan<float> magnitudes, int bins, float rate)
    {
        if (bins <= 0 || _estimate.Length < bins)
        {
            return false;
        }

        if (_initCount < InitSamples)
        {
            int offset = _initCount * bins;
            for (int i = 0; i < bins; i++)
            {
                float value = magnitudes[i];
                _initHistory[offset + i] = value * value;
            }

            _initCount++;
            if (_initCount < InitSamples)
            {
                return false;
            }

            InitializeQuantiles(bins);
            return true;
        }

        for (int bin = 0; bin < bins; bin++)
        {
            float value = magnitudes[bin];
            float power = value * value;
            float target = UpdateQuantile(bin, power);
            float estimate = _estimate[bin];
            estimate += (target - estimate) * rate;
            _estimate[bin] = estimate;
        }

        return true;
    }

    private void InitializeQuantiles(int bins)
    {
        for (int bin = 0; bin < bins; bin++)
        {
            for (int i = 0; i < InitSamples; i++)
            {
                _scratch[i] = _initHistory[i * bins + bin];
            }

            Array.Sort(_scratch, 0, InitSamples);
            int offset = bin * MarkerCount;
            for (int i = 0; i < MarkerCount; i++)
            {
                _markers[offset + i] = _scratch[i];
                _markerPositions[offset + i] = i + 1;
            }

            _desiredPositions[offset] = 1f;
            _desiredPositions[offset + 1] = 1f + 2f * Percentile;
            _desiredPositions[offset + 2] = 1f + 4f * Percentile;
            _desiredPositions[offset + 3] = 3f + 2f * Percentile;
            _desiredPositions[offset + 4] = 5f;

            _estimate[bin] = _markers[offset + 2];
        }
    }

    private float UpdateQuantile(int bin, float sample)
    {
        // P^2 streaming quantile update (Jain & Chlamtac, 1985).
        int offset = bin * MarkerCount;

        int k;
        if (sample < _markers[offset])
        {
            _markers[offset] = sample;
            k = 0;
        }
        else if (sample < _markers[offset + 1])
        {
            k = 0;
        }
        else if (sample < _markers[offset + 2])
        {
            k = 1;
        }
        else if (sample < _markers[offset + 3])
        {
            k = 2;
        }
        else if (sample < _markers[offset + 4])
        {
            k = 3;
        }
        else
        {
            _markers[offset + 4] = sample;
            k = 3;
        }

        for (int i = k + 1; i < MarkerCount; i++)
        {
            _markerPositions[offset + i]++;
        }

        for (int i = 0; i < MarkerCount; i++)
        {
            _desiredPositions[offset + i] += MarkerDeltas[i];
        }

        for (int i = 1; i <= 3; i++)
        {
            float d = _desiredPositions[offset + i] - _markerPositions[offset + i];
            if ((d >= 1f && _markerPositions[offset + i + 1] - _markerPositions[offset + i] > 1)
                || (d <= -1f && _markerPositions[offset + i - 1] - _markerPositions[offset + i] < -1))
            {
                int sign = d >= 0f ? 1 : -1;
                int n0 = _markerPositions[offset + i - 1];
                int n1 = _markerPositions[offset + i];
                int n2 = _markerPositions[offset + i + 1];
                float q0 = _markers[offset + i - 1];
                float q1 = _markers[offset + i];
                float q2 = _markers[offset + i + 1];

                double d1 = d;
                double qNew = q1 + d1 / (n2 - n0) * ((n1 - n0 + d1) * (q2 - q1) / (n2 - n1)
                                                     + (n2 - n1 - d1) * (q1 - q0) / (n1 - n0));

                if (qNew <= q0 || qNew >= q2)
                {
                    int nSign = _markerPositions[offset + i + sign];
                    float qSign = _markers[offset + i + sign];
                    qNew = q1 + sign * (qSign - q1) / (nSign - n1);
                }

                _markers[offset + i] = (float)qNew;
                _markerPositions[offset + i] = n1 + sign;
            }
        }

        return _markers[offset + 2];
    }
}
