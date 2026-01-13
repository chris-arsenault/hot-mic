using System;

namespace HotMic.Core.Plugins.BuiltIn;

internal interface IDeepFilterNetProcessor : IDisposable
{
    int HopSize { get; }
    int LatencySamples { get; }

    float LastGainReductionDb { get; }
    float LastLsnrDb { get; }
    float LastMaskMin { get; }
    float LastMaskMean { get; }
    float LastMaskMax { get; }

    bool LastApplyGains { get; }
    bool LastApplyGainZeros { get; }
    bool LastApplyDf { get; }

    void Reset();

    void ProcessHop(
        ReadOnlySpan<float> input,
        Span<float> output,
        bool postFilterEnabled,
        float attenLimitDb);
}
