using System.Numerics;
using HotMic.Core.Dsp.Analysis.Formants;
using Xunit;

namespace HotMic.Core.Tests;

/// <summary>
/// Tests to verify polynomial root finding and formant extraction math.
/// These are targeted verification tests for math correctness.
/// </summary>
public class FormantTrackerTests
{
    [Fact]
    public void PolynomialRoots_KnownQuadratic_FindsCorrectRoots()
    {
        // Polynomial: z^2 - 1.8*cos(θ)*z + 0.81 = 0
        // Has roots at r*e^(±jθ) where r = 0.9
        // For θ = π/4 (45°), roots are 0.9*(cos(π/4) ± j*sin(π/4))

        // At sample rate 1000 Hz, angle π/4 corresponds to frequency:
        // f = θ * fs / (2π) = (π/4) * 1000 / (2π) = 125 Hz

        double theta = Math.PI / 4;
        double r = 0.9;

        // FormantTracker minimum order is 4, so we need to pad the polynomial
        // Original: z^2 - 2r*cos(θ)*z + r^2
        // Padded: z^4 + 0*z^3 + (-2r*cos(θ))*z^2 + 0*z + r^2
        // Actually, to have the same roots, we need (z^2 - 2r*cos(θ)*z + r^2) * z^2
        // which adds two roots at z=0 (filtered out by magnitude check)
        float[] lpcCoeffs = new float[5];
        lpcCoeffs[0] = 1.0f;
        lpcCoeffs[1] = (float)(-2 * r * Math.Cos(theta));
        lpcCoeffs[2] = (float)(r * r);
        lpcCoeffs[3] = 0f;
        lpcCoeffs[4] = 0f;

        var tracker = new FormantTracker(4);
        float[] freqs = new float[4];
        float[] bws = new float[4];

        int count = tracker.Track(lpcCoeffs, 1000, freqs, bws, 0, 500, 4);

        // Should find one formant (only positive imaginary root counts)
        Assert.True(count >= 1, $"Expected at least 1 formant, got {count}");

        // The frequency should be close to 125 Hz
        Assert.InRange(freqs[0], 120f, 130f);

        // Bandwidth should be based on magnitude 0.9:
        // bw = -fs/π * ln(0.9) ≈ 33.5 Hz at fs=1000
        float expectedBw = (float)(-1000 / Math.PI * Math.Log(0.9));
        Assert.InRange(bws[0], expectedBw - 5, expectedBw + 5);
    }

    [Fact]
    public void PolynomialRoots_TwoFormants_FindsBoth()
    {
        // Create LPC coefficients for two resonances
        // Formant 1: 300 Hz, magnitude 0.95
        // Formant 2: 1500 Hz, magnitude 0.90
        int sampleRate = 12000;

        double f1 = 300, r1 = 0.95;
        double f2 = 1500, r2 = 0.90;

        double theta1 = 2 * Math.PI * f1 / sampleRate;
        double theta2 = 2 * Math.PI * f2 / sampleRate;

        // Each resonance contributes a second-order section:
        // (z - r*e^jθ)(z - r*e^-jθ) = z^2 - 2r*cos(θ)*z + r^2
        // Combined is 4th order

        // Build the polynomial by multiplying two quadratics
        double a1_1 = -2 * r1 * Math.Cos(theta1);
        double a1_2 = r1 * r1;
        double a2_1 = -2 * r2 * Math.Cos(theta2);
        double a2_2 = r2 * r2;

        // Convolve [1, a1_1, a1_2] with [1, a2_1, a2_2]
        // Result: [1, a1_1+a2_1, a1_2+a1_1*a2_1+a2_2, a1_1*a2_2+a1_2*a2_1, a1_2*a2_2]
        float[] lpcCoeffs = new float[5];
        lpcCoeffs[0] = 1.0f;
        lpcCoeffs[1] = (float)(a1_1 + a2_1);
        lpcCoeffs[2] = (float)(a1_2 + a1_1 * a2_1 + a2_2);
        lpcCoeffs[3] = (float)(a1_1 * a2_2 + a1_2 * a2_1);
        lpcCoeffs[4] = (float)(a1_2 * a2_2);

        var tracker = new FormantTracker(4);
        float[] freqs = new float[4];
        float[] bws = new float[4];

        int count = tracker.Track(lpcCoeffs, sampleRate, freqs, bws, 0, 6000, 4);

        Assert.True(count >= 2, $"Expected at least 2 formants, got {count}");

        // First formant should be near 300 Hz
        Assert.InRange(freqs[0], 280f, 320f);

        // Second formant should be near 1500 Hz
        Assert.InRange(freqs[1], 1450f, 1550f);
    }

