using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using HotMic.Core.Analysis;
using HotMic.Core.Dsp;
using HotMic.Core.Dsp.Analysis;
using HotMic.Core.Dsp.Analysis.Pitch;
using HotMic.Core.Dsp.Analysis.Speech;
using HotMic.Core.Presets;
using HotMic.Core.Plugins;
using NAudio.Wave;

namespace HotMic.Core.Tests;

internal static class SpeechMetricsTestHelpers
{
    internal const string TestWavRelativePath = "tests/HotMic.Core.Tests/data/noisy_voice_4sec.wav";
    internal const string DefaultChainRelativePath = "default.json";
    internal const float PipelineDeltaPercent = 0.10f;
    internal const float PipelineDeltaFloor = 2.0f;
    internal const float NormalizedDeltaFloor = 0.05f;
    internal const float NormalizedDeltaPercent = 0.15f;
    internal const float PitchDeltaFloorHz = 10.0f;
    internal const float PitchDeltaPercent = 0.15f;

    private static readonly JsonSerializerOptions PresetJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal static readonly SpeechPipelineCase TapOffCase = new(
        "tap-off",
        SpeechPipelineConfig.Build(
            vadEnabled: true,
            denoiserEnabled: true,
            gainEnabled: false,
            compressorEnabled: false,
            deEsserEnabled: false,
            analysisTapEnabled: false,
            speechPresenceMode: AnalysisTapMode.Generate));

    internal static readonly SpeechPipelineCase TapOnCase = new(
        "tap-on",
        SpeechPipelineConfig.Build(
            vadEnabled: true,
            denoiserEnabled: true,
            gainEnabled: false,
            compressorEnabled: false,
            deEsserEnabled: false,
            analysisTapEnabled: true,
            speechPresenceMode: AnalysisTapMode.UseExisting));

    internal static readonly SpeechPipelineCase TapOnNoVadDenoiserCase = new(
        "tap-on-novad-denoiser",
        SpeechPipelineConfig.Build(
            vadEnabled: false,
            denoiserEnabled: true,
            gainEnabled: false,
            compressorEnabled: false,
            deEsserEnabled: false,
            analysisTapEnabled: true,
            speechPresenceMode: AnalysisTapMode.Generate));

    internal static readonly SpeechPipelineCase TapOnVadNoDenoiserCase = new(
        "tap-on-vad-nodenoiser",
        SpeechPipelineConfig.Build(
            vadEnabled: true,
            denoiserEnabled: false,
            gainEnabled: false,
            compressorEnabled: false,
            deEsserEnabled: false,
            analysisTapEnabled: true,
            speechPresenceMode: AnalysisTapMode.UseExisting));

    internal static readonly StoredChainPreset DefaultChainPreset = LoadChainPreset(DefaultChainRelativePath);

    internal static readonly SpeechPipelineCase DefaultChainCase = new(
        "default-chain",
        SpeechPipelineConfig.FromChainPreset(DefaultChainPreset, AnalysisTapMode.Generate));

    internal static readonly SpeechPipelineCase DefaultChainPresenceExistingCase = new(
        "default-chain-presence-existing",
        SpeechPipelineConfig.FromChainPreset(DefaultChainPreset, AnalysisTapMode.UseExisting));

