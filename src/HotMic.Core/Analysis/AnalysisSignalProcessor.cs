using System;
using System.Diagnostics;
using System.Threading;
using HotMic.Core.Dsp;
using HotMic.Core.Dsp.Analysis;
using HotMic.Core.Dsp.Analysis.Pitch;
using HotMic.Core.Dsp.Fft;
using HotMic.Core.Dsp.Filters;
using HotMic.Core.Dsp.Spectrogram;
using HotMic.Core.Dsp.Voice;
using HotMic.Core.Plugins;

namespace HotMic.Core.Analysis;

public readonly record struct AnalysisSignalProcessorSettings
{
    public int AnalysisSize { get; init; }
    public PitchDetectorType PitchDetector { get; init; }
    public VoicingDetectorSettings VoicingSettings { get; init; }
    public float MinFrequency { get; init; }
    public float MaxFrequency { get; init; }
    public WindowFunction WindowFunction { get; init; }
    public bool PreEmphasisEnabled { get; init; }
    public bool HighPassEnabled { get; init; }
    public float HighPassCutoff { get; init; }

    public static AnalysisSignalProcessorSettings Default => new()
    {
        AnalysisSize = 2048,
        PitchDetector = PitchDetectorType.Yin,
        VoicingSettings = new VoicingDetectorSettings(),
        MinFrequency = 80f,
        MaxFrequency = 8000f,
        WindowFunction = WindowFunction.Hann,
        PreEmphasisEnabled = true,
        HighPassEnabled = true,
        HighPassCutoff = 60f
    };
}

internal readonly record struct AnalysisSignalProfilingSnapshot(
    long LastTotalTicks,
    long MaxTotalTicks,
    long LastPreprocessTicks,
    long MaxPreprocessTicks,
    long LastFftTicks,
    long MaxFftTicks,
    long LastPitchTicks,
    long MaxPitchTicks,
    long LastVoicingTicks,
    long MaxVoicingTicks,
    long LastFeatureTicks,
    long MaxFeatureTicks,
    long LastWriteTicks,
    long MaxWriteTicks);

public sealed class AnalysisSignalProcessor
{
    private const float SpeechThresholdDb = -50f;
    private const float SpeechRangeDb = 30f;
    private const float FricativeHighHz = 2500f;
    private const float SibilanceCenterHz = 6500f;
    private const float SibilanceQ = 1.2f;
    private const float DcCutoffHz = 10f;
    private const float DefaultPreEmphasis = 0.97f;
    public const float SpeechPresenceGateThreshold = 0.05f;
    // Pitch/CPP are evaluated every 2 analysis frames to reduce CPU cost (docs/technical/Pitch.md).
    private const int PitchFrameStride = 2;

    private int _sampleRate;
    private int _hopSize;
    private int _analysisSize;
    private int _analysisBins;
    private float _binResolution;
    private AnalysisSignalProcessorSettings _settings;

    private float[] _analysisBufferRaw = Array.Empty<float>();
    private float[] _analysisBufferProcessed = Array.Empty<float>();
    private float[] _analysisWindow = Array.Empty<float>();
    private float[] _fftReal = Array.Empty<float>();
    private float[] _fftImag = Array.Empty<float>();
    private float[] _fftMagnitudes = Array.Empty<float>();
    private float[] _highFluxPrevious = Array.Empty<float>();
    private float[] _binFrequencies = Array.Empty<float>();
    private bool _highFluxInitialized;

    private FastFft? _fft;
    private readonly SpectralFeatureExtractor _featureExtractor = new();
    private readonly VoicingDetector _voicingDetector = new();

    private OnePoleHighPass _dcHighPass;
    private BiquadFilter _rumbleHighPass = new();
    private PreEmphasisFilter _preEmphasisFilter;

    private readonly EnvelopeFollower _speechEnv = new();
    private readonly EnvelopeFollower _fricativeEnv = new();
    private readonly EnvelopeFollower _sibilanceEnv = new();
    private readonly BiquadFilter _fricativeHighPass = new();
    private readonly BiquadFilter _sibilanceBand = new();

    private YinPitchDetector? _yinPitchDetector;
    private PyinPitchDetector? _pyinPitchDetector;
    private AutocorrelationPitchDetector? _autocorrPitchDetector;
    private CepstralPitchDetector? _cepstralPitchDetector;
    private SwipePitchDetector? _swipePitchDetector;


    private float[] _speechBuffer = Array.Empty<float>();
    private float[] _voicingScoreBuffer = Array.Empty<float>();
    private float[] _voicingStateBuffer = Array.Empty<float>();
    private float[] _fricativeBuffer = Array.Empty<float>();
    private float[] _sibilanceBuffer = Array.Empty<float>();
    private float[] _onsetFluxBuffer = Array.Empty<float>();
    private float[] _pitchBuffer = Array.Empty<float>();
    private float[] _pitchConfidenceBuffer = Array.Empty<float>();
    private float[] _spectralFluxBuffer = Array.Empty<float>();
    private float[] _hnrDbBuffer = Array.Empty<float>();

    private readonly float[] _lastValues = new float[(int)AnalysisSignalId.Count];
    private float _lastCpp;
    private float _lastCentroid;
    private float _lastSlope;
    private int _pitchFrameIndex;
    private int _pitchAlgorithmRaw;
    private float _externalSpeechPresenceGate;
    private int _externalSpeechPresenceGateEnabled;
    private int _generatedSpeechPresenceGateEnabled;
    private int _profilingEnabled;
    private long _lastTotalTicks;
    private long _maxTotalTicks;
    private long _lastPreprocessTicks;
    private long _maxPreprocessTicks;
    private long _lastFftTicks;
    private long _maxFftTicks;
    private long _lastPitchTicks;
    private long _maxPitchTicks;
    private long _lastVoicingTicks;
    private long _maxVoicingTicks;
    private long _lastFeatureTicks;
    private long _maxFeatureTicks;
    private long _lastWriteTicks;
    private long _maxWriteTicks;

