using HotMic.Core.Dsp.Fft;
using Xunit;

namespace HotMic.Core.Tests;

/// <summary>
/// Tests for Constant-Q Transform.
/// Reference values computed with Python.
/// </summary>
public class ConstantQTransformTests
{
    // Pre-computed center frequencies for minHz=55, 12 bins/octave:
    // Bin 0: 55.0 Hz (A1)
    // Bin 12: 110.0 Hz (A2, one octave up)

    [Fact]
    public void Configure_SetsCorrectBinCount()
    {
        var cqt = new ConstantQTransform();

        // 55 Hz to 880 Hz = 4 octaves, 12 bins/octave = 48 bins
        cqt.Configure(48000, 55f, 880f, 12);

        Assert.Equal(48, cqt.BinCount);
        Assert.Equal(12, cqt.BinsPerOctave);
    }

    [Fact]
    public void CenterFrequencies_MatchReference()
    {
        var cqt = new ConstantQTransform();
        cqt.Configure(48000, 55f, 880f, 12);

        var freqs = cqt.CenterFrequencies;

        // Pre-computed reference values
        Assert.InRange(freqs[0], 54.9f, 55.1f);     // 55.0 Hz
        Assert.InRange(freqs[1], 58.2f, 58.4f);     // 58.27 Hz
        Assert.InRange(freqs[12], 109.9f, 110.1f);  // 110.0 Hz (one octave up)
        Assert.InRange(freqs[24], 219.9f, 220.1f);  // 220.0 Hz (two octaves up)
    }

    [Fact]
    public void Forward_SineAtBinFrequency_ProducesPeakAtThatBin()
    {
        var cqt = new ConstantQTransform();
        cqt.Configure(48000, 110f, 880f, 12);

        // Generate sine at A4 = 440 Hz
        // 440 Hz is 2 octaves above 110 Hz = bin 24
        float[] signal = new float[cqt.MaxWindowLength + 1000];
        for (int i = 0; i < signal.Length; i++)
        {
            signal[i] = MathF.Sin(2f * MathF.PI * 440f * i / 48000f);
        }

        float[] magnitudes = new float[cqt.BinCount];
        cqt.Forward(signal, magnitudes);

        // Find bin with maximum magnitude
        int maxBin = 0;
        float maxMag = 0f;
        for (int i = 0; i < magnitudes.Length; i++)
        {
            if (magnitudes[i] > maxMag)
            {
                maxMag = magnitudes[i];
                maxBin = i;
            }
        }

        // 440 Hz is exactly 2 octaves above 110 Hz -> bin 24
        Assert.Equal(24, maxBin);
    }

    [Fact]
    public void Forward_Silence_ProducesZeroOutput()
    {
        var cqt = new ConstantQTransform();
        cqt.Configure(48000, 110f, 440f, 12);

        float[] signal = new float[cqt.MaxWindowLength + 100];  // All zeros
        float[] magnitudes = new float[cqt.BinCount];

        cqt.Forward(signal, magnitudes);

        foreach (float mag in magnitudes)
        {
            Assert.InRange(mag, -0.0001f, 0.0001f);
        }
    }

    [Fact]
    public void Forward_ShortInput_ReturnsZeros()
    {
        var cqt = new ConstantQTransform();
        cqt.Configure(48000, 110f, 440f, 12);

        // Input shorter than MaxWindowLength
        float[] shortSignal = new float[10];
        float[] magnitudes = new float[cqt.BinCount];

        cqt.Forward(shortSignal, magnitudes);

        // Should return zeros, not crash
        foreach (float mag in magnitudes)
        {
            Assert.Equal(0f, mag);
        }
    }

    [Fact]
    public void GetBinFrequency_InvalidBin_ReturnsZero()
    {
        var cqt = new ConstantQTransform();
        cqt.Configure(48000, 110f, 440f, 12);

        Assert.Equal(0f, cqt.GetBinFrequency(-1));
        Assert.Equal(0f, cqt.GetBinFrequency(1000));
    }

    [Fact]
    public void ForwardComplex_Silence_ProducesZeroOutput()
    {
        var cqt = new ConstantQTransform();
        cqt.Configure(48000, 110f, 440f, 12);

        float[] signal = new float[cqt.MaxWindowLength + 100];

        float[] realOut = new float[cqt.BinCount];
        float[] imagOut = new float[cqt.BinCount];

        cqt.ForwardComplex(signal, realOut, imagOut);

        for (int i = 0; i < cqt.BinCount; i++)
        {
            Assert.InRange(realOut[i], -0.0001f, 0.0001f);
            Assert.InRange(imagOut[i], -0.0001f, 0.0001f);
        }
    }

    [Fact]
    public void Reset_ClearsPreviousPhase()
    {
        var cqt = new ConstantQTransform();
        cqt.Configure(48000, 110f, 440f, 12);

        float[] signal = new float[cqt.MaxWindowLength + 100];
        for (int i = 0; i < signal.Length; i++)
        {
            signal[i] = MathF.Sin(2f * MathF.PI * 220f * i / 48000f);
        }

        // Process twice to build up phase history
        float[] mags = new float[cqt.BinCount];
        float[] real = new float[cqt.BinCount];
        float[] imag = new float[cqt.BinCount];
        float[] timeReal = new float[cqt.BinCount];
        float[] timeImag = new float[cqt.BinCount];
        float[] phaseDiff = new float[cqt.BinCount];

        cqt.ForwardWithReassignment(signal, mags, real, imag, timeReal, timeImag, phaseDiff);
        cqt.ForwardWithReassignment(signal, mags, real, imag, timeReal, timeImag, phaseDiff);

        // Reset should clear phase history
        cqt.Reset();

        // After reset, first frame should have zero phase diff (no previous)
        cqt.ForwardWithReassignment(signal, mags, real, imag, timeReal, timeImag, phaseDiff);

        foreach (float pd in phaseDiff)
        {
            Assert.Equal(0f, pd);  // No previous frame = zero phase diff
        }
    }

    [Theory]
    [InlineData(12, 24)]
    [InlineData(24, 48)]
    [InlineData(48, 96)]
    public void Configure_DifferentBinsPerOctave_UpdatesCorrectly(int binsPerOctave, int expectedBinCount)
    {
        var cqt = new ConstantQTransform();
        cqt.Configure(48000, 110f, 440f, binsPerOctave);

        Assert.Equal(binsPerOctave, cqt.BinsPerOctave);
        // 110 to 440 = 2 octaves (pre-computed bin count)
        Assert.Equal(expectedBinCount, cqt.BinCount);
    }

    [Theory]
    [InlineData(0, 55.0000f)]
    [InlineData(1, 58.2705f)]
    [InlineData(12, 110.0000f)]
    [InlineData(24, 220.0000f)]
    public void GetBinFrequency_MatchesReferenceValues(int bin, float expectedHz)
    {
        var cqt = new ConstantQTransform();
        cqt.Configure(48000, 55f, 440f, 12);

        float binFreq = cqt.GetBinFrequency(bin);
        Assert.InRange(binFreq, expectedHz - 0.01f, expectedHz + 0.01f);
    }
}
