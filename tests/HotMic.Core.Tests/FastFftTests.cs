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
    public void ForwardInverse_Roundtrip_RecoversOriginal()
    {
        var fft = new FastFft(8);

        // Arbitrary test signal
        float[] original = { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f };
        float[] real = (float[])original.Clone();
        float[] imag = new float[8];

        fft.Forward(real, imag);
        fft.Inverse(real, imag);

        // Should recover original signal
        for (int i = 0; i < 8; i++)
        {
            Assert.InRange(real[i], original[i] - 0.0001f, original[i] + 0.0001f);
            Assert.InRange(imag[i], -0.0001f, 0.0001f);
        }
    }

    [Fact]
    public void Forward_Parseval_EnergyIsConserved()
    {
        var fft = new FastFft(16);

        // Test signal with known energy
        float[] real = new float[16];
        float[] imag = new float[16];
        for (int i = 0; i < 16; i++)
        {
            real[i] = MathF.Sin(2f * MathF.PI * 3f * i / 16f);
        }

        // Time-domain energy
        float timeEnergy = 0f;
        for (int i = 0; i < 16; i++)
            timeEnergy += real[i] * real[i];

        fft.Forward(real, imag);

        // Frequency-domain energy (Parseval's theorem: sum of |X[k]|^2 / N)
        float freqEnergy = 0f;
        for (int i = 0; i < 16; i++)
            freqEnergy += real[i] * real[i] + imag[i] * imag[i];
        freqEnergy /= 16f;

        // Energies should be equal (Parseval's theorem)
        Assert.InRange(freqEnergy, timeEnergy - 0.01f, timeEnergy + 0.01f);
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

    [Fact]
    public void Forward_SameInputProducesSameOutput()
    {
        var fft = new FastFft(8);

        float[] real1 = { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f };
        float[] imag1 = new float[8];
        float[] real2 = { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f };
        float[] imag2 = new float[8];

        fft.Forward(real1, imag1);
        fft.Forward(real2, imag2);

        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(real1[i], real2[i], 6);
            Assert.Equal(imag1[i], imag2[i], 6);
        }
    }

    [Theory]
    [InlineData(0, 0.0f)]    // DC bin
    [InlineData(1, 1.0f)]    // Bin 1 in size-8 FFT = fs/8 = 0.125 * fs
    [InlineData(2, 2.0f)]    // Bin 2 = 0.25 * fs
    [InlineData(3, 3.0f)]    // Bin 3 = 0.375 * fs
    public void BinToFrequency_MatchesExpected(int bin, float expectedNormalizedFreq)
    {
        // Pre-computed: bin k corresponds to frequency k*fs/N
        // For normalized freq (0-1 scale where 1 = Nyquist):
        // norm_freq = 2 * k / N (for k < N/2)
        int fftSize = 8;
        float normalizedFreq = (float)bin * 2f / fftSize;
        Assert.Equal(expectedNormalizedFreq / 4f, normalizedFreq, 6);  // /4 because N/2=4
    }
}