    public int SampleRate => _sampleRate;
    public int HopSize => _hopSize;
    public int AnalysisSize => _analysisSize;
    public float LastCpp => Volatile.Read(ref _lastCpp);
    public float LastCentroid => Volatile.Read(ref _lastCentroid);
    public float LastSlope => Volatile.Read(ref _lastSlope);

    public float GetLastValue(AnalysisSignalId signal)
    {
        int index = (int)signal;
        if ((uint)index >= (uint)_lastValues.Length)
        {
            return 0f;
        }

        return Volatile.Read(ref _lastValues[index]);
    }

    internal void SetProfilingEnabled(bool enabled)
    {
        int value = enabled ? 1 : 0;
        int prior = Interlocked.Exchange(ref _profilingEnabled, value);
        if (prior != value)
        {
            ResetProfiling();
            SetPitchProfilingEnabled(enabled);
        }
    }

    internal AnalysisSignalProfilingSnapshot GetProfilingSnapshot()
    {
        return new AnalysisSignalProfilingSnapshot(
            Interlocked.Read(ref _lastTotalTicks),
            Interlocked.Read(ref _maxTotalTicks),
            Interlocked.Read(ref _lastPreprocessTicks),
            Interlocked.Read(ref _maxPreprocessTicks),
            Interlocked.Read(ref _lastFftTicks),
            Interlocked.Read(ref _maxFftTicks),
            Interlocked.Read(ref _lastPitchTicks),
            Interlocked.Read(ref _maxPitchTicks),
            Interlocked.Read(ref _lastVoicingTicks),
            Interlocked.Read(ref _maxVoicingTicks),
            Interlocked.Read(ref _lastFeatureTicks),
            Interlocked.Read(ref _maxFeatureTicks),
            Interlocked.Read(ref _lastWriteTicks),
            Interlocked.Read(ref _maxWriteTicks));
    }

    internal PitchProfilingSnapshot GetPitchProfilingSnapshot()
    {
        PitchDetectorType algorithm = GetPitchAlgorithm();
        PitchProfilingSnapshot snapshot = algorithm switch
        {
            PitchDetectorType.Yin when _yinPitchDetector is not null => _yinPitchDetector.GetProfilingSnapshot(),
            PitchDetectorType.Pyin when _pyinPitchDetector is not null => _pyinPitchDetector.GetProfilingSnapshot(),
            PitchDetectorType.Autocorrelation when _autocorrPitchDetector is not null => _autocorrPitchDetector.GetProfilingSnapshot(),
            PitchDetectorType.Cepstral when _cepstralPitchDetector is not null => _cepstralPitchDetector.GetProfilingSnapshot(),
            PitchDetectorType.Swipe when _swipePitchDetector is not null => _swipePitchDetector.GetProfilingSnapshot(),
            _ => default
        };

        if (snapshot.LastTotalTicks == 0 && snapshot.MaxTotalTicks == 0)
        {
            GetPitchPeriodRange(out int minPeriod, out int maxPeriod);
            snapshot = new PitchProfilingSnapshot(
                algorithm,
                Interlocked.Read(ref _lastPitchTicks),
                Interlocked.Read(ref _maxPitchTicks),
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                _analysisSize,
                minPeriod,
                maxPeriod);
        }

        return snapshot;
    }

    internal void SetPitchAlgorithm(PitchDetectorType algorithm)
    {
        Interlocked.Exchange(ref _pitchAlgorithmRaw, (int)algorithm);
    }

    internal void SetExternalSpeechPresenceGate(float value)
    {
        if (!float.IsFinite(value))
        {
            value = 0f;
        }
        value = Math.Clamp(value, 0f, 1f);
        Volatile.Write(ref _externalSpeechPresenceGate, value);
        Volatile.Write(ref _externalSpeechPresenceGateEnabled, 1);
    }

    internal void ClearExternalSpeechPresenceGate()
    {
        Volatile.Write(ref _externalSpeechPresenceGateEnabled, 0);
    }

    internal void SetGeneratedSpeechPresenceGateEnabled(bool enabled)
    {
        Volatile.Write(ref _generatedSpeechPresenceGateEnabled, enabled ? 1 : 0);
    }

    public void Configure(int sampleRate, int hopSize, AnalysisSignalProcessorSettings settings)
    {
        _sampleRate = Math.Max(1, sampleRate);
        _hopSize = Math.Max(1, hopSize);
        _settings = settings;
        _pitchAlgorithmRaw = (int)settings.PitchDetector;

        int desiredSize = Math.Max(_hopSize, Math.Max(64, settings.AnalysisSize));
        _analysisSize = NextPowerOfTwo(desiredSize);
        _analysisBins = _analysisSize / 2;
        _binResolution = _sampleRate / (float)_analysisSize;

        EnsureBuffer(ref _analysisBufferRaw, _analysisSize);
        EnsureBuffer(ref _analysisBufferProcessed, _analysisSize);
        EnsureBuffer(ref _analysisWindow, _analysisSize);
        EnsureBuffer(ref _fftReal, _analysisSize);
        EnsureBuffer(ref _fftImag, _analysisSize);
        EnsureBuffer(ref _fftMagnitudes, _analysisBins);
        EnsureBuffer(ref _highFluxPrevious, _analysisBins);
        EnsureBuffer(ref _binFrequencies, _analysisBins);
        _highFluxInitialized = false;

        _fft ??= new FastFft(_analysisSize);
        if (_fft.Size != _analysisSize)
        {
            _fft = new FastFft(_analysisSize);
        }

        WindowFunctions.Fill(_analysisWindow, settings.WindowFunction);

        for (int i = 0; i < _analysisBins; i++)
        {
            _binFrequencies[i] = i * _binResolution;
        }

        _featureExtractor.EnsureCapacity(_analysisBins);
        _featureExtractor.UpdateFrequencies(_binFrequencies);

        EnsureSignalBuffers(_hopSize);

        ConfigureFilters(settings);
        ConfigurePitchDetectors(settings.MinFrequency, settings.MaxFrequency);
        _voicingDetector.Configure(_sampleRate, _hopSize, _binResolution, settings.VoicingSettings);
        _pitchFrameIndex = 0;
    }

