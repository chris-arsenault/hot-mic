using System;
using System.Diagnostics;
using System.Threading;
using HotMic.Core.Analysis;
using Xunit;

namespace HotMic.Core.Tests;

public sealed class AnalysisOrchestratorTapSelectionTests
{
    [Fact]
    public void AnalysisOrchestrator_ProcessesPluginAndOutputCaptures()
    {
        const int sampleRate = 48000;
        var orchestrator = new AnalysisOrchestrator();
        orchestrator.Initialize(sampleRate);
        var captureLink = new AnalysisCaptureLink { Orchestrator = orchestrator };
        orchestrator.CaptureLink = captureLink;

        using var subscription = orchestrator.Subscribe(AnalysisCapabilities.SpeechMetrics);

        int hopSize = orchestrator.Config.ComputeHopSize();
        var buffer = new float[hopSize];
        FillSine(buffer, sampleRate, 220f);

        long pluginSampleTime = 0;
        long pluginSampleClock = 0;
        orchestrator.Config.VisualizerSource = AnalysisCaptureSource.Plugin;
        long pluginFrame = PumpUntilFrame(orchestrator, captureLink, buffer, AnalysisCaptureSource.Plugin, ref pluginSampleClock, ref pluginSampleTime);

        long outputSampleTime = 0;
        long outputSampleClock = 0;
        orchestrator.Config.VisualizerSource = AnalysisCaptureSource.Output;
        long outputFrame = PumpUntilFrame(orchestrator, captureLink, buffer, AnalysisCaptureSource.Output, ref outputSampleClock, ref outputSampleTime);

        Assert.True(pluginFrame >= 0, "Plugin capture did not advance analysis frames.");
        Assert.True(outputFrame > pluginFrame, "Output capture did not advance analysis frames after plugin capture.");
    }

    private static long PumpUntilFrame(
        AnalysisOrchestrator orchestrator,
        AnalysisCaptureLink captureLink,
        float[] buffer,
        AnalysisCaptureSource source,
        ref long sampleClock,
        ref long sampleTime)
    {
        long startFrame = orchestrator.Results.LatestFrameId;
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < 2000)
        {
            for (int i = 0; i < 6; i++)
            {
                captureLink.Capture(buffer, sampleClock, sampleTime, channelId: 0, bus: null, producers: ReadOnlySpan<int>.Empty, source);
                sampleClock += buffer.Length;
                sampleTime += buffer.Length;
            }

            Thread.Sleep(5);
            long latest = orchestrator.Results.LatestFrameId;
            if (latest > startFrame)
            {
                return latest;
            }
        }

        Assert.Fail($"Timed out waiting for analysis frames (source={source}).");
        return startFrame;
    }

    private static void FillSine(float[] buffer, int sampleRate, float frequency)
    {
        float phase = 0f;
        float phaseStep = 2f * MathF.PI * frequency / sampleRate;
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = MathF.Sin(phase) * 0.2f;
            phase += phaseStep;
            if (phase > MathF.Tau)
            {
                phase -= MathF.Tau;
            }
        }
    }
}