    internal static readonly SpeechPipelineCase DefaultChainNoVadCase = new(
        "default-chain-novad",
        SpeechPipelineConfig.FromChainPreset(
            DefaultChainPreset,
            AnalysisTapMode.Generate,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["builtin:voice-gate"] = false
            }));

    internal static readonly SpeechPipelineCase DefaultChainNoDenoiserCase = new(
        "default-chain-nodenoiser",
        SpeechPipelineConfig.FromChainPreset(
            DefaultChainPreset,
            AnalysisTapMode.Generate,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["builtin:speechdenoiser"] = false
            }));

    internal static readonly SpeechPipelineCase DefaultChainNoTapCase = new(
        "default-chain-notap",
        SpeechPipelineConfig.FromChainPreset(
            DefaultChainPreset,
            AnalysisTapMode.Generate,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["builtin:analysis-tap"] = false
            }));

    internal static readonly SpeechPipelineCase DefaultChainNoDynamicsCase = new(
        "default-chain-nodynamics",
        SpeechPipelineConfig.FromChainPreset(
            DefaultChainPreset,
            AnalysisTapMode.Generate,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["builtin:gain"] = false,
                ["builtin:compressor"] = false,
                ["builtin:deesser"] = false
            }));

    internal static readonly SpeechPipelineCase[] PipelineCases =
    {
        TapOffCase,
        TapOnCase,
        DefaultChainCase,
        DefaultChainPresenceExistingCase
    };

    public static readonly object[][] PipelineCaseData =
    {
        new object[] { TapOffCase },
        new object[] { TapOnCase },
        new object[] { TapOnNoVadDenoiserCase },
        new object[] { TapOnVadNoDenoiserCase },
        new object[] { DefaultChainCase },
        new object[] { DefaultChainPresenceExistingCase },
        new object[] { DefaultChainNoVadCase },
        new object[] { DefaultChainNoDenoiserCase },
        new object[] { DefaultChainNoTapCase },
        new object[] { DefaultChainNoDynamicsCase }
    };

    internal static SpeechMetricsOfflineResult AnalyzeOffline(float[] samples, int sampleRate)
        => SpeechMetricsOfflineAnalyzer.Analyze(samples, sampleRate);

    internal static float AllowedPipelineDelta(float baseline)
        => MathF.Max(PipelineDeltaFloor, MathF.Abs(baseline) * PipelineDeltaPercent);

    internal static float AllowedNormalizedDelta(float baseline)
        => MathF.Max(NormalizedDeltaFloor, MathF.Abs(baseline) * NormalizedDeltaPercent);

    internal static float AllowedPitchDelta(float baseline)
        => MathF.Max(PitchDeltaFloorHz, MathF.Abs(baseline) * PitchDeltaPercent);

    internal static float[] LoadMonoSamples(string path, out int sampleRate)
    {
        using var reader = new AudioFileReader(path);
        sampleRate = reader.WaveFormat.SampleRate;
        int channels = reader.WaveFormat.Channels;
        var samples = new List<float>(Math.Max(1, (int)(reader.Length / 4)));
        var buffer = new float[reader.WaveFormat.SampleRate * channels];

        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            int frames = read / channels;
            for (int i = 0; i < frames; i++)
            {
                float sum = 0f;
                int baseIndex = i * channels;
                for (int c = 0; c < channels; c++)
                {
                    sum += buffer[baseIndex + c];
                }
                samples.Add(sum / channels);
            }
        }

        return samples.ToArray();
    }

    internal static float[] Repeat(float[] samples, int times)
    {
        if (times <= 1)
        {
            return samples;
        }

        var repeated = new float[samples.Length * times];
        for (int i = 0; i < times; i++)
        {
            Array.Copy(samples, 0, repeated, i * samples.Length, samples.Length);
        }
        return repeated;
    }

    internal static string FindRepoFile(string relativePath)
    {
        string fileName = Path.GetFileName(relativePath);
        string outputCandidate = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(outputCandidate))
        {
            return outputCandidate;
        }

        string outputDataCandidate = Path.Combine(AppContext.BaseDirectory, "data", fileName);
        if (File.Exists(outputDataCandidate))
        {
            return outputDataCandidate;
        }

        string outputRelativeCandidate = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (File.Exists(outputRelativeCandidate))
        {
            return outputRelativeCandidate;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            string testDataCandidate = Path.Combine(dir.FullName, "tests", "HotMic.Core.Tests", "data", fileName);
            if (File.Exists(testDataCandidate))
            {
                return testDataCandidate;
            }
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Test data not found: {relativePath}");
    }

    internal static StoredChainPreset LoadChainPreset(string relativePath)
    {
        string path = FindRepoFile(relativePath);
        string json = File.ReadAllText(path);
        var preset = JsonSerializer.Deserialize<StoredChainPreset>(json, PresetJsonOptions);
        if (preset == null || string.IsNullOrWhiteSpace(preset.Name))
        {
            throw new InvalidDataException($"Invalid chain preset: {relativePath}");
        }

        return preset;
    }
}