    public void Reset()
    {
        _dcHighPass.Reset();
        _rumbleHighPass.Reset();
        _preEmphasisFilter.Reset();
        _speechEnv.Reset();
        _fricativeEnv.Reset();
        _sibilanceEnv.Reset();
        _fricativeHighPass.Reset();
        _sibilanceBand.Reset();
        _featureExtractor.Reset();
        Array.Clear(_analysisBufferRaw, 0, _analysisBufferRaw.Length);
        Array.Clear(_analysisBufferProcessed, 0, _analysisBufferProcessed.Length);
        Array.Clear(_fftReal, 0, _fftReal.Length);
        Array.Clear(_fftImag, 0, _fftImag.Length);
        Array.Clear(_fftMagnitudes, 0, _fftMagnitudes.Length);
        Array.Clear(_highFluxPrevious, 0, _highFluxPrevious.Length);
        Array.Clear(_speechBuffer, 0, _speechBuffer.Length);
        Array.Clear(_voicingScoreBuffer, 0, _voicingScoreBuffer.Length);
        Array.Clear(_voicingStateBuffer, 0, _voicingStateBuffer.Length);
        Array.Clear(_fricativeBuffer, 0, _fricativeBuffer.Length);
        Array.Clear(_sibilanceBuffer, 0, _sibilanceBuffer.Length);
        Array.Clear(_onsetFluxBuffer, 0, _onsetFluxBuffer.Length);
        Array.Clear(_pitchBuffer, 0, _pitchBuffer.Length);
        Array.Clear(_pitchConfidenceBuffer, 0, _pitchConfidenceBuffer.Length);
        Array.Clear(_spectralFluxBuffer, 0, _spectralFluxBuffer.Length);
        Array.Clear(_hnrDbBuffer, 0, _hnrDbBuffer.Length);
        Array.Clear(_lastValues, 0, _lastValues.Length);
        _highFluxInitialized = false;
        _lastCpp = 0f;
        _lastCentroid = 0f;
        _lastSlope = 0f;
        _pitchFrameIndex = 0;
    }

    public void ProcessBlock(ReadOnlySpan<float> hop, long sampleTime, in AnalysisSignalWriter writer, AnalysisSignalMask requestedSignals)
    {
        int count = hop.Length;
        if (count == 0 || sampleTime < 0)
        {
            return;
        }

        bool profilingEnabled = Volatile.Read(ref _profilingEnabled) != 0;
        long totalStart = 0;
        if (profilingEnabled)
        {
            totalStart = Stopwatch.GetTimestamp();
        }

        if (count > _analysisSize)
        {
            hop = hop.Slice(count - _analysisSize);
            count = hop.Length;
        }

        if (_speechBuffer.Length < count || _analysisBufferRaw.Length < _analysisSize)
        {
            return;
        }

        int tail = Math.Max(0, _analysisSize - count);
        if (tail > 0)
        {
            Array.Copy(_analysisBufferRaw, count, _analysisBufferRaw, 0, tail);
            Array.Copy(_analysisBufferProcessed, count, _analysisBufferProcessed, 0, tail);
        }

        AnalysisSignalMask computeSignals = AnalysisSignalDependencies.Expand(requestedSignals);

        bool gateWithGeneratedSpeech = Volatile.Read(ref _generatedSpeechPresenceGateEnabled) != 0;
        bool needSpeech = (computeSignals & AnalysisSignalMask.SpeechPresence) != 0;
        bool needSpeechPresence = needSpeech || gateWithGeneratedSpeech;
        bool needFricative = (computeSignals & AnalysisSignalMask.FricativeActivity) != 0;
        bool needSibilance = (computeSignals & AnalysisSignalMask.SibilanceEnergy) != 0;
        bool needStreaming = needSpeechPresence || needFricative || needSibilance;

        float speechSum = 0f;
        float fricativeSum = 0f;
        float sibilanceSum = 0f;

        long preprocessStart = 0;
        if (profilingEnabled)
        {
            preprocessStart = Stopwatch.GetTimestamp();
        }

        for (int i = 0; i < count; i++)
        {
            float input = hop[i];
            if (!float.IsFinite(input))
            {
                input = 0f;
            }
            float dcRemoved = _dcHighPass.Process(input);
            float filtered = _settings.HighPassEnabled
                ? _rumbleHighPass.Process(dcRemoved)
                : dcRemoved;
            float emphasized = _settings.PreEmphasisEnabled
                ? _preEmphasisFilter.Process(filtered)
                : filtered;

            int index = tail + i;
            if ((uint)index < (uint)_analysisBufferRaw.Length)
            {
                _analysisBufferRaw[index] = filtered;
                _analysisBufferProcessed[index] = emphasized;
            }

            if (!needStreaming)
            {
                continue;
            }

            float env = _speechEnv.Process(filtered);
            float total = MathF.Max(env, 1e-6f);

            if (needSpeechPresence)
            {
                float envDb = DspUtils.LinearToDb(env);
                float presence = Math.Clamp((envDb - SpeechThresholdDb) / SpeechRangeDb, 0f, 1f);
                speechSum += presence;
                if (needSpeech)
                {
                    _speechBuffer[i] = presence;
                }
            }

            if (needFricative)
            {
                float high = _fricativeHighPass.Process(filtered);
                float highEnv = _fricativeEnv.Process(high);
                float fricative = Math.Clamp(highEnv / total, 0f, 1f);
                _fricativeBuffer[i] = fricative;
                fricativeSum += fricative;
            }

            if (needSibilance)
            {
                float sib = _sibilanceBand.Process(filtered);
                float sibEnv = _sibilanceEnv.Process(sib);
                float sibilance = Math.Clamp(sibEnv / total, 0f, 1f);
                _sibilanceBuffer[i] = sibilance;
                sibilanceSum += sibilance;
            }
        }

        if (profilingEnabled)
        {
            long preprocessTicks = Stopwatch.GetTimestamp() - preprocessStart;
            RecordProfiling(ref _lastPreprocessTicks, ref _maxPreprocessTicks, preprocessTicks);
        }

        if (needSpeechPresence)
        {
            WriteLastValue(AnalysisSignalId.SpeechPresence, speechSum / Math.Max(1, count));
        }
        if (needFricative)
        {
            WriteLastValue(AnalysisSignalId.FricativeActivity, fricativeSum / Math.Max(1, count));
        }
        if (needSibilance)
        {
            WriteLastValue(AnalysisSignalId.SibilanceEnergy, sibilanceSum / Math.Max(1, count));
        }

        bool needsFrame = NeedsFrameSignals(computeSignals);
        long fftTicks = 0;
        if (needsFrame)
        {
            long fftStart = 0;
            if (profilingEnabled)
            {
                fftStart = Stopwatch.GetTimestamp();
            }

            EnsureFftMagnitudes();

            if (profilingEnabled)
            {
                fftTicks = Stopwatch.GetTimestamp() - fftStart;
            }

            ProcessFrameCore(_analysisBufferRaw, _analysisBufferProcessed, _fftMagnitudes, computeSignals, profilingEnabled);
        }

        if (profilingEnabled)
        {
            RecordProfiling(ref _lastFftTicks, ref _maxFftTicks, fftTicks);
            if (!needsFrame)
            {
                RecordProfiling(ref _lastPitchTicks, ref _maxPitchTicks, 0);
                RecordProfiling(ref _lastVoicingTicks, ref _maxVoicingTicks, 0);
                RecordProfiling(ref _lastFeatureTicks, ref _maxFeatureTicks, 0);
            }
        }

        long writeStart = 0;
        if (profilingEnabled)
        {
            writeStart = Stopwatch.GetTimestamp();
        }

        if (needsFrame)
        {
            FillFrameBuffers(count, requestedSignals);
        }

        WriteSignals(writer, requestedSignals, sampleTime, count);

        if (profilingEnabled)
        {
            long writeTicks = Stopwatch.GetTimestamp() - writeStart;
            RecordProfiling(ref _lastWriteTicks, ref _maxWriteTicks, writeTicks);

            long totalTicks = Stopwatch.GetTimestamp() - totalStart;
            RecordProfiling(ref _lastTotalTicks, ref _maxTotalTicks, totalTicks);
        }
    }

