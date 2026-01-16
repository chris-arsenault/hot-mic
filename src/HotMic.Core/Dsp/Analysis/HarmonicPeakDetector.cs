using HotMic.Core.Dsp.Spectrogram;

namespace HotMic.Core.Dsp.Analysis;

/// <summary>
/// Detects harmonic peak frequencies around multiples of a fundamental.
/// </summary>
public static class HarmonicPeakDetector
{
    /// <summary>
    /// Default threshold in dB relative to fundamental for harmonic detection.
    /// Harmonics must be within this many dB of the fundamental to be considered "detected".
    /// </summary>
    public const float DefaultThresholdDb = -40f;

    /// <summary>
    /// Default SNR threshold in dB above local noise floor for harmonic detection.
    /// </summary>
    public const float DefaultSnrThresholdDb = 6f;

    /// <summary>
    /// Minimum absolute magnitude for fundamental to be considered valid.
    /// If fundamental is below this, pitch detection is likely wrong.
    /// </summary>
    private const float MinFundamentalMagnitude = 1e-4f;

    /// <summary>
    /// Finds harmonic peak frequencies and magnitudes using the analysis descriptor.
    /// Works with any transform type (FFT, ZoomFFT, CQT) by using the descriptor's frequency mapping.
    /// </summary>
    /// <param name="magnitudes">Magnitude spectrum from the active transform (linear scale).</param>
    /// <param name="descriptor">Analysis descriptor providing bin-to-frequency mapping.</param>
    /// <param name="fundamentalHz">Detected fundamental frequency.</param>
    /// <param name="harmonicFrequencies">Output: frequency of each harmonic slot (0 if not detected).</param>
    /// <param name="harmonicMagnitudes">Output: magnitude in dB relative to fundamental (MinValue if not detected).</param>
    /// <param name="tolerance">Frequency tolerance as fraction of expected harmonic (default 3%).</param>
    /// <returns>Number of harmonic slots populated.</returns>
    public static int Detect(
        ReadOnlySpan<float> magnitudes,
        SpectrogramAnalysisDescriptor descriptor,
        float fundamentalHz,
        Span<float> harmonicFrequencies,
        Span<float> harmonicMagnitudes,
        float tolerance = 0.03f)
    {
        if (magnitudes.IsEmpty || harmonicFrequencies.IsEmpty || fundamentalHz <= 0f || descriptor.BinCount <= 0)
        {
            return 0;
        }

        int maxHarmonics = Math.Min(harmonicFrequencies.Length, harmonicMagnitudes.Length);
        float maxFrequency = descriptor.MaxFrequencyHz;
        int count = 0;

        // Find fundamental magnitude using descriptor's frequency lookup
        var (_, fundamentalMag, _) = descriptor.FindPeakNear(magnitudes, fundamentalHz, tolerance);

        // If fundamental magnitude is too low, pitch detection is likely wrong - don't trust harmonics
        if (fundamentalMag < MinFundamentalMagnitude)
        {
            for (int i = 0; i < maxHarmonics; i++)
            {
                harmonicFrequencies[i] = 0f;
                harmonicMagnitudes[i] = float.MinValue;
            }
            return maxHarmonics;
        }

        for (int h = 1; h <= maxHarmonics; h++)
        {
            float expected = fundamentalHz * h;
            if (expected >= maxFrequency)
            {
                // Fill remaining slots with zeros
                for (int i = count; i < maxHarmonics; i++)
                {
                    harmonicFrequencies[i] = 0f;
                    harmonicMagnitudes[i] = float.MinValue;
                }
                break;
            }

            // Use descriptor to find peak near expected harmonic frequency
            var (peakBin, peakMag, peakFreq) = descriptor.FindPeakNear(magnitudes, expected, tolerance);

            // Calculate magnitude in dB relative to fundamental
            float relativeDb = 20f * MathF.Log10(peakMag / fundamentalMag + 1e-10f);

            // Only report harmonic if peak has significant absolute magnitude
            bool isSignificant = peakMag >= MinFundamentalMagnitude * 0.1f;

            harmonicFrequencies[count] = isSignificant && peakFreq > 0f ? peakFreq : 0f;
            harmonicMagnitudes[count] = isSignificant ? relativeDb : float.MinValue;
            count++;
        }

        return count;
    }
}