internal sealed class SpeechMetricsOfflineAnalyzer
{
    private readonly int _sampleRate;
    private readonly int _hopSize;
    private readonly int _analysisSize;
    private readonly float _binResolution;
    private readonly float[] _hopBuffer;
    private readonly AnalysisBufferPipeline _analysisPipeline = new();
    private readonly FftTransformProcessor _fftProcessor = new();
    private readonly AnalysisSignalProcessor _signalProcessor = new();
    private readonly SpeechMetricsProcessor _metricsProcessor = new();
    private readonly SyllableDetector _energySyllableDetector = new();
    private SyllableDetectorDebugStats _energySyllableStats;
    private long _frameId;
    private long _sampleTime;
    private SpeechMetricsFrame _lastMetrics;

    private SpeechMetricsOfflineAnalyzer(int sampleRate)
    {
        var config = new AnalysisConfiguration();
        _sampleRate = sampleRate;
        _analysisSize = config.FftSize;
        _hopSize = config.ComputeHopSize();
        _analysisPipeline.Configure(
            sampleRate,
            _hopSize,
            _analysisSize,
            config.HighPassEnabled,
            config.HighPassCutoff,
            config.PreEmphasis,
            0.97f,
            10f);
        _fftProcessor.Configure(sampleRate, _analysisSize, config.WindowFunction);
        _binResolution = _fftProcessor.BinResolution;
        _hopBuffer = new float[_hopSize];

        var settings = new AnalysisSignalProcessorSettings
        {
            AnalysisSize = _analysisSize,
            PitchDetector = PitchDetectorType.Yin,
            VoicingSettings = config.VoicingSettings,
            MinFrequency = config.MinFrequency,
            MaxFrequency = config.MaxFrequency,
            WindowFunction = config.WindowFunction,
            PreEmphasisEnabled = config.PreEmphasis,
            HighPassEnabled = config.HighPassEnabled,
            HighPassCutoff = config.HighPassCutoff
        };
        _signalProcessor.Configure(sampleRate, _hopSize, settings);
        _signalProcessor.SetGeneratedSpeechPresenceGateEnabled(true);
        _signalProcessor.Reset();
        _metricsProcessor.Configure(_hopSize, sampleRate);
        _energySyllableDetector.Reset();
    }

    public static SpeechMetricsOfflineResult Analyze(float[] samples, int sampleRate)
    {
        var analyzer = new SpeechMetricsOfflineAnalyzer(sampleRate);
        analyzer.Process(samples);
        return analyzer.CreateResult();
    }

    private void Process(float[] samples)
    {
        int index = 0;
        while (index + _hopSize <= samples.Length)
        {
            Array.Copy(samples, index, _hopBuffer, 0, _hopSize);
            index += _hopSize;
            ProcessHop();
        }
    }

