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
    public void LinearToDb_ZeroInput_ReturnsLargeNegative()
    {
        // Should not throw and should return a large negative value
        float result = DspUtils.LinearToDb(0f);
        Assert.True(result < -100f, $"Expected large negative dB for 0 input, got {result}");
    }

    [Fact]
    public void LinearToDb_NegativeInput_ReturnsLargeNegative()
    {
        // Implementation clamps to floor (1e-10), so negative input treated as ~0
        float result = DspUtils.LinearToDb(-1f);
        Assert.True(result < -100f);
    }

    [Fact]
    public void DbToLinear_Roundtrip_RecoversOriginal()
    {
        float[] testValues = { -20f, -10f, -6f, -3f, 0f, 3f, 6f, 10f, 20f };
        foreach (float db in testValues)
        {
            float linear = DspUtils.DbToLinear(db);
            float recovered = DspUtils.LinearToDb(linear);
            Assert.InRange(recovered, db - 0.01f, db + 0.01f);
        }
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

    [Theory]
    [InlineData(0f)]      // Zero time should clamp to minimum
    [InlineData(-10f)]    // Negative time should clamp
    public void TimeToCoefficient_EdgeCases_DoesNotThrow(float timeMs)
    {
        // Should not throw
        float result = DspUtils.TimeToCoefficient(timeMs, 48000);
        Assert.True(float.IsFinite(result));
        Assert.True(result > 0f && result < 1f);
    }

    [Fact]
    public void TimeToCoefficient_LongerTime_SmallerCoefficient()
    {
        // Longer time constants should produce smaller coefficients (slower response)
        float coeff10ms = DspUtils.TimeToCoefficient(10f, 48000);
        float coeff100ms = DspUtils.TimeToCoefficient(100f, 48000);
        float coeff1000ms = DspUtils.TimeToCoefficient(1000f, 48000);

        Assert.True(coeff10ms > coeff100ms, "10ms should have larger coeff than 100ms");
        Assert.True(coeff100ms > coeff1000ms, "100ms should have larger coeff than 1000ms");
    }

    [Fact]
    public void TimeToCoefficient_HigherSampleRate_SmallerCoefficient()
    {
        // Higher sample rate = more samples per time period = smaller per-sample coefficient
        float coeff12k = DspUtils.TimeToCoefficient(10f, 12000);
        float coeff48k = DspUtils.TimeToCoefficient(10f, 48000);

        Assert.True(coeff12k > coeff48k, "12kHz should have larger coeff than 48kHz for same time");
    }
}
