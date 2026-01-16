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
    public void SetLowPass_AttenuatesHighFrequency()
    {
        var filter = new BiquadFilter();
        filter.SetLowPass(48000f, 1000f, 0.707f);

        filter.Reset();

        // Generate high frequency signal (10 kHz, well above 1kHz cutoff)
        float freq = 10000f;
        float sampleRate = 48000f;
        float maxOutput = 0f;

        for (int i = 0; i < 1000; i++)
        {
            float input = MathF.Sin(2f * MathF.PI * freq * i / sampleRate);
            float output = filter.Process(input);
            if (i > 100)  // Skip transient
            {
                maxOutput = MathF.Max(maxOutput, MathF.Abs(output));
            }
        }

        // 10kHz is ~3.3 octaves above 1kHz cutoff
        // At -12dB/octave (2nd order), expect ~-40dB attenuation
        // Linear: 10^(-40/20) = 0.01
        Assert.True(maxOutput < 0.1f, $"Expected significant attenuation at 10kHz, got max output {maxOutput}");
    }

    [Fact]
    public void SetHighPass_AttenuatesLowFrequency()
    {
        var filter = new BiquadFilter();
        filter.SetHighPass(48000f, 1000f, 0.707f);

        filter.Reset();

        // Generate low frequency signal (100 Hz, well below 1kHz cutoff)
        float freq = 100f;
        float sampleRate = 48000f;
        float maxOutput = 0f;

        for (int i = 0; i < 2000; i++)
        {
            float input = MathF.Sin(2f * MathF.PI * freq * i / sampleRate);
            float output = filter.Process(input);
            if (i > 500)  // Skip transient (longer for low freq)
            {
                maxOutput = MathF.Max(maxOutput, MathF.Abs(output));
            }
        }

        // 100Hz is ~3.3 octaves below 1kHz cutoff
        Assert.True(maxOutput < 0.15f, $"Expected significant attenuation at 100Hz, got max output {maxOutput}");
    }

    [Fact]
    public void SetBandPass_PassesCenterFrequency()
    {
        var filter = new BiquadFilter();
        float centerFreq = 1000f;
        filter.SetBandPass(48000f, centerFreq, 1.0f);

        filter.Reset();

        float maxOutput = 0f;
        float sampleRate = 48000f;

        for (int i = 0; i < 1000; i++)
        {
            float input = MathF.Sin(2f * MathF.PI * centerFreq * i / sampleRate);
            float output = filter.Process(input);
            if (i > 100)
            {
                maxOutput = MathF.Max(maxOutput, MathF.Abs(output));
            }
        }

        // Band-pass should pass center frequency with minimal attenuation
        Assert.True(maxOutput > 0.3f, $"Band-pass should pass center frequency, got {maxOutput}");
    }

    [Theory]
    [InlineData(0f)]      // 0 dB gain
    [InlineData(6f)]      // +6 dB boost
    [InlineData(-6f)]     // -6 dB cut
    [InlineData(12f)]     // +12 dB boost
    public void SetPeaking_DoesNotThrow(float gainDb)
    {
        var filter = new BiquadFilter();

        // Should not throw for any reasonable gain value
        filter.SetPeaking(48000f, 1000f, gainDb, 1.0f);
        filter.Reset();

        // Process some samples to verify filter is stable
        for (int i = 0; i < 100; i++)
        {
            float output = filter.Process(1.0f);
            Assert.True(float.IsFinite(output), $"Filter output should be finite, got {output}");
        }
    }

    [Fact]
    public void SetLowShelf_BoostsLowFrequencies()
    {
        var filter = new BiquadFilter();
        filter.SetLowShelf(48000f, 1000f, 6f, 0.707f);  // +6dB boost below 1kHz

        filter.Reset();

        // Generate low frequency signal (100 Hz)
        float freq = 100f;
        float sampleRate = 48000f;
        float maxOutput = 0f;

        for (int i = 0; i < 2000; i++)
        {
            float input = MathF.Sin(2f * MathF.PI * freq * i / sampleRate);
            float output = filter.Process(input);
            if (i > 500)
            {
                maxOutput = MathF.Max(maxOutput, MathF.Abs(output));
            }
        }

        // +6dB boost = ~2x amplitude
        Assert.True(maxOutput > 1.5f, $"Low shelf should boost low frequencies, got {maxOutput}");
    }

    [Fact]
    public void SetHighShelf_BoostsHighFrequencies()
    {
        var filter = new BiquadFilter();
        filter.SetHighShelf(48000f, 1000f, 6f, 0.707f);  // +6dB boost above 1kHz

        filter.Reset();

        // Generate high frequency signal (10 kHz)
        float freq = 10000f;
        float sampleRate = 48000f;
        float maxOutput = 0f;

        for (int i = 0; i < 1000; i++)
        {
            float input = MathF.Sin(2f * MathF.PI * freq * i / sampleRate);
            float output = filter.Process(input);
            if (i > 100)
            {
                maxOutput = MathF.Max(maxOutput, MathF.Abs(output));
            }
        }

        // +6dB boost = ~2x amplitude
        Assert.True(maxOutput > 1.5f, $"High shelf should boost high frequencies, got {maxOutput}");
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

    [Fact]
    public void Process_SameInputSameOutput()
    {
        var filter1 = new BiquadFilter();
        var filter2 = new BiquadFilter();
        filter1.SetLowPass(48000f, 1000f, 0.707f);
        filter2.SetLowPass(48000f, 1000f, 0.707f);

        for (int i = 0; i < 100; i++)
        {
            float input = MathF.Sin(i * 0.3f);
            float output1 = filter1.Process(input);
            float output2 = filter2.Process(input);
            Assert.Equal(output1, output2, 6);
        }
    }
}
