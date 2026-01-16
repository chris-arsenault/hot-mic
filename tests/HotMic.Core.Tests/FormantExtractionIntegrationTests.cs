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

        // Pre-computed with Python/NumPy (Levinson-Durbin + roots + mag>=0.80 filter)
        Assert.Equal(4, count);
        Assert.InRange(freqs[0], 688f, 698f);
        Assert.InRange(bws[0], 10f, 17f);
        Assert.InRange(freqs[1], 1195f, 1206f);
        Assert.InRange(bws[1], 17f, 24f);
        Assert.InRange(freqs[2], 2495f, 2509f);
        Assert.InRange(bws[2], 4f, 10f);
        Assert.InRange(freqs[3], 3520f, 3610f);
        Assert.InRange(bws[3], 730f, 880f);
    }
}
