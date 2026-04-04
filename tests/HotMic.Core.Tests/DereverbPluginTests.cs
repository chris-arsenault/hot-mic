using HotMic.Core.Plugins.BuiltIn;
using Xunit;

namespace HotMic.Core.Tests;

/// <summary>
/// Tests for WPE dereverberation math.
/// Reference values computed with Python (NumPy), matching the frame-online
/// recursive WPE algorithm in DereverbPlugin.ProcessStftFrame.
/// </summary>
public class DereverbPluginTests
{
    // Test parameters
    const int Taps = 5;
    const int Delay = 2;
    const float Alpha = 0.99f;
    const int Bins = 5;
    const int NumFrames = 12;
    const float Reduction = 0.5f;

    // Pre-computed input STFT frames (real part) — NumPy RandomState(42)
    static readonly float[,] InputReal = new float[,] {
        { 0.4967141530f, -0.1382643012f, 0.6476885381f, 1.5230298564f, -0.2341533747f },
        { -0.2341369569f, 1.5792128155f, 0.7674347292f, -0.4694743859f, 0.5425600436f },
        { -0.4634176928f, -0.4657297536f, 0.2419622716f, -1.9132802447f, -1.7249178325f },
        { -0.5622875292f, -1.0128311203f, 0.3142473326f, -0.9080240755f, -1.4123037013f },
        { 1.4656487689f, -0.2257763005f, 0.0675282047f, -1.4247481862f, -0.5443827245f },
        { 0.1109225897f, -1.1509935774f, 0.3756980183f, -0.6006386899f, -0.2916937498f },
        { -0.6017066122f, 1.8522781845f, -0.0134972247f, -1.0577109290f, 0.8225449121f },
        { -1.2208436500f, 0.2088635950f, -1.9596701239f, -1.3281860489f, 0.1968612359f },
        { 0.7384665800f, 0.1713682812f, -0.1156482824f, -0.3011036956f, -1.4785219904f },
        { -0.7198442084f, -0.4606387710f, 1.0571222262f, 0.3436182896f, -1.7630401554f },
        { 0.3240839694f, -0.3850822804f, -0.6769220003f, 0.6116762888f, 1.0309995225f },
        { 0.9312801191f, -0.8392175232f, -0.3092123759f, 0.3312634314f, 0.9755451271f },
    };

    // Pre-computed input STFT frames (imaginary part)
    static readonly float[,] InputImag = new float[,] {
        { -0.4791742378f, -0.1856589767f, -1.1063349740f, -1.1962066241f, 0.8125258224f },
        { 1.3562400286f, -0.0720101216f, 1.0035328979f, 0.3616360250f, -0.6451197546f },
        { 0.3613956055f, 1.5380365665f, -0.0358260391f, 1.5646436558f, -2.6197451041f },
        { 0.8219025044f, 0.0870470682f, -0.2990073505f, 0.0917607765f, -1.9875689146f },
        { -0.2196718878f, 0.3571125715f, 1.4778940447f, -0.5182702183f, -0.8084936029f },
        { -0.5017570436f, 0.9154021177f, 0.3287511097f, -0.5297602038f, 0.5132674331f },
        { 0.0970775493f, 0.9686449905f, -0.7020530939f, -0.3276621466f, -0.3921081531f },
        { -1.4635149481f, 0.2961202771f, 0.2610552722f, 0.0051134566f, -0.2345871334f },
        { -1.4153707421f, -0.4206453228f, -0.3427145165f, -0.8022772692f, -0.1612857117f },
        { 0.4040508568f, 1.8861859012f, 0.1745778128f, 0.2575503907f, -0.0744459158f },
        { -1.9187712153f, -0.0265138754f, 0.0602302099f, 2.4632421125f, -0.1923609648f },
        { 0.3015473423f, -0.0347117697f, -1.1686780376f, 1.1428228145f, 0.7519330327f },
    };

    // Pre-computed expected outputs for active frames (after warmup at frame index 7).
    // Warmup = delay + taps = 7 frames, so first active frame is index 7.
    // Frame 7 is the first active frame but has zero prediction (filter is zeros),
    // so its output equals its input.
    static readonly float[,] ExpectedOutputReal = new float[,] {
        { -1.2208436500f, 0.2088635950f, -1.9596701239f, -1.3281860489f, 0.1968612359f },
        { 0.5968477298f, 0.0696627798f, -0.1902713233f, -0.0741687714f, -1.5549298721f },
        { -1.0054115792f, -0.3899856362f, 0.8236812630f, 0.3046749942f, -1.1318988411f },
        { -0.4032622480f, -0.3385942618f, -0.2883019476f, 0.7940885286f, 1.4855009994f },
        { 1.0396129615f, -0.5593510258f, -0.2373401002f, 0.2862171072f, 0.7641593185f },
    };

