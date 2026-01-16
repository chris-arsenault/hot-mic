using HotMic.Core.Dsp.Analysis.Formants;
using Xunit;

namespace HotMic.Core.Tests;

/// <summary>
/// End-to-end integration tests for formant extraction pipeline.
/// Tests LPC -> Root Finding -> Formant Extraction as a whole.
/// </summary>
public class FormantExtractionIntegrationTests
{
    private static double NextUniform(ref uint state)
    {
        state = unchecked(state * 1664525u + 1013904223u);
        return (state + 1.0) / 4294967296.0;
    }

    private static float NextGaussian(ref uint state)
    {
        double u1 = NextUniform(ref state);
        double u2 = NextUniform(ref state);
        double value = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return (float)value;
    }

    [Fact]
    public void EndToEnd_SyntheticVowel_ProducesExpectedFormants()
    {
        // Deterministic synthetic vowel (no noise) for stable reference values.
        int sampleRate = 12000;
        int n = 512; // ~43ms at 12kHz

        // Target formants for /a/ vowel (approximate)
        float f1 = 700f;  // F1
        float f2 = 1200f; // F2
        float f3 = 2500f; // F3

        float[] signal = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sampleRate;
            signal[i] = (float)(
                1.0 * Math.Sin(2 * Math.PI * f1 * t) +
                0.7 * Math.Sin(2 * Math.PI * f2 * t) +
                0.4 * Math.Sin(2 * Math.PI * f3 * t)
            );
        }

        // Run LPC
        var lpc = new LpcAnalyzer(12);
        float[] coeffs = new float[13];
        bool lpcResult = lpc.Compute(signal, coeffs);
        Assert.True(lpcResult, "LPC should succeed on synthetic vowel");

        // Run formant tracking
        var tracker = new FormantTracker(12);
        float[] freqs = new float[5];
        float[] bws = new float[5];
        int count = tracker.Track(coeffs, sampleRate, freqs, bws, 100, 5500, 5);

        // Pre-computed with Python/NumPy (Burg LPC + roots + mag>=0.80 filter)
        Assert.Equal(5, count);
        Assert.InRange(freqs[0], 249f, 269f);
        Assert.InRange(bws[0], 92f, 114f);
        Assert.InRange(freqs[1], 754f, 774f);
        Assert.InRange(bws[1], 82f, 104f);
        Assert.InRange(freqs[2], 1226f, 1246f);
        Assert.InRange(bws[2], 65f, 87f);
        Assert.InRange(freqs[3], 1639f, 1659f);
        Assert.InRange(bws[3], 41f, 63f);
        Assert.InRange(freqs[4], 1966f, 1986f);
        Assert.InRange(bws[4], 16f, 36f);
    }

    [Fact]
    public void EndToEnd_SyntheticVowelWithNoise_ProducesExpectedFormants()
    {
        // Deterministic synthetic vowel with white noise (seed=9012, 0.1 amplitude).
        int sampleRate = 12000;
        int n = 512; // ~43ms at 12kHz

        float f1 = 700f;
        float f2 = 1200f;
        float f3 = 2500f;

        uint state = 9012;
        float[] signal = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sampleRate;
            float noise = 0.1f * NextGaussian(ref state);
            signal[i] = (float)(
                1.0 * Math.Sin(2 * Math.PI * f1 * t) +
                0.7 * Math.Sin(2 * Math.PI * f2 * t) +
                0.4 * Math.Sin(2 * Math.PI * f3 * t)
            ) + noise;
        }

        var lpc = new LpcAnalyzer(12);
        float[] coeffs = new float[13];
        bool lpcResult = lpc.Compute(signal, coeffs);
        Assert.True(lpcResult, "LPC should succeed on noisy synthetic vowel");

        var tracker = new FormantTracker(12);
        float[] freqs = new float[5];
        float[] bws = new float[5];
        int count = tracker.Track(coeffs, sampleRate, freqs, bws, 100, 5500, 5);

        // Pre-computed with Python/NumPy (Burg LPC + roots + mag>=0.80 filter)
        Assert.Equal(5, count);
        Assert.InRange(freqs[0], 255f, 275f);
        Assert.InRange(bws[0], 98f, 120f);
        Assert.InRange(freqs[1], 773f, 793f);
        Assert.InRange(bws[1], 87f, 109f);
        Assert.InRange(freqs[2], 1257f, 1277f);
        Assert.InRange(bws[2], 70f, 92f);
        Assert.InRange(freqs[3], 1681f, 1701f);
        Assert.InRange(bws[3], 44f, 66f);
        Assert.InRange(freqs[4], 2018f, 2038f);
        Assert.InRange(bws[4], 18f, 38f);
    }

    [Fact]
    public void EndToEnd_SyntheticVowelWithHeavyNoise_ProducesExpectedFormants()
    {
        // Deterministic synthetic vowel with heavier white noise (seed=9012, 0.2 amplitude).
        int sampleRate = 12000;
        int n = 512; // ~43ms at 12kHz

        float f1 = 700f;
        float f2 = 1200f;
        float f3 = 2500f;

        uint state = 9012;
        float[] signal = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sampleRate;
            float noise = 0.2f * NextGaussian(ref state);
            signal[i] = (float)(
                1.0 * Math.Sin(2 * Math.PI * f1 * t) +
                0.7 * Math.Sin(2 * Math.PI * f2 * t) +
                0.4 * Math.Sin(2 * Math.PI * f3 * t)
            ) + noise;
        }

        var lpc = new LpcAnalyzer(12);
        float[] coeffs = new float[13];
        bool lpcResult = lpc.Compute(signal, coeffs);
        Assert.True(lpcResult, "LPC should succeed on noisy synthetic vowel");

        var tracker = new FormantTracker(12);
        float[] freqs = new float[5];
        float[] bws = new float[5];
        int count = tracker.Track(coeffs, sampleRate, freqs, bws, 100, 5500, 5);

        // Pre-computed with Python/NumPy (Burg LPC + roots + mag>=0.80 filter)
        Assert.Equal(5, count);
        Assert.InRange(freqs[0], 269f, 299f);
        Assert.InRange(bws[0], 113f, 143f);
        Assert.InRange(freqs[1], 826f, 856f);
        Assert.InRange(bws[1], 101f, 131f);
        Assert.InRange(freqs[2], 1348f, 1378f);
        Assert.InRange(bws[2], 80f, 110f);
        Assert.InRange(freqs[3], 1809f, 1839f);
        Assert.InRange(bws[3], 50f, 80f);
        Assert.InRange(freqs[4], 2176f, 2206f);
        Assert.InRange(bws[4], 19f, 49f);
    }
}