    private void ProcessHop()
    {
        int shift = _hopSize;
        if (!_analysisPipeline.ProcessHop(_hopBuffer.AsSpan(0, shift), out float waveformMin, out float waveformMax))
        {
            return;
        }

        _fftProcessor.Compute(_analysisPipeline.ProcessedBuffer, reassignEnabled: false);
        ReadOnlySpan<float> analysisRaw = _analysisPipeline.RawBuffer.AsSpan(0, _analysisSize);
        ReadOnlySpan<float> magnitudes = _fftProcessor.Magnitudes;

        AnalysisSignalMask signals = AnalysisSignalMask.SpeechPresence |
                                     AnalysisSignalMask.PitchHz |
                                     AnalysisSignalMask.PitchConfidence |
                                     AnalysisSignalMask.VoicingState |
                                     AnalysisSignalMask.VoicingScore |
                                     AnalysisSignalMask.SpectralFlux |
                                     AnalysisSignalMask.HnrDb;
        signals = AnalysisSignalDependencies.Expand(signals);

        _signalProcessor.ProcessBlock(_hopBuffer.AsSpan(0, shift), _sampleTime, default, signals);

        float pitch = _signalProcessor.GetLastValue(AnalysisSignalId.PitchHz);
        float pitchConfidence = _signalProcessor.GetLastValue(AnalysisSignalId.PitchConfidence);
        float hnr = _signalProcessor.GetLastValue(AnalysisSignalId.HnrDb);
        float flux = _signalProcessor.GetLastValue(AnalysisSignalId.SpectralFlux);
        float slope = _signalProcessor.LastSlope;
        float speechPresence = _signalProcessor.GetLastValue(AnalysisSignalId.SpeechPresence);
        var voicing = (VoicingState)MathF.Round(_signalProcessor.GetLastValue(AnalysisSignalId.VoicingState));

        _lastMetrics = _metricsProcessor.Process(
            waveformMin,
            waveformMax,
            analysisRaw,
            magnitudes,
            ReadOnlySpan<float>.Empty,
            _binResolution,
            speechPresence,
            pitch,
            pitchConfidence,
            voicing,
            flux,
            slope,
            hnr,
            _frameId);

        _energySyllableDetector.Process(
            _metricsProcessor.LastEnergyDb,
            voicing,
            _frameId,
            _hopSize,
            _sampleRate);
        _energySyllableStats = _energySyllableDetector.DebugStats;

        _metricFrames++;
        _sumMonotoneScore += _lastMetrics.MonotoneScore;
        _sumClarityScore += _lastMetrics.ClarityScore;
        _sumIntelligibilityScore += _lastMetrics.IntelligibilityScore;
        if (pitchConfidence > 0f)
        {
            _sumPitchHz += pitch;
            _sumPitchConfidence += pitchConfidence;
            _pitchFrames++;
        }

        _frames++;
        switch (voicing)
        {
            case VoicingState.Voiced:
                _voicedFrames++;
                break;
            case VoicingState.Unvoiced:
                _unvoicedFrames++;
                break;
            default:
                _silenceFrames++;
                break;
        }

        if (_metricsProcessor.LastEnergyDb < _minEnergyDb)
        {
            _minEnergyDb = _metricsProcessor.LastEnergyDb;
        }
        if (_metricsProcessor.LastEnergyDb > _maxEnergyDb)
        {
            _maxEnergyDb = _metricsProcessor.LastEnergyDb;
        }
        if (_metricsProcessor.LastSyllableEnergyDb < _minSyllableEnergyDb)
        {
            _minSyllableEnergyDb = _metricsProcessor.LastSyllableEnergyDb;
        }
        if (_metricsProcessor.LastSyllableEnergyDb > _maxSyllableEnergyDb)
        {
            _maxSyllableEnergyDb = _metricsProcessor.LastSyllableEnergyDb;
        }
        if (pitchConfidence < _minPitchConfidence)
        {
            _minPitchConfidence = pitchConfidence;
        }
        if (pitchConfidence > _maxPitchConfidence)
        {
            _maxPitchConfidence = pitchConfidence;
        }
        if (pitch < _minPitchHz)
        {
            _minPitchHz = pitch;
        }
        if (pitch > _maxPitchHz)
        {
            _maxPitchHz = pitch;
        }
        if (flux < _minFlux)
        {
            _minFlux = flux;
        }
        if (flux > _maxFlux)
        {
            _maxFlux = flux;
        }
        if (_lastMetrics.SyllableDetected)
        {
            _syllableDetectedCount++;
        }

        _frameId++;
        _sampleTime += shift;
    }

