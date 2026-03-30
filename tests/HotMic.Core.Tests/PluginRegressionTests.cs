using HotMic.Core.Engine;
using HotMic.Core.Plugins;
using HotMic.Core.Plugins.BuiltIn;
using Xunit;

namespace HotMic.Core.Tests;

public sealed class PluginRegressionTests
{
    private const int SampleRate = 48000;
    private const int BlockSize = 64;

    [Fact]
    public void NoiseGate_ClosedStateHonorsConfiguredAttenuationRange()
    {
        var plugin = new NoiseGatePlugin();
        plugin.SetParameter(NoiseGatePlugin.ThresholdIndex, -20f);
        plugin.SetParameter(NoiseGatePlugin.ReleaseIndex, 10f);
        plugin.Initialize(SampleRate, BlockSize);

        float[] block = new float[BlockSize];
        Array.Fill(block, 0.001f); // -60 dBFS, well below threshold.

        for (int i = 0; i < 64; i++)
        {
            Array.Fill(block, 0.001f);
            plugin.Process(block);
        }

        // Expected closed-gate attenuation is capped by GateMaxRangeDb (24 dB by default),
        // so 0.001 should settle near 0.001 * 10^(-24/20) = 0.0000631.
        Assert.InRange(block[^1], 5.0e-5f, 8.0e-5f);
    }

    [Fact]
    public void Saturation_ZeroWarmthFullBlend_IsTransparentAsideFromReportedDelay()
    {
        var plugin = new SaturationPlugin();
        plugin.SetParameter(SaturationPlugin.WarmthIndex, 0f);
        plugin.SetParameter(SaturationPlugin.BlendIndex, 100f);
        plugin.Initialize(SampleRate, BlockSize);

        int latency = plugin.LatencySamples;
        float[] input = BuildImpulseSequence(latency + BlockSize);
        float[] output = ProcessBlocks(input, BlockSize, block => plugin.Process(block.AsSpan()));

        Assert.True(latency > 0);
        for (int i = 0; i < latency; i++)
        {
            Assert.InRange(output[i], -1e-6f, 1e-6f);
        }

        Assert.InRange(output[latency], 0.999f, 1.001f);
        for (int i = latency + 1; i < output.Length; i++)
        {
            Assert.InRange(output[i], -1e-6f, 1e-6f);
        }
    }

    [Fact]
    public void ConvolutionReverb_DryOnlyPath_IsInternallyDelayCompensated()
    {
        var plugin = new ConvolutionReverbPlugin();
        plugin.SetParameter(ConvolutionReverbPlugin.DryWetIndex, 0f);
        plugin.SetParameter(ConvolutionReverbPlugin.IrPresetIndex, 1f);
        plugin.Initialize(SampleRate, BlockSize);

        int latency = plugin.LatencySamples;
        float[] input = BuildImpulseSequence(latency + BlockSize);
        float[] output = ProcessBlocks(input, BlockSize, block => plugin.Process(block.AsSpan()));

        Assert.True(latency > 0);
        for (int i = 0; i < latency; i++)
        {
            Assert.InRange(output[i], -1e-6f, 1e-6f);
        }

        Assert.InRange(output[latency], 0.999f, 1.001f);
    }

    [Fact]
    public void ConvolutionReverb_MediumHallTail_ExtendsBeyondSingleFftWindow()
    {
        var plugin = new ConvolutionReverbPlugin();
        plugin.SetParameter(ConvolutionReverbPlugin.DryWetIndex, 1f);
        plugin.SetParameter(ConvolutionReverbPlugin.IrPresetIndex, 2f); // 1 second hall
        plugin.Initialize(SampleRate, BlockSize);

        float[] input = BuildImpulseSequence(8192);
        float[] output = ProcessBlocks(input, BlockSize, block => plugin.Process(block.AsSpan()));

        float lateEnergy = 0f;
        for (int i = 5000; i < 7000; i++)
        {
            lateEnergy += MathF.Abs(output[i]);
        }

        Assert.True(lateEnergy > 1e-3f, $"Expected audible late tail energy, got {lateEnergy}.");
    }

    [Fact]
    public void BassEnhancer_SteppedVoicingSidechain_IsSmoothedToAvoidAbruptWetJump()
    {
        const int controlBlockSize = 256;

        var plugin = new BassEnhancerPlugin();
        plugin.SetParameter(BassEnhancerPlugin.AmountIndex, 1f);
        plugin.SetParameter(BassEnhancerPlugin.DriveIndex, 1f);
        plugin.SetParameter(BassEnhancerPlugin.MixIndex, 1f);
        plugin.SetParameter(BassEnhancerPlugin.CenterIndex, 110f);
        plugin.Initialize(SampleRate, controlBlockSize);

        float[] input = BuildSineSequence(controlBlockSize, 110f, amplitude: 0.4f);
        float[] buffer = (float[])input.Clone();

        float[] voicing = new float[controlBlockSize];
        Array.Fill(voicing, 0f, 0, controlBlockSize / 2);
        Array.Fill(voicing, 1f, controlBlockSize / 2, controlBlockSize - (controlBlockSize / 2));

        var context = CreateAnalysisContext(controlBlockSize, (AnalysisSignalId.VoicingScore, voicing));
        plugin.Process(buffer.AsSpan(), context);

        float immediateWet = MeanAbsDelta(buffer, input, controlBlockSize / 2, 8);
        float settledWet = MeanAbsDelta(buffer, input, controlBlockSize - 32, 32);

        Assert.True(settledWet > 1e-3f, $"Expected settled wet energy after gate opens, got {settledWet}.");
        Assert.True(immediateWet < settledWet * 0.7f,
            $"Expected smoothed gate onset, but immediate wet {immediateWet} was too close to settled wet {settledWet}.");
    }

