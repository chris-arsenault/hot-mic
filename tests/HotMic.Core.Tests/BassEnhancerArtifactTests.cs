using System;
using System.Collections.Generic;
using System.Globalization;
using HotMic.Core.Analysis;
using HotMic.Core.Dsp.Analysis;
using HotMic.Core.Dsp.Spectrogram;
using HotMic.Core.Engine;
using HotMic.Core.Presets;
using HotMic.Core.Plugins;
using HotMic.Core.Plugins.BuiltIn;
using Xunit;

namespace HotMic.Core.Tests;

public sealed class BassEnhancerArtifactTests
{
    [Fact]
    public void BassEnhancer_FullPipeline_DoesNotIntroduceBroadbandArtifacts()
    {
        string path = SpeechMetricsTestHelpers.FindRepoFile(SpeechMetricsTestHelpers.TestWavRelativePath);
        float[] samples = SpeechMetricsTestHelpers.LoadMonoSamples(path, out int sampleRate);
        Assert.True(sampleRate == 48000, $"Bass enhancer pipeline test requires 48kHz input (got {sampleRate}Hz).");

        var baselinePre = RunFullPipeline(samples, sampleRate, bassEnabled: false, SplitMode.PreBass, PipelineVariant.Full);
        var baselinePost = RunFullPipeline(samples, sampleRate, bassEnabled: false, SplitMode.PostBass, PipelineVariant.Full);
        var enhancedPre = RunFullPipeline(samples, sampleRate, bassEnabled: true, SplitMode.PreBass, PipelineVariant.Full);
        var enhancedPost = RunFullPipeline(samples, sampleRate, bassEnabled: true, SplitMode.PostBass, PipelineVariant.Full);

        var minimalBaselinePre = RunFullPipeline(samples, sampleRate, bassEnabled: false, SplitMode.PreBass, PipelineVariant.PostDisabled);
        var minimalBaselinePost = RunFullPipeline(samples, sampleRate, bassEnabled: false, SplitMode.PostBass, PipelineVariant.PostDisabled);
        var minimalEnhancedPre = RunFullPipeline(samples, sampleRate, bassEnabled: true, SplitMode.PreBass, PipelineVariant.PostDisabled);
        var minimalEnhancedPost = RunFullPipeline(samples, sampleRate, bassEnabled: true, SplitMode.PostBass, PipelineVariant.PostDisabled);

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "BassEnhancerArtifacts baseline pre ratio={0:0.000} flat={1:0.000} end ratio={2:0.000} flat={3:0.000}",
            baselinePre.Mid.HighBandRatioMean,
            baselinePre.Mid.HighBandFlatnessMean,
            baselinePre.End.HighBandRatioMean,
            baselinePre.End.HighBandFlatnessMean));
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "BassEnhancerArtifacts baseline post ratio={0:0.000} flat={1:0.000} end ratio={2:0.000} flat={3:0.000}",
            baselinePost.Mid.HighBandRatioMean,
            baselinePost.Mid.HighBandFlatnessMean,
            baselinePost.End.HighBandRatioMean,
            baselinePost.End.HighBandFlatnessMean));
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "BassEnhancerArtifacts enhanced pre ratio={0:0.000} flat={1:0.000} end ratio={2:0.000} flat={3:0.000}",
            enhancedPre.Mid.HighBandRatioMean,
            enhancedPre.Mid.HighBandFlatnessMean,
            enhancedPre.End.HighBandRatioMean,
            enhancedPre.End.HighBandFlatnessMean));
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "BassEnhancerArtifacts enhanced post ratio={0:0.000} flat={1:0.000} end ratio={2:0.000} flat={3:0.000}",
            enhancedPost.Mid.HighBandRatioMean,
            enhancedPost.Mid.HighBandFlatnessMean,
            enhancedPost.End.HighBandRatioMean,
            enhancedPost.End.HighBandFlatnessMean));
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "BassEnhancerArtifacts minimal baseline pre ratio={0:0.000} flat={1:0.000} end ratio={2:0.000} flat={3:0.000}",
            minimalBaselinePre.Mid.HighBandRatioMean,
            minimalBaselinePre.Mid.HighBandFlatnessMean,
            minimalBaselinePre.End.HighBandRatioMean,
            minimalBaselinePre.End.HighBandFlatnessMean));
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "BassEnhancerArtifacts minimal baseline post ratio={0:0.000} flat={1:0.000} end ratio={2:0.000} flat={3:0.000}",
            minimalBaselinePost.Mid.HighBandRatioMean,
            minimalBaselinePost.Mid.HighBandFlatnessMean,
            minimalBaselinePost.End.HighBandRatioMean,
            minimalBaselinePost.End.HighBandFlatnessMean));
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "BassEnhancerArtifacts minimal enhanced pre ratio={0:0.000} flat={1:0.000} end ratio={2:0.000} flat={3:0.000}",
            minimalEnhancedPre.Mid.HighBandRatioMean,
            minimalEnhancedPre.Mid.HighBandFlatnessMean,
            minimalEnhancedPre.End.HighBandRatioMean,
            minimalEnhancedPre.End.HighBandFlatnessMean));
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "BassEnhancerArtifacts minimal enhanced post ratio={0:0.000} flat={1:0.000} end ratio={2:0.000} flat={3:0.000}",
            minimalEnhancedPost.Mid.HighBandRatioMean,
            minimalEnhancedPost.Mid.HighBandFlatnessMean,
            minimalEnhancedPost.End.HighBandRatioMean,
            minimalEnhancedPost.End.HighBandFlatnessMean));
        Console.WriteLine(baselinePre.Debug.FormatSummary("BassEnhancerArtifacts baseline pre debug"));
        Console.WriteLine(baselinePost.Debug.FormatSummary("BassEnhancerArtifacts baseline post debug"));
        Console.WriteLine(enhancedPre.Debug.FormatSummary("BassEnhancerArtifacts enhanced pre debug"));
        Console.WriteLine(enhancedPost.Debug.FormatSummary("BassEnhancerArtifacts enhanced post debug"));
        Console.WriteLine(minimalBaselinePre.Debug.FormatSummary("BassEnhancerArtifacts minimal baseline pre debug"));
        Console.WriteLine(minimalBaselinePost.Debug.FormatSummary("BassEnhancerArtifacts minimal baseline post debug"));
        Console.WriteLine(minimalEnhancedPre.Debug.FormatSummary("BassEnhancerArtifacts minimal enhanced pre debug"));
        Console.WriteLine(minimalEnhancedPost.Debug.FormatSummary("BassEnhancerArtifacts minimal enhanced post debug"));

        const float maxNoiseDelta = 0.10f;
        float baselineEnd = baselinePre.End.HighBandRatioMean;
        float enhancedEnd = enhancedPre.End.HighBandRatioMean;
        float preToPostDelta = enhancedPost.Mid.HighBandRatioMean - enhancedPre.Mid.HighBandRatioMean;
        float baselineEndToPostDelta = baselinePost.End.HighBandRatioMean - baselinePost.Mid.HighBandRatioMean;
        float endToPostDelta = enhancedPost.End.HighBandRatioMean - enhancedPost.Mid.HighBandRatioMean;
        float flatnessDelta = enhancedPost.Mid.HighBandFlatnessMean - enhancedPre.Mid.HighBandFlatnessMean;

        float minimalPreToPostDelta = minimalEnhancedPost.Mid.HighBandRatioMean - minimalEnhancedPre.Mid.HighBandRatioMean;
        float minimalEndToPostDelta = minimalEnhancedPost.End.HighBandRatioMean - minimalEnhancedPost.Mid.HighBandRatioMean;

        Assert.True(
            enhancedEnd <= baselineEnd + maxNoiseDelta,
            $"Bass enhancer introduced broadband artifacts: end ratio {enhancedEnd:0.000} exceeds baseline {baselineEnd:0.000}+{maxNoiseDelta:0.000}.");

        Assert.True(
            preToPostDelta <= maxNoiseDelta,
            $"Bass enhancer noise delta too large between pre/post taps: {preToPostDelta:0.000} > {maxNoiseDelta:0.000}.");

        Assert.True(
            flatnessDelta <= maxNoiseDelta,
            $"Bass enhancer increased high-band flatness: {flatnessDelta:0.000} > {maxNoiseDelta:0.000}.");

        Assert.True(
            endToPostDelta <= baselineEndToPostDelta + 0.01f,
            $"Downstream plugins introduced extra high-band noise after bass enhancer: {endToPostDelta:0.000} exceeds baseline downstream delta {baselineEndToPostDelta:0.000}+0.010.");

        Assert.True(
            minimalPreToPostDelta <= maxNoiseDelta,
            $"Bass enhancer introduced high-band noise with downstream disabled: {minimalPreToPostDelta:0.000} > {maxNoiseDelta:0.000}.");

        Assert.True(
            minimalEndToPostDelta <= maxNoiseDelta,
            $"Unexpected high-band noise after bass enhancer with downstream disabled: {minimalEndToPostDelta:0.000} > {maxNoiseDelta:0.000}.");
    }

    private static PipelineRunResult RunFullPipeline(float[] samples, int sampleRate, bool bassEnabled, SplitMode splitMode, PipelineVariant variant)
    {
        var analysisConfig = new AnalysisConfiguration();
        int hopSize = analysisConfig.ComputeHopSize();
        int analysisSize = analysisConfig.FftSize;
        int blockSize = hopSize;

        StoredChainPreset preset = SpeechMetricsTestHelpers.DefaultChainPreset;
        var descriptors = BuildDescriptors(preset, variant, out int bassIndex);
        if (bassIndex < 0)
        {
            throw new InvalidOperationException("Bass enhancer not found in pipeline preset.");
        }

        int preCount = splitMode == SplitMode.PostBass ? bassIndex + 1 : bassIndex;
        if (preCount < 0)
        {
            preCount = 0;
        }

        var orchestrator = new AnalysisOrchestrator();
        orchestrator.Initialize(sampleRate);
        var captureLink = new AnalysisCaptureLink { Orchestrator = orchestrator };

        var preRun = BuildChain(descriptors, preCount, sampleRate, blockSize, bassEnabled, captureLink);
        var fullRun = BuildChain(descriptors, descriptors.Count, sampleRate, blockSize, bassEnabled, captureLink);
        var analysisTap = fullRun.AnalysisTap;
        var bassEnhancerPlugin = fullRun.BassEnhancer;

        try
        {
            AnalysisSignalMask requestedSignals =
                AnalysisSignalMask.SpeechPresence |
                AnalysisSignalMask.PitchHz |
                AnalysisSignalMask.PitchConfidence |
                AnalysisSignalMask.VoicingScore |
                AnalysisSignalMask.VoicingState |
                AnalysisSignalMask.SpectralFlux |
                AnalysisSignalMask.HnrDb;
            preRun.Chain.SetVisualRequestedSignals(requestedSignals);
            fullRun.Chain.SetVisualRequestedSignals(requestedSignals);

            var preRouting = new RoutingContext(channelCount: 1, sampleRate, blockSize);
            var fullRouting = new RoutingContext(channelCount: 1, sampleRate, blockSize);

            var midPipeline = CreatePipeline(analysisConfig, sampleRate, hopSize, analysisSize);
            var endPipeline = CreatePipeline(analysisConfig, sampleRate, hopSize, analysisSize);
            var midFft = new FftTransformProcessor();
            var endFft = new FftTransformProcessor();
            midFft.Configure(sampleRate, analysisSize, analysisConfig.WindowFunction);
            endFft.Configure(sampleRate, analysisSize, analysisConfig.WindowFunction);

            var midStats = new NoiseStats();
            var endStats = new NoiseStats();
            var debugStats = new ArtifactDebugStats();

            float[] preBlock = new float[blockSize];
            float[] endBlock = new float[blockSize];
            long sampleClock = 0;

            for (int offset = 0; offset + blockSize <= samples.Length; offset += blockSize)
            {
                Array.Copy(samples, offset, preBlock, 0, blockSize);
                Array.Copy(samples, offset, endBlock, 0, blockSize);

                preRouting.BeginBlock(sampleClock);
                preRun.Chain.Process(preBlock, sampleClock, channelId: 0, preRouting);

                fullRouting.BeginBlock(sampleClock);
                fullRun.Chain.Process(endBlock, sampleClock, channelId: 0, fullRouting);

                float midRatio = 0f;
                float midFlatness = 0f;
                bool hasMid = false;
                float midPeak = ComputePeak(preBlock);
                if (midPipeline.ProcessHop(preBlock, out _, out _))
                {
                    midFft.Compute(midPipeline.ProcessedBuffer.Span, reassignEnabled: false);
                    hasMid = TryComputeHighBandStats(midFft.Magnitudes.Span, midFft.BinResolution, out midRatio, out midFlatness);
                    if (hasMid)
                    {
                        midStats.Add(midRatio, midFlatness);
                    }
                }

                float endRatio = 0f;
                float endFlatness = 0f;
                bool hasEnd = false;
                float endPeak = ComputePeak(endBlock);
                if (endPipeline.ProcessHop(endBlock, out _, out _))
                {
                    endFft.Compute(endPipeline.ProcessedBuffer.Span, reassignEnabled: false);
                    hasEnd = TryComputeHighBandStats(endFft.Magnitudes.Span, endFft.BinResolution, out endRatio, out endFlatness);
                    if (hasEnd)
                    {
                        endStats.Add(endRatio, endFlatness);
                    }
                }

                if (hasEnd)
                {
                    ComputeResidualStats(preBlock, endBlock, out float residualRms, out float residualPeak);
                    float voicingScore = analysisTap?.GetValue(AnalysisSignalId.VoicingScore) ?? 0f;
                    float speechPresence = analysisTap?.GetValue(AnalysisSignalId.SpeechPresence) ?? 0f;
                    float gate = bassEnhancerPlugin?.GetVoicedGate() ?? 0f;
                    float harmonic = bassEnhancerPlugin?.GetHarmonicAmount() ?? 0f;
                    float bassEnergy = bassEnhancerPlugin?.GetBassEnergy() ?? 0f;
                    debugStats.AddFrame(
                        midRatio,
                        midFlatness,
                        endRatio,
                        endFlatness,
                        voicingScore,
                        speechPresence,
                        gate,
                        harmonic,
                        bassEnergy,
                        midPeak,
                        endPeak,
                        residualRms,
                        residualPeak);
                }

                sampleClock += blockSize;
            }

            return new PipelineRunResult(midStats, endStats, debugStats);
        }
        finally
        {
            preRun.Dispose();
            fullRun.Dispose();
            orchestrator.Dispose();
        }
    }

    private static NoiseStats ComputeNoiseStats(float[] samples, int sampleRate, AnalysisConfiguration analysisConfig)
    {
        int hopSize = analysisConfig.ComputeHopSize();
        int analysisSize = analysisConfig.FftSize;
        var pipeline = CreatePipeline(analysisConfig, sampleRate, hopSize, analysisSize);
        var fft = new FftTransformProcessor();
        fft.Configure(sampleRate, analysisSize, analysisConfig.WindowFunction);
        var stats = new NoiseStats();

        float[] block = new float[hopSize];
        for (int offset = 0; offset + hopSize <= samples.Length; offset += hopSize)
        {
            Array.Copy(samples, offset, block, 0, hopSize);
            if (!pipeline.ProcessHop(block, out _, out _))
            {
                continue;
            }

            fft.Compute(pipeline.ProcessedBuffer.Span, reassignEnabled: false);
            if (TryComputeHighBandStats(fft.Magnitudes.Span, fft.BinResolution, out float ratio, out float flatness))
            {
                stats.Add(ratio, flatness);
            }
        }

        return stats;
    }

    private static AnalysisBufferPipeline CreatePipeline(AnalysisConfiguration config, int sampleRate, int hopSize, int analysisSize)
    {
        var pipeline = new AnalysisBufferPipeline();
        pipeline.Configure(
            sampleRate,
            hopSize,
            analysisSize,
            config.HighPassEnabled,
            config.HighPassCutoff,
            config.PreEmphasis,
            0.97f,
            10f);
        pipeline.Reset();
        return pipeline;
    }

    private static bool TryComputeHighBandStats(ReadOnlySpan<float> magnitudes, float binResolution, out float ratio, out float flatness)
    {
        double totalEnergy = 0.0;
        double highBandEnergy = 0.0;
        double highBandLogSum = 0.0;
        int highBandBins = 0;
        int maxBin = magnitudes.Length - 1;
        float highBandStartHz = 2000f;

        for (int i = 1; i <= maxBin; i++)
        {
            double mag = magnitudes[i];
            double power = mag * mag;
            totalEnergy += power;
            if (i * binResolution >= highBandStartHz)
            {
                highBandEnergy += power;
                highBandLogSum += Math.Log(power + 1e-12);
                highBandBins++;
            }
        }

        if (totalEnergy <= 1e-9 || highBandBins <= 0)
        {
            ratio = 0f;
            flatness = 0f;
            return false;
        }

        double highRatio = highBandEnergy / totalEnergy;
        double highMean = highBandEnergy / highBandBins;
        double highGeo = Math.Exp(highBandLogSum / highBandBins);
        double highFlatness = highMean > 1e-12 ? highGeo / highMean : 0.0;
        ratio = (float)highRatio;
        flatness = (float)highFlatness;
        return true;
    }

    private sealed class NoiseStats
    {
        private double _sumNoiseToTotal;
        private double _sumFlatness;
        private int _frames;

        public float HighBandRatioMean => _frames > 0 ? (float)(_sumNoiseToTotal / _frames) : 0f;
        public float HighBandFlatnessMean => _frames > 0 ? (float)(_sumFlatness / _frames) : 0f;

        public void Add(double highBandRatio, double highBandFlatness)
        {
            _sumNoiseToTotal += highBandRatio;
            _sumFlatness += highBandFlatness;
            _frames++;
        }
    }

    private sealed class ArtifactDebugStats
    {
        private long _frames;
        private long _voicedFrames;
        private long _speechFrames;
        private long _clipFrames;
        private long _midSilentEndActive;

        private double _sumMidRatio;
        private double _sumEndRatio;
        private double _sumMidFlat;
        private double _sumEndFlat;
        private double _sumDeltaRatio;
        private double _sumDeltaFlat;
        private double _sumVoicing;
        private double _sumSpeech;
        private double _sumGate;
        private double _sumHarmonic;
        private double _sumBass;
        private double _sumMidPeak;
        private double _sumEndPeak;
        private double _sumResidualRms;
        private double _sumResidualPeak;
        private double _sumResidualRmsSpeech;
        private double _sumResidualRmsSilence;

        private double _sumDeltaRatioVoiced;
        private double _sumDeltaRatioUnvoiced;
        private double _sumDeltaRatioSpeech;
        private double _sumDeltaRatioNoSpeech;

        public void AddFrame(
            float midRatio,
            float midFlatness,
            float endRatio,
            float endFlatness,
            float voicingScore,
            float speechPresence,
            float gate,
            float harmonic,
            float bassEnergy,
            float midPeak,
            float endPeak,
            float residualRms,
            float residualPeak)
        {
            _frames++;
            _sumMidRatio += midRatio;
            _sumEndRatio += endRatio;
            _sumMidFlat += midFlatness;
            _sumEndFlat += endFlatness;
            _sumDeltaRatio += endRatio - midRatio;
            _sumDeltaFlat += endFlatness - midFlatness;
            _sumVoicing += voicingScore;
            _sumSpeech += speechPresence;
            _sumGate += gate;
            _sumHarmonic += harmonic;
            _sumBass += bassEnergy;
            _sumMidPeak += midPeak;
            _sumEndPeak += endPeak;
            _sumResidualRms += residualRms;
            _sumResidualPeak += residualPeak;
            if (endPeak >= 0.98f)
            {
                _clipFrames++;
            }
            if (midPeak < 1e-4f && endPeak >= 1e-3f)
            {
                _midSilentEndActive++;
            }

            bool voiced = voicingScore >= 0.5f;
            if (voiced)
            {
                _voicedFrames++;
                _sumDeltaRatioVoiced += endRatio - midRatio;
            }
            else
            {
                _sumDeltaRatioUnvoiced += endRatio - midRatio;
            }

            bool speech = speechPresence >= 0.05f;
            if (speech)
            {
                _speechFrames++;
                _sumDeltaRatioSpeech += endRatio - midRatio;
                _sumResidualRmsSpeech += residualRms;
            }
            else
            {
                _sumDeltaRatioNoSpeech += endRatio - midRatio;
                _sumResidualRmsSilence += residualRms;
            }
        }

        public string FormatSummary(string label)
        {
            if (_frames == 0)
            {
                return $"{label} frames=0";
            }

            double frames = _frames;
            double voicedFrames = Math.Max(1, _voicedFrames);
            double unvoicedFrames = Math.Max(1, _frames - _voicedFrames);
            double speechFrames = Math.Max(1, _speechFrames);
            double noSpeechFrames = Math.Max(1, _frames - _speechFrames);

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} frames={1} voiced%={2:0.0} speech%={3:0.0} clip%={4:0.0} midRatio={5:0.000} endRatio={6:0.000} deltaRatio={7:0.000} midFlat={8:0.000} endFlat={9:0.000} deltaFlat={10:0.000} voicingMean={11:0.000} speechMean={12:0.000} gateMean={13:0.000} harmMean={14:0.000} bassMean={15:0.000} midPeak={16:0.000} endPeak={17:0.000} resRms={18:0.0000} resPeak={19:0.0000} resSpeech={20:0.0000} resSilence={21:0.0000} midSilentEndActive%={22:0.0} deltaVoiced={23:0.000} deltaUnvoiced={24:0.000} deltaSpeech={25:0.000} deltaNoSpeech={26:0.000}",
                label,
                _frames,
                100.0 * _voicedFrames / frames,
                100.0 * _speechFrames / frames,
                100.0 * _clipFrames / frames,
                _sumMidRatio / frames,
                _sumEndRatio / frames,
                _sumDeltaRatio / frames,
                _sumMidFlat / frames,
                _sumEndFlat / frames,
                _sumDeltaFlat / frames,
                _sumVoicing / frames,
                _sumSpeech / frames,
                _sumGate / frames,
                _sumHarmonic / frames,
                _sumBass / frames,
                _sumMidPeak / frames,
                _sumEndPeak / frames,
                _sumResidualRms / frames,
                _sumResidualPeak / frames,
                _sumResidualRmsSpeech / speechFrames,
                _sumResidualRmsSilence / noSpeechFrames,
                100.0 * _midSilentEndActive / frames,
                _sumDeltaRatioVoiced / voicedFrames,
                _sumDeltaRatioUnvoiced / unvoicedFrames,
                _sumDeltaRatioSpeech / speechFrames,
                _sumDeltaRatioNoSpeech / noSpeechFrames);
        }
    }

    private readonly record struct PipelineRunResult(NoiseStats Mid, NoiseStats End, ArtifactDebugStats Debug);

    private static float ComputePeak(ReadOnlySpan<float> buffer)
    {
        float peak = 0f;
        for (int i = 0; i < buffer.Length; i++)
        {
            float value = MathF.Abs(buffer[i]);
            if (value > peak)
            {
                peak = value;
            }
        }

        return peak;
    }

    private static void ComputeResidualStats(ReadOnlySpan<float> pre, ReadOnlySpan<float> post, out float rms, out float peak)
    {
        double sum = 0.0;
        float max = 0f;
        int count = Math.Min(pre.Length, post.Length);
        for (int i = 0; i < count; i++)
        {
            float diff = post[i] - pre[i];
            sum += diff * diff;
            float abs = MathF.Abs(diff);
            if (abs > max)
            {
                max = abs;
            }
        }

        rms = count > 0 ? (float)Math.Sqrt(sum / count) : 0f;
        peak = max;
    }

    private enum SplitMode
    {
        PreBass,
        PostBass
    }

    private enum PipelineVariant
    {
        Full,
        PostDisabled
    }

    private sealed record PluginDescriptor(string PluginId, IReadOnlyDictionary<string, float> Parameters);

    private static List<PluginDescriptor> BuildDescriptors(
        StoredChainPreset preset,
        PipelineVariant variant,
        out int bassIndex)
    {
        var descriptors = new List<PluginDescriptor>(preset.Plugins.Count);
        bassIndex = -1;
        bool skipAfterBass = variant == PipelineVariant.PostDisabled;
        bool passedBass = false;

        foreach (var entry in preset.Plugins)
        {
            if (ShouldSkipPlugin(entry.PluginId))
            {
                continue;
            }

            if (passedBass && skipAfterBass)
            {
                continue;
            }

            descriptors.Add(new PluginDescriptor(entry.PluginId, entry.Parameters));

            if (entry.PluginId == "builtin:bass-enhancer")
            {
                bassIndex = descriptors.Count - 1;
                passedBass = true;
            }
        }

        return descriptors;
    }

    private static ChainRun BuildChain(
        List<PluginDescriptor> descriptors,
        int pluginCount,
        int sampleRate,
        int blockSize,
        bool bassEnabled,
        AnalysisCaptureLink captureLink)
    {
        AnalysisTapPlugin? analysisTap = null;
        BassEnhancerPlugin? bassEnhancer = null;

        var chain = new PluginChain(sampleRate, blockSize, initialCapacity: Math.Max(1, pluginCount));
        var plugins = new List<IPlugin>(pluginCount);
        for (int i = 0; i < pluginCount; i++)
        {
            var descriptor = descriptors[i];
            var plugin = PluginFactory.Create(descriptor.PluginId);
            if (plugin is null)
            {
                continue;
            }

            plugin.Initialize(sampleRate, blockSize);
            ApplyParameters(plugin, descriptor.Parameters);
            plugins.Add(plugin);

            if (plugin is BassEnhancerPlugin bassEnhancerPlugin)
            {
                bassEnhancerPlugin.IsBypassed = !bassEnabled;
                bassEnhancer = bassEnhancerPlugin;
            }

            if (plugin is AnalysisTapPlugin tap)
            {
                analysisTap = tap;
            }

            chain.AddSlot(plugin);
        }

        if (analysisTap is null || bassEnhancer is null)
        {
            // Ensure analysis tap and bass enhancer exist for consistent diagnostics.
            if (analysisTap is null)
            {
                analysisTap = new AnalysisTapPlugin();
                analysisTap.Initialize(sampleRate, blockSize);
                chain.AddSlot(analysisTap);
            }

            if (bassEnhancer is null)
            {
                bassEnhancer = new BassEnhancerPlugin();
                bassEnhancer.Initialize(sampleRate, blockSize);
                bassEnhancer.IsBypassed = !bassEnabled;
                plugins.Add(bassEnhancer);
                chain.AddSlot(bassEnhancer);
            }
        }

        chain.SetAnalysisCaptureLink(captureLink);
        return new ChainRun(chain, plugins, analysisTap, bassEnhancer);
    }

    private sealed class ChainRun
    {
        public ChainRun(PluginChain chain, List<IPlugin> plugins, AnalysisTapPlugin? analysisTap, BassEnhancerPlugin? bassEnhancer)
        {
            Chain = chain;
            Plugins = plugins;
            AnalysisTap = analysisTap;
            BassEnhancer = bassEnhancer;
        }

        public PluginChain Chain { get; }
        public List<IPlugin> Plugins { get; }
        public AnalysisTapPlugin? AnalysisTap { get; }
        public BassEnhancerPlugin? BassEnhancer { get; }

        public void Dispose()
        {
            for (int i = 0; i < Plugins.Count; i++)
            {
                Plugins[i].Dispose();
            }
        }
    }

    private static void ApplyParameters(IPlugin plugin, IReadOnlyDictionary<string, float> parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return;
        }

        var paramList = plugin.Parameters;
        for (int i = 0; i < paramList.Count; i++)
        {
            var param = paramList[i];
            if (parameters.TryGetValue(param.Name, out float value))
            {
                plugin.SetParameter(param.Index, value);
            }
        }
    }

    private static bool ShouldSkipPlugin(string pluginId)
    {
        return pluginId is "builtin:input"
            or "builtin:signal-generator"
            or "builtin:output-send"
            or "builtin:copy"
            or "builtin:merge"
            or "builtin:bus-input";
    }
}