    public void ProcessStreaming(ReadOnlySpan<float> hop, AnalysisSignalMask requestedSignals)
    {
        int count = hop.Length;
        if (count == 0)
        {
            return;
        }

        AnalysisSignalMask computeSignals = AnalysisSignalDependencies.Expand(requestedSignals);
        bool gateWithGeneratedSpeech = Volatile.Read(ref _generatedSpeechPresenceGateEnabled) != 0;
        bool needSpeech = (computeSignals & AnalysisSignalMask.SpeechPresence) != 0;
        bool needSpeechPresence = needSpeech || gateWithGeneratedSpeech;
        bool needFricative = (computeSignals & AnalysisSignalMask.FricativeActivity) != 0;
        bool needSibilance = (computeSignals & AnalysisSignalMask.SibilanceEnergy) != 0;
        bool needStreaming = needSpeechPresence || needFricative || needSibilance;
        if (!needStreaming)
        {
            return;
        }

        float speechSum = 0f;
        float fricativeSum = 0f;
        float sibilanceSum = 0f;

        for (int i = 0; i < count; i++)
        {
            float input = hop[i];
            if (!float.IsFinite(input))
            {
                input = 0f;
            }
            float dcRemoved = _dcHighPass.Process(input);
            float filtered = _settings.HighPassEnabled
                ? _rumbleHighPass.Process(dcRemoved)
                : dcRemoved;

            float env = _speechEnv.Process(filtered);
            float total = MathF.Max(env, 1e-6f);

            if (needSpeechPresence)
            {
                float envDb = DspUtils.LinearToDb(env);
                float presence = Math.Clamp((envDb - SpeechThresholdDb) / SpeechRangeDb, 0f, 1f);
                speechSum += presence;
            }

            if (needFricative)
            {
                float high = _fricativeHighPass.Process(filtered);
                float highEnv = _fricativeEnv.Process(high);
                fricativeSum += Math.Clamp(highEnv / total, 0f, 1f);
            }

            if (needSibilance)
            {
                float sib = _sibilanceBand.Process(filtered);
                float sibEnv = _sibilanceEnv.Process(sib);
                sibilanceSum += Math.Clamp(sibEnv / total, 0f, 1f);
            }
        }

        if (needSpeechPresence)
        {
            WriteLastValue(AnalysisSignalId.SpeechPresence, speechSum / Math.Max(1, count));
        }
        if (needFricative)
        {
            WriteLastValue(AnalysisSignalId.FricativeActivity, fricativeSum / Math.Max(1, count));
        }
        if (needSibilance)
        {
            WriteLastValue(AnalysisSignalId.SibilanceEnergy, sibilanceSum / Math.Max(1, count));
        }
    }

    public void ProcessFrame(ReadOnlySpan<float> rawFrame, ReadOnlySpan<float> processedFrame, ReadOnlySpan<float> magnitudes, AnalysisSignalMask requestedSignals)
    {
        if (rawFrame.IsEmpty || magnitudes.IsEmpty)
        {
            return;
        }

        AnalysisSignalMask computeSignals = AnalysisSignalDependencies.Expand(requestedSignals);
        ProcessFrameCore(rawFrame, processedFrame, magnitudes, computeSignals, profilingEnabled: false);
    }

