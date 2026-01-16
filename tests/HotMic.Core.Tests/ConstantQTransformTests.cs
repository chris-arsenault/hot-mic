using HotMic.Core.Dsp.Fft;
using Xunit;

namespace HotMic.Core.Tests;

/// <summary>
/// Tests for Constant-Q Transform.
/// Reference values computed with Python.
/// </summary>
public class ConstantQTransformTests
{
    // Pre-computed Q factors: Q = 1 / (2^(1/B) - 1)
    // 12 bins/octave -> Q = 16.817154
    // 24 bins/octave -> Q = 34.127088
    // 48 bins/octave -> Q = 68.750565
    // 96 bins/octave -> Q = 137.999326

    // Pre-computed center frequencies for minHz=55, 12 bins/octave:
    // Bin 0: 55.0 Hz (A1)
    // Bin 12: 110.0 Hz (A2, one octave up)

    [Theory]
    [InlineData(12, 16.817154f)]
    [InlineData(24, 34.127088f)]
    [InlineData(48, 68.750565f)]
    public void QFactor_MatchesFormula(int binsPerOctave, float expectedQ)
    {
        // Q = 1 / (2^(1/B) - 1)
        float calculatedQ = 1f / (MathF.Pow(2f, 1f / binsPerOctave) - 1f);
        Assert.InRange(calculatedQ, expectedQ - 0.001f, expectedQ + 0.001f);
    }

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
    public void CenterFrequencies_FormGeometricSequence()
    {
        var cqt = new ConstantQTransform();
        cqt.Configure(48000, 55f, 880f, 12);

        var freqs = cqt.CenterFrequencies;

        // Each bin should be 2^(1/12) times the previous
        float ratio = MathF.Pow(2f, 1f / 12f);  // Pre-computed: 1.059463

        for (int i = 1; i < freqs.Length && i < 48; i++)
        {
            float expectedRatio = freqs[i] / freqs[i - 1];
            Assert.InRange(expectedRatio, ratio - 0.001f, ratio + 0.001f);
        }
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

        // 440 Hz should be near bin 24 (2 octaves * 12 bins = 24)
        Assert.InRange(maxBin, 22, 26);
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
            Assert.InRange(mag, 0f, 0.001f);
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
    public void GetBinFrequency_MatchesCenterFrequencies()
    {
        var cqt = new ConstantQTransform();
        cqt.Configure(48000, 55f, 440f, 12);

        var freqs = cqt.CenterFrequencies;

        for (int i = 0; i < cqt.BinCount; i++)
        {
            float binFreq = cqt.GetBinFrequency(i);
            Assert.Equal(freqs[i], binFreq, 4);
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
    public void ForwardComplex_ProducesValidComplexOutput()
    {
        var cqt = new ConstantQTransform();
        cqt.Configure(48000, 110f, 440f, 12);

        float[] signal = new float[cqt.MaxWindowLength + 100];
        for (int i = 0; i < signal.Length; i++)
        {
            signal[i] = MathF.Sin(2f * MathF.PI * 220f * i / 48000f);
        }

        float[] realOut = new float[cqt.BinCount];
        float[] imagOut = new float[cqt.BinCount];

        cqt.ForwardComplex(signal, realOut, imagOut);

        // Verify magnitude matches sqrt(real^2 + imag^2)
        float[] magnitudes = new float[cqt.BinCount];
        cqt.Forward(signal, magnitudes);

        for (int i = 0; i < cqt.BinCount; i++)
        {
            float complexMag = MathF.Sqrt(realOut[i] * realOut[i] + imagOut[i] * imagOut[i]);
            Assert.InRange(complexMag, magnitudes[i] - 0.0001f, magnitudes[i] + 0.0001f);
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
    [InlineData(12)]
    [InlineData(24)]
    [InlineData(48)]
    public void Configure_DifferentBinsPerOctave_UpdatesCorrectly(int binsPerOctave)
    {
        var cqt = new ConstantQTransform();
        cqt.Configure(48000, 110f, 440f, binsPerOctave);

        Assert.Equal(binsPerOctave, cqt.BinsPerOctave);
        // 110 to 440 = 2 octaves
        Assert.Equal(binsPerOctave * 2, cqt.BinCount);
    }

    [Fact]
    public void MaxWindowLength_IncreasesWithLowerMinFreq()
    {
        var cqt = new ConstantQTransform();

        cqt.Configure(48000, 110f, 440f, 12);
        int window110 = cqt.MaxWindowLength;

        cqt.Configure(48000, 55f, 440f, 12);
        int window55 = cqt.MaxWindowLength;

        // Lower minimum frequency requires longer window
        Assert.True(window55 > window110, "Lower minFreq should require longer window");
    }
}
