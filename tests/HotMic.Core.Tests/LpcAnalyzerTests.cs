using HotMic.Core.Dsp.Analysis.Formants;
using Xunit;

namespace HotMic.Core.Tests;

/// <summary>
/// Tests to verify LPC math is correct.
/// Reference values computed with Python/NumPy (see inline comments).
/// </summary>
public class LpcAnalyzerTests
{
    [Fact]
    public void Autocorrelation_PureSineWave_ProducesExpectedCoefficients()
    {
        // A 100 Hz sine at fs=1000 should produce specific LPC coefficients
        // Pre-computed with Python/NumPy Levinson-Durbin:
        //   a[0] = 1.0, a[1] = -1.614845, a[2] = 0.996067, a[3] = -0.001204, a[4] = -0.001227
        // For a pure sinusoid, LPC models it as a 2nd-order AR process
        var lpc = new LpcAnalyzer(4);
        int sampleRate = 1000;
        int n = 256;
        float freq = 100f;

        float[] signal = new float[n];
        for (int i = 0; i < n; i++)
        {
            signal[i] = (float)Math.Sin(2 * Math.PI * freq * i / sampleRate);
        }

        float[] coeffs = new float[5];
        bool result = lpc.Compute(signal, coeffs);

        Assert.True(result, "LPC should succeed on a sine wave");
        Assert.InRange(coeffs[0], 0.999f, 1.001f);
        Assert.InRange(coeffs[1], -1.617f, -1.613f);
        Assert.InRange(coeffs[2], 0.994f, 0.998f);
        Assert.InRange(coeffs[3], -0.0025f, -0.0005f);
        Assert.InRange(coeffs[4], -0.0025f, -0.0005f);
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
    public void Compute_TwoSinusoids_ProducesExpectedCoefficients()
    {
        // Pre-computed with Python/NumPy Levinson-Durbin for:
        // signal = sin(2*pi*400*t) + 0.5*sin(2*pi*1500*t), fs=12000, n=512, order=8
        var lpc = new LpcAnalyzer(8);
        int sampleRate = 12000;
        int n = 512;

        float[] signal = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sampleRate;
            signal[i] = (float)(Math.Sin(2 * Math.PI * 400 * t) + 0.5 * Math.Sin(2 * Math.PI * 1500 * t));
        }

        float[] coeffs = new float[9];
        bool result = lpc.Compute(signal, coeffs);

        Assert.True(result);
        Assert.InRange(coeffs[0], 0.999f, 1.001f);
        Assert.InRange(coeffs[1], -2.049f, -2.045f);
        Assert.InRange(coeffs[2], 1.511f, 1.515f);
        Assert.InRange(coeffs[3], -0.337f, -0.333f);
        Assert.InRange(coeffs[4], -0.065f, -0.061f);
        Assert.InRange(coeffs[5], -0.048f, -0.044f);
        Assert.InRange(coeffs[6], 0.053f, 0.056f);
        Assert.InRange(coeffs[7], -0.267f, -0.263f);
        Assert.InRange(coeffs[8], 0.303f, 0.307f);
    }

    [Fact]
    public void LpcOrder_ClampedToValidRange()
    {
        // These are specific boundary tests with exact expected values
        var lpc1 = new LpcAnalyzer(2);  // Too low, should clamp to 4
        Assert.Equal(4, lpc1.Order);

        var lpc2 = new LpcAnalyzer(100); // Too high, should clamp to 32
        Assert.Equal(32, lpc2.Order);

        var lpc3 = new LpcAnalyzer(12);  // Valid, should stay at 12
        Assert.Equal(12, lpc3.Order);

        var lpc4 = new LpcAnalyzer(4);   // Minimum valid
        Assert.Equal(4, lpc4.Order);

        var lpc5 = new LpcAnalyzer(32);  // Maximum valid
        Assert.Equal(32, lpc5.Order);
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

        float[] smallCoeffs = new float[5]; // Too small for order 12 (needs 13)
        bool result = lpc.Compute(signal, smallCoeffs);

        Assert.False(result);
    }

}