    private void ProcessFrameCore(ReadOnlySpan<float> rawFrame, ReadOnlySpan<float> processedFrame,
        ReadOnlySpan<float> magnitudes, AnalysisSignalMask computeSignals, bool profilingEnabled)
    {
        bool needsPitch = (computeSignals & (AnalysisSignalMask.PitchHz | AnalysisSignalMask.PitchConfidence)) != 0;
        bool needsVoicing = (computeSignals & (AnalysisSignalMask.VoicingScore | AnalysisSignalMask.VoicingState)) != 0;
        bool needsSpectralFlux = (computeSignals & AnalysisSignalMask.SpectralFlux) != 0;
        bool needsOnsetFlux = (computeSignals & AnalysisSignalMask.OnsetFluxHigh) != 0;
        bool needsHnr = (computeSignals & AnalysisSignalMask.HnrDb) != 0;

        if (needsVoicing)
        {
            needsPitch = true;
        }
        else if (!needsPitch)
        {
            _pitchFrameIndex = 0;
        }

        float pitch = 0f;
        float pitchConfidence = 0f;

        long pitchTicks = 0;
        bool pitchComputed = false;
        if (needsPitch)
        {
            bool shouldComputePitch = (_pitchFrameIndex++ % PitchFrameStride) == 0;
            if (shouldComputePitch)
            {
                long pitchStart = 0;
                if (profilingEnabled)
                {
                    pitchStart = Stopwatch.GetTimestamp();
                }

                bool gateOpen = ShouldRunPitch();
                if (gateOpen)
                {
                    DetectPitch(GetPitchAlgorithm(), rawFrame, magnitudes, ref pitch, ref pitchConfidence);
                }
                else
                {
                    pitch = 0f;
                    pitchConfidence = 0f;
                    Volatile.Write(ref _lastCpp, 0f);
                }
                WriteLastValue(AnalysisSignalId.PitchHz, pitch);
                WriteLastValue(AnalysisSignalId.PitchConfidence, pitchConfidence);
                pitchComputed = true;

                if (profilingEnabled)
                {
                    pitchTicks = Stopwatch.GetTimestamp() - pitchStart;
                }
            }
            else
            {
                pitch = GetLastValue(AnalysisSignalId.PitchHz);
                pitchConfidence = GetLastValue(AnalysisSignalId.PitchConfidence);
            }
        }

        VoicingState voicing = VoicingState.Silence;
        float voicingScore = 0f;

        long voicingTicks = 0;
        if (needsVoicing)
        {
            bool gateOpen = ShouldRunPitch();
            if (gateOpen)
            {
                long voicingStart = 0;
                if (profilingEnabled)
                {
                    voicingStart = Stopwatch.GetTimestamp();
                }

                float cpp = Volatile.Read(ref _lastCpp);
                var voicingResult = _voicingDetector.Process(rawFrame, magnitudes, pitchConfidence, cpp);
                voicing = voicingResult.State;
                voicingScore = voicing == VoicingState.Silence ? 0f : voicingResult.Score;
                WriteLastValue(AnalysisSignalId.VoicingState, (float)voicing);
                WriteLastValue(AnalysisSignalId.VoicingScore, voicingScore);

                if (profilingEnabled)
                {
                    voicingTicks = Stopwatch.GetTimestamp() - voicingStart;
                }
            }
            else
            {
                voicing = VoicingState.Silence;
                voicingScore = 0f;
                WriteLastValue(AnalysisSignalId.VoicingState, (float)voicing);
                WriteLastValue(AnalysisSignalId.VoicingScore, voicingScore);
            }
        }

        long featureTicks = 0;
        if (needsSpectralFlux || needsOnsetFlux || needsHnr)
        {
            long featureStart = 0;
            if (profilingEnabled)
            {
                featureStart = Stopwatch.GetTimestamp();
            }

            if (needsSpectralFlux)
            {
                _featureExtractor.Compute(magnitudes, Math.Min(_analysisBins, magnitudes.Length),
                    out float centroid, out float slope, out float flux);
                WriteLastValue(AnalysisSignalId.SpectralFlux, flux);
                Volatile.Write(ref _lastCentroid, centroid);
                Volatile.Write(ref _lastSlope, slope);
            }

            if (needsOnsetFlux)
            {
                float fluxHigh = ComputeHighBandFlux(magnitudes);
                WriteLastValue(AnalysisSignalId.OnsetFluxHigh, fluxHigh);
            }

            if (needsHnr)
            {
                float flatness = ComputeSpectralFlatness(magnitudes);
                // HNR approximation: -10 * log10(flatness), flatness in [0,1].
                float hnr = -10f * MathF.Log10(MathF.Max(flatness, 1e-6f));
                WriteLastValue(AnalysisSignalId.HnrDb, hnr);
            }

            if (profilingEnabled)
            {
                featureTicks = Stopwatch.GetTimestamp() - featureStart;
            }
        }

        if (profilingEnabled)
        {
            if (pitchComputed)
            {
                RecordProfiling(ref _lastPitchTicks, ref _maxPitchTicks, pitchTicks);
            }
            RecordProfiling(ref _lastVoicingTicks, ref _maxVoicingTicks, voicingTicks);
            RecordProfiling(ref _lastFeatureTicks, ref _maxFeatureTicks, featureTicks);
        }
    }

