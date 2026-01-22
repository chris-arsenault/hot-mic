using System.Threading;
using HotMic.Core.Dsp;
using HotMic.Core.Dsp.Analysis;
using HotMic.Core.Dsp.Analysis.Pitch;
using HotMic.Core.Dsp.Analysis.Speech;
using HotMic.Core.Dsp.Fft;
using HotMic.Core.Dsp.Filters;
using HotMic.Core.Dsp.Mapping;
using HotMic.Core.Dsp.Spectrogram;
using HotMic.Core.Dsp.Voice;
using HotMic.Core.Plugins;
using HotMic.Core.Threading;

namespace HotMic.Core.Analysis;

/// <summary>
/// Orchestrates audio analysis for visualizers. Runs analysis based on active consumers.
/// Single instance per audio session. Not a plugin.
/// </summary>
public sealed class AnalysisOrchestrator : IDisposable
{
    private const int CaptureBufferSize = 262144;
    private const float DcCutoffHz = 10f;
    private const float DefaultPreEmphasis = 0.97f;
    private const int ZoomFftZoomFactor = 8;
    private const float MaxReassignBinShift = 0.5f;
    private const float MaxReassignFrameShift = 0.5f;

    private readonly AnalysisResultStore _resultStore = new();
    private readonly VisualizerSyncHub _syncHub = new();
    private readonly LockFreeRingBuffer _captureBuffer = new(CaptureBufferSize);
    private readonly object _consumerLock = new();
    private readonly List<AnalysisConsumer> _consumers = new();
    private readonly AnalysisConfiguration _config = new();

    private Thread? _analysisThread;
    private CancellationTokenSource? _analysisCts;
    private int _sampleRate;
    private int _consumerCount;
    private AnalysisCaptureLink? _captureLink;
    private int _requestedSignalsRaw;
    private long _analysisReadSampleTime = long.MinValue;

    // FFT/Transform
    private FastFft? _fft;
    private ConstantQTransform? _cqt;
    private ZoomFft? _zoomFft;
    private float[] _analysisBufferRaw = Array.Empty<float>();
    private float[] _analysisBufferProcessed = Array.Empty<float>();
    private float[] _hopBuffer = Array.Empty<float>();
    private readonly AnalysisSignalProcessor _analysisSignalProcessor = new();
    private readonly float[] _analysisSignalValues = new float[(int)AnalysisSignalId.Count];
    private AnalysisSignalProcessorSettings _activeSignalSettings;
    private int _activeSignalHopSize;
    private bool _hasActiveSignalSettings;
    private float[] _fftReal = Array.Empty<float>();
    private float[] _fftImag = Array.Empty<float>();
    private float[] _fftWindow = Array.Empty<float>();
    private float[] _fftWindowTime = Array.Empty<float>();
    private float[] _fftWindowDerivative = Array.Empty<float>();
    private float[] _fftTimeReal = Array.Empty<float>();
    private float[] _fftTimeImag = Array.Empty<float>();
    private float[] _fftDerivReal = Array.Empty<float>();
    private float[] _fftDerivImag = Array.Empty<float>();
    private float[] _fftMagnitudes = Array.Empty<float>();
    private float[] _fftDisplayMagnitudes = Array.Empty<float>();
    private float[] _aWeighting = Array.Empty<float>();
    private float[] _zoomReal = Array.Empty<float>();
    private float[] _zoomImag = Array.Empty<float>();
    private float[] _zoomTimeReal = Array.Empty<float>();
    private float[] _zoomTimeImag = Array.Empty<float>();
    private float[] _zoomDerivReal = Array.Empty<float>();
    private float[] _zoomDerivImag = Array.Empty<float>();
    private float[] _cqtMagnitudes = Array.Empty<float>();
    private float[] _cqtReal = Array.Empty<float>();
    private float[] _cqtImag = Array.Empty<float>();
    private float[] _cqtTimeReal = Array.Empty<float>();
    private float[] _cqtTimeImag = Array.Empty<float>();
    private float[] _cqtPhaseDiff = Array.Empty<float>();

    // Clarity processing
    private float[] _spectrumScratch = Array.Empty<float>();
    private float[] _displayWork = Array.Empty<float>();
    private float[] _displayProcessed = Array.Empty<float>();
    private float[] _displaySmoothed = Array.Empty<float>();
    private float[] _displayGain = Array.Empty<float>();
    private readonly SpectrogramDisplayMapper _displayMapper = new();
    private float[] _displayMapScratch = Array.Empty<float>();
    private SpectrogramAnalysisDescriptor? _displayMapperDescriptor;
    private FrequencyScale _displayMapperScale;
    private float _displayMapperMinHz;
    private float _displayMapperMaxHz;
    private int _displayMapperBins;
    private readonly SpectrogramNoiseReducer _noiseReducer = new();
    private readonly SpectrogramHpssProcessor _hpssProcessor = new();
    private readonly SpectrogramSmoother _smoother = new();
    private readonly SpectrogramHarmonicComb _harmonicComb = new();

    // Filters (not readonly - these are mutable structs)
    private OnePoleHighPass _dcHighPass;
    private BiquadFilter _rumbleHighPass = new();
    private PreEmphasisFilter _preEmphasisFilter;
    private readonly float[] _harmonicScratch = new float[AnalysisConfiguration.MaxHarmonics];
    private readonly float[] _harmonicMagScratch = new float[AnalysisConfiguration.MaxHarmonics];

    // Speech coach
    private readonly SpeechCoach _speechCoach = new();
    private const float BandSmoothingAlpha = 0.3f;
    private float _bandLowSmooth;
    private float _bandMidSmooth;
    private float _bandPresenceSmooth;
    private float _bandHighSmooth;
    private bool _bandSmoothInitialized;

    // Active configuration
    private int _activeFftSize;
    private int _activeOverlapIndex;
    private int _activeHopSize;
    private int _activeFrameCapacity;
    private int _activeAnalysisSize;
    private int _activeAnalysisBins;
    private int _activeDisplayBins;
    private float _activeTimeWindow;
    private float _activeMinFrequency;
    private float _activeMaxFrequency;
    private WindowFunction _activeWindow;
    private FrequencyScale _activeScale;
    private SpectrogramTransformType _activeTransformType;
    private SpectrogramReassignMode _activeReassignMode;
    private int _activeCqtBinsPerOctave;
    private bool _activeHighPassEnabled;
    private float _activeHighPassCutoff;
    private bool _activePreEmphasisEnabled;
    private float _fftNormalization;
    private float _binResolution;
    private int _analysisFilled;
    private int _reassignLatencyFrames;
    private SpectrogramAnalysisDescriptor? _analysisDescriptor;
    private long _frameCounter;
    private long _lastDroppedHops;

    // Debug counters
    private long _debugEnqueueCalls;
    private long _debugEnqueueSkippedChannel;
    private long _debugEnqueueSkippedEmpty;
    private long _debugEnqueueWritten;
    private long _debugEnqueueSamplesWritten;
    private long _debugLoopIterations;
    private long _debugLoopNoConsumers;
    private long _debugLoopNotEnoughData;
    private long _debugLoopFramesProcessed;
    private long _debugLoopFramesWritten;
    private float _debugLastHopMax;
    private float _debugLastFftMax;
    private float _debugLastDisplayMax;
    private float _debugLastAnalysisBufMax;
    private float _debugLastWindowMax;
    private float _debugLastFftRealMax;
    private bool _debugFftNull;
    private int _debugTransformPath; // 0=FFT, 1=CQT, 2=ZoomFFT
    private float _debugLastProcessedMax;
    private int _debugAnalysisFilled;

    public long DebugEnqueueCalls => Interlocked.Read(ref _debugEnqueueCalls);
    public long DebugEnqueueSkippedChannel => Interlocked.Read(ref _debugEnqueueSkippedChannel);
    public long DebugEnqueueSkippedEmpty => Interlocked.Read(ref _debugEnqueueSkippedEmpty);
    public long DebugEnqueueWritten => Interlocked.Read(ref _debugEnqueueWritten);
    public long DebugEnqueueSamplesWritten => Interlocked.Read(ref _debugEnqueueSamplesWritten);
    public long DebugLoopIterations => Interlocked.Read(ref _debugLoopIterations);
    public long DebugLoopNoConsumers => Interlocked.Read(ref _debugLoopNoConsumers);
    public long DebugLoopNotEnoughData => Interlocked.Read(ref _debugLoopNotEnoughData);
    public long DebugLoopFramesProcessed => Interlocked.Read(ref _debugLoopFramesProcessed);
    public long DebugLoopFramesWritten => Interlocked.Read(ref _debugLoopFramesWritten);
    public int DebugCaptureBufferAvailable => _captureBuffer.AvailableRead;
    public int DebugActiveHopSize => Volatile.Read(ref _activeHopSize);
    public int DebugActiveFrameCapacity => Volatile.Read(ref _activeFrameCapacity);
    public int DebugActiveDisplayBins => Volatile.Read(ref _activeDisplayBins);
    public int DebugActiveAnalysisBins => Volatile.Read(ref _activeAnalysisBins);
    public int DebugConsumerCount => Volatile.Read(ref _consumerCount);
    public float DebugLastHopMax => Volatile.Read(ref _debugLastHopMax);
    public float DebugLastFftMax => Volatile.Read(ref _debugLastFftMax);
    public float DebugLastDisplayMax => Volatile.Read(ref _debugLastDisplayMax);
    public float DebugLastAnalysisBufMax => Volatile.Read(ref _debugLastAnalysisBufMax);
    public float DebugLastWindowMax => Volatile.Read(ref _debugLastWindowMax);
    public float DebugLastFftRealMax => Volatile.Read(ref _debugLastFftRealMax);
    public bool DebugFftNull => Volatile.Read(ref _debugFftNull);
    public int DebugTransformPath => Volatile.Read(ref _debugTransformPath);
    public float DebugLastProcessedMax => Volatile.Read(ref _debugLastProcessedMax);
    public int DebugAnalysisFilled => Volatile.Read(ref _debugAnalysisFilled);

    public IAnalysisResultStore Results => _resultStore;
    public VisualizerSyncHub SyncHub => _syncHub;
    public AnalysisConfiguration Config => _config;
    public int SampleRate => Volatile.Read(ref _sampleRate);
    public int ReassignFrameLookback => _activeReassignMode.HasFlag(SpectrogramReassignMode.Time)
        ? _reassignLatencyFrames + (int)MathF.Ceiling(MaxReassignFrameShift) + 1
        : 0;
    public bool HasActiveConsumers => Volatile.Read(ref _consumerCount) > 0;

