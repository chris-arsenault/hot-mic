using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HotMic.Core.Analysis;
using HotMic.Core.Dsp;
using HotMic.Core.Dsp.Analysis;
using HotMic.Core.Dsp.Analysis.Pitch;
using HotMic.Core.Dsp.Analysis.Speech;
using HotMic.Core.Plugins;
using NAudio.Wave;
using Xunit;

namespace HotMic.Core.Tests;

public class SpeechMetricsIntegrationTests
{
    private const string TestWavRelativePath = "tests/HotMic.Core.Tests/data/noisy_voice_4sec.wav";
    private const float ExpectedSingleWpm = 24f; // Baseline from current pipeline.
    private const float ExpectedSerialWpm = 68f; // Baseline from current pipeline.
    private const float ExpectedTolerance = 1.0f;

    [Fact]
    public void SpeechMetrics_WpmMatchesBaseline()
    {
        string path = FindRepoFile(TestWavRelativePath);
        float[] samples = LoadMonoSamples(path, out int sampleRate);

        var single = SpeechMetricsOfflineAnalyzer.Analyze(samples, sampleRate);
        var serial = SpeechMetricsOfflineAnalyzer.Analyze(Repeat(samples, 5), sampleRate);

        Console.WriteLine(single.FormatDebugSummary("single"));
        Console.WriteLine(serial.FormatDebugSummary("serial"));

        Assert.InRange(single.WordsPerMinute, ExpectedSingleWpm - ExpectedTolerance, ExpectedSingleWpm + ExpectedTolerance);
        Assert.InRange(serial.WordsPerMinute, ExpectedSerialWpm - ExpectedTolerance, ExpectedSerialWpm + ExpectedTolerance);
    }

    private static float[] LoadMonoSamples(string path, out int sampleRate)
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

    private static float[] Repeat(float[] samples, int times)
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

    private static string FindRepoFile(string relativePath)
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

    private sealed class SpeechMetricsOfflineAnalyzer
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
            return new SpeechMetricsOfflineResult(
                _lastMetrics.WordsPerMinute,
                _lastMetrics.ArticulationWpm,
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
                _metricsProcessor.SyllableDebugStats,
                _energySyllableStats,
                _metricsProcessor.RateDebugStats);
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
    }

    private readonly record struct SpeechMetricsOfflineResult(
        float WordsPerMinute,
        float ArticulationWpm,
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
        SyllableDetectorDebugStats SyllableStats,
        SyllableDetectorDebugStats EnergyStats,
        SpeechRateDebugStats RateStats)
    {
        public string FormatDebugSummary(string label)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "SpeechMetricsSummary {0} wpm={1:0.00} artWpm={2:0.00} frames={3} voiced={4} unvoiced={5} silence={6} syllDet={7} energyDb=[{8:0.0},{9:0.0}] syllDb=[{10:0.0},{11:0.0}] pitchConf=[{12:0.00},{13:0.00}] pitchHz=[{14:0.0},{15:0.0}] flux=[{16:0.000},{17:0.000}] peaks={18} voicedPeaks={19} det={20} rejPeak={21} rejVoiced={22} rejProm={23} rejMinInt={24} maxProm={25:0.00} energyDet={26} energyMaxProm={27:0.00} rateSyll={28} ratePauses={29}",
                label,
                WordsPerMinute,
                ArticulationWpm,
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
                SyllableStats.RejectNotPeak,
                SyllableStats.RejectUnvoiced,
                SyllableStats.RejectLowProminence,
                SyllableStats.RejectMinInterval,
                SyllableStats.MaxProminenceDb,
                EnergyStats.Detected,
                EnergyStats.MaxProminenceDb,
                RateStats.SyllablesRecorded,
                RateStats.PausesRecorded);
        }
    }
}
