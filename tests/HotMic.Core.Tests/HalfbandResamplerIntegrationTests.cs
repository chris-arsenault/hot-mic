using HotMic.Core.Dsp.Filters;
using Xunit;

namespace HotMic.Core.Tests;

public class HalfbandResamplerIntegrationTests
{
    private const float Tolerance = 1e-5f;

    [Fact]
    public void Downsample_MixedSine_MatchesReference()
    {
        // Pre-computed with NumPy (see compute_reference_values.py).
        float[] input =
        [
            0.0f,
            0.910070872f,
            1.01139008f,
            0.604098847f,
            0.560123288f,
            0.963525492f,
            0.980767038f,
            0.156481177f,
            -0.820538079f,
            -1.04768062f,
            -0.65716389f,
            -0.527226458f,
            -0.906994542f,
            -1.03188415f,
            -0.30939551f,
            0.713525492f,
            1.06980373f,
            0.71688684f,
            0.506883103f,
            0.84491142f,
            1.06331351f,
            0.455294712f,
            -0.59096344f,
            -1.07557733f,
            -0.780482109f,
            -0.5f,
            -0.780482109f,
            -1.07557733f,
            -0.59096344f,
            0.455294712f,
            1.06331351f,
            0.84491142f
        ];

        float[] expected =
        [
            0.0f,
            -0.003693044f,
            0.014318273f,
            -0.063709839f,
            0.745706017f,
            0.704437848f,
            0.933080927f,
            0.136852592f,
            -0.985013773f,
            -0.635005571f,
            -0.929577818f,
            0.629081276f,
            0.770029611f,
            0.858635738f,
            0.399228083f,
            -0.986520374f
        ];

        var resampler = new HalfbandResampler();
        float[] output = new float[input.Length / 2];
        resampler.ProcessDownsample(input, output);

        Assert.Equal(expected.Length, output.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.InRange(output[i], expected[i] - Tolerance, expected[i] + Tolerance);
        }
    }

    [Fact]
    public void Downsample_TwoStage_MatchesReference()
    {
        // Pre-computed with NumPy (see compute_reference_values.py).
        float[] input =
        [
            0.0f,
            0.910070872f,
            1.01139008f,
            0.604098847f,
            0.560123288f,
            0.963525492f,
            0.980767038f,
            0.156481177f,
            -0.820538079f,
            -1.04768062f,
            -0.65716389f,
            -0.527226458f,
            -0.906994542f,
            -1.03188415f,
            -0.30939551f,
            0.713525492f,
            1.06980373f,
            0.71688684f,
            0.506883103f,
            0.84491142f,
            1.06331351f,
            0.455294712f,
            -0.59096344f,
            -1.07557733f,
            -0.780482109f,
            -0.5f,
            -0.780482109f,
            -1.07557733f,
            -0.59096344f,
            0.455294712f,
            1.06331351f,
            0.84491142f
        ];

        float[] expected =
        [
            0.0f,
            -0.000052283f,
            -0.002491252f,
            0.007678327f,
            -0.029808904f,
            0.123417084f,
            0.914035588f,
            0.076657211f
        ];

        var stage1 = new HalfbandResampler();
        var stage2 = new HalfbandResampler();
        float[] down1 = new float[input.Length / 2];
        stage1.ProcessDownsample(input, down1);
        float[] down2 = new float[down1.Length / 2];
        stage2.ProcessDownsample(down1, down2);

        Assert.Equal(expected.Length, down2.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.InRange(down2[i], expected[i] - Tolerance, expected[i] + Tolerance);
        }
    }
}