    public AnalysisCaptureLink? CaptureLink
    {
        get => Volatile.Read(ref _captureLink);
        set => Volatile.Write(ref _captureLink, value);
    }

    public AnalysisSignalMask RequestedSignals => (AnalysisSignalMask)Volatile.Read(ref _requestedSignalsRaw);

    public event Action<AnalysisSignalMask>? RequestedSignalsChanged;

    public void Initialize(int sampleRate)
    {
        _sampleRate = sampleRate;
        ConfigureAnalysis(force: true);
    }

    public void EnqueueAudio(ReadOnlySpan<float> buffer, int channelIndex)
    {
        Interlocked.Increment(ref _debugEnqueueCalls);

        // For now, only capture channel 0
        if (channelIndex != 0)
        {
            Interlocked.Increment(ref _debugEnqueueSkippedChannel);
            return;
        }

        if (buffer.IsEmpty)
        {
            Interlocked.Increment(ref _debugEnqueueSkippedEmpty);
            return;
        }

        Interlocked.Increment(ref _debugEnqueueWritten);
        Interlocked.Add(ref _debugEnqueueSamplesWritten, buffer.Length);
        _captureBuffer.Write(buffer);
    }

    public IDisposable Subscribe(AnalysisCapabilities required)
    {
        var consumer = new AnalysisConsumer(required, this);
        AnalysisSignalMask requestedSignals;
        lock (_consumerLock)
        {
            _consumers.Add(consumer);
            Interlocked.Increment(ref _consumerCount);

            // Start analysis thread if this is the first consumer
            if (_consumers.Count == 1)
            {
                StartAnalysisThread();
            }

            requestedSignals = ComputeRequestedSignalsLocked();
        }

        UpdateRequestedSignals(requestedSignals);
        return consumer;
    }

    private void Unsubscribe(AnalysisConsumer consumer)
    {
        AnalysisSignalMask requestedSignals;
        lock (_consumerLock)
        {
            _consumers.Remove(consumer);
            Interlocked.Decrement(ref _consumerCount);

            // Stop analysis thread if no consumers left
            if (_consumers.Count == 0)
            {
                StopAnalysisThread();
            }

            requestedSignals = ComputeRequestedSignalsLocked();
        }

        UpdateRequestedSignals(requestedSignals);
    }

    private AnalysisCapabilities GetActiveCapabilities()
    {
        var caps = AnalysisCapabilities.None;
        lock (_consumerLock)
        {
            foreach (var consumer in _consumers)
                caps |= consumer.RequiredCapabilities;
        }
        return caps;
    }

    private static AnalysisSignalMask ComputeRequestedSignals(AnalysisCapabilities caps)
    {
        AnalysisSignalMask mask = AnalysisSignalMask.None;

        bool needsPitch = caps.HasFlag(AnalysisCapabilities.Pitch) ||
                          caps.HasFlag(AnalysisCapabilities.Harmonics) ||
                          caps.HasFlag(AnalysisCapabilities.SpeechMetrics);
        if (needsPitch)
        {
            mask |= AnalysisSignalMask.PitchHz | AnalysisSignalMask.PitchConfidence;
        }

        bool needsVoicing = caps.HasFlag(AnalysisCapabilities.VoicingState) ||
                            caps.HasFlag(AnalysisCapabilities.SpeechMetrics);
        if (needsVoicing)
        {
            mask |= AnalysisSignalMask.VoicingState | AnalysisSignalMask.VoicingScore;
        }

        if (caps.HasFlag(AnalysisCapabilities.SpectralFeatures) || caps.HasFlag(AnalysisCapabilities.SpeechMetrics))
        {
            mask |= AnalysisSignalMask.SpectralFlux | AnalysisSignalMask.HnrDb;
        }

        return AnalysisSignalDependencies.Expand(mask);
    }

    private void UpdateRequestedSignals(AnalysisSignalMask requestedSignals)
    {
        AnalysisSignalMask previous = (AnalysisSignalMask)Volatile.Read(ref _requestedSignalsRaw);
        if (previous == requestedSignals)
        {
            return;
        }

        Volatile.Write(ref _requestedSignalsRaw, (int)requestedSignals);
        RequestedSignalsChanged?.Invoke(requestedSignals);
    }

    private AnalysisSignalMask ComputeRequestedSignalsLocked()
    {
        var caps = AnalysisCapabilities.None;
        foreach (var consumer in _consumers)
        {
            caps |= consumer.RequiredCapabilities;
        }

        return ComputeRequestedSignals(caps);
    }

    public void Reset()
    {
        _captureBuffer.Clear();
        _resultStore.Clear();
        _syncHub.Reset();
        _analysisSignalProcessor.Reset();
        _speechCoach.Reset();
        Array.Clear(_analysisSignalValues, 0, _analysisSignalValues.Length);
        Volatile.Write(ref _analysisFilled, 0);
        Volatile.Write(ref _frameCounter, 0);
        Volatile.Write(ref _lastDroppedHops, 0);
        Volatile.Write(ref _analysisReadSampleTime, long.MinValue);
        ResetSpeechBandSmoothing();
    }

    private void StartAnalysisThread()
    {
        if (_analysisThread is not null)
            return;

        _analysisCts = new CancellationTokenSource();
        _analysisThread = new Thread(() => AnalysisLoop(_analysisCts.Token))
        {
            IsBackground = true,
            Name = "HotMic-Analysis"
        };
        _analysisThread.Start();
    }

    private void StopAnalysisThread()
    {
        if (_analysisThread is null)
            return;

        _analysisCts?.Cancel();
        _analysisThread.Join(500);
        _analysisThread = null;
        _analysisCts?.Dispose();
        _analysisCts = null;
    }

    private void AnalysisLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Interlocked.Increment(ref _debugLoopIterations);

            if (!HasActiveConsumers)
            {
                Interlocked.Increment(ref _debugLoopNoConsumers);
                Thread.Sleep(20);
                continue;
            }

            ConfigureAnalysis(force: false);

            long droppedSamples = _captureBuffer.DroppedSamples;
            long droppedHops = _activeHopSize > 0 ? droppedSamples / _activeHopSize : droppedSamples;
            if (droppedHops != _lastDroppedHops)
            {
                _lastDroppedHops = droppedHops;
                ResetAfterDrop();
                Thread.Sleep(1);
                continue;
            }

            var captureLink = Volatile.Read(ref _captureLink);
            int availableRead = _captureBuffer.AvailableRead;
            if (availableRead < _activeHopSize)
            {
                Interlocked.Increment(ref _debugLoopNotEnoughData);
                Thread.Sleep(1);
                continue;
            }

            long hopSampleTime = GetReadSampleTime(captureLink, availableRead);
            int read = _captureBuffer.Read(_hopBuffer);
            if (read < _activeHopSize)
            {
                Interlocked.Increment(ref _debugLoopNotEnoughData);
                Thread.Sleep(1);
                continue;
            }
            Volatile.Write(ref _analysisReadSampleTime, hopSampleTime + read);

            // Track max hop buffer value for debugging
            float hopMax = 0f;
            for (int i = 0; i < read; i++)
                hopMax = MathF.Max(hopMax, MathF.Abs(_hopBuffer[i]));
            Volatile.Write(ref _debugLastHopMax, hopMax);

            var capabilities = GetActiveCapabilities();
            bool ready = ProcessHopBuffer(out float waveformMin, out float waveformMax);
            if (!ready)
            {
                Interlocked.Increment(ref _debugLoopFramesProcessed);
                continue;
            }

            Interlocked.Increment(ref _debugLoopFramesProcessed);

            // Compute transform
            var transformType = _activeTransformType;
            bool reassignEnabled = _activeReassignMode != SpectrogramReassignMode.Off;
            int clarityBins = _activeAnalysisBins;
            ReadOnlySpan<float> displayMagnitudes = _fftMagnitudes;

            if (transformType == SpectrogramTransformType.Cqt && _cqt is not null)
            {
                Volatile.Write(ref _debugTransformPath, 1);
                clarityBins = ComputeCqtTransform(reassignEnabled);
            }
            else if (transformType == SpectrogramTransformType.ZoomFft && _zoomFft is not null)
            {
                Volatile.Write(ref _debugTransformPath, 2);
                clarityBins = ComputeZoomFftTransform(reassignEnabled);
                displayMagnitudes = _fftDisplayMagnitudes;
            }
            else
            {
                Volatile.Write(ref _debugTransformPath, 0);
                ComputeFftTransform(reassignEnabled);
                displayMagnitudes = NormalizeFftMagnitudes();
            }

            AnalysisSignalMask requestedSignals = ComputeRequestedSignals(capabilities);
            Array.Clear(_analysisSignalValues, 0, _analysisSignalValues.Length);

            AnalysisSignalMask availableSignals = ReadSignalsFromBus(captureLink, hopSampleTime, read, requestedSignals, _analysisSignalValues);
            AnalysisSignalMask missingSignals = requestedSignals & ~availableSignals;

            bool needsSpectralFeatures = capabilities.HasFlag(AnalysisCapabilities.SpectralFeatures) ||
                                         capabilities.HasFlag(AnalysisCapabilities.SpeechMetrics);
            bool needsCpp = (requestedSignals & AnalysisSignalMask.PitchHz) != 0 &&
                            _config.PitchAlgorithm == PitchDetectorType.Cepstral;

            AnalysisSignalMask processorSignals = missingSignals;
            if (needsSpectralFeatures)
            {
                processorSignals |= AnalysisSignalMask.SpectralFlux;
                if ((requestedSignals & AnalysisSignalMask.HnrDb) != 0 &&
                    (availableSignals & AnalysisSignalMask.HnrDb) == 0)
                {
                    processorSignals |= AnalysisSignalMask.HnrDb;
                }
            }

            if (needsCpp)
            {
                processorSignals |= AnalysisSignalMask.PitchHz | AnalysisSignalMask.PitchConfidence;
            }

            if (processorSignals != AnalysisSignalMask.None)
            {
                _analysisSignalProcessor.ProcessBlock(_hopBuffer.AsSpan(0, read), hopSampleTime, default, processorSignals);
                FillSignalValuesFromProcessor(missingSignals, _analysisSignalValues);
            }