    private void WriteSignals(in AnalysisSignalWriter writer, AnalysisSignalMask requestedSignals, long sampleTime, int count)
    {
        if (!writer.IsEnabled)
        {
            return;
        }

        if ((requestedSignals & AnalysisSignalMask.SpeechPresence) != 0)
        {
            writer.WriteBlock(AnalysisSignalId.SpeechPresence, sampleTime, _speechBuffer.AsSpan(0, count));
        }
        if ((requestedSignals & AnalysisSignalMask.VoicingScore) != 0)
        {
            writer.WriteBlock(AnalysisSignalId.VoicingScore, sampleTime, _voicingScoreBuffer.AsSpan(0, count));
        }
        if ((requestedSignals & AnalysisSignalMask.VoicingState) != 0)
        {
            writer.WriteBlock(AnalysisSignalId.VoicingState, sampleTime, _voicingStateBuffer.AsSpan(0, count));
        }
        if ((requestedSignals & AnalysisSignalMask.FricativeActivity) != 0)
        {
            writer.WriteBlock(AnalysisSignalId.FricativeActivity, sampleTime, _fricativeBuffer.AsSpan(0, count));
        }
        if ((requestedSignals & AnalysisSignalMask.SibilanceEnergy) != 0)
        {
            writer.WriteBlock(AnalysisSignalId.SibilanceEnergy, sampleTime, _sibilanceBuffer.AsSpan(0, count));
        }
        if ((requestedSignals & AnalysisSignalMask.OnsetFluxHigh) != 0)
        {
            writer.WriteBlock(AnalysisSignalId.OnsetFluxHigh, sampleTime, _onsetFluxBuffer.AsSpan(0, count));
        }
        if ((requestedSignals & AnalysisSignalMask.PitchHz) != 0)
        {
            writer.WriteBlock(AnalysisSignalId.PitchHz, sampleTime, _pitchBuffer.AsSpan(0, count));
        }
        if ((requestedSignals & AnalysisSignalMask.PitchConfidence) != 0)
        {
            writer.WriteBlock(AnalysisSignalId.PitchConfidence, sampleTime, _pitchConfidenceBuffer.AsSpan(0, count));
        }
        if ((requestedSignals & AnalysisSignalMask.SpectralFlux) != 0)
        {
            writer.WriteBlock(AnalysisSignalId.SpectralFlux, sampleTime, _spectralFluxBuffer.AsSpan(0, count));
        }
        if ((requestedSignals & AnalysisSignalMask.HnrDb) != 0)
        {
            writer.WriteBlock(AnalysisSignalId.HnrDb, sampleTime, _hnrDbBuffer.AsSpan(0, count));
        }
    }

    private void FillFrameBuffers(int count, AnalysisSignalMask requestedSignals)
    {
        if ((requestedSignals & AnalysisSignalMask.VoicingScore) != 0)
        {
            _voicingScoreBuffer.AsSpan(0, count).Fill(GetLastValue(AnalysisSignalId.VoicingScore));
        }
        if ((requestedSignals & AnalysisSignalMask.VoicingState) != 0)
        {
            _voicingStateBuffer.AsSpan(0, count).Fill(GetLastValue(AnalysisSignalId.VoicingState));
        }
        if ((requestedSignals & AnalysisSignalMask.OnsetFluxHigh) != 0)
        {
            _onsetFluxBuffer.AsSpan(0, count).Fill(GetLastValue(AnalysisSignalId.OnsetFluxHigh));
        }
        if ((requestedSignals & AnalysisSignalMask.PitchHz) != 0)
        {
            _pitchBuffer.AsSpan(0, count).Fill(GetLastValue(AnalysisSignalId.PitchHz));
        }
        if ((requestedSignals & AnalysisSignalMask.PitchConfidence) != 0)
        {
            _pitchConfidenceBuffer.AsSpan(0, count).Fill(GetLastValue(AnalysisSignalId.PitchConfidence));
        }
        if ((requestedSignals & AnalysisSignalMask.SpectralFlux) != 0)
        {
            _spectralFluxBuffer.AsSpan(0, count).Fill(GetLastValue(AnalysisSignalId.SpectralFlux));
        }
        if ((requestedSignals & AnalysisSignalMask.HnrDb) != 0)
        {
            _hnrDbBuffer.AsSpan(0, count).Fill(GetLastValue(AnalysisSignalId.HnrDb));
        }
    }

    private void ResetProfiling()
    {
        Interlocked.Exchange(ref _lastTotalTicks, 0);
        Interlocked.Exchange(ref _maxTotalTicks, 0);
        Interlocked.Exchange(ref _lastPreprocessTicks, 0);
        Interlocked.Exchange(ref _maxPreprocessTicks, 0);
        Interlocked.Exchange(ref _lastFftTicks, 0);
        Interlocked.Exchange(ref _maxFftTicks, 0);
        Interlocked.Exchange(ref _lastPitchTicks, 0);
        Interlocked.Exchange(ref _maxPitchTicks, 0);
        Interlocked.Exchange(ref _lastVoicingTicks, 0);
        Interlocked.Exchange(ref _maxVoicingTicks, 0);
        Interlocked.Exchange(ref _lastFeatureTicks, 0);
        Interlocked.Exchange(ref _maxFeatureTicks, 0);
        Interlocked.Exchange(ref _lastWriteTicks, 0);
        Interlocked.Exchange(ref _maxWriteTicks, 0);
    }

    private static void RecordProfiling(ref long lastTicks, ref long maxTicks, long elapsedTicks)
    {
        Interlocked.Exchange(ref lastTicks, elapsedTicks);
        if (elapsedTicks <= 0)
        {
            return;
        }

        UpdateMax(ref maxTicks, elapsedTicks);
    }

    private static void UpdateMax(ref long location, long value)
    {
        long current = Interlocked.Read(ref location);
        while (value > current)
        {
            long prior = Interlocked.CompareExchange(ref location, value, current);
            if (prior == current)
            {
                break;
            }

            current = prior;
        }
    }