    [Fact]
    public void AirExciter_SteppedSibilanceSidechain_IsSmoothedToAvoidAbruptWetDropout()
    {
        const int controlBlockSize = 256;

        var plugin = new AirExciterPlugin();
        plugin.SetParameter(AirExciterPlugin.DriveIndex, 1f);
        plugin.SetParameter(AirExciterPlugin.MixIndex, 1f);
        plugin.SetParameter(AirExciterPlugin.CutoffIndex, 3000f);
        plugin.Initialize(SampleRate, controlBlockSize);

        float[] input = BuildSineSequence(controlBlockSize, 6000f, amplitude: 0.2f);
        float[] buffer = (float[])input.Clone();

        float[] voicing = new float[controlBlockSize];
        float[] sibilance = new float[controlBlockSize];
        Array.Fill(voicing, 1f);
        Array.Fill(sibilance, 0f, 0, controlBlockSize / 2);
        Array.Fill(sibilance, 1f, controlBlockSize / 2, controlBlockSize - (controlBlockSize / 2));

        var context = CreateAnalysisContext(
            controlBlockSize,
            (AnalysisSignalId.VoicingScore, voicing),
            (AnalysisSignalId.SibilanceEnergy, sibilance));
        plugin.Process(buffer.AsSpan(), context);

        float immediateWet = MeanAbsDelta(buffer, input, controlBlockSize / 2, 8);
        float tailWet = MeanAbsDelta(buffer, input, controlBlockSize - 32, 32);

        Assert.True(immediateWet > 1e-3f, $"Expected residual wet energy during smoothed close, got {immediateWet}.");
        Assert.True(immediateWet > tailWet * 1.5f,
            $"Expected wet path to decay instead of dropping out instantly, but immediate wet {immediateWet} was not meaningfully above tail wet {tailWet}.");
    }

    private static float[] BuildImpulseSequence(int length)
    {
        var samples = new float[length];
        samples[0] = 1f;
        return samples;
    }

    private static float[] BuildSineSequence(int length, float frequencyHz, float amplitude)
    {
        var samples = new float[length];
        float phaseStep = 2f * MathF.PI * frequencyHz / SampleRate;
        for (int i = 0; i < length; i++)
        {
            samples[i] = amplitude * MathF.Sin(phaseStep * i);
        }

        return samples;
    }

    private static PluginProcessContext CreateAnalysisContext(
        int blockSize,
        params (AnalysisSignalId Signal, float[] Data)[] signals)
    {
        var routing = new RoutingContext(channelCount: 1, SampleRate, blockSize);
        routing.BeginBlock(0);

        var bus = new AnalysisSignalBus(producerCount: 1, capacitySamples: blockSize * 4);
        var producers = new int[(int)AnalysisSignalId.Count];
        Array.Fill(producers, -1);

        foreach (var (signal, data) in signals)
        {
            producers[(int)signal] = 0;
            bus.WriteBlock(0, signal, sampleTime: 0, data);
        }

        return new PluginProcessContext(
            SampleRate,
            blockSize,
            sampleClock: 0,
            sampleTime: 0,
            slotIndex: 0,
            cumulativeLatencySamples: 0,
            channelId: 0,
            profilingEnabled: false,
            routing,
            analysisCapture: null,
            analysisSignalBus: bus,
            signalProducers: producers,
            producedSignals: AnalysisSignalMask.None,
            requestedSignals: AnalysisSignalMask.None);
    }

    private static float MeanAbsDelta(float[] output, float[] input, int start, int count)
    {
        float sum = 0f;
        for (int i = 0; i < count; i++)
        {
            sum += MathF.Abs(output[start + i] - input[start + i]);
        }

        return sum / Math.Max(1, count);
    }

    private static float[] ProcessBlocks(float[] input, int blockSize, Action<float[]> process)
    {
        var output = new float[input.Length];
        var block = new float[blockSize];

        for (int offset = 0; offset < input.Length; offset += blockSize)
        {
            Array.Clear(block, 0, block.Length);
            int count = Math.Min(blockSize, input.Length - offset);
            Array.Copy(input, offset, block, 0, count);
            process(block);
            Array.Copy(block, 0, output, offset, count);
        }

        return output;
    }
}
