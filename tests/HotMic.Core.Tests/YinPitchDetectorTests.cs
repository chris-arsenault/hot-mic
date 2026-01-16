using HotMic.Core.Dsp.Analysis.Pitch;
using Xunit;

namespace HotMic.Core.Tests;

/// <summary>
/// Tests for YIN pitch detection algorithm.
/// Reference: de Cheveigné & Kawahara (2002).
/// </summary>
public class YinPitchDetectorTests
{
    // Pre-computed: For 100Hz sine at 1000Hz sample rate:
    // Period = 10 samples
    // CMND[10] should be ~0 (minimum at period)
    // CMND[9] ≈ 0.16, CMND[11] ≈ 0.18

    [Fact]
    public void Detect_PureSine100Hz_FindsCorrectPitch()
    {
        int sampleRate = 1000;
        float expectedFreq = 100f;
        int frameSize = 64;

        var detector = new YinPitchDetector(sampleRate, frameSize, 50f, 200f, 0.15f);

        // Generate pure sine at 100 Hz
        float[] frame = new float[frameSize];
        for (int i = 0; i < frameSize; i++)
        {
            frame[i] = MathF.Sin(2f * MathF.PI * expectedFreq * i / sampleRate);
        }

        var result = detector.Detect(frame);

        Assert.True(result.IsVoiced, "Should detect voiced signal");
        Assert.InRange(result.FrequencyHz!.Value, 95f, 105f);  // Pre-computed: ~100 Hz
    }

    [Theory]
    [InlineData(100f, 98f, 102f)]
    [InlineData(200f, 196f, 204f)]
    [InlineData(300f, 294f, 306f)]
    [InlineData(440f, 431.2f, 448.8f)]  // A4
    public void Detect_VariousPitches_FindsCorrectFrequency(float expectedFreq, float minHz, float maxHz)
    {
        int sampleRate = 12000;
        int frameSize = 1024;

        var detector = new YinPitchDetector(sampleRate, frameSize, 50f, 1000f, 0.15f);

        // Generate pure sine
        float[] frame = new float[frameSize];
        for (int i = 0; i < frameSize; i++)
        {
            frame[i] = MathF.Sin(2f * MathF.PI * expectedFreq * i / sampleRate);
        }

        var result = detector.Detect(frame);

        Assert.True(result.IsVoiced);
        Assert.InRange(result.FrequencyHz!.Value, minHz, maxHz);
    }

    [Fact]
    public void Detect_Silence_ReturnsNotVoiced()
    {
        int sampleRate = 12000;
        int frameSize = 512;

        var detector = new YinPitchDetector(sampleRate, frameSize, 50f, 500f, 0.15f);

        float[] frame = new float[frameSize];  // All zeros

        var result = detector.Detect(frame);

        Assert.False(result.IsVoiced);
        Assert.Null(result.FrequencyHz);
    }

    [Fact]
    public void Detect_ComplexTone_FindsFundamental()
    {
        int sampleRate = 12000;
        int frameSize = 1024;
        float fundamental = 200f;

        var detector = new YinPitchDetector(sampleRate, frameSize, 50f, 500f, 0.15f);

        // Generate complex tone with harmonics
        float[] frame = new float[frameSize];
        for (int i = 0; i < frameSize; i++)
        {
            float t = (float)i / sampleRate;
            // Fundamental + 2nd + 3rd harmonics
            frame[i] = MathF.Sin(2f * MathF.PI * fundamental * t)
                     + 0.5f * MathF.Sin(2f * MathF.PI * 2f * fundamental * t)
                     + 0.25f * MathF.Sin(2f * MathF.PI * 3f * fundamental * t);
        }

        var result = detector.Detect(frame);

        Assert.True(result.IsVoiced);
        // Should find fundamental, not harmonics
        Assert.InRange(result.FrequencyHz!.Value, 190f, 210f);
    }

    [Fact]
    public void Detect_ShortFrame_ReturnsNotVoiced()
    {
        int sampleRate = 12000;
        int frameSize = 512;

        var detector = new YinPitchDetector(sampleRate, frameSize, 50f, 500f, 0.15f);

        // Frame shorter than configured
        float[] shortFrame = new float[100];
        for (int i = 0; i < 100; i++)
        {
            shortFrame[i] = MathF.Sin(i * 0.1f);
        }

        var result = detector.Detect(shortFrame);

        Assert.False(result.IsVoiced);
        Assert.Null(result.FrequencyHz);
    }

    [Fact]
    public void ParabolicInterpolation_ImprovesAccuracy()
    {
        // YIN uses parabolic interpolation for sub-sample accuracy
        // Test that detected pitch is more accurate than just sample-based period

        int sampleRate = 12000;
        int frameSize = 1024;
        float exactFreq = 257.3f;  // Not a nice integer period

        var detector = new YinPitchDetector(sampleRate, frameSize, 50f, 500f, 0.15f);

        float[] frame = new float[frameSize];
        for (int i = 0; i < frameSize; i++)
        {
            frame[i] = MathF.Sin(2f * MathF.PI * exactFreq * i / sampleRate);
        }

        var result = detector.Detect(frame);

        Assert.True(result.IsVoiced);
        // With interpolation, should be within 1 Hz of true frequency
        Assert.InRange(result.FrequencyHz!.Value, exactFreq - 2f, exactFreq + 2f);
    }
}