    private void EnsureFftMagnitudes()
    {
        int size = _analysisSize;
        if (_fftReal.Length != size || _fftImag.Length != size)
        {
            return;
        }

        for (int i = 0; i < size; i++)
        {
            _fftReal[i] = _analysisBufferProcessed[i] * _analysisWindow[i];
            _fftImag[i] = 0f;
        }

        _fft?.Forward(_fftReal, _fftImag);

        int bins = _analysisBins;
        for (int i = 0; i < bins; i++)
        {
            float re = _fftReal[i];
            float im = _fftImag[i];
            _fftMagnitudes[i] = MathF.Sqrt(re * re + im * im);
        }
    }

    private float ComputeHighBandFlux(ReadOnlySpan<float> magnitudes)
    {
        int bins = Math.Min(magnitudes.Length, _highFluxPrevious.Length);
        if (bins <= 0)
        {
            return 0f;
        }

        int start = (int)MathF.Floor(2000f / MathF.Max(_binResolution, 1e-6f));
        start = Math.Clamp(start, 0, bins - 1);

        if (!_highFluxInitialized)
        {
            for (int i = start; i < bins; i++)
            {
                _highFluxPrevious[i] = DspUtils.LinearToDb(magnitudes[i]);
            }
            _highFluxInitialized = true;
            return 0f;
        }

        double sum = 0.0;
        int count = 0;
        for (int i = start; i < bins; i++)
        {
            float currentDb = DspUtils.LinearToDb(magnitudes[i]);
            float diff = currentDb - _highFluxPrevious[i];
            if (diff > 0f)
            {
                sum += diff;
            }
            _highFluxPrevious[i] = currentDb;
            count++;
        }

        if (count <= 0)
        {
            return 0f;
        }

        return (float)(sum / count);
    }

    private void DetectPitch(PitchDetectorType algorithm, ReadOnlySpan<float> rawFrame, ReadOnlySpan<float> magnitudes,
        ref float pitch, ref float confidence)
    {
        Volatile.Write(ref _lastCpp, 0f);
        switch (algorithm)
        {
            case PitchDetectorType.Yin when _yinPitchDetector is not null:
                var yin = _yinPitchDetector.Detect(rawFrame);
                pitch = yin.FrequencyHz ?? 0f;
                confidence = yin.Confidence;
                break;

            case PitchDetectorType.Pyin when _pyinPitchDetector is not null:
                var pyin = _pyinPitchDetector.Detect(rawFrame);
                pitch = pyin.FrequencyHz ?? 0f;
                confidence = pyin.Confidence;
                break;

            case PitchDetectorType.Autocorrelation when _autocorrPitchDetector is not null:
                var auto = _autocorrPitchDetector.Detect(rawFrame);
                pitch = auto.FrequencyHz ?? 0f;
                confidence = auto.Confidence;
                break;

            case PitchDetectorType.Cepstral when _cepstralPitchDetector is not null:
                var cep = _cepstralPitchDetector.Detect(rawFrame);
                pitch = cep.FrequencyHz ?? 0f;
                confidence = cep.Confidence;
                Volatile.Write(ref _lastCpp, _cepstralPitchDetector.LastCpp);
                break;

            case PitchDetectorType.Swipe when _swipePitchDetector is not null:
                var swipe = _swipePitchDetector.Detect(magnitudes);
                pitch = swipe.FrequencyHz ?? 0f;
                confidence = swipe.Confidence;
                break;
        }
    }

    private void ConfigureFilters(AnalysisSignalProcessorSettings settings)
    {
        _dcHighPass.Configure(DcCutoffHz, _sampleRate);
        _dcHighPass.Reset();
        _rumbleHighPass.SetHighPass(_sampleRate, settings.HighPassCutoff, 0.707f);
        _rumbleHighPass.Reset();
        _preEmphasisFilter.Configure(DefaultPreEmphasis);
        _preEmphasisFilter.Reset();

        _speechEnv.Configure(5f, 80f, _sampleRate);
        _fricativeEnv.Configure(2f, 60f, _sampleRate);
        _sibilanceEnv.Configure(2f, 60f, _sampleRate);

        _fricativeHighPass.SetHighPass(_sampleRate, FricativeHighHz, 0.707f);
        _fricativeHighPass.Reset();
        _sibilanceBand.SetBandPass(_sampleRate, SibilanceCenterHz, SibilanceQ);
        _sibilanceBand.Reset();
    }

    private void ConfigurePitchDetectors(float minFrequency, float maxFrequency)
    {
        float maxPitch = MathF.Min(_sampleRate * 0.45f, MathF.Max(minFrequency + 1f, maxFrequency));
        float minPitch = MathF.Max(20f, MathF.Min(minFrequency, maxPitch - 1f));

        _yinPitchDetector ??= new YinPitchDetector(_sampleRate, _analysisSize, minPitch, maxPitch, 0.15f);
        _yinPitchDetector.Configure(_sampleRate, _analysisSize, minPitch, maxPitch, 0.15f);

        _pyinPitchDetector ??= new PyinPitchDetector(_sampleRate, _analysisSize, minPitch, maxPitch, 0.15f);
        _pyinPitchDetector.Configure(_sampleRate, _analysisSize, minPitch, maxPitch, 0.15f);

        _autocorrPitchDetector ??= new AutocorrelationPitchDetector(_sampleRate, _analysisSize, minPitch, maxPitch, 0.3f);
        _autocorrPitchDetector.Configure(_sampleRate, _analysisSize, minPitch, maxPitch, 0.3f);

        _cepstralPitchDetector ??= new CepstralPitchDetector(_sampleRate, _analysisSize, minPitch, maxPitch, 2f);
        _cepstralPitchDetector.Configure(_sampleRate, _analysisSize, minPitch, maxPitch, 2f);

        _swipePitchDetector ??= new SwipePitchDetector(_sampleRate, _analysisSize, minFrequency, maxFrequency);
        _swipePitchDetector.Configure(_sampleRate, _analysisSize, minFrequency, maxFrequency);

        SetPitchProfilingEnabled(Volatile.Read(ref _profilingEnabled) != 0);
    }

