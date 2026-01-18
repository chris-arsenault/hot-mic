using System.Threading;
using HotMic.Core.Dsp;
using HotMic.Core.Dsp.Analysis;
using HotMic.Core.Dsp.Analysis.Formants;
using HotMic.Core.Dsp.Analysis.Pitch;
using HotMic.Core.Dsp.Analysis.Speech;
using HotMic.Core.Dsp.Fft;
using HotMic.Core.Dsp.Filters;
using HotMic.Core.Dsp.Mapping;
using HotMic.Core.Dsp.Spectrogram;
using HotMic.Core.Dsp.Voice;
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
    private const float LpcWindowSeconds = 0.025f;
    private const float LpcGaussianSigma = 0.4f;
    private const int ZoomFftZoomFactor = 8;
    private const float MaxReassignFrameShift = 0.5f;
    private const float MaxReassignBinShift = 0.5f;
    private const float VowelEnergyMinHz = 200f;
    private const float VowelEnergyMaxHz = 1000f;
    private const float VowelEnergyRatioThreshold = 0.15f;

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

    // FFT/Transform
    private FastFft? _fft;
    private ConstantQTransform? _cqt;
    private ZoomFft? _zoomFft;
    private float[] _analysisBufferRaw = Array.Empty<float>();
    private float[] _analysisBufferProcessed = Array.Empty<float>();
    private float[] _hopBuffer = Array.Empty<float>();
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
    private readonly SpectrogramNoiseReducer _noiseReducer = new();
    private readonly SpectrogramHpssProcessor _hpssProcessor = new();
    private readonly SpectrogramSmoother _smoother = new();
    private readonly SpectrogramHarmonicComb _harmonicComb = new();
    private readonly SpectralFeatureExtractor _featureExtractor = new();

    // Filters (not readonly - these are mutable structs)
    private OnePoleHighPass _dcHighPass;
    private BiquadFilter _rumbleHighPass = new();
    private PreEmphasisFilter _preEmphasisFilter;
    private DecimatingFilter _lpcDecimator1 = new();
    private DecimatingFilter _lpcDecimator2 = new();
    private PreEmphasisFilter _lpcPreEmphasisFilter;

    // Pitch detection
    private YinPitchDetector? _yinPitchDetector;
    private PyinPitchDetector? _pyinPitchDetector;
    private AutocorrelationPitchDetector? _autocorrPitchDetector;
    private CepstralPitchDetector? _cepstralPitchDetector;
    private SwipePitchDetector? _swipePitchDetector;
    private readonly VoicingDetector _voicingDetector = new();

    // Formant analysis
    private LpcAnalyzer? _lpcAnalyzer;
    private FormantTracker? _formantTracker;
    private BeamSearchFormantTracker? _beamFormantTracker;
    private float[] _lpcCoefficients = Array.Empty<float>();
    private float[] _lpcInputBuffer = Array.Empty<float>();
    private float[] _lpcDecimateBuffer1 = Array.Empty<float>();
    private float[] _lpcDecimatedBuffer = Array.Empty<float>();
    private float[] _lpcWindowedBuffer = Array.Empty<float>();
    private float[] _lpcWindow = Array.Empty<float>();
    private int _lpcWindowLength;
    private int _lpcWindowSamples;
    private readonly float[] _formantFreqScratch = new float[AnalysisConfiguration.MaxFormants];
    private readonly float[] _formantBwScratch = new float[AnalysisConfiguration.MaxFormants];
    private readonly float[] _harmonicScratch = new float[AnalysisConfiguration.MaxHarmonics];
    private readonly float[] _harmonicMagScratch = new float[AnalysisConfiguration.MaxHarmonics];

    // Speech coach
    private readonly SpeechCoach _speechCoach = new();

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
    private FormantProfile _activeFormantProfile;
    private FormantTrackingPreset _activeFormantPreset;
    private float _activeFormantCeilingHz;
    private int _activeLpcSampleRate;
    private int _activeLpcDecimationStages;
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

    public AnalysisTap? DebugTap { get; set; }

    public IAnalysisResultStore Results => _resultStore;
    public VisualizerSyncHub SyncHub => _syncHub;
    public AnalysisConfiguration Config => _config;
    public int SampleRate => Volatile.Read(ref _sampleRate);
    public bool HasActiveConsumers => Volatile.Read(ref _consumerCount) > 0;

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
        lock (_consumerLock)
        {
            _consumers.Add(consumer);
            Interlocked.Increment(ref _consumerCount);

            // Start analysis thread if this is the first consumer
            if (_consumers.Count == 1)
            {
                StartAnalysisThread();
            }
        }
        return consumer;
    }

    private void Unsubscribe(AnalysisConsumer consumer)
    {
        lock (_consumerLock)
        {
            _consumers.Remove(consumer);
            Interlocked.Decrement(ref _consumerCount);

            // Stop analysis thread if no consumers left
            if (_consumers.Count == 0)
            {
                StopAnalysisThread();
            }
        }
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

    public void Reset()
    {
        _captureBuffer.Clear();
        _resultStore.Clear();
        _syncHub.Reset();
        Volatile.Write(ref _analysisFilled, 0);
        Volatile.Write(ref _frameCounter, 0);
        Volatile.Write(ref _lastDroppedHops, 0);
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
        int pitchFrameCounter = 0;
        int cppFrameCounter = 0;
        float lastPitch = 0f;
        float lastConfidence = 0f;
        float lastCpp = 0f;
        float lastHnr = 0f;

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

            if (_captureBuffer.AvailableRead < _activeHopSize)
            {
                Interlocked.Increment(ref _debugLoopNotEnoughData);
                Thread.Sleep(1);
                continue;
            }

            int read = _captureBuffer.Read(_hopBuffer);
            if (read < _activeHopSize)
            {
                Interlocked.Increment(ref _debugLoopNotEnoughData);
                Thread.Sleep(1);
                continue;
            }

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

            // Pitch and voicing (demand-driven)
            bool needsPitch = capabilities.HasFlag(AnalysisCapabilities.Pitch) ||
                              capabilities.HasFlag(AnalysisCapabilities.Harmonics) ||
                              capabilities.HasFlag(AnalysisCapabilities.Formants);
            bool needsVoicing = capabilities.HasFlag(AnalysisCapabilities.VoicingState) ||
                                capabilities.HasFlag(AnalysisCapabilities.Formants);

            VoicingState voicing = VoicingState.Silence;
            int formantCount = 0;
            int harmonicCount = 0;

            if (needsPitch || needsVoicing)
            {
                AnalyzePitchAndVoicing(ref pitchFrameCounter, ref cppFrameCounter,
                    ref lastPitch, ref lastConfidence, ref lastCpp,
                    _fftMagnitudes, capabilities,
                    out voicing, out formantCount, out harmonicCount);
            }

            // Clarity processing
            lastHnr = ProcessClarity(clarityBins, voicing, lastPitch, lastConfidence);

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

            // Compute spectral features if needed
            float centroid = 0f, slope = 0f, flux = 0f;
            if (capabilities.HasFlag(AnalysisCapabilities.SpectralFeatures))
            {
                _featureExtractor.Compute(_displaySmoothed, clarityBins, out centroid, out slope, out flux);
            }

            // Write overlay data
            _resultStore.WritePitchFrame(frameIndex, lastPitch, lastConfidence, voicing);
            _resultStore.WriteWaveformFrame(frameIndex, waveformMin, waveformMax);
            _resultStore.WriteSpectralFeatures(frameIndex, centroid, slope, flux, lastHnr, lastCpp);

            if (formantCount > 0)
            {
                _resultStore.WriteFormantFrame(frameIndex, _formantFreqScratch, _formantBwScratch);
            }

            if (harmonicCount > 0)
            {
                _resultStore.WriteHarmonicFrame(frameIndex, _harmonicScratch, _harmonicMagScratch, harmonicCount);
            }

            // Speech metrics if needed
            if (capabilities.HasFlag(AnalysisCapabilities.SpeechMetrics))
            {
                var metrics = ProcessSpeechMetrics(waveformMin, waveformMax, lastPitch, lastConfidence,
                    voicing, flux, slope, lastHnr, formantCount, frameId);
                _resultStore.WriteSpeechMetrics(frameIndex, metrics);
            }

            _resultStore.EndWriteFrame(frameId);
            _frameCounter++;
            Interlocked.Increment(ref _debugLoopFramesWritten);

            // Update sync hub
            _syncHub.UpdateViewRange(frameId, _activeFrameCapacity, _sampleRate, _activeHopSize);
        }
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

    private void AnalyzePitchAndVoicing(
        ref int pitchFrameCounter,
        ref int cppFrameCounter,
        ref float lastPitch,
        ref float lastConfidence,
        ref float lastCpp,
        ReadOnlySpan<float> fftMagnitudes,
        AnalysisCapabilities caps,
        out VoicingState voicing,
        out int formantCount,
        out int harmonicCount)
    {
        var pitchAlgorithm = _config.PitchAlgorithm;
        if (_activeTransformType == SpectrogramTransformType.Cqt && pitchAlgorithm == PitchDetectorType.Swipe)
            pitchAlgorithm = PitchDetectorType.Yin;

        bool needsPitch = caps.HasFlag(AnalysisCapabilities.Pitch) ||
                          caps.HasFlag(AnalysisCapabilities.Harmonics) ||
                          caps.HasFlag(AnalysisCapabilities.Formants);
        bool needsVoicing = caps.HasFlag(AnalysisCapabilities.VoicingState) ||
                            caps.HasFlag(AnalysisCapabilities.Formants);
        bool needsFormants = caps.HasFlag(AnalysisCapabilities.Formants);
        bool needsHarmonics = caps.HasFlag(AnalysisCapabilities.Harmonics);
        bool needsCpp = pitchAlgorithm == PitchDetectorType.Cepstral && needsPitch;

        if (!needsPitch)
        {
            pitchFrameCounter = 0;
            lastPitch = 0f;
            lastConfidence = 0f;
        }
        else
        {
            pitchFrameCounter++;
            if (pitchFrameCounter >= 2)
            {
                pitchFrameCounter = 0;
                DetectPitch(pitchAlgorithm, fftMagnitudes, ref lastPitch, ref lastConfidence);
            }
        }

        if (!needsCpp)
        {
            cppFrameCounter = 0;
            lastCpp = 0f;
        }
        else
        {
            cppFrameCounter++;
            if (cppFrameCounter >= 2)
            {
                cppFrameCounter = 0;
                if (_cepstralPitchDetector is not null)
                {
                    var pitch = _cepstralPitchDetector.Detect(_analysisBufferRaw);
                    lastCpp = _cepstralPitchDetector.LastCpp;
                    if (pitchAlgorithm == PitchDetectorType.Cepstral)
                    {
                        lastPitch = pitch.FrequencyHz ?? 0f;
                        lastConfidence = pitch.Confidence;
                    }
                }
            }
        }

        voicing = needsVoicing
            ? _voicingDetector.Detect(_analysisBufferRaw, fftMagnitudes, lastConfidence)
            : VoicingState.Silence;

        formantCount = 0;
        harmonicCount = 0;

        bool vowelLike = false;
        if (needsFormants && voicing == VoicingState.Voiced)
        {
            float vowelMinHz = MathF.Max(VowelEnergyMinHz, _activeFormantPreset.F1MinHz);
            float vowelMaxHz = MathF.Min(VowelEnergyMaxHz, _activeFormantPreset.F1MaxHz);
            if (vowelMaxHz <= vowelMinHz)
            {
                vowelMinHz = VowelEnergyMinHz;
                vowelMaxHz = VowelEnergyMaxHz;
            }

            float ratio = DspUtils.ComputeBandEnergyRatio(fftMagnitudes, _binResolution,
                vowelMinHz, vowelMaxHz);
            vowelLike = ratio >= VowelEnergyRatioThreshold;
        }

        if (needsFormants && voicing == VoicingState.Voiced && vowelLike &&
            _lpcAnalyzer is not null && _beamFormantTracker is not null)
        {
            formantCount = ExtractFormants();
        }
        else if (_beamFormantTracker is not null && needsFormants)
        {
            _beamFormantTracker.MarkNoUpdate();
        }

        if (needsHarmonics && lastPitch > 0f)
        {
            var descriptor = _analysisDescriptor;
            if (descriptor is not null)
            {
                ReadOnlySpan<float> activeMagnitudes = _activeTransformType switch
                {
                    SpectrogramTransformType.Cqt when _cqtMagnitudes.Length > 0 => _cqtMagnitudes,
                    SpectrogramTransformType.ZoomFft when _fftDisplayMagnitudes.Length > 0 => _fftDisplayMagnitudes,
                    _ => fftMagnitudes
                };

                harmonicCount = HarmonicPeakDetector.Detect(activeMagnitudes, descriptor, lastPitch,
                    _harmonicScratch, _harmonicMagScratch);
            }
        }
    }

    private void DetectPitch(PitchDetectorType algorithm, ReadOnlySpan<float> fftMagnitudes,
        ref float pitch, ref float confidence)
    {
        switch (algorithm)
        {
            case PitchDetectorType.Yin when _yinPitchDetector is not null:
                var yinResult = _yinPitchDetector.Detect(_analysisBufferRaw);
                pitch = yinResult.FrequencyHz ?? 0f;
                confidence = yinResult.Confidence;
                break;

            case PitchDetectorType.Pyin when _pyinPitchDetector is not null:
                var pyinResult = _pyinPitchDetector.Detect(_analysisBufferRaw);
                pitch = pyinResult.FrequencyHz ?? 0f;
                confidence = pyinResult.Confidence;
                break;

            case PitchDetectorType.Autocorrelation when _autocorrPitchDetector is not null:
                var autoResult = _autocorrPitchDetector.Detect(_analysisBufferRaw);
                pitch = autoResult.FrequencyHz ?? 0f;
                confidence = autoResult.Confidence;
                break;

            case PitchDetectorType.Swipe when _swipePitchDetector is not null:
                var swipeResult = _swipePitchDetector.Detect(fftMagnitudes);
                pitch = swipeResult.FrequencyHz ?? 0f;
                confidence = swipeResult.Confidence;
                break;
        }
    }

    private int ExtractFormants()
    {
        int bufferLen = _analysisBufferRaw.Length;
        int lpcLen = Math.Min(_lpcWindowSamples, bufferLen);
        int lpcStart = bufferLen - lpcLen;

        _analysisBufferRaw.AsSpan(lpcStart, lpcLen).CopyTo(_lpcInputBuffer.AsSpan(0, lpcLen));

        int decimatedLen = lpcLen;
        if (_activeLpcDecimationStages >= 1)
        {
            _lpcDecimator1.Reset();
            int decimated1Len = lpcLen / 2;
            _lpcDecimator1.ProcessDownsample(_lpcInputBuffer.AsSpan(0, lpcLen),
                _lpcDecimateBuffer1.AsSpan(0, decimated1Len));

            if (_activeLpcDecimationStages == 2)
            {
                _lpcDecimator2.Reset();
                decimatedLen = decimated1Len / 2;
                _lpcDecimator2.ProcessDownsample(_lpcDecimateBuffer1.AsSpan(0, decimated1Len),
                    _lpcDecimatedBuffer.AsSpan(0, decimatedLen));
            }
            else
            {
                decimatedLen = decimated1Len;
                _lpcDecimateBuffer1.AsSpan(0, decimatedLen).CopyTo(_lpcDecimatedBuffer.AsSpan(0, decimatedLen));
            }
        }
        else
        {
            _lpcInputBuffer.AsSpan(0, lpcLen).CopyTo(_lpcDecimatedBuffer.AsSpan(0, lpcLen));
        }

        float preEmphasisAlpha = ComputePreEmphasisAlpha(FormantProfileInfo.DefaultPreEmphasisHz, _activeLpcSampleRate);
        _lpcPreEmphasisFilter.Configure(preEmphasisAlpha);
        _lpcPreEmphasisFilter.Reset();

        if (_lpcWindowLength != decimatedLen)
        {
            WindowFunctions.FillGaussian(_lpcWindow.AsSpan(0, decimatedLen), LpcGaussianSigma);
            _lpcWindowLength = decimatedLen;
        }

        for (int i = 0; i < decimatedLen; i++)
        {
            float emphasized = _lpcPreEmphasisFilter.Process(_lpcDecimatedBuffer[i]);
            _lpcWindowedBuffer[i] = emphasized * _lpcWindow[i];
        }

        if (_lpcAnalyzer!.Compute(_lpcWindowedBuffer.AsSpan(0, decimatedLen), _lpcCoefficients))
        {
            return _beamFormantTracker!.Track(_lpcCoefficients, _activeLpcSampleRate,
                _formantFreqScratch, _formantBwScratch,
                50f, _activeFormantCeilingHz, AnalysisConfiguration.MaxFormants);
        }

        _beamFormantTracker?.MarkNoUpdate();
        return 0;
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
        // Simple linear mapping for now - can be enhanced with frequency scale mapping
        var displayBuffer = new float[_activeDisplayBins];
        float ratio = (float)clarityBins / _activeDisplayBins;

        for (int i = 0; i < _activeDisplayBins; i++)
        {
            int srcIdx = Math.Min((int)(i * ratio), clarityBins - 1);
            displayBuffer[i] = _displaySmoothed[srcIdx];
        }

        _resultStore.WriteSpectrogramFrame(frameIndex, displayBuffer);
    }

    private void ApplyReassignment(long frameId, ReadOnlySpan<float> displayMagnitudes, SpectrogramTransformType transformType)
    {
        // Simplified reassignment - just map to display for now
        // Full reassignment can be ported from VocalSpectrographPlugin if needed
        int frameIndex = (int)(frameId % _activeFrameCapacity);
        MapToDisplayBins(_activeAnalysisBins, frameIndex);
    }

    private SpeechMetricsFrame ProcessSpeechMetrics(
        float waveformMin, float waveformMax,
        float lastPitch, float lastConfidence,
        VoicingState voicing,
        float flux, float slope, float lastHnr,
        int formantCount, long frameId)
    {
        float peakAmplitude = MathF.Max(MathF.Abs(waveformMin), MathF.Abs(waveformMax));
        float energyDb = peakAmplitude > 1e-8f ? 20f * MathF.Log10(peakAmplitude) : -80f;
        float f1Hz = formantCount > 0 ? _formantFreqScratch[0] : 0f;
        float f2Hz = formantCount > 1 ? _formantFreqScratch[1] : 0f;
        float spectralFlatness = ComputeSpectralFlatness(_fftMagnitudes);

        var metrics = _speechCoach.Process(energyDb, lastPitch, lastConfidence, voicing,
            spectralFlatness, flux, slope, lastHnr, f1Hz, f2Hz, frameId);

        return new SpeechMetricsFrame
        {
            SyllableRate = metrics.SyllableRate,
            ArticulationRate = metrics.ArticulationRate,
            PauseRatio = metrics.PauseRatio,
            MonotoneScore = metrics.MonotoneScore,
            ClarityScore = metrics.OverallClarity,
            IntelligibilityScore = metrics.IntelligibilityScore,
            SpeakingState = (byte)metrics.CurrentState,
            SyllableDetected = metrics.SyllableDetected
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
        var formantProfile = _config.FormantProfile;
        float hpfCutoff = _config.HighPassCutoff;
        bool hpfEnabled = _config.HighPassEnabled;
        bool preEmphasis = _config.PreEmphasis;
        int cqtBinsPerOctave = _config.CqtBinsPerOctave;

        FormantTrackingPreset formantPreset = FormantProfileInfo.GetTrackingPreset(formantProfile);
        float formantCeilingHz = formantPreset.FormantCeilingHz;
        int lpcDecimationStages = GetLpcDecimationStages(_sampleRate, formantCeilingHz);
        int lpcSampleRate = _sampleRate;
        for (int i = 0; i < lpcDecimationStages; i++)
            lpcSampleRate /= 2;

        int recommendedLpcOrder = formantPreset.LpcOrder;

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

            // LPC buffers
            _lpcWindowSamples = ComputeLpcWindowSamples(_sampleRate, recommendedLpcOrder, fftSize);
            _lpcInputBuffer = new float[_lpcWindowSamples];
            _lpcDecimateBuffer1 = new float[Math.Max(1, _lpcWindowSamples / 2)];
            _lpcDecimatedBuffer = new float[_lpcWindowSamples];
            _lpcWindowedBuffer = new float[_lpcWindowSamples];
            _lpcWindow = new float[_lpcWindowSamples];
            _lpcWindowLength = 0;

            UpdateAWeighting();
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

            float maxPitch = MathF.Min(_sampleRate * 0.25f, maxHz);
            ConfigurePitchDetectors(maxPitch);
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
                    _lpcInputBuffer = new float[requiredSize];
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
                    _lpcInputBuffer = new float[requiredSize];
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
            _featureExtractor.EnsureCapacity(desiredAnalysisBins);
        }

        if (_activeDisplayBins == 0)
            _activeDisplayBins = AnalysisConfiguration.FixedDisplayBins;

        _activeTransformType = transformType;

        // Formant analysis
        bool lpcOrderChanged = _lpcAnalyzer is null || _lpcAnalyzer.Order != recommendedLpcOrder;
        bool formantProfileChanged = formantProfile != _activeFormantProfile;
        bool lpcSampleRateChanged = lpcSampleRate != _activeLpcSampleRate;
        bool formantConfigChanged = force || formantProfileChanged || lpcSampleRateChanged || lpcOrderChanged;

        if (formantConfigChanged)
        {
            _activeFormantProfile = formantProfile;
            _activeFormantPreset = formantPreset;
            _activeFormantCeilingHz = formantCeilingHz;
            _activeLpcSampleRate = lpcSampleRate;
            _activeLpcDecimationStages = lpcDecimationStages;
            _lpcWindowLength = 0;
            ConfigureLpcAnalyzers(recommendedLpcOrder, formantPreset);
        }
        else
        {
            _beamFormantTracker?.UpdatePreset(formantPreset);
        }

        if (sizeChanged || force)
        {
            float frameSeconds = _activeHopSize / (float)Math.Max(1, _sampleRate);
            _beamFormantTracker?.UpdateFrameSeconds(frameSeconds);
        }

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
    }

    private void ConfigurePitchDetectors(float maxPitch)
    {
        _yinPitchDetector ??= new YinPitchDetector(_sampleRate, _activeFftSize, 50f, maxPitch, 0.15f);
        _yinPitchDetector.Configure(_sampleRate, _activeFftSize, 50f, maxPitch, 0.15f);

        _pyinPitchDetector ??= new PyinPitchDetector(_sampleRate, _activeFftSize, 50f, maxPitch, 0.15f);
        _pyinPitchDetector.Configure(_sampleRate, _activeFftSize, 50f, maxPitch, 0.15f);

        _autocorrPitchDetector ??= new AutocorrelationPitchDetector(_sampleRate, _activeFftSize, 50f, maxPitch, 0.3f);
        _autocorrPitchDetector.Configure(_sampleRate, _activeFftSize, 50f, maxPitch, 0.3f);

        _cepstralPitchDetector ??= new CepstralPitchDetector(_sampleRate, _activeFftSize, 50f, maxPitch, 2f);
        _cepstralPitchDetector.Configure(_sampleRate, _activeFftSize, 50f, maxPitch, 2f);

        _swipePitchDetector ??= new SwipePitchDetector(_sampleRate, _activeFftSize, _activeMinFrequency, _activeMaxFrequency);
        _swipePitchDetector.Configure(_sampleRate, _activeFftSize, _activeMinFrequency, _activeMaxFrequency);
    }

    private void ConfigureLpcAnalyzers(int lpcOrder, FormantTrackingPreset preset)
    {
        _lpcAnalyzer ??= new LpcAnalyzer(lpcOrder);
        _lpcAnalyzer.Configure(lpcOrder);

        _formantTracker ??= new FormantTracker(lpcOrder);
        _formantTracker.Configure(lpcOrder);

        float frameSeconds = _activeHopSize / (float)Math.Max(1, _sampleRate);
        _beamFormantTracker ??= new BeamSearchFormantTracker(lpcOrder, preset, frameSeconds, beamWidth: 5);
        _beamFormantTracker.Configure(lpcOrder, preset, frameSeconds, beamWidth: 5);

        _lpcCoefficients = new float[lpcOrder + 1];
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
            _featureExtractor.UpdateFrequencies(descriptor.BinCentersHz.Span);
            _analysisDescriptor = descriptor;
            _resultStore.SetAnalysisDescriptor(descriptor);
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

    private static int ComputeLpcWindowSamples(int sampleRate, int lpcOrder, int maxWindowSamples)
    {
        int desired = (int)MathF.Round(LpcWindowSeconds * sampleRate);
        int maxWindow = Math.Max(1, maxWindowSamples);
        int minWindow = Math.Min(maxWindow, Math.Max(lpcOrder + 1, 128));
        return Math.Clamp(desired, minWindow, maxWindow);
    }

    private static int GetLpcDecimationStages(int sampleRate, float formantCeilingHz)
    {
        if (sampleRate <= 0 || formantCeilingHz <= 0f)
            return 0;

        float requiredRate = formantCeilingHz * 2f;
        int stages = 0;
        float currentRate = sampleRate;
        while (stages < 2 && currentRate / 2f >= requiredRate)
        {
            currentRate /= 2f;
            stages++;
        }
        return stages;
    }

    private static float ComputePreEmphasisAlpha(float cutoffHz, int sampleRate)
    {
        if (cutoffHz <= 0f || sampleRate <= 0)
            return 0f;
        return MathF.Exp(-2f * MathF.PI * cutoffHz / sampleRate);
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
