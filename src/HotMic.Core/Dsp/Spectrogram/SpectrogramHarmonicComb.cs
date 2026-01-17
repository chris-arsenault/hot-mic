using HotMic.Core.Dsp.Analysis;

namespace HotMic.Core.Dsp.Spectrogram;

/// <summary>
/// Harmonic comb filter for spectrogram processing.
/// Enhances harmonic structure based on detected fundamental frequency.
/// </summary>
public sealed class SpectrogramHarmonicComb
{
    private float[] _harmonicMask = Array.Empty<float>();
    private int _capacity;

    /// <summary>
    /// Ensure internal buffers have sufficient capacity.
    /// </summary>
    public void EnsureCapacity(int bins)
    {
        if (_capacity >= bins)
            return;

        _harmonicMask = new float[bins];
        _capacity = bins;
    }

    /// <summary>
    /// Update the harmonic mask based on the detected pitch.
    /// </summary>
    /// <param name="fundamentalHz">Detected fundamental frequency in Hz.</param>
    /// <param name="confidence">Pitch confidence (0-1).</param>
    /// <param name="voicingState">Current voicing state.</param>
    /// <param name="binResolution">Hz per FFT bin.</param>
    /// <param name="bins">Number of bins to process.</param>
    public void UpdateMaskLinear(
        float fundamentalHz,
        float confidence,
        VoicingState voicingState,
        float binResolution,
        int bins)
    {
        EnsureCapacity(bins);

        // Clear mask if not voiced or low confidence
        if (voicingState != VoicingState.Voiced || confidence < 0.3f || fundamentalHz <= 0f)
        {
            Array.Clear(_harmonicMask, 0, bins);
            return;
        }

        // Build harmonic comb mask
        float f0Bin = fundamentalHz / binResolution;
        float harmonicWidth = MathF.Max(1f, f0Bin * 0.05f); // 5% of harmonic frequency

        for (int i = 0; i < bins; i++)
        {
            float bestMatch = 0f;

            // Check harmonics 1-16
            for (int h = 1; h <= 16; h++)
            {
                float harmonicBin = f0Bin * h;
                if (harmonicBin >= bins)
                    break;

                float distance = MathF.Abs(i - harmonicBin);
                if (distance < harmonicWidth)
                {
                    // Gaussian weighting centered on harmonic
                    float weight = MathF.Exp(-distance * distance / (2f * harmonicWidth * harmonicWidth));
                    // Harmonic amplitude falloff
                    weight *= 1f / h;
                    bestMatch = MathF.Max(bestMatch, weight);
                }
            }

            _harmonicMask[i] = bestMatch * confidence;
        }
    }

    /// <summary>
    /// Apply harmonic enhancement and compute HNR.
    /// </summary>
    /// <param name="spectrum">Input spectrum magnitude (modified in place for harmonic enhancement).</param>
    /// <param name="harmonicAmount">Amount of harmonic enhancement (0-1).</param>
    /// <param name="bins">Number of bins to process.</param>
    /// <returns>Estimated HNR (Harmonic-to-Noise Ratio) in dB.</returns>
    public float Apply(Span<float> spectrum, float harmonicAmount, int bins)
    {
        if (_harmonicMask.Length < bins || spectrum.Length < bins)
            return 0f;

        float harmonicEnergy = 0f;
        float noiseEnergy = 0f;

        for (int i = 0; i < bins; i++)
        {
            float mag = spectrum[i];
            float mask = _harmonicMask[i];

            // Separate harmonic and noise components
            float harmonic = mag * mask;
            float noise = mag * (1f - mask);

            harmonicEnergy += harmonic * harmonic;
            noiseEnergy += noise * noise;

            // Apply harmonic enhancement - blend towards harmonic component
            spectrum[i] = mag + (harmonic - mag) * harmonicAmount * mask;
        }

        // Compute HNR in dB
        if (noiseEnergy < 1e-10f)
            return 40f; // Very clean signal

        float hnr = 10f * MathF.Log10(harmonicEnergy / noiseEnergy + 1e-10f);
        return Math.Clamp(hnr, -20f, 40f);
    }
}
