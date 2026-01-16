using HotMic.Core.Dsp.Analysis.Pitch;
using Xunit;

namespace HotMic.Core.Tests;

/// <summary>
/// Tests for YIN pitch detection algorithm.
/// Reference: de Cheveigne & Kawahara (2002).
/// Expected frequencies pre-computed with Python/NumPy using a canonical YIN implementation.
/// Script: tests/HotMic.Core.Tests/compute_reference_values.py
/// </summary>
public class YinPitchDetectorTests
{
    // Pre-computed with Python/NumPy: canonical YIN (CMND + threshold + parabolic interp).

    [Fact]
    public void Detect_PureSine100Hz_FindsCorrectPitch()
    {
        int sampleRate = 1000;
        float inputFreq = 100f;
        float expectedFreq = 100.343994f;
        int frameSize = 64;

        var detector = new YinPitchDetector(sampleRate, frameSize, 50f, 200f, 0.15f);

        // Generate pure sine at 100 Hz
        float[] frame = new float[frameSize];
        for (int i = 0; i < frameSize; i++)
        {
            frame[i] = MathF.Sin(2f * MathF.PI * inputFreq * i / sampleRate);
        }

        var result = detector.Detect(frame);

        Assert.True(result.IsVoiced, "Should detect voiced signal");
        Assert.InRange(result.FrequencyHz!.Value, expectedFreq - 0.1f, expectedFreq + 0.1f);
    }

    [Theory]
    [InlineData(55f, 55.000366f)]
    [InlineData(100f, 100.002571f)]
    [InlineData(200f, 200.024490f)]
    [InlineData(300f, 300.086700f)]
    [InlineData(440f, 440.236755f)]  // A4
    [InlineData(950f, 951.203796f)]
    public void Detect_VariousPitches_FindsCorrectFrequency(float inputFreq, float expectedFreq)
    {
        int sampleRate = 12000;
        int frameSize = 1024;

        var detector = new YinPitchDetector(sampleRate, frameSize, 50f, 1000f, 0.15f);

        // Generate pure sine
        float[] frame = new float[frameSize];
        for (int i = 0; i < frameSize; i++)
        {
            frame[i] = MathF.Sin(2f * MathF.PI * inputFreq * i / sampleRate);
        }

        var result = detector.Detect(frame);

        Assert.True(result.IsVoiced);
        Assert.InRange(result.FrequencyHz!.Value, expectedFreq - 0.1f, expectedFreq + 0.1f);
    }

    [Fact]
    public void Detect_ComplexTone_FindsFundamental()
    {
        int sampleRate = 12000;
        int frameSize = 1024;
        float fundamental = 200f;
        float expectedFreq = 200.019974f;

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
        Assert.InRange(result.FrequencyHz!.Value, expectedFreq - 0.1f, expectedFreq + 0.1f);
    }

    [Fact]
    public void Detect_PureSine257_3Hz_MatchesReference()
    {
        int sampleRate = 12000;
        int frameSize = 1024;
        float inputFreq = 257.3f;
        float expectedFreq = 257.330322f;

        var detector = new YinPitchDetector(sampleRate, frameSize, 50f, 500f, 0.15f);

        float[] frame = new float[frameSize];
        for (int i = 0; i < frameSize; i++)
        {
            frame[i] = MathF.Sin(2f * MathF.PI * inputFreq * i / sampleRate);
        }

        var result = detector.Detect(frame);

        Assert.True(result.IsVoiced);
        Assert.InRange(result.FrequencyHz!.Value, expectedFreq - 0.1f, expectedFreq + 0.1f);
    }
}
