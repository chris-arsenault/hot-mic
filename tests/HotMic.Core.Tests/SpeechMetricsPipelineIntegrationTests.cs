using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using HotMic.Core.Analysis;
using HotMic.Core.Dsp.Analysis;
using HotMic.Core.Dsp.Analysis.Speech;
using HotMic.Core.Dsp.Filters;
using HotMic.Core.Dsp.Spectrogram;
using HotMic.Core.Engine;
using HotMic.Core.Plugins;
using HotMic.Core.Plugins.BuiltIn;
using Xunit;

namespace HotMic.Core.Tests;

public sealed class SpeechMetricsPipelineIntegrationTests
{
    [Theory]
    [MemberData(nameof(SpeechMetricsTestHelpers.PipelineCaseData), MemberType = typeof(SpeechMetricsTestHelpers))]
    public void SpeechMetrics_FullPipeline_MatchesBaseline(SpeechPipelineCase pipelineCase)
    {
        string path = SpeechMetricsTestHelpers.FindRepoFile(SpeechMetricsTestHelpers.TestWavRelativePath);
        float[] samples = SpeechMetricsTestHelpers.LoadMonoSamples(path, out int sampleRate);
        Assert.True(sampleRate == 48000, $"Pipeline test requires 48kHz input (got {sampleRate}Hz).");

        var baselineSingle = SpeechMetricsTestHelpers.AnalyzeOffline(samples, sampleRate);
        var pipelineSingle = AnalyzePipeline(samples, sampleRate, pipelineCase.Config, out var gateSingle);

        float singleWpmDelta = SpeechMetricsTestHelpers.AllowedPipelineDelta(baselineSingle.WordsPerMinute);
        float singleArtDelta = SpeechMetricsTestHelpers.AllowedPipelineDelta(baselineSingle.ArticulationWpm);
        float singleMonoDelta = SpeechMetricsTestHelpers.AllowedNormalizedDelta(baselineSingle.MeanMonotoneScore);
        float singleClarityDelta = SpeechMetricsTestHelpers.AllowedNormalizedDelta(baselineSingle.MeanClarityScore);
        float singleIntelDelta = SpeechMetricsTestHelpers.AllowedNormalizedDelta(baselineSingle.MeanIntelligibilityScore);
        float singlePitchDelta = SpeechMetricsTestHelpers.AllowedPitchDelta(baselineSingle.MeanPitchHz);
        float singlePitchConfDelta = SpeechMetricsTestHelpers.AllowedNormalizedDelta(baselineSingle.MeanPitchConfidence);

        string caseLabel = pipelineCase.Name;
        Console.WriteLine($"SpeechPipelineCase {pipelineCase.Describe()}");
        Console.WriteLine(baselineSingle.FormatDebugSummary($"baseline-{caseLabel}-single"));
        Console.WriteLine(FormatPipelineSummary($"pipeline-{caseLabel}-single", pipelineSingle));
        Console.WriteLine(FormatGateSummary($"pipeline-{caseLabel}-single", gateSingle));

        float[] serialSamples = SpeechMetricsTestHelpers.Repeat(samples, 5);
        var baselineSerial = SpeechMetricsTestHelpers.AnalyzeOffline(serialSamples, sampleRate);
        var pipelineSerial = AnalyzePipeline(serialSamples, sampleRate, pipelineCase.Config, out var gateSerial);

        float serialWpmDelta = SpeechMetricsTestHelpers.AllowedPipelineDelta(baselineSerial.WordsPerMinute);
        float serialArtDelta = SpeechMetricsTestHelpers.AllowedPipelineDelta(baselineSerial.ArticulationWpm);
        float serialMonoDelta = SpeechMetricsTestHelpers.AllowedNormalizedDelta(baselineSerial.MeanMonotoneScore);
        float serialClarityDelta = SpeechMetricsTestHelpers.AllowedNormalizedDelta(baselineSerial.MeanClarityScore);
        float serialIntelDelta = SpeechMetricsTestHelpers.AllowedNormalizedDelta(baselineSerial.MeanIntelligibilityScore);
        float serialPitchDelta = SpeechMetricsTestHelpers.AllowedPitchDelta(baselineSerial.MeanPitchHz);
        float serialPitchConfDelta = SpeechMetricsTestHelpers.AllowedNormalizedDelta(baselineSerial.MeanPitchConfidence);

        Console.WriteLine(baselineSerial.FormatDebugSummary($"baseline-{caseLabel}-serial"));
        Console.WriteLine(FormatPipelineSummary($"pipeline-{caseLabel}-serial", pipelineSerial));
        Console.WriteLine(FormatGateSummary($"pipeline-{caseLabel}-serial", gateSerial));

        var failures = new List<string>();
        AddDeltaFailure(failures, $"{caseLabel} single WPM", pipelineSingle.Metrics.WordsPerMinute, baselineSingle.WordsPerMinute, singleWpmDelta);
        AddDeltaFailure(failures, $"{caseLabel} single articulation WPM", pipelineSingle.Metrics.ArticulationWpm, baselineSingle.ArticulationWpm, singleArtDelta);
        AddDeltaFailure(failures, $"{caseLabel} single monotone mean", pipelineSingle.Averages.MeanMonotoneScore, baselineSingle.MeanMonotoneScore, singleMonoDelta);
        AddDeltaFailure(failures, $"{caseLabel} single clarity mean", pipelineSingle.Averages.MeanClarityScore, baselineSingle.MeanClarityScore, singleClarityDelta);
        AddDeltaFailure(failures, $"{caseLabel} single intelligibility mean", pipelineSingle.Averages.MeanIntelligibilityScore, baselineSingle.MeanIntelligibilityScore, singleIntelDelta);
        AddDeltaFailure(failures, $"{caseLabel} single pitch mean", pipelineSingle.Averages.MeanPitchHz, baselineSingle.MeanPitchHz, singlePitchDelta);
        AddDeltaFailure(failures, $"{caseLabel} single pitch confidence mean", pipelineSingle.Averages.MeanPitchConfidence, baselineSingle.MeanPitchConfidence, singlePitchConfDelta);
        AddDeltaFailure(failures, $"{caseLabel} serial WPM", pipelineSerial.Metrics.WordsPerMinute, baselineSerial.WordsPerMinute, serialWpmDelta);
        AddDeltaFailure(failures, $"{caseLabel} serial articulation WPM", pipelineSerial.Metrics.ArticulationWpm, baselineSerial.ArticulationWpm, serialArtDelta);
        AddDeltaFailure(failures, $"{caseLabel} serial monotone mean", pipelineSerial.Averages.MeanMonotoneScore, baselineSerial.MeanMonotoneScore, serialMonoDelta);
        AddDeltaFailure(failures, $"{caseLabel} serial clarity mean", pipelineSerial.Averages.MeanClarityScore, baselineSerial.MeanClarityScore, serialClarityDelta);
        AddDeltaFailure(failures, $"{caseLabel} serial intelligibility mean", pipelineSerial.Averages.MeanIntelligibilityScore, baselineSerial.MeanIntelligibilityScore, serialIntelDelta);
        AddDeltaFailure(failures, $"{caseLabel} serial pitch mean", pipelineSerial.Averages.MeanPitchHz, baselineSerial.MeanPitchHz, serialPitchDelta);
        AddDeltaFailure(failures, $"{caseLabel} serial pitch confidence mean", pipelineSerial.Averages.MeanPitchConfidence, baselineSerial.MeanPitchConfidence, serialPitchConfDelta);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static SpeechPipelineResult AnalyzePipeline(float[] samples, int sampleRate, SpeechPipelineConfig pipelineConfig, out GateComparisonStats gateStats)
    {
        var analysisConfig = new AnalysisConfiguration();

        int hopSize = analysisConfig.ComputeHopSize();
        int analysisSize = analysisConfig.FftSize;
        int blockSize = hopSize;

        var orchestrator = new AnalysisOrchestrator();
        orchestrator.Initialize(sampleRate);

        var captureLink = new AnalysisCaptureLink { Orchestrator = orchestrator };

        SileroVoiceGatePlugin? vad = null;
        SpeechDenoiserPlugin? speechDenoiser = null;
        GainPlugin? gain = null;
        CompressorPlugin? compressor = null;
        DeEsserPlugin? deEsser = null;
        AnalysisTapPlugin? tap = null;
        int tapIndex = -1;
        var plugins = new List<IPlugin>(6);
        var pluginParameters = new List<(IPlugin Plugin, IReadOnlyDictionary<string, float> Parameters)>();

        try
        {
            if (pipelineConfig.ChainPreset is not null)
            {
                foreach (var entry in pipelineConfig.ChainPreset.Plugins)
                {
                    if (!IsPluginEnabled(entry.PluginId, pipelineConfig.PluginOverrides))
                    {
                        continue;
                    }

                    if (ShouldSkipPlugin(entry.PluginId))
                    {
                        continue;
                    }

                    var plugin = PluginFactory.Create(entry.PluginId);
                    if (plugin is null)
                    {
                        Console.WriteLine($"[PipelineTest] Skipping unknown plugin '{entry.PluginId}'.");
                        continue;
                    }

                    if (plugin is SileroVoiceGatePlugin voiceGate)
                    {
                        vad = voiceGate;
                    }
                    else if (plugin is SpeechDenoiserPlugin denoiser)
                    {
                        speechDenoiser = denoiser;
                    }
                    else if (plugin is GainPlugin gainPlugin)
                    {
                        gain = gainPlugin;
                    }
                    else if (plugin is CompressorPlugin compressorPlugin)
                    {
                        compressor = compressorPlugin;
                    }
                    else if (plugin is DeEsserPlugin deEsserPlugin)
                    {
                        deEsser = deEsserPlugin;
                    }
                    else if (plugin is AnalysisTapPlugin analysisTap)
                    {
                        tap = analysisTap;
                    }

                    if (plugin is AnalysisTapPlugin)
                    {
                        tapIndex = plugins.Count;
                    }

                    plugins.Add(plugin);
                    pluginParameters.Add((plugin, entry.Parameters));
                }
            }
            else
            {
                vad = pipelineConfig.VadEnabled ? new SileroVoiceGatePlugin() : null;
                speechDenoiser = pipelineConfig.DenoiserEnabled ? new SpeechDenoiserPlugin() : null;
                gain = pipelineConfig.GainEnabled ? new GainPlugin() : null;
                compressor = pipelineConfig.CompressorEnabled ? new CompressorPlugin() : null;
                deEsser = pipelineConfig.DeEsserEnabled ? new DeEsserPlugin() : null;
                tap = pipelineConfig.AnalysisTapEnabled ? new AnalysisTapPlugin() : null;

                AddPlugin(plugins, vad);
                AddPlugin(plugins, speechDenoiser);
                AddPlugin(plugins, gain);
                AddPlugin(plugins, compressor);
                AddPlugin(plugins, deEsser);
                AddPlugin(plugins, tap);
                if (tap is not null)
                {
                    tapIndex = plugins.Count - 1;
                }
            }

            for (int i = 0; i < plugins.Count; i++)
            {
                plugins[i].Initialize(sampleRate, blockSize);
            }

            for (int i = 0; i < pluginParameters.Count; i++)
            {
                ApplyParameters(pluginParameters[i].Plugin, pluginParameters[i].Parameters);
            }

            if (vad is not null)
            {
                Assert.True(vad.ProducedSignals != AnalysisSignalMask.None,
                    $"Silero VAD unavailable: {vad.StatusMessage}");
            }
            if (speechDenoiser is not null)
            {
                Assert.True(string.IsNullOrWhiteSpace(speechDenoiser.StatusMessage),
                    $"Speech Denoiser unavailable: {speechDenoiser.StatusMessage}");
            }

            if (tap is not null)
            {
                // Use configured speech presence mode to gate pitch/voicing in the analysis tap.
                tap.SetParameter((int)AnalysisSignalId.SpeechPresence, ModeToValue(pipelineConfig.SpeechPresenceMode));
            }

            var chain = new PluginChain(sampleRate, blockSize, initialCapacity: Math.Max(1, plugins.Count));
            for (int i = 0; i < plugins.Count; i++)
            {
                chain.AddSlot(plugins[i]);
            }
            chain.SetAnalysisCaptureLink(captureLink);

            AnalysisSignalMask requestedSignals =
                AnalysisSignalMask.SpeechPresence |
                AnalysisSignalMask.PitchHz |
                AnalysisSignalMask.PitchConfidence |
                AnalysisSignalMask.VoicingState |
                AnalysisSignalMask.VoicingScore |
                AnalysisSignalMask.SpectralFlux |
                AnalysisSignalMask.HnrDb;
            chain.SetVisualRequestedSignals(requestedSignals);

            var routing = new RoutingContext(channelCount: 1, sampleRate, blockSize);

            var hpf = new BiquadFilter();
            hpf.SetHighPass(sampleRate, analysisConfig.HighPassCutoff, 0.707f);

            var analysisPipeline = new AnalysisBufferPipeline();
            analysisPipeline.Configure(
                sampleRate,
                hopSize,
                analysisSize,
                analysisConfig.HighPassEnabled,
                analysisConfig.HighPassCutoff,
                analysisConfig.PreEmphasis,
                0.97f,
                10f);
            analysisPipeline.Reset();

            var fftProcessor = new FftTransformProcessor();
            fftProcessor.Configure(sampleRate, analysisSize, analysisConfig.WindowFunction);

            var binCenters = new float[analysisSize / 2];
            for (int i = 0; i < binCenters.Length; i++)
            {
                binCenters[i] = i * fftProcessor.BinResolution;
            }

            var featureExtractor = new SpectralFeatureExtractor();
            featureExtractor.UpdateFrequencies(binCenters);

            var speechMetrics = new SpeechMetricsProcessor();
            speechMetrics.Configure(hopSize, sampleRate);

            var signalProcessor = new AnalysisSignalProcessor();
            var signalSettings = new AnalysisSignalProcessorSettings
            {
                AnalysisSize = analysisSize,
                PitchDetector = analysisConfig.PitchAlgorithm,
                VoicingSettings = analysisConfig.VoicingSettings,
                MinFrequency = analysisConfig.MinFrequency,
                MaxFrequency = analysisConfig.MaxFrequency,
                WindowFunction = analysisConfig.WindowFunction,
                PreEmphasisEnabled = analysisConfig.PreEmphasis,
                HighPassEnabled = analysisConfig.HighPassEnabled,
                HighPassCutoff = analysisConfig.HighPassCutoff
            };
            signalProcessor.Configure(sampleRate, hopSize, signalSettings);
            signalProcessor.SetGeneratedSpeechPresenceGateEnabled(true);
            signalProcessor.Reset();

            float[] block = new float[blockSize];
            float[]? analysisBlock = tapIndex >= 0 ? new float[blockSize] : null;
            long sampleClock = 0;
            long frameId = 0;
            SpeechMetricsFrame lastMetrics = default;
            double sumMonotone = 0;
            double sumClarity = 0;
            double sumIntelligibility = 0;
            double sumPitchHz = 0;
            double sumPitchConfidence = 0;
            long metricFrames = 0;
            long pitchFrames = 0;
            var vadPresenceSeries = new List<float>(samples.Length / Math.Max(1, blockSize));
            var energyPresenceSeries = new List<float>(samples.Length / Math.Max(1, blockSize));

            for (int offset = 0; offset + blockSize <= samples.Length; offset += blockSize)
            {
                long blockSampleTime = sampleClock;
                Array.Copy(samples, offset, block, 0, blockSize);

                for (int i = 0; i < block.Length; i++)
                {
                    block[i] = hpf.Process(block[i]);
                }

                routing.BeginBlock(blockSampleTime);
                if (tapIndex >= 0 && analysisBlock is not null)
                {
                    chain.ProcessWithSplit(block, blockSampleTime, channelId: 0, routing, tapIndex,
                        splitBuffer => splitBuffer.CopyTo(analysisBlock));
                }
                else
                {
                    chain.Process(block, blockSampleTime, channelId: 0, routing);
                }

                ReadOnlySpan<float> analysisSource = analysisBlock is not null ? analysisBlock : block;

                if (!analysisPipeline.ProcessHop(analysisSource, out float waveformMin, out float waveformMax))
                {
                    sampleClock += blockSize;
                    ContinueRealTimePace(blockSize, sampleRate);
                    continue;
                }

                signalProcessor.ProcessBlock(analysisSource, blockSampleTime, default, requestedSignals);

                fftProcessor.Compute(analysisPipeline.ProcessedBuffer, reassignEnabled: false);

                ReadOnlySpan<float> analysisRaw = analysisPipeline.RawBuffer.AsSpan(0, analysisSize);
                ReadOnlySpan<float> magnitudes = fftProcessor.Magnitudes;

                featureExtractor.Compute(magnitudes, out _, out float slope, out _);

                float energyPresence = signalProcessor.GetLastValue(AnalysisSignalId.SpeechPresence);
                energyPresenceSeries.Add(energyPresence);

                float speechPresence;
                float pitchHz;
                float pitchConfidence;
                VoicingState voicing;
                float flux;
                float hnr;

                if (tap is not null)
                {
                    speechPresence = ResolveTapValue(tap, signalProcessor, AnalysisSignalId.SpeechPresence);
                    pitchHz = ResolveTapValue(tap, signalProcessor, AnalysisSignalId.PitchHz);
                    pitchConfidence = ResolveTapValue(tap, signalProcessor, AnalysisSignalId.PitchConfidence);
                    voicing = (VoicingState)MathF.Round(ResolveTapValue(tap, signalProcessor, AnalysisSignalId.VoicingState));
                    flux = ResolveTapValue(tap, signalProcessor, AnalysisSignalId.SpectralFlux);
                    hnr = ResolveTapValue(tap, signalProcessor, AnalysisSignalId.HnrDb);
                    vadPresenceSeries.Add(speechPresence);
                }
                else
                {
                    speechPresence = energyPresence;
                    pitchHz = signalProcessor.GetLastValue(AnalysisSignalId.PitchHz);
                    pitchConfidence = signalProcessor.GetLastValue(AnalysisSignalId.PitchConfidence);
                    voicing = (VoicingState)MathF.Round(signalProcessor.GetLastValue(AnalysisSignalId.VoicingState));
                    flux = signalProcessor.GetLastValue(AnalysisSignalId.SpectralFlux);
                    hnr = signalProcessor.GetLastValue(AnalysisSignalId.HnrDb);
                    if (vad is not null)
                    {
                        vadPresenceSeries.Add(vad.VadProbability);
                    }
                }

                lastMetrics = speechMetrics.Process(
                    waveformMin,
                    waveformMax,
                    analysisRaw,
                    magnitudes,
                    ReadOnlySpan<float>.Empty,
                    fftProcessor.BinResolution,
                    speechPresence,
                    pitchHz,
                    pitchConfidence,
                    voicing,
                    flux,
                    slope,
                    hnr,
                    frameId);

                metricFrames++;
                sumMonotone += lastMetrics.MonotoneScore;
                sumClarity += lastMetrics.ClarityScore;
                sumIntelligibility += lastMetrics.IntelligibilityScore;
                if (pitchConfidence > 0f)
                {
                    sumPitchHz += pitchHz;
                    sumPitchConfidence += pitchConfidence;
                    pitchFrames++;
                }

                frameId++;
                sampleClock += blockSize;
                ContinueRealTimePace(blockSize, sampleRate);
            }

            gateStats = BuildGateComparison(vadPresenceSeries, energyPresenceSeries);
            var averages = new SpeechMetricAverages(
                metricFrames > 0 ? (float)(sumMonotone / metricFrames) : 0f,
                metricFrames > 0 ? (float)(sumClarity / metricFrames) : 0f,
                metricFrames > 0 ? (float)(sumIntelligibility / metricFrames) : 0f,
                pitchFrames > 0 ? (float)(sumPitchHz / pitchFrames) : 0f,
                pitchFrames > 0 ? (float)(sumPitchConfidence / pitchFrames) : 0f);

            return new SpeechPipelineResult(lastMetrics, averages, speechMetrics.SyllableDebugStats, speechMetrics.RateDebugStats, speechMetrics.PauseDebugStats);
        }
        finally
        {
            for (int i = 0; i < plugins.Count; i++)
            {
                plugins[i].Dispose();
            }
            orchestrator.Dispose();
        }
    }

    private static void AddPlugin(List<IPlugin> plugins, IPlugin? plugin)
    {
        if (plugin is not null)
        {
            plugins.Add(plugin);
        }
    }

    private static float ModeToValue(AnalysisTapMode mode)
    {
        return mode switch
        {
            AnalysisTapMode.Disabled => 0f,
            AnalysisTapMode.Generate => 1f,
            AnalysisTapMode.UseExisting => 2f,
            _ => 1f
        };
    }

    private static void ContinueRealTimePace(int blockSize, int sampleRate)
    {
        int blockMs = (int)MathF.Round(1000f * blockSize / Math.Max(1, sampleRate));
        if (blockMs > 0)
        {
            Thread.Sleep(blockMs);
        }
    }

    private static void AddDeltaFailure(List<string> failures, string label, float actual, float expected, float delta)
    {
        float diff = MathF.Abs(actual - expected);
        if (diff > delta)
        {
            failures.Add($"{label} expected {expected:0.00}±{delta:0.00}, actual {actual:0.00} (diff {diff:0.00}).");
        }
    }

    private static float ResolveTapValue(AnalysisTapPlugin tap, AnalysisSignalProcessor processor, AnalysisSignalId signal)
    {
        AnalysisTapMode mode = tap.GetMode(signal);
        if (mode == AnalysisTapMode.Disabled)
        {
            return processor.GetLastValue(signal);
        }

        if (mode == AnalysisTapMode.UseExisting && !tap.HasSource(signal))
        {
            return processor.GetLastValue(signal);
        }

        return tap.GetValue(signal);
    }

    private static bool IsPluginEnabled(string pluginId, IReadOnlyDictionary<string, bool>? overrides)
    {
        if (overrides is null)
        {
            return true;
        }

        if (overrides.TryGetValue(pluginId, out bool enabled))
        {
            return enabled;
        }

        return true;
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

    private static string FormatPipelineSummary(string label, SpeechPipelineResult result)
    {
        SpeechMetricsFrame metrics = result.Metrics;
        SpeechMetricAverages averages = result.Averages;
        SyllableDetectorDebugStats syllableStats = result.SyllableStats;
        SpeechRateDebugStats rateStats = result.RateStats;
        PauseDetectorDebugStats pauseStats = result.PauseStats;
        return string.Format(
            CultureInfo.InvariantCulture,
            "SpeechMetricsPipeline {0} wpm={1:0.00} artWpm={2:0.00} pauseRatio={3:0.00} meanPauseMs={4:0.0} ppm={5:0.0} filledRatio={6:0.00} pauseBins=m{7} s{8} md{9} l{10} peaks={11} voicedPeaks={12} det={13} detV={14} detU={15} rejProm={16} rejPromInst={17} rejPromMean={18} rejMinInt={19} clamp={20} meanPenalty={21} maxPromClamp={22:0.00} maxProm={23:0.00} maxPromInst={24:0.00} maxPromMean={25:0.00} baseDb=[{26:0.0},{27:0.0}] baseUpd={28} baseSkip={29} rateSyll={30} ratePauses={31} pauseFrames={32} pauseSilence={33} pauseFilledCand={34} pauseSpeaking={35} pauseEvS={36} pauseEvF={37} pauseFramesS={38} pauseFramesF={39} monoMean={40:0.000} clarityMean={41:0.000} intelMean={42:0.000} pitchMean={43:0.0} pitchConfMean={44:0.00}",
            label,
            metrics.WordsPerMinute,
            metrics.ArticulationWpm,
            metrics.PauseRatio,
            metrics.MeanPauseDurationMs,
            metrics.PausesPerMinute,
            metrics.FilledPauseRatio,
            metrics.PauseMicroCount,
            metrics.PauseShortCount,
            metrics.PauseMediumCount,
            metrics.PauseLongCount,
            syllableStats.Peaks,
            syllableStats.VoicedPeaks,
            syllableStats.Detected,
            syllableStats.DetectedVoiced,
            syllableStats.DetectedUnvoiced,
            syllableStats.RejectLowProminence,
            syllableStats.RejectLowProminenceInstant,
            syllableStats.RejectLowProminenceMean,
            syllableStats.RejectMinInterval,
            syllableStats.ClampApplied,
            syllableStats.MeanPenaltyApplied,
            syllableStats.MaxProminenceClampDb,
            syllableStats.MaxProminenceDb,
            syllableStats.MaxProminenceInstantDb,
            syllableStats.MaxProminenceMeanDb,
            syllableStats.MinBaselineDb,
            syllableStats.MaxBaselineDb,
            syllableStats.BaselineUpdates,
            syllableStats.BaselineSkips,
            rateStats.SyllablesRecorded,
            rateStats.PausesRecorded,
            pauseStats.Frames,
            pauseStats.SilenceFrames,
            pauseStats.FilledCandidateFrames,
            pauseStats.SpeakingFrames,
            pauseStats.SilentPauseEvents,
            pauseStats.FilledPauseEvents,
            pauseStats.SilentPauseFrames,
            pauseStats.FilledPauseFrames,
            averages.MeanMonotoneScore,
            averages.MeanClarityScore,
            averages.MeanIntelligibilityScore,
            averages.MeanPitchHz,
            averages.MeanPitchConfidence);
    }

    private static GateComparisonStats BuildGateComparison(List<float> vadSeries, List<float> energySeries)
    {
        int count = Math.Min(vadSeries.Count, energySeries.Count);
        if (count == 0)
        {
            return new GateComparisonStats(false, 0f, 0f, 0f, 0f, 0f, 0f, 0, 0f);
        }

        float vadSum = 0f;
        float energySum = 0f;
        float absDiffSum = 0f;
        int vadOn = 0;
        int energyOn = 0;
        int mismatched = 0;
        float threshold = AnalysisSignalProcessor.SpeechPresenceGateThreshold;

        for (int i = 0; i < count; i++)
        {
            float vad = vadSeries[i];
            float energy = energySeries[i];
            vadSum += vad;
            energySum += energy;
            absDiffSum += MathF.Abs(vad - energy);

            bool vadOpen = vad > threshold;
            bool energyOpen = energy > threshold;
            if (vadOpen) vadOn++;
            if (energyOpen) energyOn++;
            if (vadOpen != energyOpen) mismatched++;
        }

        int bestLag = 0;
        float bestMatch = 0f;
        int maxLag = Math.Min(20, count / 4);
        for (int lag = -maxLag; lag <= maxLag; lag++)
        {
            int match = 0;
            int total = 0;
            for (int i = 0; i < count; i++)
            {
                int j = i + lag;
                if ((uint)j >= (uint)count)
                {
                    continue;
                }

                bool vadOpen = vadSeries[i] > threshold;
                bool energyOpen = energySeries[j] > threshold;
                if (vadOpen == energyOpen)
                {
                    match++;
                }
                total++;
            }

            if (total == 0)
            {
                continue;
            }

            float score = (float)match / total;
            if (score > bestMatch)
            {
                bestMatch = score;
                bestLag = lag;
            }
        }

        return new GateComparisonStats(
            true,
            vadSum / count,
            energySum / count,
            absDiffSum / count,
            (float)vadOn / count,
            (float)energyOn / count,
            (float)mismatched / count,
            bestLag,
            bestMatch);
    }

    private static string FormatGateSummary(string label, GateComparisonStats stats)
    {
        if (!stats.HasData)
        {
            return $"SpeechGateCompare {label} (no data)";
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "SpeechGateCompare {0} vadMean={1:0.000} energyMean={2:0.000} absDiff={3:0.000} vadOn={4:P0} energyOn={5:P0} mismatch={6:P0} bestLag={7} match@lag={8:P0}",
            label,
            stats.VadMean,
            stats.EnergyMean,
            stats.MeanAbsDiff,
            stats.VadOnRatio,
            stats.EnergyOnRatio,
            stats.MismatchRatio,
            stats.BestLag,
            stats.BestLagMatchRatio);
    }

    private readonly record struct SpeechPipelineResult(
        SpeechMetricsFrame Metrics,
        SpeechMetricAverages Averages,
        SyllableDetectorDebugStats SyllableStats,
        SpeechRateDebugStats RateStats,
        PauseDetectorDebugStats PauseStats);

    private readonly record struct SpeechMetricAverages(
        float MeanMonotoneScore,
        float MeanClarityScore,
        float MeanIntelligibilityScore,
        float MeanPitchHz,
        float MeanPitchConfidence);

    private readonly record struct GateComparisonStats(
        bool HasData,
        float VadMean,
        float EnergyMean,
        float MeanAbsDiff,
        float VadOnRatio,
        float EnergyOnRatio,
        float MismatchRatio,
        int BestLag,
        float BestLagMatchRatio);
}