            float lastPitch = _analysisSignalValues[(int)AnalysisSignalId.PitchHz];
            float lastConfidence = _analysisSignalValues[(int)AnalysisSignalId.PitchConfidence];
            float lastHnr = _analysisSignalValues[(int)AnalysisSignalId.HnrDb];
            float flux = _analysisSignalValues[(int)AnalysisSignalId.SpectralFlux];
            var voicing = (VoicingState)MathF.Round(_analysisSignalValues[(int)AnalysisSignalId.VoicingState]);

            float centroid = needsSpectralFeatures && (processorSignals & AnalysisSignalMask.SpectralFlux) != 0
                ? _analysisSignalProcessor.LastCentroid
                : 0f;
            float slope = needsSpectralFeatures && (processorSignals & AnalysisSignalMask.SpectralFlux) != 0
                ? _analysisSignalProcessor.LastSlope
                : 0f;
            float lastCpp = needsCpp && (processorSignals & (AnalysisSignalMask.PitchHz | AnalysisSignalMask.PitchConfidence)) != 0
                ? _analysisSignalProcessor.LastCpp
                : 0f;

            int harmonicCount = ComputeHarmonics(capabilities, lastPitch);

            // Clarity processing
            _ = ProcessClarity(clarityBins, voicing, lastPitch, lastConfidence);

            // Write frame to result store
            long frameId = _frameCounter;
            int frameIndex = _resultStore.BeginWriteFrame(frameId);

            // Write linear magnitudes
            _resultStore.WriteLinearMagnitudes(frameIndex, _displaySmoothed.AsSpan(0, clarityBins));

            // Apply reassignment to display buffer if enabled
            if (reassignEnabled)
            {
                ApplyReassignment(frameId, displayMagnitudes, transformType);
            }
            else
            {
                // Map to display bins
                MapToDisplayBins(clarityBins, frameIndex);
            }

            // Write overlay data
            _resultStore.WritePitchFrame(frameIndex, lastPitch, lastConfidence, voicing);
            _resultStore.WriteWaveformFrame(frameIndex, waveformMin, waveformMax);
            _resultStore.WriteSpectralFeatures(frameIndex, centroid, slope, flux, lastHnr, lastCpp);
            _resultStore.WriteAnalysisSignalFrame(frameIndex, _analysisSignalValues);

            if (harmonicCount > 0)
            {
                _resultStore.WriteHarmonicFrame(frameIndex, _harmonicScratch, _harmonicMagScratch, harmonicCount);
            }

            // Speech metrics if needed
            if (capabilities.HasFlag(AnalysisCapabilities.SpeechMetrics))
            {
                var metrics = ProcessSpeechMetrics(waveformMin, waveformMax, lastPitch, lastConfidence,
                    voicing, flux, slope, lastHnr, frameId);
                _resultStore.WriteSpeechMetrics(frameIndex, metrics);
            }

            int displayLatencyFrames = _activeReassignMode.HasFlag(SpectrogramReassignMode.Time)
                ? _reassignLatencyFrames
                : 0;
            _resultStore.EndWriteFrame(frameId, displayLatencyFrames);
            _frameCounter++;
            Interlocked.Increment(ref _debugLoopFramesWritten);

