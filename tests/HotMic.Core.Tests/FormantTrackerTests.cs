using HotMic.Core.Dsp.Analysis.Formants;
using Xunit;

namespace HotMic.Core.Tests;

/// <summary>
/// Tests to verify polynomial root finding and formant extraction math.
/// Reference values computed externally with Python/NumPy.
/// Script: tests/HotMic.Core.Tests/compute_reference_values.py
/// </summary>
public class FormantTrackerTests
{
    // Pre-computed reference values (from Python: freq = angle * sr / (2*pi), bw = -sr/pi * ln(mag))
    // At sr=12000: bw=100Hz -> mag=0.974160, bw=500Hz -> mag=0.877306, bw=1000Hz -> mag=0.769665
    // At sr=1000, theta=π/4, r=0.9: freq=125Hz, bw=33.54Hz

    [Fact]
    public void PolynomialRoots_KnownQuadratic_FindsCorrectRoots()
    {
        // Polynomial with known roots: r=0.9, theta=π/4 (45°)
        // Pre-computed coefficients: z^2 - 2*0.9*cos(π/4)*z + 0.81
        // = z^2 - 1.2727922...*z + 0.81
        // Padded to order 4 by multiplying by z^2 (adds roots at z=0, filtered by magnitude)

        // Pre-computed coefficient: -2 * 0.9 * cos(π/4) = -1.2727922061357855
        float[] lpcCoeffs = new float[5];
        lpcCoeffs[0] = 1.0f;
        lpcCoeffs[1] = -1.2727922f;  // Pre-computed: -2 * 0.9 * cos(π/4)
        lpcCoeffs[2] = 0.81f;        // Pre-computed: 0.9^2
        lpcCoeffs[3] = 0f;
        lpcCoeffs[4] = 0f;

        var tracker = new FormantTracker(4);
        float[] freqs = new float[4];
        float[] bws = new float[4];

        int count = tracker.Track(lpcCoeffs, 1000, freqs, bws, 0, 500, 4);

        Assert.Equal(1, count);

        // Pre-computed expected: freq = (π/4) * 1000 / (2π) = 125 Hz
        Assert.InRange(freqs[0], 120f, 130f);

        // Pre-computed expected: bw = -1000/π * ln(0.9) = 33.54 Hz
        Assert.InRange(bws[0], 28f, 39f);
    }

    [Fact]
    public void PolynomialRoots_TwoFormants_FindsBoth()
    {
        // Pre-computed LPC coefficients for two resonances:
        // Formant 1: 300 Hz, magnitude 0.95 (bw ≈ 196 Hz)
        // Formant 2: 1500 Hz, magnitude 0.90 (bw ≈ 402 Hz)
        // Sample rate: 12000 Hz
        // Coefficients computed in Python by convolving two second-order sections

        float[] lpcCoeffs = new float[5];
        lpcCoeffs[0] = 1.0f;
        lpcCoeffs[1] = -3.1494001f;   // Pre-computed
        lpcCoeffs[2] = 4.1010318f;    // Pre-computed
        lpcCoeffs[3] = -2.6687473f;   // Pre-computed
        lpcCoeffs[4] = 0.731025f;     // Pre-computed (0.95^2 * 0.90^2)

        var tracker = new FormantTracker(4);
        float[] freqs = new float[4];
        float[] bws = new float[4];

        int count = tracker.Track(lpcCoeffs, 12000, freqs, bws, 0, 6000, 4);

        Assert.Equal(2, count);

        // First formant should be near 300 Hz
        Assert.InRange(freqs[0], 280f, 320f);

        // Second formant should be near 1500 Hz
        Assert.InRange(freqs[1], 1450f, 1550f);

        // Bandwidths should match expected values
        // F1: ~196 Hz, F2: ~402 Hz
        Assert.InRange(bws[0], 150f, 250f);
        Assert.InRange(bws[1], 350f, 450f);
    }

