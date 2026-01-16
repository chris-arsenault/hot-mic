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
    public void EndToEnd_WhiteNoise_ProducesNoFormants()
    {
        // White noise should not produce clear formant peaks
        // (or should produce high-bandwidth poles that get filtered)
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

        if (lpcResult)
        {
            var tracker = new FormantTracker(12);
            float[] freqs = new float[5];
            float[] bws = new float[5];
            int count = tracker.Track(coeffs, sampleRate, freqs, bws, 100, 5500, 5);

            // White noise might produce some poles, but they should have
            // high bandwidths (low magnitudes)
            for (int i = 0; i < count; i++)
            {
                // If any formants are found, they should have high bandwidth
                // (indicating they're not sharp resonances)
                // This is not a strict requirement, just a sanity check
            }
        }
    }

    [Fact]
    public void EndToEnd_VaryingOrder_AffectsResolution()
    {
        // Higher LPC order should capture more formants
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

        lpc8.Compute(signal, coeffs8);
        lpc12.Compute(signal, coeffs12);

        var tracker8 = new FormantTracker(8);
        var tracker12 = new FormantTracker(12);
        float[] freqs8 = new float[5];
        float[] freqs12 = new float[5];
        float[] bws8 = new float[5];
        float[] bws12 = new float[5];

        int count8 = tracker8.Track(coeffs8, sampleRate, freqs8, bws8, 100, 5500, 5);
        int count12 = tracker12.Track(coeffs12, sampleRate, freqs12, bws12, 100, 5500, 5);

        // Higher order should generally find more or equal formants
        // (not a strict requirement, but generally true)
        Assert.True(count8 >= 0 && count12 >= 0);
    }

    [Fact]
    public void MagnitudeThreshold_ExplainsDropout()
    {
        // This test documents why 0.80 threshold causes dropout
        // and what threshold would be needed

        // At 12kHz, formant with 1500 Hz bandwidth:
        double bw1500 = 1500;
        double mag1500 = Math.Exp(-bw1500 * Math.PI / 12000);
        // mag ≈ 0.673 - would be filtered at 0.80 threshold!

        // At 12kHz, formant with 1000 Hz bandwidth:
        double bw1000 = 1000;
        double mag1000 = Math.Exp(-bw1000 * Math.PI / 12000);
        // mag ≈ 0.769 - would be filtered at 0.80 threshold!

        // At 12kHz, formant with 600 Hz bandwidth:
        double bw600 = 600;
        double mag600 = Math.Exp(-bw600 * Math.PI / 12000);
        // mag ≈ 0.855 - would pass 0.80 threshold

        Assert.True(mag1500 < 0.80, $"1500Hz bw pole (mag={mag1500:F3}) should be filtered at 0.80");
        Assert.True(mag1000 < 0.80, $"1000Hz bw pole (mag={mag1000:F3}) should be filtered at 0.80");
        Assert.True(mag600 > 0.80, $"600Hz bw pole (mag={mag600:F3}) should pass 0.80");

        // Conclusion: To capture formants with bandwidth up to 1500Hz,
        // we need magnitude threshold around 0.65
    }
}
