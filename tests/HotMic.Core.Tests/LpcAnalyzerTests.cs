using HotMic.Core.Dsp.Analysis.Formants;
using Xunit;

namespace HotMic.Core.Tests;

/// <summary>
/// Tests to verify LPC math is correct.
/// Reference values computed with Python/NumPy Burg recursion
/// (independent implementation).
/// Script: tests/HotMic.Core.Tests/compute_reference_values.py
/// </summary>
public class LpcAnalyzerTests
{
    // Coefficients correspond to the LPC error filter: A(z) = 1 + sum(a_k * z^-k).

    private static double NextUniform(ref uint state)
    {
        // LCG for deterministic cross-language reference generation.
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
    public void Burg_PureSineWave_ProducesExpectedCoefficients()
    {
        // A 100 Hz sine at fs=1000 should produce specific LPC coefficients
        // Pre-computed with Python/NumPy Burg recursion.
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
        float[] expected =
        {
            1.0000000f,
            -2.7681730f,
            3.5978012f,
            -2.5182621f,
            0.8088672f
        };

        for (int i = 0; i < expected.Length; i++)
        {
            Assert.InRange(coeffs[i], expected[i] - 0.01f, expected[i] + 0.01f);
        }
    }

    [Fact]
    public void Compute_TwoSinusoids_ProducesExpectedCoefficients()
    {
        // Pre-computed with Python/NumPy Burg recursion for:
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

        float[] expected =
        {
            1.0000000f,
            -6.8968568f,
            21.694699f,
            -40.573688f,
            49.289505f,
            -39.809380f,
            20.878693f,
            -6.5062108f,
            0.9237014f
        };

        for (int i = 0; i < expected.Length; i++)
        {
            Assert.InRange(coeffs[i], expected[i] - 0.01f, expected[i] + 0.01f);
        }
    }

    [Fact]
    public void Compute_TwoTone250And500Hz_ProducesExpectedCoefficients()
    {
        // Pre-computed with Python/NumPy Burg recursion
        // signal = sin(2*pi*250*t) + 0.4*sin(2*pi*500*t), fs=8000, n=256, order=6
        var lpc = new LpcAnalyzer(6);
        int sampleRate = 8000;
        int n = 256;

        float[] signal = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sampleRate;
            signal[i] = (float)(Math.Sin(2 * Math.PI * 250 * t) + 0.4 * Math.Sin(2 * Math.PI * 500 * t));
        }

        float[] coeffs = new float[7];
        bool result = lpc.Compute(signal, coeffs);

        Assert.True(result);

        float[] expected =
        {
            1.0000000f,
            -5.7081442f,
            13.828353f,
            -18.192215f,
            13.705722f,
            -5.6068192f,
            0.9732517f
        };

        for (int i = 0; i < expected.Length; i++)
        {
            Assert.InRange(coeffs[i], expected[i] - 0.01f, expected[i] + 0.01f);
        }
    }

    [Fact]
    public void Compute_TwoSinusoids_500And1200Hz_ProducesExpectedCoefficients()
    {
        // Pre-computed with Python/NumPy Burg recursion
        // signal = sin(2*pi*500*t) + 0.6*sin(2*pi*1200*t), fs=12000, n=512, order=8
        var lpc = new LpcAnalyzer(8);
        int sampleRate = 12000;
        int n = 512;

        float[] signal = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sampleRate;
            signal[i] = (float)(Math.Sin(2 * Math.PI * 500 * t) + 0.6 * Math.Sin(2 * Math.PI * 1200 * t));
        }

        float[] coeffs = new float[9];
        bool result = lpc.Compute(signal, coeffs);

        Assert.True(result);

        float[] expected =
        {
            1.0000000f,
            -6.9002481f,
            21.712900f,
            -40.616509f,
            49.346111f,
            -39.854000f,
            20.898977f,
            -6.5107317f,
            0.9239601f
        };

        for (int i = 0; i < expected.Length; i++)
        {
            Assert.InRange(coeffs[i], expected[i] - 0.01f, expected[i] + 0.01f);
        }
    }

    [Fact]
    public void Compute_SyntheticVowel_ProducesExpectedCoefficients()
    {
        // Pre-computed with Python/NumPy Burg recursion
        // signal = sin(2*pi*700*t) + 0.7*sin(2*pi*1200*t) + 0.4*sin(2*pi*2500*t)
        // fs=12000, n=512, order=12
        var lpc = new LpcAnalyzer(12);
        int sampleRate = 12000;
        int n = 512;

        float[] signal = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sampleRate;
            signal[i] = (float)(
                Math.Sin(2 * Math.PI * 700 * t) +
                0.7 * Math.Sin(2 * Math.PI * 1200 * t) +
                0.4 * Math.Sin(2 * Math.PI * 2500 * t)
            );
        }

        float[] coeffs = new float[13];
        bool result = lpc.Compute(signal, coeffs);

        Assert.True(result);

        float[] expected =
        {
            1.0000000f,
            -8.4136231f,
            34.834347f,
            -93.280800f,
            179.231032f,
            -259.656647f,
            290.397400f,
            -252.468567f,
            169.376068f,
            -85.603058f,
            30.999166f,
            -7.2448874f,
            0.8304539f
        };

        for (int i = 0; i < expected.Length; i++)
        {
            Assert.InRange(coeffs[i], expected[i] - 0.02f, expected[i] + 0.02f);
        }
    }

    [Fact]
    public void Compute_WhiteNoise_ProducesExpectedCoefficients()
    {
        // Pre-computed with Python/NumPy Burg recursion
        // Seeded white noise, fs=12000, n=1024, order=8 (seed=1234, LCG + Box-Muller)
        var lpc = new LpcAnalyzer(8);
        int n = 1024;

        uint state = 1234;
        float[] signal = new float[n];
        for (int i = 0; i < n; i++)
        {
            signal[i] = NextGaussian(ref state);
        }

        float[] coeffs = new float[9];
        bool result = lpc.Compute(signal, coeffs);

        Assert.True(result);

        float[] expected =
        {
            1.0000000f,
            -0.0176247f,
            0.0178198f,
            -0.0172275f,
            0.0178027f,
            -0.0171739f,
            0.0166999f,
            -0.0171929f,
            0.0162766f
        };

        for (int i = 0; i < expected.Length; i++)
        {
            Assert.InRange(coeffs[i], expected[i] - 0.01f, expected[i] + 0.01f);
        }
    }

    [Fact]
    public void Compute_SineWithNoise_ProducesExpectedCoefficients()
    {
        // Pre-computed with Python/NumPy Burg recursion
        // signal = sin(2*pi*300*t) + 0.25*noise, fs=12000, n=1024, order=8 (seed=5678, LCG + Box-Muller)
        var lpc = new LpcAnalyzer(8);
        int sampleRate = 12000;
        int n = 1024;

        uint state = 5678;
        float[] signal = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sampleRate;
            signal[i] = (float)(Math.Sin(2 * Math.PI * 300 * t) + 0.25 * NextGaussian(ref state));
        }

        float[] coeffs = new float[9];
        bool result = lpc.Compute(signal, coeffs);

        Assert.True(result);

        float[] expected =
        {
            1.0000000f,
            -6.3151422f,
            18.682215f,
            -33.642773f,
            40.224079f,
            -32.661491f,
            17.595463f,
            -5.7608547f,
            0.8810094f
        };

        for (int i = 0; i < expected.Length; i++)
        {
            Assert.InRange(coeffs[i], expected[i] - 0.02f, expected[i] + 0.02f);
        }
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
}
