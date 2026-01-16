using HotMic.Core.Dsp.Fft;
using Xunit;

namespace HotMic.Core.Tests;

/// <summary>
/// Tests for window function generation.
/// Reference values computed with Python/NumPy.
/// </summary>
public class WindowFunctionsTests
{
    // Pre-computed reference values from Python/NumPy for n=8
    // Hann: 0.5 - 0.5 * cos(2*pi*i/(n-1))
    private static readonly float[] HannReference8 =
    {
        0.000000f, 0.188255f, 0.611260f, 0.950484f,
        0.950484f, 0.611260f, 0.188255f, 0.000000f
    };

    // Hamming: 0.54 - 0.46 * cos(2*pi*i/(n-1))
    private static readonly float[] HammingReference8 =
    {
        0.080000f, 0.253195f, 0.642360f, 0.954446f,
        0.954446f, 0.642360f, 0.253195f, 0.080000f
    };

    // Blackman-Harris: a0 - a1*cos + a2*cos(2x) - a3*cos(3x)
    // a0=0.35875, a1=0.48829, a2=0.14128, a3=0.01168
    private static readonly float[] BlackmanHarrisReference8 =
    {
        0.000060f, 0.033392f, 0.332834f, 0.889370f,
        0.889370f, 0.332834f, 0.033392f, 0.000060f
    };

    // Gaussian with sigma=0.25
    private static readonly float[] GaussianReference8 =
    {
        0.000335f, 0.016880f, 0.230066f, 0.849366f,
        0.849366f, 0.230066f, 0.016880f, 0.000335f
    };

    // Kaiser with beta=9 (using Bessel I0 approximation)
    private static readonly float[] KaiserReference8 =
    {
        0.000914f, 0.080794f, 0.442200f, 0.916685f,
        0.916685f, 0.442200f, 0.080794f, 0.000914f
    };

    [Fact]
    public void FillHann_MatchesReference()
    {
        float[] window = new float[8];
        WindowFunctions.FillHann(window);

        for (int i = 0; i < 8; i++)
        {
            Assert.InRange(window[i], HannReference8[i] - 0.0001f, HannReference8[i] + 0.0001f);
        }
    }

    [Fact]
    public void FillHamming_MatchesReference()
    {
        float[] window = new float[8];
        WindowFunctions.FillHamming(window);

        for (int i = 0; i < 8; i++)
        {
            Assert.InRange(window[i], HammingReference8[i] - 0.0001f, HammingReference8[i] + 0.0001f);
        }
    }

    [Fact]
    public void FillBlackmanHarris_MatchesReference()
    {
        float[] window = new float[8];
        WindowFunctions.FillBlackmanHarris(window);

        for (int i = 0; i < 8; i++)
        {
            Assert.InRange(window[i], BlackmanHarrisReference8[i] - 0.0001f, BlackmanHarrisReference8[i] + 0.0001f);
        }
    }

    [Fact]
    public void FillGaussian_MatchesReference()
    {
        float[] window = new float[8];
        WindowFunctions.FillGaussian(window, 0.25f);

        for (int i = 0; i < 8; i++)
        {
            Assert.InRange(window[i], GaussianReference8[i] - 0.001f, GaussianReference8[i] + 0.001f);
        }
    }

    [Fact]
    public void FillKaiser_MatchesReference()
    {
        float[] window = new float[8];
        WindowFunctions.FillKaiser(window, 9f);

        for (int i = 0; i < 8; i++)
        {
            Assert.InRange(window[i], KaiserReference8[i] - 0.001f, KaiserReference8[i] + 0.001f);
        }
    }

    [Fact]
    public void Hann_IsSymmetric()
    {
        float[] window = new float[8];
        WindowFunctions.FillHann(window);

        // Hann should be symmetric
        Assert.Equal(window[0], window[7], 6);
        Assert.Equal(window[1], window[6], 6);
        Assert.Equal(window[2], window[5], 6);
        Assert.Equal(window[3], window[4], 6);
    }

    [Fact]
    public void Hann_EndpointsAreZero()
    {
        float[] window = new float[8];
        WindowFunctions.FillHann(window);

        // Hann window should have zero endpoints
        Assert.Equal(0f, window[0], 6);
        Assert.Equal(0f, window[7], 6);
    }

    [Fact]
    public void Hamming_EndpointsAreNonZero()
    {
        float[] window = new float[8];
        WindowFunctions.FillHamming(window);

        // Hamming endpoints should be 0.08 (not zero)
        Assert.InRange(window[0], 0.079f, 0.081f);
        Assert.InRange(window[7], 0.079f, 0.081f);
    }

    [Fact]
    public void Fill_SingleElement_ReturnsOne()
    {
        float[] window = new float[1];

        WindowFunctions.Fill(window, WindowFunction.Hann);
        Assert.Equal(1f, window[0]);

        WindowFunctions.Fill(window, WindowFunction.Hamming);
        Assert.Equal(1f, window[0]);

        WindowFunctions.Fill(window, WindowFunction.BlackmanHarris);
        Assert.Equal(1f, window[0]);
    }

    [Fact]
    public void Fill_Empty_DoesNotThrow()
    {
        float[] window = Array.Empty<float>();

        // Should not throw
        WindowFunctions.Fill(window, WindowFunction.Hann);
        WindowFunctions.Fill(window, WindowFunction.Hamming);
        WindowFunctions.Fill(window, WindowFunction.BlackmanHarris);
    }

    [Theory]
    [InlineData(WindowFunction.Hann)]
    [InlineData(WindowFunction.Hamming)]
    [InlineData(WindowFunction.BlackmanHarris)]
    [InlineData(WindowFunction.Gaussian)]
    [InlineData(WindowFunction.Kaiser)]
    public void Fill_AllTypes_ProduceValuesInZeroOneRange(WindowFunction type)
    {
        float[] window = new float[256];
        WindowFunctions.Fill(window, type);

        foreach (float value in window)
        {
            Assert.InRange(value, 0f, 1.001f);  // Small tolerance for floating point
        }
    }

    [Theory]
    [InlineData(WindowFunction.Hann)]
    [InlineData(WindowFunction.Hamming)]
    [InlineData(WindowFunction.BlackmanHarris)]
    [InlineData(WindowFunction.Gaussian)]
    [InlineData(WindowFunction.Kaiser)]
    public void Fill_AllTypes_SameInputProducesSameOutput(WindowFunction type)
    {
        float[] window1 = new float[64];
        float[] window2 = new float[64];

        WindowFunctions.Fill(window1, type);
        WindowFunctions.Fill(window2, type);

        for (int i = 0; i < 64; i++)
        {
            Assert.Equal(window1[i], window2[i], 6);
        }
    }
}
