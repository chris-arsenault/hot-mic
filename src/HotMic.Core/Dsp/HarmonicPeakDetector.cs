namespace HotMic.Core.Dsp;

/// <summary>
/// Detects harmonic peak frequencies around multiples of a fundamental.
/// </summary>
public static class HarmonicPeakDetector
{
    /// <summary>
    /// Finds harmonic peak frequencies near expected multiples of the fundamental.
    /// </summary>
    public static int Detect(ReadOnlySpan<float> magnitudes, int sampleRate, int fftSize,
        float fundamentalHz, Span<float> harmonicFrequencies, float tolerance = 0.03f)
    {
        if (magnitudes.IsEmpty || harmonicFrequencies.IsEmpty || fundamentalHz <= 0f)
        {
            return 0;
        }

        float binResolution = sampleRate / (float)fftSize;
        float nyquist = sampleRate * 0.5f;
        int count = 0;

        for (int h = 1; h <= harmonicFrequencies.Length; h++)
        {
            float expected = fundamentalHz * h;
            if (expected >= nyquist)
            {
                break;
            }

            float delta = expected * tolerance;
            int startBin = (int)MathF.Max(1f, (expected - delta) / binResolution);
            int endBin = (int)MathF.Min(magnitudes.Length - 1, (expected + delta) / binResolution);

            int bestBin = startBin;
            float bestMag = 0f;
            for (int bin = startBin; bin <= endBin; bin++)
            {
                float mag = magnitudes[bin];
                if (mag > bestMag)
                {
                    bestMag = mag;
                    bestBin = bin;
                }
            }

            harmonicFrequencies[count] = bestBin * binResolution;
            count++;
        }

        return count;
    }
}