    [Fact]
    public void Track_InvalidCoefficients_ReturnsZero()
    {
        var tracker = new FormantTracker(4);
        float[] freqs = new float[4];
        float[] bws = new float[4];

        // NaN coefficients
        float[] nanCoeffs = new float[] { 1, float.NaN, 0, 0, 0 };
        int count = tracker.Track(nanCoeffs, 12000, freqs, bws, 0, 6000, 4);
        Assert.Equal(0, count);

        // Infinity coefficients
        float[] infCoeffs = new float[] { 1, float.PositiveInfinity, 0, 0, 0 };
        count = tracker.Track(infCoeffs, 12000, freqs, bws, 0, 6000, 4);
        Assert.Equal(0, count);

        // Unreasonably large coefficients
        float[] largeCoeffs = new float[] { 1, 200f, 0, 0, 0 };
        count = tracker.Track(largeCoeffs, 12000, freqs, bws, 0, 6000, 4);
        Assert.Equal(0, count);
    }

    [Fact]
    public void Track_EmptyBuffers_ReturnsZero()
    {
        var tracker = new FormantTracker(4);
        float[] lpcCoeffs = new float[] { 1, -0.5f, 0.25f, 0, 0 };

        int count = tracker.Track(lpcCoeffs, 12000, Span<float>.Empty, Span<float>.Empty, 0, 6000, 4);
        Assert.Equal(0, count);
    }

    [Fact]
    public void FrequencyFromAngle_CorrectFormula()
    {
        // Verify the formula: freq = angle * sampleRate / (2 * PI)
        int sampleRate = 12000;
        double angle = Math.PI / 3; // 60 degrees

        double expectedFreq = angle * sampleRate / (2 * Math.PI);
        // = (π/3) * 12000 / (2π) = 12000/6 = 2000 Hz

        Assert.Equal(2000.0, expectedFreq, 1);
    }

    [Fact]
    public void BandwidthFromMagnitude_CorrectFormula()
    {
        // Verify the formula: bandwidth = -sampleRate / PI * ln(magnitude)
        int sampleRate = 12000;
        double magnitude = 0.95;

        double expectedBw = -sampleRate / Math.PI * Math.Log(magnitude);
        // = -12000 / π * ln(0.95) ≈ -12000 / 3.14159 * (-0.05129) ≈ 196 Hz

        Assert.InRange(expectedBw, 190, 200);
    }

    [Fact]
    public void MagnitudeThreshold_AtTypicalFormantBandwidths()
    {
        // At 12kHz sample rate, what magnitudes correspond to typical bandwidths?
        int sampleRate = 12000;

        // For bandwidth 100 Hz: mag = exp(-bw * π / sr) = exp(-100 * π / 12000)
        double mag100 = Math.Exp(-100 * Math.PI / sampleRate);
        Assert.InRange(mag100, 0.97, 0.98); // Should be about 0.974

        // For bandwidth 200 Hz:
        double mag200 = Math.Exp(-200 * Math.PI / sampleRate);
        Assert.InRange(mag200, 0.94, 0.96); // Should be about 0.949

        // For bandwidth 500 Hz:
        double mag500 = Math.Exp(-500 * Math.PI / sampleRate);
        Assert.InRange(mag500, 0.87, 0.89); // Should be about 0.877

        // For bandwidth 1000 Hz (wide formant):
        double mag1000 = Math.Exp(-1000 * Math.PI / sampleRate);
        Assert.InRange(mag1000, 0.76, 0.78); // Should be about 0.769

        // This shows why 0.80 threshold was filtering out F1!
        // A formant with 1000 Hz bandwidth has magnitude ~0.77
    }
}
