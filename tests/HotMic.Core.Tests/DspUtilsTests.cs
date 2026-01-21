using HotMic.Core.Dsp;
using Xunit;

namespace HotMic.Core.Tests;

/// <summary>
/// Tests for DSP utility functions.
/// Reference values computed with Python: 10^(dB/20), 20*log10(linear).
/// </summary>
public class DspUtilsTests
{
    // Pre-computed dB to linear conversions: linear = 10^(dB/20)
    [Theory]
    [InlineData(-6f, 0.501187f)]   // Pre-computed: 10^(-6/20) = 0.501187
    [InlineData(-3f, 0.707946f)]   // Pre-computed: 10^(-3/20) = 0.707946
    [InlineData(0f, 1.0f)]         // Pre-computed: 10^(0/20) = 1.0
    [InlineData(3f, 1.412538f)]    // Pre-computed: 10^(3/20) = 1.412538
    [InlineData(6f, 1.995262f)]    // Pre-computed: 10^(6/20) = 1.995262
    [InlineData(20f, 10.0f)]       // Pre-computed: 10^(20/20) = 10.0
    public void DbToLinear_MatchesReference(float db, float expectedLinear)
    {
        float result = DspUtils.DbToLinear(db);
        Assert.InRange(result, expectedLinear - 0.001f, expectedLinear + 0.001f);
    }

    // Pre-computed linear to dB conversions: dB = 20*log10(linear)
    [Theory]
    [InlineData(0.5f, -6.0206f)]      // Pre-computed: 20*log10(0.5) = -6.0206
    [InlineData(0.707107f, -3.0103f)] // Pre-computed: 20*log10(0.707107) = -3.0103
    [InlineData(1.0f, 0f)]            // Pre-computed: 20*log10(1.0) = 0
    [InlineData(1.414214f, 3.0103f)]  // Pre-computed: 20*log10(1.414214) = 3.0103
    [InlineData(2.0f, 6.0206f)]       // Pre-computed: 20*log10(2.0) = 6.0206
    [InlineData(10.0f, 20.0f)]        // Pre-computed: 20*log10(10.0) = 20.0
    public void LinearToDb_MatchesReference(float linear, float expectedDb)
    {
        float result = DspUtils.LinearToDb(linear);
        Assert.InRange(result, expectedDb - 0.01f, expectedDb + 0.01f);
    }

    [Fact]
    public void LinearToDb_ZeroInput_MatchesReferenceFloor()
    {
        // Pre-computed: clamp to 1e-10 -> 20*log10(1e-10) = -200 dB
        float result = DspUtils.LinearToDb(0f);
        Assert.InRange(result, -200.1f, -199.9f);
    }

    [Fact]
    public void LinearToDb_NegativeInput_MatchesReferenceFloor()
    {
        // Pre-computed: clamp to 1e-10 -> 20*log10(1e-10) = -200 dB
        float result = DspUtils.LinearToDb(-1f);
        Assert.InRange(result, -200.1f, -199.9f);
    }

    // Pre-computed time-to-coefficient: 1 - exp(-1/(time_s * sampleRate))
    [Theory]
    [InlineData(10f, 48000, 0.0020811647f)]   // Pre-computed
    [InlineData(50f, 48000, 0.0004165799f)]   // Pre-computed
    [InlineData(100f, 48000, 0.0002083116f)]  // Pre-computed
    [InlineData(10f, 12000, 0.0082987074f)]   // Pre-computed
    public void TimeToCoefficient_MatchesReference(float timeMs, int sampleRate, float expected)
    {
        float result = DspUtils.TimeToCoefficient(timeMs, sampleRate);
        Assert.InRange(result, expected - 0.00001f, expected + 0.00001f);
    }

}
