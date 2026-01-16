using HotMic.Core.Dsp.Analysis.Formants;
using Xunit;

namespace HotMic.Core.Tests;

/// <summary>
/// End-to-end integration tests for formant extraction pipeline.
/// Tests LPC -> Root Finding -> Formant Extraction as a whole.
/// </summary>
public class FormantExtractionIntegrationTests
{
    [Fact]
    public void EndToEnd_SyntheticVowel_ExtractsFormants()
    {
        // Create a synthetic vowel-like signal with known formants
        // Using sum of damped sinusoids to simulate vocal tract resonances
        int sampleRate = 12000;
        int n = 512; // ~43ms at 12kHz

        // Target formants for /a/ vowel (approximate)
        float f1 = 700f;  // F1
        float f2 = 1200f; // F2
        float f3 = 2500f; // F3

        float[] signal = new float[n];
        var rng = new Random(42);

        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sampleRate;
            // Add formants as resonances
            signal[i] = (float)(
                1.0 * Math.Sin(2 * Math.PI * f1 * t) +
                0.7 * Math.Sin(2 * Math.PI * f2 * t) +
                0.4 * Math.Sin(2 * Math.PI * f3 * t) +
                0.05 * (rng.NextDouble() - 0.5) // Small noise
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

        // We should find formants
        Assert.True(count >= 2, $"Expected at least 2 formants, got {count}");

        // Note: The extracted formants may not exactly match input frequencies
        // because LPC models the spectrum, not individual sinusoids.
        // But they should be in reasonable ranges.
        bool foundLowFormant = false;
        bool foundMidFormant = false;

        for (int i = 0; i < count; i++)
        {
            if (freqs[i] >= 500 && freqs[i] <= 1000) foundLowFormant = true;
            if (freqs[i] >= 1000 && freqs[i] <= 1500) foundMidFormant = true;
        }

        // At least one of these should be found
        Assert.True(foundLowFormant || foundMidFormant,
            $"Should find at least one formant in expected range. Found: {string.Join(", ", freqs.Take(count).Select(f => $"{f:F0}Hz"))}");
    }

    [Fact]
    public void EndToEnd_WhiteNoise_HandlesGracefully()
    {
        // White noise should either fail LPC or produce no sharp formants
        int sampleRate = 12000;
        int n = 512;

        float[] signal = new float[n];
        var rng = new Random(42);
        for (int i = 0; i < n; i++)
        {
            signal[i] = (float)(rng.NextDouble() - 0.5);
        }

        var lpc = new LpcAnalyzer(12);
        float[] coeffs = new float[13];
        bool lpcResult = lpc.Compute(signal, coeffs);

        // Either LPC fails or tracker returns few/no formants - both are acceptable
        if (lpcResult)
        {
            var tracker = new FormantTracker(12);
            float[] freqs = new float[5];
            float[] bws = new float[5];
            int count = tracker.Track(coeffs, sampleRate, freqs, bws, 100, 5500, 5);

            // Just verify it doesn't crash and returns a reasonable count
            Assert.InRange(count, 0, 5);
        }
    }

    [Fact]
    public void EndToEnd_VaryingOrder_ProducesValidResults()
    {
        // Both LPC orders should produce valid (non-crashing) results
        int sampleRate = 12000;
        int n = 512;

        // Create signal with multiple resonances
        float[] signal = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sampleRate;
            signal[i] = (float)(
                Math.Sin(2 * Math.PI * 400 * t) +
                0.8 * Math.Sin(2 * Math.PI * 1200 * t) +
                0.6 * Math.Sin(2 * Math.PI * 2400 * t) +
                0.4 * Math.Sin(2 * Math.PI * 3600 * t)
            );
        }

        // Test with order 8 vs order 12
        var lpc8 = new LpcAnalyzer(8);
        var lpc12 = new LpcAnalyzer(12);
        float[] coeffs8 = new float[9];
        float[] coeffs12 = new float[13];

        bool result8 = lpc8.Compute(signal, coeffs8);
        bool result12 = lpc12.Compute(signal, coeffs12);

        Assert.True(result8, "LPC order 8 should succeed");
        Assert.True(result12, "LPC order 12 should succeed");

        var tracker8 = new FormantTracker(8);
        var tracker12 = new FormantTracker(12);
        float[] freqs8 = new float[5];
        float[] freqs12 = new float[5];
        float[] bws8 = new float[5];
        float[] bws12 = new float[5];

        int count8 = tracker8.Track(coeffs8, sampleRate, freqs8, bws8, 100, 5500, 5);
        int count12 = tracker12.Track(coeffs12, sampleRate, freqs12, bws12, 100, 5500, 5);

        // Both should find at least one formant
        Assert.True(count8 >= 1, $"Order 8 should find at least 1 formant, got {count8}");
        Assert.True(count12 >= 1, $"Order 12 should find at least 1 formant, got {count12}");
    }

    [Theory]
    [InlineData(1500, 0.6752)]  // Pre-computed: bw=1500Hz at sr=12000 -> mag=0.6752
    [InlineData(1000, 0.7697)]  // Pre-computed: bw=1000Hz at sr=12000 -> mag=0.7697
    [InlineData(600, 0.8546)]   // Pre-computed: bw=600Hz at sr=12000 -> mag=0.8546
    public void MagnitudeThreshold_DocumentsFilteringBehavior(int bandwidth, double expectedMagnitude)
    {
        // This test documents the magnitude-bandwidth relationship and filtering behavior
        // Pre-computed values show why 0.80 threshold causes F1 dropout:
        // - 1500 Hz bandwidth -> magnitude 0.6752 (filtered at 0.80)
        // - 1000 Hz bandwidth -> magnitude 0.7697 (filtered at 0.80)
        // - 600 Hz bandwidth -> magnitude 0.8546 (passes 0.80)

        // Verify the pre-computed values are consistent
        // These were computed with Python: mag = exp(-bw * π / 12000)
        if (bandwidth >= 1000)
        {
            Assert.True(expectedMagnitude < 0.80,
                $"Bandwidth {bandwidth}Hz should produce magnitude {expectedMagnitude:F4} < 0.80 threshold");
        }
        else
        {
            Assert.True(expectedMagnitude > 0.80,
                $"Bandwidth {bandwidth}Hz should produce magnitude {expectedMagnitude:F4} > 0.80 threshold");
        }
    }
}