    [Fact]
    public void Track_SingleResonance_ExtractsCorrectBandwidth()
    {
        // Test that bandwidth extraction matches the magnitude-bandwidth relationship
        // Pre-computed: at sr=12000, magnitude 0.95 -> bandwidth = 195.93 Hz
        // Polynomial for single resonance at 1000 Hz, mag 0.95:
        // theta = 2*pi*1000/12000 = 0.5236 rad
        // coeffs: [1, -2*0.95*cos(0.5236), 0.95^2] = [1, -1.6454, 0.9025]

        float[] lpcCoeffs = new float[5];
        lpcCoeffs[0] = 1.0f;
        lpcCoeffs[1] = -1.6454f;  // Pre-computed: -2 * 0.95 * cos(2π*1000/12000)
        lpcCoeffs[2] = 0.9025f;   // Pre-computed: 0.95^2
        lpcCoeffs[3] = 0f;
        lpcCoeffs[4] = 0f;

        var tracker = new FormantTracker(4);
        float[] freqs = new float[4];
        float[] bws = new float[4];

        int count = tracker.Track(lpcCoeffs, 12000, freqs, bws, 0, 6000, 4);

        Assert.Equal(1, count);
        Assert.InRange(freqs[0], 950f, 1050f);  // ~1000 Hz
        Assert.InRange(bws[0], 180f, 210f);     // Pre-computed: 195.93 Hz
    }

    [Fact]
    public void Track_SingleResonance_At500Hz_ExtractsCorrectBandwidth()
    {
        // Pre-computed with Python/NumPy: sr=16000, freq=500 Hz, bw=80 Hz
        // r = exp(-pi*bw/sr) = 0.98441476, coeffs: [1, -1.93099902, 0.96907243]
        float[] lpcCoeffs = new float[5];
        lpcCoeffs[0] = 1.0f;
        lpcCoeffs[1] = -1.9309990f;
        lpcCoeffs[2] = 0.9690724f;
        lpcCoeffs[3] = 0f;
        lpcCoeffs[4] = 0f;

        var tracker = new FormantTracker(4);
        float[] freqs = new float[4];
        float[] bws = new float[4];

        int count = tracker.Track(lpcCoeffs, 16000, freqs, bws, 0, 8000, 4);

        Assert.Equal(1, count);
        Assert.InRange(freqs[0], 495f, 505f);
        Assert.InRange(bws[0], 75f, 85f);
    }

    [Fact]
    public void Track_TwoFormants_500And2000Hz_FindsBoth()
    {
        // Pre-computed with Python/NumPy: sr=16000
        // Formant 1: 500 Hz, bw 100 Hz (r=0.98055656)
        // Formant 2: 2000 Hz, bw 300 Hz (r=0.94279646)
        // Coeffs from convolving two second-order sections:
        // [1, -3.25674641, 4.41489660, -2.99164181, 0.85463600]
        float[] lpcCoeffs = new float[5];
        lpcCoeffs[0] = 1.0f;
        lpcCoeffs[1] = -3.2567464f;
        lpcCoeffs[2] = 4.4148966f;
        lpcCoeffs[3] = -2.9916418f;
        lpcCoeffs[4] = 0.8546360f;

        var tracker = new FormantTracker(4);
        float[] freqs = new float[4];
        float[] bws = new float[4];

        int count = tracker.Track(lpcCoeffs, 16000, freqs, bws, 0, 8000, 4);

        Assert.Equal(2, count);
        Assert.InRange(freqs[0], 495f, 505f);
        Assert.InRange(freqs[1], 1985f, 2015f);
        Assert.InRange(bws[0], 90f, 110f);
        Assert.InRange(bws[1], 280f, 320f);
    }

    [Fact]
    public void Track_SingleResonance_NarrowBandwidth_IsAccepted()
    {
        // Pre-computed with Python/NumPy: sr=12000, freq=2500 Hz, bw=5 Hz
        // r = exp(-pi*bw/sr) = 0.99869186, coeffs: [1, -0.51696095, 0.99738543]
        float[] lpcCoeffs = new float[5];
        lpcCoeffs[0] = 1.0f;
        lpcCoeffs[1] = -0.5169609f;
        lpcCoeffs[2] = 0.9973854f;
        lpcCoeffs[3] = 0f;
        lpcCoeffs[4] = 0f;

        var tracker = new FormantTracker(4);
        float[] freqs = new float[4];
        float[] bws = new float[4];

        int count = tracker.Track(lpcCoeffs, 12000, freqs, bws, 0, 6000, 4);

        Assert.Equal(1, count);
        Assert.InRange(freqs[0], 2490f, 2510f);
        Assert.InRange(bws[0], 4f, 6f);
    }

}