    private SpeechMetricsOfflineResult CreateResult()
    {
        float meanMonotone = _metricFrames > 0 ? (float)(_sumMonotoneScore / _metricFrames) : 0f;
        float meanClarity = _metricFrames > 0 ? (float)(_sumClarityScore / _metricFrames) : 0f;
        float meanIntelligibility = _metricFrames > 0 ? (float)(_sumIntelligibilityScore / _metricFrames) : 0f;
        float meanPitchHz = _pitchFrames > 0 ? (float)(_sumPitchHz / _pitchFrames) : 0f;
        float meanPitchConfidence = _pitchFrames > 0 ? (float)(_sumPitchConfidence / _pitchFrames) : 0f;

        return new SpeechMetricsOfflineResult(
            _lastMetrics.WordsPerMinute,
            _lastMetrics.ArticulationWpm,
            _lastMetrics.PauseRatio,
            _lastMetrics.MeanPauseDurationMs,
            _lastMetrics.PausesPerMinute,
            _lastMetrics.FilledPauseRatio,
            _lastMetrics.PauseMicroCount,
            _lastMetrics.PauseShortCount,
            _lastMetrics.PauseMediumCount,
            _lastMetrics.PauseLongCount,
            _frames,
            _voicedFrames,
            _unvoicedFrames,
            _silenceFrames,
            _syllableDetectedCount,
            FixInf(_minEnergyDb),
            FixInf(_maxEnergyDb),
            FixInf(_minSyllableEnergyDb),
            FixInf(_maxSyllableEnergyDb),
            FixInf(_minPitchConfidence),
            FixInf(_maxPitchConfidence),
            FixInf(_minPitchHz),
            FixInf(_maxPitchHz),
            FixInf(_minFlux),
            FixInf(_maxFlux),
            meanMonotone,
            meanClarity,
            meanIntelligibility,
            meanPitchHz,
            meanPitchConfidence,
            _metricsProcessor.SyllableDebugStats,
            _energySyllableStats,
            _metricsProcessor.RateDebugStats,
            _metricsProcessor.PauseDebugStats);
    }

    private static float FixInf(float value)
    {
        if (float.IsPositiveInfinity(value) || float.IsNegativeInfinity(value))
        {
            return 0f;
        }
        return value;
    }

    private long _frames;
    private long _metricFrames;
    private long _pitchFrames;
    private long _voicedFrames;
    private long _unvoicedFrames;
    private long _silenceFrames;
    private long _syllableDetectedCount;
    private float _minEnergyDb = float.PositiveInfinity;
    private float _maxEnergyDb = float.NegativeInfinity;
    private float _minSyllableEnergyDb = float.PositiveInfinity;
    private float _maxSyllableEnergyDb = float.NegativeInfinity;
    private float _minPitchConfidence = float.PositiveInfinity;
    private float _maxPitchConfidence = float.NegativeInfinity;
    private float _minPitchHz = float.PositiveInfinity;
    private float _maxPitchHz = float.NegativeInfinity;
    private float _minFlux = float.PositiveInfinity;
    private float _maxFlux = float.NegativeInfinity;
    private double _sumMonotoneScore;
    private double _sumClarityScore;
    private double _sumIntelligibilityScore;
    private double _sumPitchHz;
    private double _sumPitchConfidence;
}

