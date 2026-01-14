namespace HotMic.Core.Dsp.Spectrogram;

/// <summary>
/// Applies a harmonic comb mask to emphasize integer multiples of the detected pitch.
/// </summary>
public sealed class HarmonicCombEnhancer
{
    private readonly int _maxHarmonics;

    // Spec defaults (docs/technical/Cleanup.md##Harmonic Comb): +/-50 cents tolerance, 1.0-1.5x boost, 0.1-0.3x attenuation.
    private const float HarmonicBoost = 1.35f;
    private const float HarmonicAttenuation = 0.25f;
    private const float ConfidenceThreshold = 0.35f;
    private const float ToleranceCents = 50f;

    private float[] _mask = Array.Empty<float>();
    private bool _maskActive;

    public HarmonicCombEnhancer(int maxHarmonics)
    {
        _maxHarmonics = Math.Max(1, maxHarmonics);
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

        if (_mask.Length != bins)
        {
            _mask = new float[bins];
            _maskActive = false;
        }
    }

    /// <summary>
    /// Clear the internal harmonic mask.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_mask, 0, _mask.Length);
        _maskActive = false;
    }

    /// <summary>
    /// Update the harmonic comb mask using the latest pitch estimate.
    /// </summary>
    public void UpdateMask(float pitchHz, float confidence, VoicingState voicing,
        FrequencyScale scale, float minFrequency, float maxFrequency,
        float scaledMin, float scaledRange, int displayBins)
    {
        if (pitchHz <= 0f || confidence < ConfidenceThreshold || voicing != VoicingState.Voiced)
        {
            if (_maskActive)
            {
                Array.Clear(_mask, 0, _mask.Length);
                _maskActive = false;
            }
            return;
        }

        Array.Clear(_mask, 0, _mask.Length);
        _maskActive = true;

        float ratio = MathF.Pow(2f, ToleranceCents / 1200f);
        float maxPos = Math.Max(1f, displayBins - 1);
        for (int harmonic = 1; harmonic <= _maxHarmonics; harmonic++)
        {
            float frequency = pitchHz * harmonic;
            if (frequency > maxFrequency)
            {
                break;
            }

            float startFreq = MathF.Max(minFrequency, frequency / ratio);
            float endFreq = MathF.Min(maxFrequency, frequency * ratio);
            float centerPos = GetDisplayPosition(scale, minFrequency, maxFrequency, scaledMin, scaledRange, frequency, maxPos);
            float startPos = GetDisplayPosition(scale, minFrequency, maxFrequency, scaledMin, scaledRange, startFreq, maxPos);
            float endPos = GetDisplayPosition(scale, minFrequency, maxFrequency, scaledMin, scaledRange, endFreq, maxPos);

            int startBin = Math.Clamp((int)MathF.Floor(startPos), 0, displayBins - 1);
            int endBin = Math.Clamp((int)MathF.Ceiling(endPos), 0, displayBins - 1);
            float halfSpan = MathF.Max(1f, (endBin - startBin) * 0.5f);

            for (int bin = startBin; bin <= endBin; bin++)
            {
                float dist = MathF.Abs(bin - centerPos);
                float weight = 1f - dist / halfSpan;
                if (weight > _mask[bin])
                {
                    _mask[bin] = Math.Clamp(weight, 0f, 1f);
                }
            }
        }
    }

    /// <summary>
    /// Apply the harmonic comb to the magnitude spectrum and return the HNR estimate.
    /// </summary>
    public float Apply(float[] magnitudes, float amount)
    {
        if (!_maskActive || magnitudes.Length == 0)
        {
            return 0f;
        }

        float harmonicEnergy = 0f;
        float noiseEnergy = 0f;

        for (int i = 0; i < magnitudes.Length; i++)
        {
            float mask = _mask[i];
            float value = magnitudes[i];
            float power = value * value;
            harmonicEnergy += power * mask;
            noiseEnergy += power * (1f - mask);

            float targetGain = HarmonicAttenuation + (HarmonicBoost - HarmonicAttenuation) * mask;
            float gain = 1f + (targetGain - 1f) * amount;
            magnitudes[i] = value * gain;
        }

        return 10f * MathF.Log10((harmonicEnergy + 1e-9f) / (noiseEnergy + 1e-9f));
    }

    private static float GetDisplayPosition(
        FrequencyScale scale,
        float minFrequency,
        float maxFrequency,
        float scaledMin,
        float scaledRange,
        float frequency,
        float maxPos)
    {
        if (scaledRange <= 0f)
        {
            return 0f;
        }

        float clamped = Math.Clamp(frequency, minFrequency, maxFrequency);
        float scaled = FrequencyScaleUtils.ToScale(scale, clamped);
        float norm = (scaled - scaledMin) / scaledRange;
        return Math.Clamp(norm * maxPos, 0f, maxPos);
    }
}
