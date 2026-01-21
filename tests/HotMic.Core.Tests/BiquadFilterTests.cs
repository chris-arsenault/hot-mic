using HotMic.Core.Dsp.Filters;
using Xunit;

namespace HotMic.Core.Tests;

/// <summary>
/// Tests for biquad filter coefficient calculation.
/// Reference values computed with Python using standard audio EQ cookbook formulas.
/// </summary>
public class BiquadFilterTests
{
    // Pre-computed coefficients for low-pass filter
    // fs=48000, fc=1000, Q=0.707 (Butterworth)
    // omega = 2*pi*1000/48000 = 0.1309
    // alpha = sin(omega)/(2*Q) = 0.0924
    [Fact]
    public void SetLowPass_ProducesExpectedResponse()
    {
        var filter = new BiquadFilter();
        filter.SetLowPass(48000f, 1000f, 0.707f);

        // Test response: DC (0 Hz) should pass through unchanged
        filter.Reset();
        float dcInput = 1.0f;
        float dcOutput = 0f;

        // Run several samples to let filter settle
        for (int i = 0; i < 1000; i++)
        {
            dcOutput = filter.Process(dcInput);
        }

        // Low-pass should pass DC (gain ~1.0)
        Assert.InRange(dcOutput, 0.99f, 1.01f);
    }

    [Fact]
    public void SetHighPass_AttenuatesDc()
    {
        var filter = new BiquadFilter();
        filter.SetHighPass(48000f, 1000f, 0.707f);

        filter.Reset();
        float dcInput = 1.0f;
        float dcOutput = 0f;

        // Run several samples to let filter settle
        for (int i = 0; i < 1000; i++)
        {
            dcOutput = filter.Process(dcInput);
        }

        // High-pass should attenuate DC (gain ~0)
        Assert.InRange(dcOutput, -0.01f, 0.01f);
    }

    [Fact]
    public void SetBandPass_DcGain_IsZero()
    {
        var filter = new BiquadFilter();
        filter.SetBandPass(48000f, 1000f, 1.0f);

        filter.Reset();

        float output = 0f;
        for (int i = 0; i < 1000; i++)
        {
            output = filter.Process(1.0f);
        }

        Assert.InRange(output, -0.001f, 0.001f);
    }

    [Theory]
    [InlineData(0f)]      // 0 dB gain
    [InlineData(6f)]      // +6 dB boost
    [InlineData(-6f)]     // -6 dB cut
    [InlineData(12f)]     // +12 dB boost
    public void SetPeaking_DcGain_IsUnity(float gainDb)
    {
        var filter = new BiquadFilter();
        filter.SetPeaking(48000f, 1000f, gainDb, 1.0f);

        filter.Reset();

        float output = 0f;
        for (int i = 0; i < 1000; i++)
        {
            output = filter.Process(1.0f);
        }

        Assert.InRange(output, 0.99f, 1.01f);
    }

    [Fact]
    public void SetLowShelf_DcGain_MatchesReference()
    {
        var filter = new BiquadFilter();
        filter.SetLowShelf(48000f, 1000f, 6f, 0.707f);

        filter.Reset();

        float output = 0f;
        for (int i = 0; i < 2000; i++)
        {
            output = filter.Process(1.0f);
        }

        // Pre-computed: +6 dB -> 10^(6/20) = 1.995262
        Assert.InRange(output, 1.98f, 2.01f);
    }

    [Fact]
    public void SetHighShelf_DcGain_IsUnity()
    {
        var filter = new BiquadFilter();
        filter.SetHighShelf(48000f, 1000f, 6f, 0.707f);

        filter.Reset();

        float output = 0f;
        for (int i = 0; i < 2000; i++)
        {
            output = filter.Process(1.0f);
        }

        Assert.InRange(output, 0.99f, 1.01f);
    }
    [Fact]
    public void Reset_ClearsState()
    {
        var filter = new BiquadFilter();
        filter.SetLowPass(48000f, 1000f, 0.707f);

        // Process some samples to build up state
        for (int i = 0; i < 100; i++)
        {
            filter.Process(MathF.Sin(i * 0.1f));
        }

        filter.Reset();

        // After reset, filter state should be cleared
        // Processing zero should give zero output
        float output = filter.Process(0f);
        Assert.Equal(0f, output, 6);
    }

}
