using System;
using HotMic.Core.Dsp;
using Xunit;

namespace HotMic.Core.Tests.Dsp;

public class DeepFilterNetStftTests
{
    [Fact]
    public void DeepFilterNetStft_RoundTrip_ReconstructsSignal_960()
    {
        const int fftSize = 960;
        const int hopSize = 480;
        const int hopCount = 10;

        var stft = new DeepFilterNetStft(fftSize, hopSize);
        int totalSamples = hopSize * hopCount;
        var input = new float[totalSamples];
        var output = new float[totalSamples];

        for (int i = 0; i < totalSamples; i++)
        {
            float t = i / 48000f;
            input[i] = 0.6f * MathF.Sin(2f * MathF.PI * 440f * t)
                       + 0.3f * MathF.Cos(2f * MathF.PI * 1200f * t)
                       + 0.1f * MathF.Sin(2f * MathF.PI * 3300f * t);
        }

        var hop = new float[hopSize];
        var hopOut = new float[hopSize];
        var spec = new float[(fftSize / 2 + 1) * 2];

        int inIndex = 0;
        int outIndex = 0;
        for (int hopIndex = 0; hopIndex < hopCount; hopIndex++)
        {
            Array.Copy(input, inIndex, hop, 0, hopSize);
            stft.Analyze(hop, spec);
            stft.Synthesize(spec, hopOut);
            Array.Copy(hopOut, 0, output, outIndex, hopSize);
            inIndex += hopSize;
            outIndex += hopSize;
        }

        // Overlap-add introduces fftSize-hopSize samples of latency.
        int delay = fftSize - hopSize;
        float maxError = 0f;
        for (int i = delay; i < totalSamples; i++)
        {
            float expected = input[i - delay];
            float err = MathF.Abs(output[i] - expected);
            if (err > maxError)
            {
                maxError = err;
            }
        }

        Assert.InRange(maxError, 0f, 1e-3f);
    }
}