internal readonly record struct SpeechMetricsOfflineResult(
    float WordsPerMinute,
    float ArticulationWpm,
    float PauseRatio,
    float MeanPauseDurationMs,
    float PausesPerMinute,
    float FilledPauseRatio,
    float PauseMicroCount,
    float PauseShortCount,
    float PauseMediumCount,
    float PauseLongCount,
    long Frames,
    long VoicedFrames,
    long UnvoicedFrames,
    long SilenceFrames,
    long SyllableDetected,
    float MinEnergyDb,
    float MaxEnergyDb,
    float MinSyllableEnergyDb,
    float MaxSyllableEnergyDb,
    float MinPitchConfidence,
    float MaxPitchConfidence,
    float MinPitchHz,
    float MaxPitchHz,
    float MinSpectralFlux,
    float MaxSpectralFlux,
    float MeanMonotoneScore,
    float MeanClarityScore,
    float MeanIntelligibilityScore,
    float MeanPitchHz,
    float MeanPitchConfidence,
    SyllableDetectorDebugStats SyllableStats,
    SyllableDetectorDebugStats EnergyStats,
    SpeechRateDebugStats RateStats,
    PauseDetectorDebugStats PauseStats)
{
    public string FormatDebugSummary(string label)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "SpeechMetricsSummary {0} wpm={1:0.00} artWpm={2:0.00} pauseRatio={3:0.00} meanPauseMs={4:0.0} ppm={5:0.0} filledRatio={6:0.00} pauseBins=m{7} s{8} md{9} l{10} frames={11} voiced={12} unvoiced={13} silence={14} syllDet={15} energyDb=[{16:0.0},{17:0.0}] syllDb=[{18:0.0},{19:0.0}] pitchConf=[{20:0.00},{21:0.00}] pitchHz=[{22:0.0},{23:0.0}] flux=[{24:0.000},{25:0.000}] peaks={26} voicedPeaks={27} det={28} detV={29} detU={30} rejPeak={31} rejVoiced={32} rejProm={33} rejPromInst={34} rejPromMean={35} rejMinInt={36} clamp={37} meanPenalty={38} maxPromClamp={39:0.00} maxProm={40:0.00} maxPromInst={41:0.00} maxPromMean={42:0.00} baseDb=[{43:0.0},{44:0.0}] baseUpd={45} baseSkip={46} energyDet={47} energyMaxProm={48:0.00} rateSyll={49} ratePauses={50} pauseFrames={51} pauseSilence={52} pauseFilledCand={53} pauseSpeaking={54} pauseEvS={55} pauseEvF={56} pauseFramesS={57} pauseFramesF={58} monoMean={59:0.000} clarityMean={60:0.000} intelMean={61:0.000} pitchMean={62:0.0} pitchConfMean={63:0.00}",
            label,
            WordsPerMinute,
            ArticulationWpm,
            PauseRatio,
            MeanPauseDurationMs,
            PausesPerMinute,
            FilledPauseRatio,
            PauseMicroCount,
            PauseShortCount,
            PauseMediumCount,
            PauseLongCount,
            Frames,
            VoicedFrames,
            UnvoicedFrames,
            SilenceFrames,
            SyllableDetected,
            MinEnergyDb,
            MaxEnergyDb,
            MinSyllableEnergyDb,
            MaxSyllableEnergyDb,
            MinPitchConfidence,
            MaxPitchConfidence,
            MinPitchHz,
            MaxPitchHz,
            MinSpectralFlux,
            MaxSpectralFlux,
            SyllableStats.Peaks,
            SyllableStats.VoicedPeaks,
            SyllableStats.Detected,
            SyllableStats.DetectedVoiced,
            SyllableStats.DetectedUnvoiced,
            SyllableStats.RejectNotPeak,
            SyllableStats.RejectUnvoiced,
            SyllableStats.RejectLowProminence,
            SyllableStats.RejectLowProminenceInstant,
            SyllableStats.RejectLowProminenceMean,
            SyllableStats.RejectMinInterval,
            SyllableStats.ClampApplied,
            SyllableStats.MeanPenaltyApplied,
            SyllableStats.MaxProminenceClampDb,
            SyllableStats.MaxProminenceDb,
            SyllableStats.MaxProminenceInstantDb,
            SyllableStats.MaxProminenceMeanDb,
            SyllableStats.MinBaselineDb,
            SyllableStats.MaxBaselineDb,
            SyllableStats.BaselineUpdates,
            SyllableStats.BaselineSkips,
            EnergyStats.Detected,
            EnergyStats.MaxProminenceDb,
            RateStats.SyllablesRecorded,
            RateStats.PausesRecorded,
            PauseStats.Frames,
            PauseStats.SilenceFrames,
            PauseStats.FilledCandidateFrames,
            PauseStats.SpeakingFrames,
            PauseStats.SilentPauseEvents,
            PauseStats.FilledPauseEvents,
            PauseStats.SilentPauseFrames,
            PauseStats.FilledPauseFrames,
            MeanMonotoneScore,
            MeanClarityScore,
            MeanIntelligibilityScore,
            MeanPitchHz,
            MeanPitchConfidence);
    }
}

