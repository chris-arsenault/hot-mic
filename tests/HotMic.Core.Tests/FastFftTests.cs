using HotMic.Core.Dsp.Fft;
using Xunit;

namespace HotMic.Core.Tests;

/// <summary>
/// Tests for FFT implementation.
/// Reference values computed with Python/NumPy np.fft.fft().
/// </summary>
public class FastFftTests
{
    // Pre-computed reference: FFT of cos(2*pi*2*n/8) for n=0..7
    // Input: [1, 0, -1, 0, 1, 0, -1, 0]
    // Expected FFT: [0, 0, 4, 0, 0, 0, 4, 0] (real), [0, 0, 0, 0, 0, 0, 0, 0] (imag)
    // A cosine at bin 2 produces peaks at bins 2 and N-2=6

    [Fact]
    public void Forward_PureCosine_ProducesCorrectBins()
    {
        var fft = new FastFft(8);

        // cos(2*pi*2*n/8) - frequency at bin 2
        float[] real = new float[8];
        float[] imag = new float[8];
        for (int i = 0; i < 8; i++)
        {
            real[i] = MathF.Cos(2f * MathF.PI * 2f * i / 8f);
            imag[i] = 0f;
        }

        fft.Forward(real, imag);

        // Pre-computed: bin 2 and bin 6 should have magnitude 4, others ~0
        Assert.InRange(real[0], -0.001f, 0.001f);
        Assert.InRange(real[1], -0.001f, 0.001f);
        Assert.InRange(real[2], 3.999f, 4.001f);  // Pre-computed: 4.0
        Assert.InRange(real[3], -0.001f, 0.001f);
        Assert.InRange(real[4], -0.001f, 0.001f);
        Assert.InRange(real[5], -0.001f, 0.001f);
        Assert.InRange(real[6], 3.999f, 4.001f);  // Pre-computed: 4.0
        Assert.InRange(real[7], -0.001f, 0.001f);

        // Imaginary should be ~0 for cosine
        for (int i = 0; i < 8; i++)
        {
            Assert.InRange(imag[i], -0.001f, 0.001f);
        }
    }

    [Fact]
    public void Forward_PureSine_ProducesCorrectBins()
    {
        var fft = new FastFft(8);

        // sin(2*pi*2*n/8) - frequency at bin 2
        float[] real = new float[8];
        float[] imag = new float[8];
        for (int i = 0; i < 8; i++)
        {
            real[i] = MathF.Sin(2f * MathF.PI * 2f * i / 8f);
            imag[i] = 0f;
        }

        fft.Forward(real, imag);

        // Pre-computed: sine produces imaginary components at bins 2 and 6
        // Bin 2: imag = -4, Bin 6: imag = +4
        Assert.InRange(imag[2], -4.001f, -3.999f);  // Pre-computed: -4.0
        Assert.InRange(imag[6], 3.999f, 4.001f);    // Pre-computed: +4.0

        // Real parts should be ~0 for pure sine
        for (int i = 0; i < 8; i++)
        {
            Assert.InRange(real[i], -0.001f, 0.001f);
        }
    }

    [Fact]
    public void Forward_DcSignal_AllEnergyInBinZero()
    {
        var fft = new FastFft(8);
        float[] real = { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
        float[] imag = new float[8];

        fft.Forward(real, imag);

        // DC signal: all energy in bin 0
        Assert.InRange(real[0], 7.999f, 8.001f);  // Pre-computed: 8.0 (sum of all samples)
        for (int i = 1; i < 8; i++)
        {
            Assert.InRange(real[i], -0.001f, 0.001f);
            Assert.InRange(imag[i], -0.001f, 0.001f);
        }
    }

    [Fact]
    public void Inverse_PureCosineSpectrum_ProducesExpectedSignal()
    {
        var fft = new FastFft(8);

        // Pre-computed with NumPy: ifft([0,0,4,0,0,0,4,0]) -> [1,0,-1,0,1,0,-1,0]
        float[] real = { 0f, 0f, 4f, 0f, 0f, 0f, 4f, 0f };
        float[] imag = new float[8];
        float[] expected = { 1f, 0f, -1f, 0f, 1f, 0f, -1f, 0f };

        fft.Inverse(real, imag);

        for (int i = 0; i < 8; i++)
        {
            Assert.InRange(real[i], expected[i] - 0.0001f, expected[i] + 0.0001f);
            Assert.InRange(imag[i], -0.0001f, 0.0001f);
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1024)]
    public void Constructor_PowerOfTwo_Succeeds(int size)
    {
        var fft = new FastFft(size);
        Assert.Equal(size, fft.Size);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(100)]
    public void Constructor_NotPowerOfTwo_ThrowsArgumentException(int size)
    {
        Assert.Throws<ArgumentException>(() => new FastFft(size));
    }
}