    private void SetPitchProfilingEnabled(bool enabled)
    {
        _yinPitchDetector?.SetProfilingEnabled(enabled);
        _pyinPitchDetector?.SetProfilingEnabled(enabled);
        _autocorrPitchDetector?.SetProfilingEnabled(enabled);
        _cepstralPitchDetector?.SetProfilingEnabled(enabled);
        _swipePitchDetector?.SetProfilingEnabled(enabled);
    }

    private PitchDetectorType GetPitchAlgorithm()
    {
        int raw = Volatile.Read(ref _pitchAlgorithmRaw);
        if ((uint)raw > (uint)PitchDetectorType.Swipe)
        {
            return PitchDetectorType.Yin;
        }

        return (PitchDetectorType)raw;
    }

    private bool ShouldRunPitch()
    {
        if (Volatile.Read(ref _externalSpeechPresenceGateEnabled) != 0)
        {
            float presence = Volatile.Read(ref _externalSpeechPresenceGate);
            if (!float.IsFinite(presence))
            {
                return false;
            }

            return presence > SpeechPresenceGateThreshold;
        }

        if (Volatile.Read(ref _generatedSpeechPresenceGateEnabled) != 0)
        {
            float presence = GetLastValue(AnalysisSignalId.SpeechPresence);
            if (!float.IsFinite(presence))
            {
                return false;
            }

            return presence > SpeechPresenceGateThreshold;
        }

        return true;
    }

    private void GetPitchPeriodRange(out int minPeriod, out int maxPeriod)
    {
        if (_analysisSize <= 0 || _sampleRate <= 0)
        {
            minPeriod = 0;
            maxPeriod = 0;
            return;
        }

        float maxFreq = Math.Clamp(_settings.MaxFrequency, 40f, _sampleRate * 0.45f);
        float minFreq = Math.Clamp(_settings.MinFrequency, 20f, maxFreq - 1f);
        minPeriod = Math.Max(2, (int)(_sampleRate / maxFreq));
        maxPeriod = Math.Min(_analysisSize - 2, (int)(_sampleRate / minFreq));
        if (maxPeriod < minPeriod)
        {
            maxPeriod = minPeriod;
        }
    }

    private static void EnsureBuffer(ref float[] buffer, int length)
    {
        if (buffer.Length != length)
        {
            buffer = new float[length];
        }
    }

    private void EnsureSignalBuffers(int length)
    {
        EnsureBuffer(ref _speechBuffer, length);
        EnsureBuffer(ref _voicingScoreBuffer, length);
        EnsureBuffer(ref _voicingStateBuffer, length);
        EnsureBuffer(ref _fricativeBuffer, length);
        EnsureBuffer(ref _sibilanceBuffer, length);
        EnsureBuffer(ref _onsetFluxBuffer, length);
        EnsureBuffer(ref _pitchBuffer, length);
        EnsureBuffer(ref _pitchConfidenceBuffer, length);
        EnsureBuffer(ref _spectralFluxBuffer, length);
        EnsureBuffer(ref _hnrDbBuffer, length);
    }

    private void WriteLastValue(AnalysisSignalId signal, float value)
    {
        int index = (int)signal;
        if ((uint)index >= (uint)_lastValues.Length)
        {
            return;
        }

        value = SanitizeSignalValue(signal, value);
        Volatile.Write(ref _lastValues[index], value);
    }

    private static bool NeedsFrameSignals(AnalysisSignalMask requestedSignals)
    {
        const AnalysisSignalMask frameSignals =
            AnalysisSignalMask.VoicingScore |
            AnalysisSignalMask.VoicingState |
            AnalysisSignalMask.OnsetFluxHigh |
            AnalysisSignalMask.PitchHz |
            AnalysisSignalMask.PitchConfidence |
            AnalysisSignalMask.SpectralFlux |
            AnalysisSignalMask.HnrDb;

        return (requestedSignals & frameSignals) != 0;
    }

    private static float SanitizeSignalValue(AnalysisSignalId signal, float value)
    {
        if (!float.IsFinite(value))
        {
            return 0f;
        }

        return signal switch
        {
            AnalysisSignalId.SpeechPresence => Clamp01(value),
            AnalysisSignalId.VoicingScore => Clamp01(value),
            AnalysisSignalId.VoicingState => Math.Clamp(value, 0f, 2f),
            AnalysisSignalId.FricativeActivity => Clamp01(value),
            AnalysisSignalId.SibilanceEnergy => Clamp01(value),
            AnalysisSignalId.OnsetFluxHigh => MathF.Max(0f, value),
            AnalysisSignalId.PitchHz => MathF.Max(0f, value),
            AnalysisSignalId.PitchConfidence => Clamp01(value),
            AnalysisSignalId.SpectralFlux => MathF.Max(0f, value),
            AnalysisSignalId.HnrDb => Math.Clamp(value, -120f, 120f),
            _ => value
        };
    }

    private static float Clamp01(float value)
    {
        if (value <= 0f)
        {
            return 0f;
        }

        if (value >= 1f)
        {
            return 1f;
        }

        return value;
    }

    private static int NextPowerOfTwo(int value)
    {
        int power = 1;
        while (power < value)
        {
            power <<= 1;
        }
        return power;
    }

    private static float ComputeSpectralFlatness(ReadOnlySpan<float> magnitudes)
    {
        if (magnitudes.IsEmpty)
        {
            return 1f;
        }

        float logSum = 0f;
        float linSum = 0f;
        int count = magnitudes.Length;
        for (int i = 0; i < count; i++)
        {
            float mag = MathF.Max(magnitudes[i], 1e-12f);
            logSum += MathF.Log(mag);
            linSum += mag;
        }

        float geometric = MathF.Exp(logSum / count);
        float arithmetic = linSum / count;
        return arithmetic > 1e-12f ? geometric / arithmetic : 1f;
    }
}