public sealed record SpeechPipelineConfig(
    bool VadEnabled,
    bool DenoiserEnabled,
    bool GainEnabled,
    bool CompressorEnabled,
    bool DeEsserEnabled,
    bool AnalysisTapEnabled,
    AnalysisTapMode SpeechPresenceMode,
    StoredChainPreset? ChainPreset,
    IReadOnlyDictionary<string, bool>? PluginOverrides)
{
    public static SpeechPipelineConfig Build(
        bool vadEnabled,
        bool denoiserEnabled,
        bool gainEnabled,
        bool compressorEnabled,
        bool deEsserEnabled,
        bool analysisTapEnabled,
        AnalysisTapMode speechPresenceMode)
    {
        return new SpeechPipelineConfig(
            vadEnabled,
            denoiserEnabled,
            gainEnabled,
            compressorEnabled,
            deEsserEnabled,
            analysisTapEnabled,
            speechPresenceMode,
            ChainPreset: null,
            PluginOverrides: null);
    }

    public static SpeechPipelineConfig FromChainPreset(
        StoredChainPreset preset,
        AnalysisTapMode speechPresenceMode,
        IReadOnlyDictionary<string, bool>? pluginOverrides = null)
    {
        if (preset is null)
        {
            throw new ArgumentNullException(nameof(preset));
        }

        return new SpeechPipelineConfig(
            VadEnabled: false,
            DenoiserEnabled: false,
            GainEnabled: false,
            CompressorEnabled: false,
            DeEsserEnabled: false,
            AnalysisTapEnabled: false,
            SpeechPresenceMode: speechPresenceMode,
            ChainPreset: preset,
            PluginOverrides: pluginOverrides);
    }

    public string Describe()
    {
        if (ChainPreset is not null)
        {
            string overrides = string.Empty;
            if (PluginOverrides is not null && PluginOverrides.Count > 0)
            {
                overrides = string.Join(", ",
                    PluginOverrides.Select(kvp => $"{kvp.Key}={(kvp.Value ? "on" : "off")}"));
                overrides = $" overrides=[{overrides}]";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "chain={0} tapMode={1}{2}",
                ChainPreset.Name,
                SpeechPresenceMode,
                overrides);
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "vad={0} denoiser={1} gain={2} compressor={3} deesser={4} tap={5} tapMode={6}",
            FormatToggle(VadEnabled),
            FormatToggle(DenoiserEnabled),
            FormatToggle(GainEnabled),
            FormatToggle(CompressorEnabled),
            FormatToggle(DeEsserEnabled),
            FormatToggle(AnalysisTapEnabled),
            SpeechPresenceMode);
    }

    private static string FormatToggle(bool enabled)
        => enabled ? "on" : "off";
}

public sealed record SpeechPipelineCase(string Name, SpeechPipelineConfig Config)
{
    public string Describe()
    {
        return $"{Name} ({Config.Describe()})";
    }

    public override string ToString()
    {
        return Describe();
    }
}