            // Update sync hub
            long displayLatestFrameId = frameId - displayLatencyFrames;
            if (displayLatestFrameId >= 0)
            {
                _syncHub.UpdateViewRange(displayLatestFrameId, _activeFrameCapacity, _sampleRate, _activeHopSize);
            }
        }
    }

    private long GetReadSampleTime(AnalysisCaptureLink? captureLink, int availableRead)
    {
        long readTime = Volatile.Read(ref _analysisReadSampleTime);
        if (readTime != long.MinValue)
        {
            return readTime;
        }

        long baseTime = 0;
        if (captureLink is not null)
        {
            long writeTime = captureLink.WriteSampleTime;
            baseTime = writeTime - availableRead;
            if (baseTime < 0)
            {
                baseTime = 0;
            }
        }

        Volatile.Write(ref _analysisReadSampleTime, baseTime);
        return baseTime;
    }

    private AnalysisSignalMask ReadSignalsFromBus(AnalysisCaptureLink? captureLink, long sampleTime, int count,
        AnalysisSignalMask requestedSignals, float[] values)
    {
        if (captureLink is null || requestedSignals == AnalysisSignalMask.None || sampleTime < 0 || count <= 0)
        {
            return AnalysisSignalMask.None;
        }

        var bus = captureLink.SignalBus;
        var producers = captureLink.SignalProducers;
        if (bus is null || producers.Length < values.Length)
        {
            return AnalysisSignalMask.None;
        }

        int lastIndex = count - 1;
        long lastTime = sampleTime + lastIndex;
        AnalysisSignalMask available = AnalysisSignalMask.None;

        bool wantSpeech = (requestedSignals & AnalysisSignalMask.SpeechPresence) != 0;
        bool wantFricative = (requestedSignals & AnalysisSignalMask.FricativeActivity) != 0;
        bool wantSibilance = (requestedSignals & AnalysisSignalMask.SibilanceEnergy) != 0;

        AnalysisSignalSource speechSource = default;
        AnalysisSignalSource fricativeSource = default;
        AnalysisSignalSource sibilanceSource = default;
        bool hasSpeech = wantSpeech && TryGetSource(bus, producers, AnalysisSignalId.SpeechPresence, out speechSource);
        bool hasFricative = wantFricative && TryGetSource(bus, producers, AnalysisSignalId.FricativeActivity, out fricativeSource);
        bool hasSibilance = wantSibilance && TryGetSource(bus, producers, AnalysisSignalId.SibilanceEnergy, out sibilanceSource);

        if (hasSpeech || hasFricative || hasSibilance)
        {
            float speechSum = 0f;
            float fricativeSum = 0f;
            float sibilanceSum = 0f;

            for (int i = 0; i < count; i++)
            {
                long t = sampleTime + i;
                if (hasSpeech)
                {
                    speechSum += speechSource.ReadSample(t);
                }
                if (hasFricative)
                {
                    fricativeSum += fricativeSource.ReadSample(t);
                }
                if (hasSibilance)
                {
                    sibilanceSum += sibilanceSource.ReadSample(t);
                }
            }

            float invCount = 1f / count;
            if (hasSpeech)
            {
                values[(int)AnalysisSignalId.SpeechPresence] = speechSum * invCount;
                available |= AnalysisSignalMask.SpeechPresence;
            }
            if (hasFricative)
            {
                values[(int)AnalysisSignalId.FricativeActivity] = fricativeSum * invCount;
                available |= AnalysisSignalMask.FricativeActivity;
            }
            if (hasSibilance)
            {
                values[(int)AnalysisSignalId.SibilanceEnergy] = sibilanceSum * invCount;
                available |= AnalysisSignalMask.SibilanceEnergy;
            }
        }

        if ((requestedSignals & AnalysisSignalMask.VoicingScore) != 0 &&
            TryReadSample(bus, producers, AnalysisSignalId.VoicingScore, lastTime, out float voicingScore))
        {
            values[(int)AnalysisSignalId.VoicingScore] = voicingScore;
            available |= AnalysisSignalMask.VoicingScore;
        }

        if ((requestedSignals & AnalysisSignalMask.VoicingState) != 0 &&
            TryReadSample(bus, producers, AnalysisSignalId.VoicingState, lastTime, out float voicingState))
        {
            values[(int)AnalysisSignalId.VoicingState] = voicingState;
            available |= AnalysisSignalMask.VoicingState;
        }

        if ((requestedSignals & AnalysisSignalMask.OnsetFluxHigh) != 0 &&
            TryReadSample(bus, producers, AnalysisSignalId.OnsetFluxHigh, lastTime, out float onsetFlux))
        {
            values[(int)AnalysisSignalId.OnsetFluxHigh] = onsetFlux;
            available |= AnalysisSignalMask.OnsetFluxHigh;
        }

        if ((requestedSignals & AnalysisSignalMask.PitchHz) != 0 &&
            TryReadSample(bus, producers, AnalysisSignalId.PitchHz, lastTime, out float pitch))
        {
            values[(int)AnalysisSignalId.PitchHz] = pitch;
            available |= AnalysisSignalMask.PitchHz;
        }

        if ((requestedSignals & AnalysisSignalMask.PitchConfidence) != 0 &&
            TryReadSample(bus, producers, AnalysisSignalId.PitchConfidence, lastTime, out float pitchConfidence))
        {
            values[(int)AnalysisSignalId.PitchConfidence] = pitchConfidence;
            available |= AnalysisSignalMask.PitchConfidence;
        }

        if ((requestedSignals & AnalysisSignalMask.SpectralFlux) != 0 &&
            TryReadSample(bus, producers, AnalysisSignalId.SpectralFlux, lastTime, out float flux))
        {
            values[(int)AnalysisSignalId.SpectralFlux] = flux;
            available |= AnalysisSignalMask.SpectralFlux;
        }

        if ((requestedSignals & AnalysisSignalMask.HnrDb) != 0 &&
            TryReadSample(bus, producers, AnalysisSignalId.HnrDb, lastTime, out float hnr))
        {
            values[(int)AnalysisSignalId.HnrDb] = hnr;
            available |= AnalysisSignalMask.HnrDb;
        }

        return available;
    }

    private static bool TryGetSource(AnalysisSignalBus bus, int[] producers, AnalysisSignalId signal, out AnalysisSignalSource source)
    {
        int index = (int)signal;
        if ((uint)index >= (uint)producers.Length)
        {
            source = default;
            return false;
        }

        int producerIndex = producers[index];
        if (producerIndex < 0)
        {
            source = default;
            return false;
        }

        source = bus.GetSource(producerIndex, signal);
        return true;
    }

    private static bool TryReadSample(AnalysisSignalBus bus, int[] producers, AnalysisSignalId signal, long sampleTime, out float value)
    {
        if (TryGetSource(bus, producers, signal, out var source))
        {
            value = source.ReadSample(sampleTime);
            return true;
        }

        value = 0f;
        return false;
    }

    private void FillSignalValuesFromProcessor(AnalysisSignalMask missingSignals, float[] values)
    {
        if ((missingSignals & AnalysisSignalMask.SpeechPresence) != 0)
        {
            values[(int)AnalysisSignalId.SpeechPresence] = _analysisSignalProcessor.GetLastValue(AnalysisSignalId.SpeechPresence);
        }
        if ((missingSignals & AnalysisSignalMask.VoicingScore) != 0)
        {
            values[(int)AnalysisSignalId.VoicingScore] = _analysisSignalProcessor.GetLastValue(AnalysisSignalId.VoicingScore);
        }
        if ((missingSignals & AnalysisSignalMask.VoicingState) != 0)
        {
            values[(int)AnalysisSignalId.VoicingState] = _analysisSignalProcessor.GetLastValue(AnalysisSignalId.VoicingState);
        }
        if ((missingSignals & AnalysisSignalMask.FricativeActivity) != 0)
        {
            values[(int)AnalysisSignalId.FricativeActivity] = _analysisSignalProcessor.GetLastValue(AnalysisSignalId.FricativeActivity);
        }
        if ((missingSignals & AnalysisSignalMask.SibilanceEnergy) != 0)
        {
            values[(int)AnalysisSignalId.SibilanceEnergy] = _analysisSignalProcessor.GetLastValue(AnalysisSignalId.SibilanceEnergy);
        }
        if ((missingSignals & AnalysisSignalMask.OnsetFluxHigh) != 0)
        {
            values[(int)AnalysisSignalId.OnsetFluxHigh] = _analysisSignalProcessor.GetLastValue(AnalysisSignalId.OnsetFluxHigh);
        }
        if ((missingSignals & AnalysisSignalMask.PitchHz) != 0)
        {
            values[(int)AnalysisSignalId.PitchHz] = _analysisSignalProcessor.GetLastValue(AnalysisSignalId.PitchHz);
        }
        if ((missingSignals & AnalysisSignalMask.PitchConfidence) != 0)
        {
            values[(int)AnalysisSignalId.PitchConfidence] = _analysisSignalProcessor.GetLastValue(AnalysisSignalId.PitchConfidence);
        }
        if ((missingSignals & AnalysisSignalMask.SpectralFlux) != 0)
        {
            values[(int)AnalysisSignalId.SpectralFlux] = _analysisSignalProcessor.GetLastValue(AnalysisSignalId.SpectralFlux);
        }
        if ((missingSignals & AnalysisSignalMask.HnrDb) != 0)
        {
            values[(int)AnalysisSignalId.HnrDb] = _analysisSignalProcessor.GetLastValue(AnalysisSignalId.HnrDb);
        }
    }

    private int ComputeHarmonics(AnalysisCapabilities capabilities, float pitchHz)
    {
        if (!capabilities.HasFlag(AnalysisCapabilities.Harmonics) || pitchHz <= 0f)
        {
            return 0;
        }

        var descriptor = _analysisDescriptor;
        if (descriptor is null)
        {
            return 0;
        }

        ReadOnlySpan<float> activeMagnitudes = _activeTransformType switch
        {
            SpectrogramTransformType.Cqt when _cqtMagnitudes.Length > 0 => _cqtMagnitudes,
            SpectrogramTransformType.ZoomFft when _fftDisplayMagnitudes.Length > 0 => _fftDisplayMagnitudes,
            _ => _fftMagnitudes
        };

        return HarmonicPeakDetector.Detect(activeMagnitudes, descriptor, pitchHz, _harmonicScratch, _harmonicMagScratch);
    }

    private bool ProcessHopBuffer(out float waveformMin, out float waveformMax)
    {
        int shift = _activeHopSize;
        int analysisSize = _activeAnalysisSize;
        int tail = analysisSize - shift;

        Array.Copy(_analysisBufferRaw, shift, _analysisBufferRaw, 0, tail);
        Array.Copy(_analysisBufferProcessed, shift, _analysisBufferProcessed, 0, tail);

        waveformMin = float.MaxValue;
        waveformMax = float.MinValue;

        bool preEmphasis = _config.PreEmphasis;
        bool hpfEnabled = _config.HighPassEnabled;

        float processedMax = 0f;
        for (int i = 0; i < shift; i++)
        {
            float sample = _hopBuffer[i];
            float dcRemoved = _dcHighPass.Process(sample);
            float filtered = hpfEnabled ? _rumbleHighPass.Process(dcRemoved) : dcRemoved;
            float emphasized = preEmphasis ? _preEmphasisFilter.Process(filtered) : filtered;

            _analysisBufferRaw[tail + i] = filtered;
            _analysisBufferProcessed[tail + i] = emphasized;
            processedMax = MathF.Max(processedMax, MathF.Abs(emphasized));

            if (filtered < waveformMin) waveformMin = filtered;
            if (filtered > waveformMax) waveformMax = filtered;
        }
        Volatile.Write(ref _debugLastProcessedMax, processedMax);

        int filled = Volatile.Read(ref _analysisFilled);
        filled = Math.Min(analysisSize, filled + shift);
        Volatile.Write(ref _analysisFilled, filled);
        Volatile.Write(ref _debugAnalysisFilled, filled);
        return filled >= analysisSize;
    }

    private int ComputeCqtTransform(bool reassignEnabled)
    {
        int clarityBins = Math.Min(_cqt!.BinCount, _activeAnalysisBins);

        // Track analysis buffer max for CQT path
        float analysisBufMax = 0f;
        for (int i = 0; i < _activeFftSize && i < _analysisBufferProcessed.Length; i++)
            analysisBufMax = MathF.Max(analysisBufMax, MathF.Abs(_analysisBufferProcessed[i]));
        Volatile.Write(ref _debugLastAnalysisBufMax, analysisBufMax);

        if (reassignEnabled)
        {
            bool needsTimeData = _activeReassignMode.HasFlag(SpectrogramReassignMode.Time);
            bool needsFreqData = _activeReassignMode.HasFlag(SpectrogramReassignMode.Frequency);
            _cqt.ForwardWithReassignment(_analysisBufferProcessed, _cqtMagnitudes,
                _cqtReal, _cqtImag, _cqtTimeReal, _cqtTimeImag, _cqtPhaseDiff,
                needsTimeData, needsFreqData);
        }
        else
        {
            _cqt.Forward(_analysisBufferProcessed, _cqtMagnitudes);
        }

        // Track CQT output max
        float cqtMax = 0f;
        for (int i = 0; i < clarityBins; i++)
            cqtMax = MathF.Max(cqtMax, MathF.Abs(_cqtMagnitudes[i]));
        Volatile.Write(ref _debugLastFftMax, cqtMax);

        Array.Copy(_cqtMagnitudes, _spectrumScratch, clarityBins);
        if (clarityBins < _activeAnalysisBins)
            Array.Clear(_spectrumScratch, clarityBins, _activeAnalysisBins - clarityBins);

        return clarityBins;
    }

    private int ComputeZoomFftTransform(bool reassignEnabled)
    {
        int zoomBins = _zoomFft!.OutputBins;
        int clarityBins = Math.Min(zoomBins, _activeAnalysisBins);
        Span<float> zoomMagnitudes = _fftDisplayMagnitudes.AsSpan(0, zoomBins);

        // Track analysis buffer max for ZoomFFT path
        float analysisBufMax = 0f;
        for (int i = 0; i < _activeFftSize && i < _analysisBufferProcessed.Length; i++)
            analysisBufMax = MathF.Max(analysisBufMax, MathF.Abs(_analysisBufferProcessed[i]));
        Volatile.Write(ref _debugLastAnalysisBufMax, analysisBufMax);

        bool needsTimeData = reassignEnabled && _activeReassignMode.HasFlag(SpectrogramReassignMode.Time);
        bool needsFreqData = reassignEnabled && _activeReassignMode.HasFlag(SpectrogramReassignMode.Frequency);

        if (needsTimeData || needsFreqData)
        {
            _zoomFft.ForwardWithReassignment(_analysisBufferProcessed, zoomMagnitudes,
                _zoomReal, _zoomImag, _zoomTimeReal, _zoomTimeImag, _zoomDerivReal, _zoomDerivImag);
        }
        else
        {
            _zoomFft.Forward(_analysisBufferProcessed, zoomMagnitudes);
        }

        // Track ZoomFFT output max
        float zoomMax = 0f;
        for (int i = 0; i < clarityBins; i++)
            zoomMax = MathF.Max(zoomMax, MathF.Abs(zoomMagnitudes[i]));
        Volatile.Write(ref _debugLastFftMax, zoomMax);

        zoomMagnitudes.Slice(0, clarityBins).CopyTo(_spectrumScratch);
        if (clarityBins < _activeAnalysisBins)
            Array.Clear(_spectrumScratch, clarityBins, _activeAnalysisBins - clarityBins);

        return clarityBins;
    }

    private void ComputeFftTransform(bool reassignEnabled)
    {
        if (reassignEnabled)
        {
            for (int i = 0; i < _activeFftSize; i++)
            {
                float sample = _analysisBufferProcessed[i];
                _fftReal[i] = sample * _fftWindow[i];
                _fftImag[i] = 0f;
                _fftTimeReal[i] = sample * _fftWindowTime[i];
                _fftTimeImag[i] = 0f;
                _fftDerivReal[i] = sample * _fftWindowDerivative[i];
                _fftDerivImag[i] = 0f;
            }

            _fft?.Forward(_fftReal, _fftImag);
            _fft?.Forward(_fftTimeReal, _fftTimeImag);
            _fft?.Forward(_fftDerivReal, _fftDerivImag);
        }
        else
        {
            float analysisBufMax = 0f, windowMax = 0f, fftRealMax = 0f;
            for (int i = 0; i < _activeFftSize; i++)
            {
                float sample = _analysisBufferProcessed[i];
                float win = _fftWindow[i];
                _fftReal[i] = sample * win;
                _fftImag[i] = 0f;
                analysisBufMax = MathF.Max(analysisBufMax, MathF.Abs(sample));
                windowMax = MathF.Max(windowMax, MathF.Abs(win));
                fftRealMax = MathF.Max(fftRealMax, MathF.Abs(_fftReal[i]));
            }
            Volatile.Write(ref _debugLastAnalysisBufMax, analysisBufMax);
            Volatile.Write(ref _debugLastWindowMax, windowMax);
            Volatile.Write(ref _debugLastFftRealMax, fftRealMax);
            Volatile.Write(ref _debugFftNull, _fft is null);

            _fft?.Forward(_fftReal, _fftImag);
        }

        float normalization = _fftNormalization;
        int half = _activeFftSize / 2;
        float fftMax = 0f;
        for (int i = 0; i < half; i++)
        {
            float re = _fftReal[i];
            float im = _fftImag[i];
            float mag = MathF.Sqrt(re * re + im * im) * normalization;
            _fftMagnitudes[i] = mag;
            fftMax = MathF.Max(fftMax, mag);
        }
        Volatile.Write(ref _debugLastFftMax, fftMax);
    }

    private ReadOnlySpan<float> NormalizeFftMagnitudes()
    {
        int half = _activeFftSize / 2;
        var mode = _config.NormalizationMode;

        if (mode == SpectrogramNormalizationMode.AWeighted)
        {
            for (int i = 0; i < half; i++)
                _fftDisplayMagnitudes[i] = _fftMagnitudes[i] * _aWeighting[i];
            _fftDisplayMagnitudes.AsSpan(0, half).CopyTo(_spectrumScratch);
            return _fftDisplayMagnitudes;
        }
        else if (mode == SpectrogramNormalizationMode.Peak)
        {
            float peak = 0f;
            for (int i = 0; i < half; i++)
                if (_fftMagnitudes[i] > peak) peak = _fftMagnitudes[i];

            float inv = peak > 1e-12f ? 1f / peak : 0f;
            for (int i = 0; i < half; i++)
                _fftDisplayMagnitudes[i] = _fftMagnitudes[i] * inv;
            _fftDisplayMagnitudes.AsSpan(0, half).CopyTo(_spectrumScratch);
            return _fftDisplayMagnitudes;
        }
        else if (mode == SpectrogramNormalizationMode.Rms)
        {
            double sum = 0.0;
            for (int i = 0; i < half; i++)
            {
                float mag = _fftMagnitudes[i];
                sum += mag * mag;
            }
            float rms = sum > 0.0 ? MathF.Sqrt((float)(sum / half)) : 0f;
            float inv = rms > 1e-12f ? 1f / rms : 0f;
            for (int i = 0; i < half; i++)
                _fftDisplayMagnitudes[i] = _fftMagnitudes[i] * inv;
            _fftDisplayMagnitudes.AsSpan(0, half).CopyTo(_spectrumScratch);
            return _fftDisplayMagnitudes;
        }

        _fftMagnitudes.AsSpan(0, half).CopyTo(_spectrumScratch);
        return _fftMagnitudes;
    }

    private float ProcessClarity(int clarityBins, VoicingState voicing, float lastPitch, float lastConfidence)
    {
        var clarityMode = _config.ClarityMode;
        float clarityNoise = _config.ClarityNoise;
        float clarityHarmonic = _config.ClarityHarmonic;
        float claritySmoothing = _config.ClaritySmoothing;
        var smoothingMode = _config.SmoothingMode;

        bool clarityEnabled = clarityMode != ClarityProcessingMode.None;
        bool useNoise = clarityMode is ClarityProcessingMode.Noise or ClarityProcessingMode.Full;
        bool useHarmonic = clarityMode is ClarityProcessingMode.Harmonic or ClarityProcessingMode.Full;
        float lastHnr = 0f;

        if (!clarityEnabled)
        {
            Array.Copy(_spectrumScratch, _displaySmoothed, clarityBins);
            Array.Copy(_spectrumScratch, _displayProcessed, clarityBins);
        }
        else
        {
            Array.Copy(_spectrumScratch, _displayWork, clarityBins);

            if (useNoise && clarityNoise > 0f)
                _noiseReducer.Apply(_displayWork, clarityNoise, voicing, clarityBins);

            if (useHarmonic && clarityHarmonic > 0f)
                _hpssProcessor.Apply(_displayWork, _displayProcessed, clarityHarmonic, clarityBins);
            else
                Array.Copy(_displayWork, _displayProcessed, clarityBins);

            if (clarityMode == ClarityProcessingMode.Full && clarityHarmonic > 0f)
            {
                _harmonicComb.UpdateMaskLinear(lastPitch, lastConfidence, voicing, _binResolution, clarityBins);
                lastHnr = _harmonicComb.Apply(_displayProcessed, clarityHarmonic, clarityBins);
            }

            if (claritySmoothing > 0f && smoothingMode != SpectrogramSmoothingMode.Off)
            {
                if (smoothingMode == SpectrogramSmoothingMode.Bilateral)
                    _smoother.ApplyBilateral(_displayProcessed, _displaySmoothed, claritySmoothing, clarityBins);
                else
                    _smoother.ApplyEma(_displayProcessed, _displaySmoothed, claritySmoothing, clarityBins);
            }
            else
            {
                Array.Copy(_displayProcessed, _displaySmoothed, clarityBins);
            }
        }

        // Track max display value for debugging
        float displayMax = 0f;
        for (int i = 0; i < clarityBins; i++)
            displayMax = MathF.Max(displayMax, MathF.Abs(_displaySmoothed[i]));
        Volatile.Write(ref _debugLastDisplayMax, displayMax);

        return lastHnr;
    }

    private void MapToDisplayBins(int clarityBins, int frameIndex)
    {
        if (_activeDisplayBins <= 0 || clarityBins <= 0)
        {
            return;
        }

        if (_displayMapScratch.Length != _activeDisplayBins)
        {
            _displayMapScratch = new float[_activeDisplayBins];
        }

        if (_displayMapper.IsConfigured)
        {
            _displayMapper.MapMax(_displaySmoothed.AsSpan(0, clarityBins), _displayMapScratch);
            _resultStore.WriteSpectrogramFrame(frameIndex, _displayMapScratch);
            return;
        }

        float ratio = (float)clarityBins / _activeDisplayBins;
        for (int i = 0; i < _activeDisplayBins; i++)
        {
            int srcIdx = Math.Min((int)(i * ratio), clarityBins - 1);
            _displayMapScratch[i] = _displaySmoothed[srcIdx];
        }

        _resultStore.WriteSpectrogramFrame(frameIndex, _displayMapScratch);
    }

    private void ApplyReassignment(long frameId, ReadOnlySpan<float> displayMagnitudes, SpectrogramTransformType transformType)
    {
        int frameIndex = (int)(frameId % _activeFrameCapacity);
        _resultStore.ClearSpectrogramFrame(frameIndex);
        BuildDisplayGain();

        float reassignThresholdDb = _config.ReassignThreshold;
        float reassignThresholdLinear = DspUtils.DbToLinear(reassignThresholdDb);
        float reassignSpread = Math.Clamp(_config.ReassignSpread, 0f, 1f);
        float maxTimeShift = MaxReassignFrameShift * reassignSpread;
        float maxBinShift = MaxReassignBinShift * reassignSpread;
        float invHop = 1f / MathF.Max(1f, _activeHopSize);
        long oldestFrameId = Math.Max(0, frameId - _activeFrameCapacity + 1);

        int numBins;
        float freqBinScale;
        float binResHz;
        float minFreqHz = _displayMapper.IsConfigured ? _displayMapper.MinFrequencyHz : _activeMinFrequency;
        float maxFreqHz = _displayMapper.IsConfigured ? _displayMapper.MaxFrequencyHz : _activeMaxFrequency;
        ReadOnlySpan<float> centerFreqs = ReadOnlySpan<float>.Empty;

        if (transformType == SpectrogramTransformType.Cqt && _cqt is not null)
        {
            numBins = _cqt.BinCount;
            freqBinScale = 0f;
            binResHz = 0f;
            centerFreqs = _cqt.CenterFrequencies;
        }
        else if (transformType == SpectrogramTransformType.ZoomFft && _zoomFft is not null)
        {
            numBins = _zoomFft.OutputBins;
            binResHz = _zoomFft.BinResolutionHz;
            freqBinScale = numBins * 2 / (MathF.PI * 2f);
        }
        else
        {
            numBins = _activeFftSize / 2;
            binResHz = _binResolution;
            freqBinScale = _activeFftSize / (MathF.PI * 2f);
        }

        float scaledMin = FrequencyScaleUtils.ToScale(_activeScale, minFreqHz);
        float scaledMax = FrequencyScaleUtils.ToScale(_activeScale, maxFreqHz);
        float scaledRange = MathF.Max(1e-6f, scaledMax - scaledMin);
        float invScaledRange = 1f / scaledRange;
        float maxPos = Math.Max(1f, _activeDisplayBins - 1);

        for (int bin = 0; bin < numBins; bin++)
        {
            float mag;
            double re, im, reTime, imTime, reDeriv, imDeriv;
            float binFreqHz;
            float phaseDiff = 0f;

            if (transformType == SpectrogramTransformType.Cqt)
            {
                mag = _cqtMagnitudes[bin];
                re = _cqtReal[bin];
                im = _cqtImag[bin];
                reTime = _cqtTimeReal[bin];
                imTime = _cqtTimeImag[bin];
                reDeriv = 0;
                imDeriv = 0;
                binFreqHz = centerFreqs[bin];
                phaseDiff = _cqtPhaseDiff[bin];
            }
            else if (transformType == SpectrogramTransformType.ZoomFft)
            {
                mag = _fftDisplayMagnitudes[bin];
                re = _zoomReal[bin];
                im = _zoomImag[bin];
                reTime = _zoomTimeReal[bin];
                imTime = _zoomTimeImag[bin];
                reDeriv = _zoomDerivReal[bin];
                imDeriv = _zoomDerivImag[bin];
                binFreqHz = _zoomFft!.GetBinFrequency(bin);
            }
            else
            {
                mag = displayMagnitudes[bin];
                re = _fftReal[bin];
                im = _fftImag[bin];
                reTime = _fftTimeReal[bin];
                imTime = _fftTimeImag[bin];
                reDeriv = _fftDerivReal[bin];
                imDeriv = _fftDerivImag[bin];
                binFreqHz = bin * _binResolution;
            }

            if (mag <= 0f)
            {
                continue;
            }

            float adjustedMag = mag;
            if (transformType == SpectrogramTransformType.Fft)
            {
                float gain = _displayGain[bin];
                if (gain <= 0f)
                {
                    continue;
                }
                adjustedMag = mag * gain;
            }

            if (adjustedMag < reassignThresholdLinear)
            {
                continue;
            }

            double denom = re * re + im * im + 1e-12;

            float timeShiftFrames = 0f;
            if (_activeReassignMode.HasFlag(SpectrogramReassignMode.Time))
            {
                double timeShiftSamples = (reTime * re + imTime * im) / denom;
                double timeShiftScaled = timeShiftSamples * invHop;
                timeShiftFrames = (float)Math.Clamp(timeShiftScaled, -maxTimeShift, maxTimeShift);
            }

            float reassignedFreqHz = binFreqHz;
            if (_activeReassignMode.HasFlag(SpectrogramReassignMode.Frequency))
            {
                if (transformType == SpectrogramTransformType.Cqt)
                {
                    float hopTime = _activeHopSize / (float)_sampleRate;
                    float twoPi = MathF.PI * 2f;
                    float expectedPhaseAdvance = twoPi * binFreqHz * hopTime;
                    float expectedMod = expectedPhaseAdvance;
                    while (expectedMod > MathF.PI) expectedMod -= twoPi;
                    while (expectedMod < -MathF.PI) expectedMod += twoPi;

                    float deviation = phaseDiff - expectedMod;
                    while (deviation > MathF.PI) deviation -= twoPi;
                    while (deviation < -MathF.PI) deviation += twoPi;

                    float logDeviation = deviation / (twoPi * hopTime * binFreqHz);
                    reassignedFreqHz = binFreqHz * MathF.Exp(logDeviation);
                }
                else
                {
                    double imagPart = (imDeriv * re - reDeriv * im) / denom;
                    double freqShift = imagPart * freqBinScale;
                    float freqShiftBins = (float)Math.Clamp(freqShift, -maxBinShift, maxBinShift);
                    reassignedFreqHz = binFreqHz + freqShiftBins * binResHz;
                }
            }

            if (reassignedFreqHz < minFreqHz || reassignedFreqHz > maxFreqHz)
            {
                continue;
            }

            float targetFrame = frameId + timeShiftFrames - _reassignLatencyFrames;
            long frameBase = (long)MathF.Floor(targetFrame);
            float frameFrac = targetFrame - frameBase;
            if (frameBase < oldestFrameId || frameBase > frameId)
            {
                continue;
            }

            float clamped = Math.Clamp(reassignedFreqHz, minFreqHz, maxFreqHz);
            float scaled = FrequencyScaleUtils.ToScale(_activeScale, clamped);
            float norm = (scaled - scaledMin) * invScaledRange;
            float displayPos = Math.Clamp(norm * maxPos, 0f, maxPos);

            float binHalfHz;
            if (transformType == SpectrogramTransformType.Cqt)
            {
                float prev = bin > 0 ? centerFreqs[bin - 1] : centerFreqs[bin];
                float next = bin + 1 < numBins ? centerFreqs[bin + 1] : centerFreqs[bin];
                binHalfHz = 0.5f * MathF.Max(1e-3f, next - prev);
            }
            else
            {
                binHalfHz = MathF.Max(1e-3f, binResHz * 0.5f);
            }

            float startHz = reassignedFreqHz - binHalfHz;
            float endHz = reassignedFreqHz + binHalfHz;

            float scaledStart = FrequencyScaleUtils.ToScale(_activeScale, Math.Clamp(startHz, minFreqHz, maxFreqHz));
            float scaledEnd = FrequencyScaleUtils.ToScale(_activeScale, Math.Clamp(endHz, minFreqHz, maxFreqHz));
            float normStart = (scaledStart - scaledMin) * invScaledRange;
            float normEnd = (scaledEnd - scaledMin) * invScaledRange;
            float displayStart = Math.Clamp(normStart * maxPos, 0f, maxPos);
            float displayEnd = Math.Clamp(normEnd * maxPos, 0f, maxPos);

            float low = MathF.Min(displayStart, displayEnd);
            float high = MathF.Max(displayStart, displayEnd);
            int binStart = (int)MathF.Floor(low);
            int binEnd = Math.Max(binStart, (int)MathF.Ceiling(high));
            binStart = Math.Clamp(binStart, 0, _activeDisplayBins - 1);
            binEnd = Math.Clamp(binEnd, binStart, _activeDisplayBins - 1);

            float valueBase = adjustedMag;
            if (valueBase <= 0f)
            {
                continue;
            }

            float wFrame0 = 1f - frameFrac;
            float wFrame1 = frameFrac;

            if (frameBase >= oldestFrameId)
            {
                WriteReassignFrame(frameBase, wFrame0, binStart, binEnd, displayPos, valueBase);
            }

            long frame1 = frameBase + 1;
            if (wFrame1 > 0f && frame1 <= frameId && frame1 >= oldestFrameId)
            {
                WriteReassignFrame(frame1, wFrame1, binStart, binEnd, displayPos, valueBase);
            }
        }
    }

    private void WriteReassignFrame(long frameId, float frameWeight, int binStart, int binEnd, float displayPos, float valueBase)
    {
        if (frameWeight <= 0f)
        {
            return;
        }

        int targetIndex = (int)(frameId % _activeFrameCapacity);
        if (targetIndex < 0)
        {
            targetIndex += _activeFrameCapacity;
        }

        Span<float> target = _resultStore.GetSpectrogramFrameSpan(targetIndex);
        if (target.IsEmpty)
        {
            return;
        }

        if (binStart == binEnd)
        {
            float value = valueBase * frameWeight;
            if (value > target[binStart])
            {
                target[binStart] = value;
            }
            return;
        }

        float invSpan = 1f / (binEnd - binStart);
        for (int targetBin = binStart; targetBin <= binEnd; targetBin++)
        {
            float weight = 1f - MathF.Abs(targetBin - displayPos) * invSpan;
            if (weight <= 0f)
            {
                continue;
            }

            float value = valueBase * frameWeight * weight;
            if (value > target[targetBin])
            {
                target[targetBin] = value;
            }
        }
    }

    private void BuildDisplayGain()
    {
        int bins = Math.Min(_activeAnalysisBins, _displayGain.Length);
        for (int i = 0; i < bins; i++)
        {
            float raw = _spectrumScratch[i];
            float processed = _displaySmoothed[i];
            float gain = raw > 1e-8f ? processed / raw : 0f;
            _displayGain[i] = Math.Clamp(gain, 0f, 4f);
        }

        if (bins < _displayGain.Length)
        {
            Array.Clear(_displayGain, bins, _displayGain.Length - bins);
        }
    }

    private SpeechMetricsFrame ProcessSpeechMetrics(
        float waveformMin, float waveformMax,
        float lastPitch, float lastConfidence,
        VoicingState voicing,
        float flux, float slope, float lastHnr,
        long frameId)
    {
        float energyDb = ComputeRmsDb(_analysisBufferRaw);
        float f1Hz = 0f;
        float f2Hz = 0f;
        ReadOnlySpan<float> speechMagnitudes = GetSpeechMagnitudes(out var speechBinCenters, out var speechBinResolution);
        float spectralFlatness = ComputeSpectralFlatness(speechMagnitudes);
        float syllableEnergyDb = energyDb;
        float bandLowRatio = 0f;
        float bandMidRatio = 0f;
        float bandPresenceRatio = 0f;
        float bandHighRatio = 0f;
        float clarityRatio = 0f;
        float syllableEnergyRatio = 0f;

        if (!speechMagnitudes.IsEmpty && (speechBinCenters.Length > 0 || speechBinResolution > 0f))
        {
            ComputeSpeechBandMetrics(
                speechMagnitudes,
                speechBinCenters,
                speechBinResolution,
                out bandLowRatio,
                out bandMidRatio,
                out bandPresenceRatio,
                out bandHighRatio,
                out clarityRatio,
                out syllableEnergyRatio);

            if (syllableEnergyRatio > 0f)
            {
                // Band-limited energy estimate based on 300-2000 Hz ratio.
                syllableEnergyDb = energyDb + 10f * MathF.Log10(MathF.Max(syllableEnergyRatio, 1e-3f));
            }
        }

        var metrics = _speechCoach.Process(
            energyDb,
            syllableEnergyDb,
            lastPitch,
            lastConfidence,
            voicing,
            spectralFlatness,
            flux,
            slope,
            lastHnr,
            f1Hz,
            f2Hz,
            bandLowRatio,
            bandMidRatio,
            bandPresenceRatio,
            bandHighRatio,
            clarityRatio,
            frameId);

        return new SpeechMetricsFrame
        {
            SyllableRate = metrics.SyllableRate,
            ArticulationRate = metrics.ArticulationRate,
            WordsPerMinute = metrics.WordsPerMinute,
            ArticulationWpm = metrics.ArticulationWpm,
            PauseRatio = metrics.PauseRatio,
            MeanPauseDurationMs = metrics.MeanPauseDurationMs,
            PausesPerMinute = metrics.PausesPerMinute,
            FilledPauseRatio = metrics.FilledPauseRatio,
            PauseMicroCount = metrics.PauseMicroCount,
            PauseShortCount = metrics.PauseShortCount,
            PauseMediumCount = metrics.PauseMediumCount,
            PauseLongCount = metrics.PauseLongCount,
            MonotoneScore = metrics.MonotoneScore,
            ClarityScore = metrics.OverallClarity,
            IntelligibilityScore = metrics.IntelligibilityScore,
            BandLowRatio = metrics.BandLowRatio,
            BandMidRatio = metrics.BandMidRatio,
            BandPresenceRatio = metrics.BandPresenceRatio,
            BandHighRatio = metrics.BandHighRatio,
            ClarityRatio = metrics.ClarityRatio,
            SpeakingState = (byte)metrics.CurrentState,
            SyllableDetected = metrics.SyllableDetected,
            EmphasisDetected = metrics.EmphasisDetected
        };
    }

    private void ConfigureAnalysis(bool force)
    {
        // Read current config
        int fftSize = SelectDiscrete(_config.FftSize, AnalysisConfiguration.FftSizes);
        int overlapIndex = _config.OverlapIndex;
        float overlap = AnalysisConfiguration.OverlapOptions[overlapIndex];
        float timeWindow = _config.TimeWindow;
        float minHz = _config.MinFrequency;
        float maxHz = _config.MaxFrequency;
        var window = _config.WindowFunction;
        var scale = _config.FrequencyScale;
        var transformType = _config.TransformType;
        var reassignMode = _config.ReassignMode;
        float hpfCutoff = _config.HighPassCutoff;
        bool hpfEnabled = _config.HighPassEnabled;
        bool preEmphasis = _config.PreEmphasis;
        int cqtBinsPerOctave = _config.CqtBinsPerOctave;

        bool sizeChanged = force || fftSize != _activeFftSize || overlapIndex != _activeOverlapIndex ||
                           MathF.Abs(timeWindow - _activeTimeWindow) > 1e-3f;

        // Compute transform reconfiguration flags BEFORE active values are updated
        bool transformChanged = transformType != _activeTransformType;
        bool freqRangeChanged = MathF.Abs(minHz - _activeMinFrequency) > 1e-3f ||
                                MathF.Abs(maxHz - _activeMaxFrequency) > 1e-3f;
        bool needsTransformConfig = force || transformChanged || freqRangeChanged || sizeChanged;

        if (sizeChanged)
        {
            _activeFftSize = fftSize;
            _activeOverlapIndex = overlapIndex;
            _activeTimeWindow = timeWindow;
            _activeHopSize = Math.Max(1, (int)(fftSize * (1f - overlap)));
            _activeFrameCapacity = Math.Max(1, (int)MathF.Ceiling(timeWindow * _sampleRate / _activeHopSize));
            _activeAnalysisSize = fftSize;
            _analysisFilled = 0;

            // Allocate FFT buffers
            _fft = new FastFft(fftSize);
            _analysisBufferRaw = new float[fftSize];
            _analysisBufferProcessed = new float[fftSize];
            _hopBuffer = new float[_activeHopSize];
            _fftReal = new float[fftSize];
            _fftImag = new float[fftSize];
            _fftWindow = new float[fftSize];
            _fftWindowTime = new float[fftSize];
            _fftWindowDerivative = new float[fftSize];
            _fftTimeReal = new float[fftSize];
            _fftTimeImag = new float[fftSize];
            _fftDerivReal = new float[fftSize];
            _fftDerivImag = new float[fftSize];
            _fftMagnitudes = new float[fftSize / 2];
            _fftDisplayMagnitudes = new float[fftSize / 2];
            _aWeighting = new float[fftSize / 2];
            _fftNormalization = 2f / MathF.Max(1f, fftSize);
            _binResolution = _sampleRate / (float)fftSize;

            UpdateAWeighting();
            ResetSpeechBandSmoothing();
        }

        if (force || window != _activeWindow || sizeChanged)
        {
            _activeWindow = window;
            WindowFunctions.Fill(_fftWindow, window);
            UpdateWindowNormalization();
            UpdateReassignWindows();
        }

        if (force || scale != _activeScale || MathF.Abs(minHz - _activeMinFrequency) > 1e-3f ||
            MathF.Abs(maxHz - _activeMaxFrequency) > 1e-3f || sizeChanged)
        {
            _activeScale = scale;
            _activeMinFrequency = minHz;
            _activeMaxFrequency = maxHz;
        }

        if (force || reassignMode != _activeReassignMode)
        {
            _activeReassignMode = reassignMode;
            _reassignLatencyFrames = reassignMode.HasFlag(SpectrogramReassignMode.Time)
                ? (int)MathF.Ceiling(MaxReassignFrameShift)
                : 0;
        }

        // Configure transforms (only when parameters change)
        if (transformType == SpectrogramTransformType.ZoomFft)
        {
            _zoomFft ??= new ZoomFft();
            if (needsTransformConfig)
            {
                _zoomFft.Configure(_sampleRate, fftSize, minHz, maxHz, ZoomFftZoomFactor, window);

                int requiredSize = _zoomFft.RequiredInputSize;
                if (requiredSize > _analysisBufferRaw.Length)
                {
                    _analysisBufferRaw = new float[requiredSize];
                    _analysisBufferProcessed = new float[requiredSize];
                    _analysisFilled = 0;
                }
                _activeAnalysisSize = requiredSize;

                int zoomBins = _zoomFft.OutputBins;
                if (_zoomReal.Length < zoomBins)
                {
                    _zoomReal = new float[zoomBins];
                    _zoomImag = new float[zoomBins];
                    _zoomTimeReal = new float[zoomBins];
                    _zoomTimeImag = new float[zoomBins];
                    _zoomDerivReal = new float[zoomBins];
                    _zoomDerivImag = new float[zoomBins];
                }
            }
        }

        if (transformType == SpectrogramTransformType.Cqt)
        {
            _cqt ??= new ConstantQTransform();
            if (needsTransformConfig)
            {
                _cqt.Configure(_sampleRate, minHz, maxHz, cqtBinsPerOctave);

                int requiredSize = _cqt.MaxWindowLength;
                if (requiredSize > _analysisBufferRaw.Length)
                {
                    _analysisBufferRaw = new float[requiredSize];
                    _analysisBufferProcessed = new float[requiredSize];
                    _analysisFilled = 0;
                }
                _activeAnalysisSize = requiredSize;

                if (_cqtMagnitudes.Length < _cqt.BinCount)
                {
                    _cqtMagnitudes = new float[_cqt.BinCount];
                    _cqtReal = new float[_cqt.BinCount];
                    _cqtImag = new float[_cqt.BinCount];
                    _cqtTimeReal = new float[_cqt.BinCount];
                    _cqtTimeImag = new float[_cqt.BinCount];
                    _cqtPhaseDiff = new float[_cqt.BinCount];
                }

                _activeCqtBinsPerOctave = cqtBinsPerOctave;
            }
        }

        if (transformType == SpectrogramTransformType.Fft && _activeAnalysisSize != fftSize)
        {
            _activeAnalysisSize = fftSize;
            _analysisFilled = 0;
        }

        // Update analysis bins
        int desiredAnalysisBins = transformType switch
        {
            SpectrogramTransformType.Cqt => _cqt?.BinCount ?? _activeAnalysisBins,
            SpectrogramTransformType.ZoomFft => _zoomFft?.OutputBins ?? _activeAnalysisBins,
            _ => fftSize / 2
        };
        desiredAnalysisBins = Math.Max(1, desiredAnalysisBins);

        bool analysisBinsChanged = desiredAnalysisBins != _activeAnalysisBins;
        if (analysisBinsChanged)
        {
            _activeAnalysisBins = desiredAnalysisBins;
            _spectrumScratch = new float[desiredAnalysisBins];
            _displayWork = new float[desiredAnalysisBins];
            _displayProcessed = new float[desiredAnalysisBins];
            _displaySmoothed = new float[desiredAnalysisBins];
            _displayGain = new float[desiredAnalysisBins];

            _noiseReducer.EnsureCapacity(desiredAnalysisBins);
            _hpssProcessor.EnsureCapacity(desiredAnalysisBins);
            _smoother.EnsureCapacity(desiredAnalysisBins);
            _harmonicComb.EnsureCapacity(desiredAnalysisBins);
        }

        if (_activeDisplayBins == 0)
            _activeDisplayBins = AnalysisConfiguration.FixedDisplayBins;

        _activeTransformType = transformType;

        // Filters
        if (sizeChanged || force || MathF.Abs(hpfCutoff - _activeHighPassCutoff) > 1e-3f ||
            hpfEnabled != _activeHighPassEnabled || preEmphasis != _activePreEmphasisEnabled)
        {
            _dcHighPass.Configure(DcCutoffHz, _sampleRate);
            _dcHighPass.Reset();
            _rumbleHighPass.SetHighPass(_sampleRate, hpfCutoff, 0.707f);
            _rumbleHighPass.Reset();
            _activeHighPassCutoff = hpfCutoff;
            _activeHighPassEnabled = hpfEnabled;
            _preEmphasisFilter.Configure(DefaultPreEmphasis);
            _preEmphasisFilter.Reset();
            _activePreEmphasisEnabled = preEmphasis;
        }

        // Speech coach
        if (sizeChanged || force)
        {
            _speechCoach.Configure(_activeHopSize, _sampleRate);
        }

        // Update result store - only reconfigure when display dimensions change
        // Record discontinuity when analysis parameters change (preserves display buffer)
        UpdateAnalysisDescriptor(transformType);
        UpdateDisplayMapper();

        if (sizeChanged || force)
        {
            // Display dimensions changed - must reconfigure (clears buffers)
            _resultStore.Configure(_sampleRate, _activeFrameCapacity, _activeDisplayBins,
                _activeAnalysisBins, _binResolution, transformType, _config);
        }
        else if (analysisBinsChanged || transformChanged)
        {
            // Only analysis parameters changed - record discontinuity but preserve display
            var discontinuity = DiscontinuityType.None;
            if (transformChanged)
                discontinuity |= DiscontinuityType.TransformChange;
            if (analysisBinsChanged)
                discontinuity |= DiscontinuityType.ResolutionChange;
            if (freqRangeChanged)
                discontinuity |= DiscontinuityType.FrequencyRangeChange;

            _resultStore.RecordDiscontinuity(discontinuity);

            // Still need to update config for the store (but it won't clear display now)
            _resultStore.Configure(_sampleRate, _activeFrameCapacity, _activeDisplayBins,
                _activeAnalysisBins, _binResolution, transformType, _config);
        }

        var pitchAlgorithm = _config.PitchAlgorithm;
        if (transformType == SpectrogramTransformType.Cqt && pitchAlgorithm == PitchDetectorType.Swipe)
        {
            pitchAlgorithm = PitchDetectorType.Yin;
        }

        var signalSettings = new AnalysisSignalProcessorSettings
        {
            AnalysisSize = _activeFftSize,
            PitchDetector = pitchAlgorithm,
            VoicingSettings = _config.VoicingSettings,
            MinFrequency = minHz,
            MaxFrequency = maxHz,
            WindowFunction = window,
            PreEmphasisEnabled = preEmphasis,
            HighPassEnabled = hpfEnabled,
            HighPassCutoff = hpfCutoff
        };
        bool signalConfigChanged = force ||
                                   !_hasActiveSignalSettings ||
                                   _activeSignalHopSize != _activeHopSize ||
                                   !_activeSignalSettings.Equals(signalSettings);
        if (signalConfigChanged)
        {
            _analysisSignalProcessor.Configure(_sampleRate, _activeHopSize, signalSettings);
            _activeSignalSettings = signalSettings;
            _activeSignalHopSize = _activeHopSize;
            _hasActiveSignalSettings = true;
        }
    }

    private void UpdateAnalysisDescriptor(SpectrogramTransformType transformType)
    {
        SpectrogramAnalysisDescriptor? descriptor = null;
        switch (transformType)
        {
            case SpectrogramTransformType.Cqt when _cqt is not null:
                descriptor = SpectrogramAnalysisDescriptor.CreateFromCenters(
                    SpectrogramTransformType.Cqt, _cqt.CenterFrequencies);
                break;
            case SpectrogramTransformType.ZoomFft when _zoomFft is not null:
                descriptor = SpectrogramAnalysisDescriptor.CreateLinear(
                    SpectrogramTransformType.ZoomFft, _zoomFft.OutputBins,
                    _zoomFft.MinFrequency, _zoomFft.BinResolutionHz);
                break;
            default:
                int half = _activeFftSize / 2;
                descriptor = SpectrogramAnalysisDescriptor.CreateLinear(
                    SpectrogramTransformType.Fft, half, 0f, _binResolution);
                break;
        }

        if (descriptor is not null)
        {
            _analysisDescriptor = descriptor;
            _resultStore.SetAnalysisDescriptor(descriptor);
        }
    }

    private void UpdateDisplayMapper()
    {
        if (_analysisDescriptor is null || _activeDisplayBins <= 0)
        {
            return;
        }

        float minHz = _activeMinFrequency;
        float maxHz = _activeMaxFrequency;
        bool mappingChanged = !_displayMapper.IsConfigured
                              || _displayMapperBins != _activeDisplayBins
                              || _displayMapperScale != _activeScale
                              || MathF.Abs(minHz - _displayMapperMinHz) > 1e-3f
                              || MathF.Abs(maxHz - _displayMapperMaxHz) > 1e-3f
                              || !ReferenceEquals(_displayMapperDescriptor, _analysisDescriptor);

        if (mappingChanged)
        {
            _displayMapper.Configure(_analysisDescriptor, _activeDisplayBins, minHz, maxHz, _activeScale);
            _displayMapperBins = _activeDisplayBins;
            _displayMapperScale = _activeScale;
            _displayMapperMinHz = minHz;
            _displayMapperMaxHz = maxHz;
            _displayMapperDescriptor = _analysisDescriptor;
        }

        if (_displayMapScratch.Length != _activeDisplayBins)
        {
            _displayMapScratch = new float[_activeDisplayBins];
        }
    }

    private void UpdateAWeighting()
    {
        int half = _activeFftSize / 2;
        for (int bin = 0; bin < half; bin++)
        {
            float freq = bin * _binResolution;
            _aWeighting[bin] = AWeighting.GetLinearWeight(freq);
        }
    }

    private void UpdateWindowNormalization()
    {
        double sum = 0.0;
        for (int i = 0; i < _fftWindow.Length; i++)
            sum += _fftWindow[i];

        float denom = sum > 1e-6 ? (float)sum : 1f;
        _fftNormalization = 2f / denom;
    }

    private void UpdateReassignWindows()
    {
        if (_fftWindowTime.Length != _activeFftSize || _fftWindowDerivative.Length != _activeFftSize)
            return;

        float center = 0.5f * (_activeFftSize - 1);
        for (int i = 0; i < _activeFftSize; i++)
        {
            float t = i - center;
            _fftWindowTime[i] = _fftWindow[i] * t;
        }

        for (int i = 0; i < _activeFftSize; i++)
        {
            float prev = i > 0 ? _fftWindow[i - 1] : _fftWindow[i];
            float next = i < _activeFftSize - 1 ? _fftWindow[i + 1] : _fftWindow[i];
            _fftWindowDerivative[i] = 0.5f * (next - prev);
        }
    }

    private void ResetAfterDrop()
    {
        Volatile.Write(ref _analysisFilled, 0);
        Array.Clear(_analysisBufferRaw);
        Array.Clear(_analysisBufferProcessed);
        Array.Clear(_hopBuffer);
        _analysisSignalProcessor.Reset();
        _speechCoach.Reset();
        Array.Clear(_analysisSignalValues, 0, _analysisSignalValues.Length);
        Volatile.Write(ref _analysisReadSampleTime, long.MinValue);
        ResetSpeechBandSmoothing();
    }

    private static int SelectDiscrete(int value, int[] options)
    {
        int best = options[0];
        int bestDelta = Math.Abs(options[0] - value);
        for (int i = 1; i < options.Length; i++)
        {
            int delta = Math.Abs(options[i] - value);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = options[i];
            }
        }
        return best;
    }

    private static float ComputeSpectralFlatness(ReadOnlySpan<float> magnitudes)
    {
        if (magnitudes.IsEmpty)
            return 1f;

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

    private static float ComputeRmsDb(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return -80f;
        }

        double sum = 0.0;
        for (int i = 0; i < samples.Length; i++)
        {
            float value = samples[i];
            sum += value * value;
        }

        double mean = sum / samples.Length;
        float rms = mean > 0.0 ? MathF.Sqrt((float)mean) : 0f;
        return rms > 1e-8f ? 20f * MathF.Log10(rms) : -80f;
    }

    private void ResetSpeechBandSmoothing()
    {
        _bandLowSmooth = 0f;
        _bandMidSmooth = 0f;
        _bandPresenceSmooth = 0f;
        _bandHighSmooth = 0f;
        _bandSmoothInitialized = false;
    }

    private ReadOnlySpan<float> GetSpeechMagnitudes(out ReadOnlySpan<float> binCentersHz, out float binResolutionHz)
    {
        ReadOnlySpan<float> magnitudes = _activeTransformType switch
        {
            SpectrogramTransformType.Cqt => _cqtMagnitudes,
            SpectrogramTransformType.ZoomFft => _fftDisplayMagnitudes,
            _ => _fftMagnitudes
        };

        int count = Math.Min(_activeAnalysisBins, magnitudes.Length);
        if (count <= 0)
        {
            binCentersHz = ReadOnlySpan<float>.Empty;
            binResolutionHz = _binResolution;
            return ReadOnlySpan<float>.Empty;
        }

        magnitudes = magnitudes.Slice(0, count);

        var descriptor = _analysisDescriptor;
        if (descriptor is not null)
        {
            ReadOnlySpan<float> centers = descriptor.BinCentersHz.Span;
            binCentersHz = centers.Length >= count ? centers.Slice(0, count) : centers;
            binResolutionHz = descriptor.BinResolutionHz;
        }
        else
        {
            binCentersHz = ReadOnlySpan<float>.Empty;
            binResolutionHz = _binResolution;
        }

        return magnitudes;
    }

    private void ComputeSpeechBandMetrics(
        ReadOnlySpan<float> magnitudes,
        ReadOnlySpan<float> binCentersHz,
        float binResolutionHz,
        out float bandLowRatio,
        out float bandMidRatio,
        out float bandPresenceRatio,
        out float bandHighRatio,
        out float clarityRatio,
        out float syllableEnergyRatio)
    {
        // Band ranges and smoothing per SPEECH.md 9.2-9.3.
        float lowPower = 0f;
        float midPower = 0f;
        float presencePower = 0f;
        float highPower = 0f;
        float syllablePower = 0f;

        bool useCenters = !binCentersHz.IsEmpty && binCentersHz.Length >= magnitudes.Length;
        bool useResolution = binResolutionHz > 0f;
        if (!useCenters && !useResolution)
        {
            bandLowRatio = 0f;
            bandMidRatio = 0f;
            bandPresenceRatio = 0f;
            bandHighRatio = 0f;
            clarityRatio = 0f;
            syllableEnergyRatio = 0f;
            return;
        }

        int count = magnitudes.Length;
        if (useCenters && binCentersHz.Length < count)
        {
            count = binCentersHz.Length;
        }

        for (int i = 0; i < count; i++)
        {
            float freq = useCenters ? binCentersHz[i] : i * binResolutionHz;
            float mag = magnitudes[i];
            float power = mag * mag;

            if (freq >= 80f && freq < 300f)
            {
                lowPower += power;
            }
            else if (freq >= 300f && freq < 1000f)
            {
                midPower += power;
            }
            else if (freq >= 1000f && freq < 4000f)
            {
                presencePower += power;
            }
            else if (freq >= 4000f && freq < 8000f)
            {
                highPower += power;
            }

            if (freq >= 300f && freq <= 2000f)
            {
                syllablePower += power;
            }
        }

        float totalPower = lowPower + midPower + presencePower + highPower;
        float invTotal = totalPower > 1e-12f ? 1f / totalPower : 0f;

        float lowRatio = lowPower * invTotal;
        float midRatio = midPower * invTotal;
        float presenceRatio = presencePower * invTotal;
        float highRatio = highPower * invTotal;

        if (!_bandSmoothInitialized)
        {
            _bandLowSmooth = lowRatio;
            _bandMidSmooth = midRatio;
            _bandPresenceSmooth = presenceRatio;
            _bandHighSmooth = highRatio;
            _bandSmoothInitialized = true;
        }
        else
        {
            _bandLowSmooth = BandSmoothingAlpha * lowRatio + (1f - BandSmoothingAlpha) * _bandLowSmooth;
            _bandMidSmooth = BandSmoothingAlpha * midRatio + (1f - BandSmoothingAlpha) * _bandMidSmooth;
            _bandPresenceSmooth = BandSmoothingAlpha * presenceRatio + (1f - BandSmoothingAlpha) * _bandPresenceSmooth;
            _bandHighSmooth = BandSmoothingAlpha * highRatio + (1f - BandSmoothingAlpha) * _bandHighSmooth;
        }

        bandLowRatio = _bandLowSmooth;
        bandMidRatio = _bandMidSmooth;
        bandPresenceRatio = _bandPresenceSmooth;
        bandHighRatio = _bandHighSmooth;

        float clarityDenom = bandLowRatio + bandMidRatio;
        clarityRatio = clarityDenom > 1e-6f ? bandPresenceRatio / clarityDenom : 0f;

        syllableEnergyRatio = totalPower > 1e-12f ? syllablePower / totalPower : 0f;
    }

    public void Dispose()
    {
        StopAnalysisThread();
    }

    private sealed class AnalysisConsumer : IDisposable
    {
        private readonly AnalysisOrchestrator _orchestrator;
        public AnalysisCapabilities RequiredCapabilities { get; }

        public AnalysisConsumer(AnalysisCapabilities capabilities, AnalysisOrchestrator orchestrator)
        {
            RequiredCapabilities = capabilities;
            _orchestrator = orchestrator;
        }

        public void Dispose()
        {
            _orchestrator.Unsubscribe(this);
        }
    }
}
