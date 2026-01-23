using System.Collections.Generic;
using System.IO;
using HotMic.Core.Analysis;
using HotMic.Core.Dsp;
using HotMic.Core.Dsp.Analysis;
using HotMic.Core.Dsp.Analysis.Pitch;
using HotMic.Core.Dsp.Analysis.Speech;
using HotMic.Core.Dsp.Filters;
using HotMic.Core.Dsp.Fft;
using HotMic.Core.Plugins;
using NAudio.Wave;
using Xunit;

namespace HotMic.Core.Tests;

public class SpeechMetricsIntegrationTests
{
    private const string TestWavRelativePath = "tests/HotMic.Core.Tests/data/noisy_voice_4sec.wav";
    private const float ExpectedSingleWpm = 0f; // Baseline from current pipeline.
    private const float ExpectedSerialWpm = 0f; // Baseline from current pipeline.
    private const float ExpectedTolerance = 0.5f;

    [Fact]
    public void SpeechMetrics_WpmMatchesBaseline()
    {
        string path = FindRepoFile(TestWavRelativePath);
        float[] samples = LoadMonoSamples(path, out int sampleRate);

        float singleWpm = SpeechMetricsOfflineAnalyzer.ComputeWpm(samples, sampleRate);
        float serialWpm = SpeechMetricsOfflineAnalyzer.ComputeWpm(Repeat(samples, 5), sampleRate);

        Assert.InRange(singleWpm, ExpectedSingleWpm - ExpectedTolerance, ExpectedSingleWpm + ExpectedTolerance);
        Assert.InRange(serialWpm, ExpectedSerialWpm - ExpectedTolerance, ExpectedSerialWpm + ExpectedTolerance);
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
        private readonly int _fftSize;
        private readonly int _hopSize;
        private readonly int _analysisSize;
        private readonly float _binResolution;
        private readonly float _fftNormalization;
        private readonly FastFft _fft;
        private readonly float[] _analysisBufferRaw;
        private readonly float[] _analysisBufferProcessed;
        private readonly float[] _hopBuffer;
        private readonly float[] _fftReal;
        private readonly float[] _fftImag;
        private readonly float[] _fftWindow;
        private readonly float[] _fftMagnitudes;
        private readonly OnePoleHighPass _dcHighPass = new();
        private readonly BiquadFilter _rumbleHighPass = new();
        private readonly PreEmphasisFilter _preEmphasisFilter = new();
        private readonly bool _preEmphasisEnabled;
        private readonly bool _highPassEnabled;
        private readonly float _highPassCutoff;
        private readonly AnalysisSignalProcessor _signalProcessor = new();
        private readonly SpeechMetricsProcessor _metricsProcessor = new();
        private int _analysisFilled;
        private long _frameId;
        private long _sampleTime;
        private SpeechMetricsFrame _lastMetrics;

        private SpeechMetricsOfflineAnalyzer(int sampleRate)
        {
            var config = new AnalysisConfiguration();
            _sampleRate = sampleRate;
            _fftSize = config.FftSize;
            _analysisSize = _fftSize;
            _hopSize = config.ComputeHopSize();
            _binResolution = sampleRate / (float)_fftSize;
            _fft = new FastFft(_fftSize);
            _analysisBufferRaw = new float[_analysisSize];
            _analysisBufferProcessed = new float[_analysisSize];
            _hopBuffer = new float[_hopSize];
            _fftReal = new float[_fftSize];
            _fftImag = new float[_fftSize];
            _fftWindow = new float[_fftSize];
            _fftMagnitudes = new float[_fftSize / 2];
            _preEmphasisEnabled = config.PreEmphasis;
            _highPassEnabled = config.HighPassEnabled;
            _highPassCutoff = config.HighPassCutoff;

            WindowFunctions.Fill(_fftWindow, config.WindowFunction);
            double sum = 0.0;
            for (int i = 0; i < _fftWindow.Length; i++)
            {
                sum += _fftWindow[i];
            }
            _fftNormalization = 2f / (float)Math.Max(1e-6, sum);

            _dcHighPass.Configure(10f, sampleRate);
            _dcHighPass.Reset();
            _rumbleHighPass.SetHighPass(sampleRate, _highPassCutoff, 0.707f);
            _rumbleHighPass.Reset();
            _preEmphasisFilter.Configure(0.97f);
            _preEmphasisFilter.Reset();

            var settings = new AnalysisSignalProcessorSettings
            {
                AnalysisSize = _fftSize,
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
            _signalProcessor.Reset();
            _metricsProcessor.Configure(_hopSize, sampleRate);
        }

        public static float ComputeWpm(float[] samples, int sampleRate)
        {
            var analyzer = new SpeechMetricsOfflineAnalyzer(sampleRate);
            analyzer.Process(samples);
            return analyzer._lastMetrics.WordsPerMinute;
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
            int tail = _analysisSize - shift;

            Array.Copy(_analysisBufferRaw, shift, _analysisBufferRaw, 0, tail);
            Array.Copy(_analysisBufferProcessed, shift, _analysisBufferProcessed, 0, tail);

            float waveformMin = float.MaxValue;
            float waveformMax = float.MinValue;

            for (int i = 0; i < shift; i++)
            {
                float sample = _hopBuffer[i];
                float dcRemoved = _dcHighPass.Process(sample);
                float filtered = _highPassEnabled ? _rumbleHighPass.Process(dcRemoved) : dcRemoved;
                float emphasized = _preEmphasisEnabled ? _preEmphasisFilter.Process(filtered) : filtered;

                _analysisBufferRaw[tail + i] = filtered;
                _analysisBufferProcessed[tail + i] = emphasized;

                if (filtered < waveformMin) waveformMin = filtered;
                if (filtered > waveformMax) waveformMax = filtered;
            }

            _analysisFilled = Math.Min(_analysisSize, _analysisFilled + shift);
            if (_analysisFilled < _analysisSize)
            {
                return;
            }

            for (int i = 0; i < _analysisSize; i++)
            {
                float sample = _analysisBufferProcessed[i];
                _fftReal[i] = sample * _fftWindow[i];
                _fftImag[i] = 0f;
            }

            _fft.Forward(_fftReal, _fftImag);

            int half = _fftSize / 2;
            for (int i = 0; i < half; i++)
            {
                float re = _fftReal[i];
                float im = _fftImag[i];
                _fftMagnitudes[i] = MathF.Sqrt(re * re + im * im) * _fftNormalization;
            }

            AnalysisSignalMask signals = AnalysisSignalMask.PitchHz |
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
            var voicing = (VoicingState)MathF.Round(_signalProcessor.GetLastValue(AnalysisSignalId.VoicingState));

            _lastMetrics = _metricsProcessor.Process(
                waveformMin,
                waveformMax,
                _analysisBufferRaw.AsSpan(tail, shift),
                _fftMagnitudes,
                ReadOnlySpan<float>.Empty,
                _binResolution,
                pitch,
                pitchConfidence,
                voicing,
                flux,
                slope,
                hnr,
                _frameId);

            _frameId++;
            _sampleTime += shift;
        }
    }
}