    static readonly float[,] ExpectedOutputImag = new float[,] {
        { -1.4635149481f, 0.2961202771f, 0.2610552722f, 0.0051134566f, -0.2345871334f },
        { -1.3686416935f, -0.3629104532f, -0.5280660910f, -0.9143305088f, -0.1168109721f },
        { 0.1851895689f, 1.8671445786f, 0.1788584096f, 0.4261284436f, 0.2054086514f },
        { -1.6469710404f, -0.1300724919f, -1.0056231909f, 2.5461179883f, -0.1219128778f },
        { 0.0256731270f, -0.0123543548f, -0.3665551750f, 0.8385266713f, 0.7546985881f },
    };

    [Fact]
    public void WpeCore_MatchesPythonReference()
    {
        var plugin = new DereverbPlugin();
        plugin.ConfigureWpe(Bins, Taps, Delay, Alpha);

        var outReal = new float[Bins];
        var outImag = new float[Bins];
        var frameReal = new float[Bins];
        var frameImag = new float[Bins];

        int warmup = Delay + Taps;
        int activeIdx = 0;

        for (int t = 0; t < NumFrames; t++)
        {
            for (int f = 0; f < Bins; f++)
            {
                frameReal[f] = InputReal[t, f];
                frameImag[f] = InputImag[t, f];
            }

            plugin.ProcessStftFrame(frameReal, frameImag, outReal, outImag, Reduction);

            if (t >= warmup)
            {
                for (int f = 0; f < Bins; f++)
                {
                    Assert.InRange(outReal[f],
                        ExpectedOutputReal[activeIdx, f] - 1e-3f,
                        ExpectedOutputReal[activeIdx, f] + 1e-3f);
                    Assert.InRange(outImag[f],
                        ExpectedOutputImag[activeIdx, f] - 1e-3f,
                        ExpectedOutputImag[activeIdx, f] + 1e-3f);
                }
                activeIdx++;
            }
        }

        Assert.Equal(ExpectedOutputReal.GetLength(0), activeIdx);
    }

    [Fact]
    public void WpeCore_WarmupFramesPassThrough()
    {
        var plugin = new DereverbPlugin();
        plugin.ConfigureWpe(Bins, Taps, Delay, Alpha);

        var outReal = new float[Bins];
        var outImag = new float[Bins];
        var frameReal = new float[Bins];
        var frameImag = new float[Bins];

        int warmup = Delay + Taps;

        for (int t = 0; t < warmup; t++)
        {
            for (int f = 0; f < Bins; f++)
            {
                frameReal[f] = InputReal[t, f];
                frameImag[f] = InputImag[t, f];
            }

            plugin.ProcessStftFrame(frameReal, frameImag, outReal, outImag, Reduction);

            // During warmup, output should equal input exactly
            for (int f = 0; f < Bins; f++)
            {
                Assert.Equal(InputReal[t, f], outReal[f]);
                Assert.Equal(InputImag[t, f], outImag[f]);
            }
        }
    }

    [Fact]
    public void WpeCore_ZeroReduction_OutputEqualsInput()
    {
        var plugin = new DereverbPlugin();
        plugin.ConfigureWpe(Bins, Taps, Delay, Alpha);

        var outReal = new float[Bins];
        var outImag = new float[Bins];
        var frameReal = new float[Bins];
        var frameImag = new float[Bins];

        // Feed all frames with zero reduction — output should always equal input
        for (int t = 0; t < NumFrames; t++)
        {
            for (int f = 0; f < Bins; f++)
            {
                frameReal[f] = InputReal[t, f];
                frameImag[f] = InputImag[t, f];
            }

            plugin.ProcessStftFrame(frameReal, frameImag, outReal, outImag, reduction: 0f);

            for (int f = 0; f < Bins; f++)
            {
                Assert.InRange(outReal[f], InputReal[t, f] - 1e-6f, InputReal[t, f] + 1e-6f);
                Assert.InRange(outImag[f], InputImag[t, f] - 1e-6f, InputImag[t, f] + 1e-6f);
            }
        }
    }
}
