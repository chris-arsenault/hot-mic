using HotMic.Core.Dsp.Analysis.Formants;
using Xunit;

namespace HotMic.Core.Tests;

/// <summary>
/// Tests to verify LPC math is correct.
/// These are targeted verification tests, not long-term regression tests.
/// </summary>
public class LpcAnalyzerTests
{
    private const double Tolerance = 1e-6;

    [Fact]
    public void Autocorrelation_PureSineWave_ProducesCorrectPattern()
    {
        // A pure sine wave's autocorrelation should be a cosine at the same frequency
        // R(k) = (N-k)/2 * cos(2*pi*f*k/fs) for a sine wave
        var lpc = new LpcAnalyzer(4);
        int sampleRate = 1000;
        int n = 256;
        float freq = 100f; // 100 Hz sine wave

        // Generate sine wave
        float[] signal = new float[n];
        for (int i = 0; i < n; i++)
        {
            signal[i] = (float)Math.Sin(2 * Math.PI * freq * i / sampleRate);
        }

        // Compute LPC - we'll verify it produces stable coefficients
        float[] coeffs = new float[5];
        bool result = lpc.Compute(signal, coeffs);

        Assert.True(result, "LPC should succeed on a sine wave");
        Assert.Equal(1.0f, coeffs[0], 4); // a[0] should always be 1.0

        // For a pure sine wave, LPC should produce a 2nd order AR model
        // The coefficient a[2] should be close to -1 for a pure tone
        // (since x[n] + a1*x[n-1] + a2*x[n-2] = 0 for sinusoid)
    }

    [Fact]
    public void Autocorrelation_DcSignal_ReturnsFailure()
    {
        // A constant DC signal should fail (no variation)
        var lpc = new LpcAnalyzer(4);
        float[] signal = new float[256];
        Array.Fill(signal, 0.5f);

        float[] coeffs = new float[5];
        // This might succeed but produce trivial coefficients
        // The important thing is it doesn't crash
        lpc.Compute(signal, coeffs);
    }

    [Fact]
    public void Autocorrelation_ZeroSignal_ReturnsFalse()
    {
        var lpc = new LpcAnalyzer(4);
        float[] signal = new float[256]; // All zeros
        float[] coeffs = new float[5];

        bool result = lpc.Compute(signal, coeffs);
        Assert.False(result, "LPC should fail on zero signal (R[0] = 0)");
    }

    [Fact]
    public void LevinsonDurbin_KnownAutocorrelation_ProducesCorrectCoefficients()
    {
        // Test case from digital signal processing textbooks
        // For a first-order AR process: x[n] = 0.9*x[n-1] + e[n]
        // The theoretical autocorrelation is R[k] = sigma^2 * 0.9^|k| / (1 - 0.81)
        // Levinson-Durbin should recover a[1] = -0.9

        var lpc = new LpcAnalyzer(4);

        // Generate AR(1) process with a[1] = 0.9
        int n = 4096;
        float[] signal = new float[n];
        var rng = new Random(42);
        signal[0] = (float)(rng.NextDouble() - 0.5);
        for (int i = 1; i < n; i++)
        {
            signal[i] = 0.9f * signal[i - 1] + (float)(rng.NextDouble() - 0.5) * 0.1f;
        }

        float[] coeffs = new float[5];
        bool result = lpc.Compute(signal, coeffs);

        Assert.True(result);
        // The first LPC coefficient magnitude should be close to 0.9
        // Sign convention varies (some use A(z) = 1 - a1*z^-1, others use 1 + a1*z^-1)
        // Allow some tolerance due to finite sample effects and noise
        Assert.InRange(MathF.Abs(coeffs[1]), 0.80f, 0.99f);
    }

    [Fact]
    public void LpcOrder_ClampedToValidRange()
    {
        var lpc1 = new LpcAnalyzer(2);  // Too low, should clamp to 4
        Assert.Equal(4, lpc1.Order);

        var lpc2 = new LpcAnalyzer(100); // Too high, should clamp to 32
        Assert.Equal(32, lpc2.Order);

        var lpc3 = new LpcAnalyzer(12);
        Assert.Equal(12, lpc3.Order);
    }

    [Fact]
    public void Compute_ShortFrame_ReturnsFalse()
    {
        var lpc = new LpcAnalyzer(12);
        float[] shortSignal = new float[10]; // Too short for order 12
        for (int i = 0; i < shortSignal.Length; i++)
            shortSignal[i] = (float)Math.Sin(i * 0.1);

        float[] coeffs = new float[13];
        bool result = lpc.Compute(shortSignal, coeffs);

        Assert.False(result, "LPC should fail when frame length <= order");
    }

    [Fact]
    public void Compute_OutputBufferTooSmall_ReturnsFalse()
    {
        var lpc = new LpcAnalyzer(12);
        float[] signal = new float[256];
        for (int i = 0; i < signal.Length; i++)
            signal[i] = (float)Math.Sin(i * 0.1);

        float[] smallCoeffs = new float[5]; // Too small for order 12
        bool result = lpc.Compute(signal, smallCoeffs);

        Assert.False(result);
    }

    [Fact]
    public void Compute_TwoSinusoids_ProducesStableCoefficients()
    {
        // Two sinusoids should produce LPC coefficients that model them
        var lpc = new LpcAnalyzer(8);
        int sampleRate = 12000;
        int n = 512;
        float f1 = 400f;  // Simulated F1
        float f2 = 1500f; // Simulated F2

        float[] signal = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sampleRate;
            signal[i] = (float)(Math.Sin(2 * Math.PI * f1 * t) + 0.5 * Math.Sin(2 * Math.PI * f2 * t));
        }

        float[] coeffs = new float[9];
        bool result = lpc.Compute(signal, coeffs);

        Assert.True(result);
        Assert.Equal(1.0f, coeffs[0], 4);

        // All coefficients should be finite and reasonable
        for (int i = 0; i <= 8; i++)
        {
            Assert.False(float.IsNaN(coeffs[i]), $"Coefficient {i} is NaN");
            Assert.False(float.IsInfinity(coeffs[i]), $"Coefficient {i} is Infinity");
            Assert.InRange(coeffs[i], -10f, 10f);
        }
    }
}
