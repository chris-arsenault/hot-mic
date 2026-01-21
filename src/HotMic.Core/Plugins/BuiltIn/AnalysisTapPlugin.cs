using System.Diagnostics;
using System.Threading;
using HotMic.Core.Analysis;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class AnalysisTapPlugin : IPlugin, IAnalysisSignalProducer, IAnalysisSignalBlocker
{
    private static readonly AnalysisTapSignalInfo[] SignalInfos =
    [
        new AnalysisTapSignalInfo(AnalysisSignalId.SpeechPresence, "Speech Presence"),
        new AnalysisTapSignalInfo(AnalysisSignalId.VoicingScore, "Voicing Score"),
        new AnalysisTapSignalInfo(AnalysisSignalId.VoicingState, "Voicing State"),
        new AnalysisTapSignalInfo(AnalysisSignalId.FricativeActivity, "Fricative Activity"),
        new AnalysisTapSignalInfo(AnalysisSignalId.SibilanceEnergy, "Sibilance Energy"),
        new AnalysisTapSignalInfo(AnalysisSignalId.OnsetFluxHigh, "Onset Flux High"),
        new AnalysisTapSignalInfo(AnalysisSignalId.PitchHz, "Pitch (Hz)"),
        new AnalysisTapSignalInfo(AnalysisSignalId.PitchConfidence, "Pitch Confidence"),
        new AnalysisTapSignalInfo(AnalysisSignalId.SpectralFlux, "Spectral Flux"),
        new AnalysisTapSignalInfo(AnalysisSignalId.HnrDb, "HNR (dB)")
    ];

    private readonly AnalysisTapMode[] _modes = new AnalysisTapMode[(int)AnalysisSignalId.Count];
    private readonly AnalysisSignalSource[] _sources = new AnalysisSignalSource[(int)AnalysisSignalId.Count];
    private readonly int[] _hasSource = new int[(int)AnalysisSignalId.Count];
    private readonly float[] _meterValues = new float[(int)AnalysisSignalId.Count];
    private readonly int[] _producerSnapshot = new int[(int)AnalysisSignalId.Count];
    private AnalysisSignalMask _generatedMask;
    private AnalysisSignalMask _blockedMask;

    private readonly AnalysisSignalProcessor _processor = new();
    private int _profilingEnabled;
    private long _lastResolveTicks;
    private long _maxResolveTicks;
    private long _lastCaptureTicks;
    private long _maxCaptureTicks;
    private int _pitchAlgorithmRaw = -1;
    private float _speechPresenceGateValue;
    private int _speechPresenceGateEnabled;
    private int _speechPresenceHasSource;
    private int _speechPresenceModeRaw;
    private int _nonFiniteSignalMask;

    public AnalysisTapPlugin()
    {
        Parameters = BuildParameters();
        for (int i = 0; i < _modes.Length; i++)
        {
            _modes[i] = AnalysisTapMode.Generate;
        }
        UpdateSignalMask();
    }

    public string Id => "builtin:analysis-tap";

    public string Name => "Analysis Tap";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public AnalysisSignalMask ProducedSignals => _generatedMask;

    public AnalysisSignalMask BlockedSignals => _blockedMask;

    public int SampleRate { get; private set; }

    /// <summary>
    /// Gets the latest profiling snapshot for analysis tap processing.
    /// </summary>
    public AnalysisTapProfilingSnapshot ProfilingSnapshot => BuildProfilingSnapshot();

    public AnalysisTapMode GetMode(AnalysisSignalId signal)
    {
        return _modes[(int)signal];
    }

    public bool HasSource(AnalysisSignalId signal)
    {
        return Volatile.Read(ref _hasSource[(int)signal]) != 0;
    }

    public float GetValue(AnalysisSignalId signal)
    {
        return Volatile.Read(ref _meterValues[(int)signal]);
    }

    public void Initialize(int sampleRate, int blockSize)
    {
        SampleRate = sampleRate;
        _processor.Configure(sampleRate, blockSize, AnalysisSignalProcessorSettings.Default);
        _processor.Reset();
        Array.Clear(_meterValues, 0, _meterValues.Length);
        Array.Clear(_hasSource, 0, _hasSource.Length);
        for (int i = 0; i < _sources.Length; i++)
        {
            _sources[i] = AnalysisSignalSource.Empty;
        }
        Interlocked.Exchange(ref _nonFiniteSignalMask, 0);
        ResetProfiling();
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        UpdatePitchAlgorithm(context.AnalysisCapture);
        UpdateProfilingEnabled(context.ProfilingEnabled);
        bool profilingEnabled = Volatile.Read(ref _profilingEnabled) != 0;

        int count = (int)AnalysisSignalId.Count;
        for (int i = 0; i < count; i++)
        {
            var signal = (AnalysisSignalId)i;
            bool hasSource = context.TryGetAnalysisSignalSource(signal, out var source);
            _sources[i] = source;
            Volatile.Write(ref _hasSource[i], hasSource ? 1 : 0);
        }

        int lastIndex = buffer.Length - 1;
        long sampleTime = context.SampleTime;
        UpdateSpeechPresenceGate(sampleTime, lastIndex);

        if (IsBypassed)
        {
            return;
        }

        AnalysisSignalMask computeMask = _generatedMask & context.RequestedSignals;
        if (computeMask != AnalysisSignalMask.None)
        {
            _processor.ProcessBlock(buffer, context.SampleTime, context.AnalysisSignalWriter, computeMask);
        }

        long resolveStart = 0;
        if (profilingEnabled)
        {
            resolveStart = Stopwatch.GetTimestamp();
        }

        for (int i = 0; i < count; i++)
        {
            var signal = (AnalysisSignalId)i;
            float generated = _processor.GetLastValue(signal);
            float value = ResolveMeterValue(_modes[i], HasSource(signal), _sources[i], generated, sampleTime, lastIndex);
            if (!float.IsFinite(value))
            {
                SetSignalMask(ref _nonFiniteSignalMask, 1 << i);
                value = 0f;
            }
            Volatile.Write(ref _meterValues[i], value);
        }

        if (profilingEnabled)
        {
            long resolveTicks = Stopwatch.GetTimestamp() - resolveStart;
            RecordProfiling(ref _lastResolveTicks, ref _maxResolveTicks, resolveTicks);
        }

        AnalysisSignalMask producedMask = _generatedMask & context.RequestedSignals;
        context.CopyAnalysisSignalProducers(_producerSnapshot);
        if (producedMask != AnalysisSignalMask.None)
        {
            UpdateProducerSnapshot(_producerSnapshot, producedMask, context.SlotIndex);
        }
        if (_blockedMask != AnalysisSignalMask.None)
        {
            ApplySignalBlocks(_producerSnapshot, _blockedMask);
        }

        long captureStart = 0;
        if (profilingEnabled)
        {
            captureStart = Stopwatch.GetTimestamp();
        }

        context.AnalysisCapture?.Capture(buffer, context.SampleClock, context.SampleTime, context.ChannelId,
            context.AnalysisSignalBus, _producerSnapshot, AnalysisCaptureSource.Plugin);

        if (profilingEnabled)
        {
            long captureTicks = Stopwatch.GetTimestamp() - captureStart;
            RecordProfiling(ref _lastCaptureTicks, ref _maxCaptureTicks, captureTicks);
        }
    }

    public void Process(Span<float> buffer)
    {
        // Tap does not modify audio; this path is kept for interface parity.
    }

    public void SetParameter(int index, float value)
    {
        if ((uint)index >= (uint)_modes.Length)
        {
            return;
        }

        _modes[index] = ModeFromValue(value);
        UpdateSignalMask();
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * _modes.Length];
        for (int i = 0; i < _modes.Length; i++)
        {
            float stored = ValueFromMode(_modes[i]);
            Buffer.BlockCopy(BitConverter.GetBytes(stored), 0, bytes, i * sizeof(float), sizeof(float));
        }

        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float))
        {
            return;
        }

        int count = Math.Min(state.Length / sizeof(float), _modes.Length);
        for (int i = 0; i < count; i++)
        {
            _modes[i] = ModeFromValue(BitConverter.ToSingle(state, i * sizeof(float)));
        }

        UpdateSignalMask();
    }

    public void Dispose()
    {
    }

    public static IReadOnlyList<AnalysisTapSignalInfo> Signals => SignalInfos;

    /// <summary>
    /// Returns a bitmask of signals that produced non-finite meter values since the last call.
    /// </summary>
    public int ConsumeNonFiniteSignalMask()
    {
        return Interlocked.Exchange(ref _nonFiniteSignalMask, 0);
    }

    private AnalysisTapProfilingSnapshot BuildProfilingSnapshot()
    {
        var processor = _processor.GetProfilingSnapshot();
        var pitchProfile = _processor.GetPitchProfilingSnapshot();
        return new AnalysisTapProfilingSnapshot(
            processorLastTicks: processor.LastTotalTicks,
            processorMaxTicks: processor.MaxTotalTicks,
            preprocessLastTicks: processor.LastPreprocessTicks,
            preprocessMaxTicks: processor.MaxPreprocessTicks,
            fftLastTicks: processor.LastFftTicks,
            fftMaxTicks: processor.MaxFftTicks,
            pitchLastTicks: processor.LastPitchTicks,
            pitchMaxTicks: processor.MaxPitchTicks,
            voicingLastTicks: processor.LastVoicingTicks,
            voicingMaxTicks: processor.MaxVoicingTicks,
            featureLastTicks: processor.LastFeatureTicks,
            featureMaxTicks: processor.MaxFeatureTicks,
            writeLastTicks: processor.LastWriteTicks,
            writeMaxTicks: processor.MaxWriteTicks,
            resolveLastTicks: Interlocked.Read(ref _lastResolveTicks),
            resolveMaxTicks: Interlocked.Read(ref _maxResolveTicks),
            captureLastTicks: Interlocked.Read(ref _lastCaptureTicks),
            captureMaxTicks: Interlocked.Read(ref _maxCaptureTicks),
            pitchProfile: pitchProfile,
            speechPresenceMode: (AnalysisTapMode)Volatile.Read(ref _speechPresenceModeRaw),
            speechPresenceHasSource: Volatile.Read(ref _speechPresenceHasSource) != 0,
            speechPresenceGateEnabled: Volatile.Read(ref _speechPresenceGateEnabled) != 0,
            speechPresenceGateValue: Volatile.Read(ref _speechPresenceGateValue));
    }

    private void UpdateProfilingEnabled(bool enabled)
    {
        int value = enabled ? 1 : 0;
        int prior = Interlocked.Exchange(ref _profilingEnabled, value);
        if (prior == value)
        {
            return;
        }

        _processor.SetProfilingEnabled(enabled);
        ResetProfiling();
    }

    private void UpdatePitchAlgorithm(AnalysisCaptureLink? capture)
    {
        var orchestrator = capture?.Orchestrator;
        if (orchestrator is null)
        {
            return;
        }

        var config = orchestrator.Config;
        var algorithm = config.PitchAlgorithm;
        if (config.TransformType == SpectrogramTransformType.Cqt && algorithm == PitchDetectorType.Swipe)
        {
            algorithm = PitchDetectorType.Yin;
        }

        int raw = (int)algorithm;
        int prior = Interlocked.Exchange(ref _pitchAlgorithmRaw, raw);
        if (prior != raw)
        {
            _processor.SetPitchAlgorithm(algorithm);
        }
    }

    private void UpdateSpeechPresenceGate(long sampleTime, int lastIndex)
    {
        int index = (int)AnalysisSignalId.SpeechPresence;
        var mode = _modes[index];
        Volatile.Write(ref _speechPresenceModeRaw, (int)mode);
        bool hasSource = HasSource(AnalysisSignalId.SpeechPresence);
        Volatile.Write(ref _speechPresenceHasSource, hasSource ? 1 : 0);
        float gateValue = 0f;
        bool gateEnabled = false;
        switch (mode)
        {
            case AnalysisTapMode.UseExisting:
                float presence = hasSource ? _sources[index].ReadSample(sampleTime + lastIndex) : 0f;
                if (!float.IsFinite(presence))
                {
                    presence = 0f;
                }
                _processor.SetExternalSpeechPresenceGate(presence);
                _processor.SetGeneratedSpeechPresenceGateEnabled(false);
                gateValue = presence;
                gateEnabled = true;
                break;
            case AnalysisTapMode.Generate:
                _processor.ClearExternalSpeechPresenceGate();
                _processor.SetGeneratedSpeechPresenceGateEnabled(true);
                gateValue = _processor.GetLastValue(AnalysisSignalId.SpeechPresence);
                if (!float.IsFinite(gateValue))
                {
                    gateValue = 0f;
                }
                gateEnabled = true;
                break;
            default:
                _processor.ClearExternalSpeechPresenceGate();
                _processor.SetGeneratedSpeechPresenceGateEnabled(false);
                break;
        }

        Volatile.Write(ref _speechPresenceGateValue, gateValue);
        Volatile.Write(ref _speechPresenceGateEnabled, gateEnabled ? 1 : 0);
    }

    private void ResetProfiling()
    {
        Interlocked.Exchange(ref _lastResolveTicks, 0);
        Interlocked.Exchange(ref _maxResolveTicks, 0);
        Interlocked.Exchange(ref _lastCaptureTicks, 0);
        Interlocked.Exchange(ref _maxCaptureTicks, 0);
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

    private static PluginParameter[] BuildParameters()
    {
        var parameters = new PluginParameter[SignalInfos.Length];
        for (int i = 0; i < SignalInfos.Length; i++)
        {
            var info = SignalInfos[i];
            parameters[i] = new PluginParameter
            {
                Index = (int)info.Signal,
                Name = info.Label,
                MinValue = 0f,
                MaxValue = 2f,
                DefaultValue = 1f,
                Unit = string.Empty
            };
        }

        return parameters;
    }

    private static void SetSignalMask(ref int location, int mask)
    {
        int current = Volatile.Read(ref location);
        while (true)
        {
            int updated = current | mask;
            int prior = Interlocked.CompareExchange(ref location, updated, current);
            if (prior == current)
            {
                break;
            }

            current = prior;
        }
    }

    private void UpdateSignalMask()
    {
        AnalysisSignalMask generated = AnalysisSignalMask.None;
        AnalysisSignalMask blocked = AnalysisSignalMask.None;

        for (int i = 0; i < _modes.Length; i++)
        {
            var signalMask = (AnalysisSignalMask)(1 << i);
            switch (_modes[i])
            {
                case AnalysisTapMode.Generate:
                    generated |= signalMask;
                    break;
                case AnalysisTapMode.Disabled:
                    blocked |= signalMask;
                    break;
            }
        }

        _generatedMask = generated;
        _blockedMask = blocked;
    }

    private static AnalysisTapMode ModeFromValue(float value)
    {
        if (value < 0.5f)
        {
            return AnalysisTapMode.Disabled;
        }
        if (value < 1.5f)
        {
            return AnalysisTapMode.Generate;
        }

        return AnalysisTapMode.UseExisting;
    }

    private static float ValueFromMode(AnalysisTapMode mode)
    {
        return mode switch
        {
            AnalysisTapMode.Disabled => 0f,
            AnalysisTapMode.Generate => 1f,
            AnalysisTapMode.UseExisting => 2f,
            _ => 1f
        };
    }

    private static float ResolveMeterValue(AnalysisTapMode mode, bool hasSource, in AnalysisSignalSource source, float generatedValue, long sampleTime, int lastIndex)
    {
        if (sampleTime < 0 || lastIndex < 0)
        {
            return 0f;
        }

        return mode switch
        {
            AnalysisTapMode.Generate => generatedValue,
            AnalysisTapMode.UseExisting => hasSource ? source.ReadSample(sampleTime + lastIndex) : 0f,
            _ => 0f
        };
    }

    private static void UpdateProducerSnapshot(int[] producers, AnalysisSignalMask producedSignals, int slotIndex)
    {
        int mask = (int)producedSignals;
        for (int i = 0; i < producers.Length; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                producers[i] = slotIndex;
            }
        }
    }

    private static void ApplySignalBlocks(int[] producers, AnalysisSignalMask blockedSignals)
    {
        int mask = (int)blockedSignals;
        for (int i = 0; i < producers.Length; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                producers[i] = -1;
            }
        }
    }

    public readonly record struct AnalysisTapSignalInfo(AnalysisSignalId Signal, string Label);
}

/// <summary>
/// Snapshot of analysis tap profiling timings (Stopwatch ticks).
/// </summary>
public readonly struct AnalysisTapProfilingSnapshot
{
    /// <summary>
    /// Initializes a new profiling snapshot.
    /// </summary>
    public AnalysisTapProfilingSnapshot(
        long processorLastTicks,
        long processorMaxTicks,
        long preprocessLastTicks,
        long preprocessMaxTicks,
        long fftLastTicks,
        long fftMaxTicks,
        long pitchLastTicks,
        long pitchMaxTicks,
        long voicingLastTicks,
        long voicingMaxTicks,
        long featureLastTicks,
        long featureMaxTicks,
        long writeLastTicks,
        long writeMaxTicks,
        long resolveLastTicks,
        long resolveMaxTicks,
        long captureLastTicks,
        long captureMaxTicks,
        PitchProfilingSnapshot pitchProfile,
        AnalysisTapMode speechPresenceMode,
        bool speechPresenceHasSource,
        bool speechPresenceGateEnabled,
        float speechPresenceGateValue)
    {
        ProcessorLastTicks = processorLastTicks;
        ProcessorMaxTicks = processorMaxTicks;
        PreprocessLastTicks = preprocessLastTicks;
        PreprocessMaxTicks = preprocessMaxTicks;
        FftLastTicks = fftLastTicks;
        FftMaxTicks = fftMaxTicks;
        PitchLastTicks = pitchLastTicks;
        PitchMaxTicks = pitchMaxTicks;
        VoicingLastTicks = voicingLastTicks;
        VoicingMaxTicks = voicingMaxTicks;
        FeatureLastTicks = featureLastTicks;
        FeatureMaxTicks = featureMaxTicks;
        WriteLastTicks = writeLastTicks;
        WriteMaxTicks = writeMaxTicks;
        ResolveLastTicks = resolveLastTicks;
        ResolveMaxTicks = resolveMaxTicks;
        CaptureLastTicks = captureLastTicks;
        CaptureMaxTicks = captureMaxTicks;
        PitchProfile = pitchProfile;
        SpeechPresenceMode = speechPresenceMode;
        SpeechPresenceHasSource = speechPresenceHasSource;
        SpeechPresenceGateEnabled = speechPresenceGateEnabled;
        SpeechPresenceGateValue = speechPresenceGateValue;
    }

    /// <summary>Gets the last processor total time in stopwatch ticks.</summary>
    public long ProcessorLastTicks { get; }

    /// <summary>Gets the max processor total time in stopwatch ticks.</summary>
    public long ProcessorMaxTicks { get; }

    /// <summary>Gets the last preprocessing time in stopwatch ticks.</summary>
    public long PreprocessLastTicks { get; }

    /// <summary>Gets the max preprocessing time in stopwatch ticks.</summary>
    public long PreprocessMaxTicks { get; }

    /// <summary>Gets the last FFT time in stopwatch ticks.</summary>
    public long FftLastTicks { get; }

    /// <summary>Gets the max FFT time in stopwatch ticks.</summary>
    public long FftMaxTicks { get; }

    /// <summary>Gets the last pitch detection time in stopwatch ticks.</summary>
    public long PitchLastTicks { get; }

    /// <summary>Gets the max pitch detection time in stopwatch ticks.</summary>
    public long PitchMaxTicks { get; }

    /// <summary>Gets the last voicing time in stopwatch ticks.</summary>
    public long VoicingLastTicks { get; }

    /// <summary>Gets the max voicing time in stopwatch ticks.</summary>
    public long VoicingMaxTicks { get; }

    /// <summary>Gets the last spectral feature time in stopwatch ticks.</summary>
    public long FeatureLastTicks { get; }

    /// <summary>Gets the max spectral feature time in stopwatch ticks.</summary>
    public long FeatureMaxTicks { get; }

    /// <summary>Gets the last signal write time in stopwatch ticks.</summary>
    public long WriteLastTicks { get; }

    /// <summary>Gets the max signal write time in stopwatch ticks.</summary>
    public long WriteMaxTicks { get; }

    /// <summary>Gets the last meter resolve time in stopwatch ticks.</summary>
    public long ResolveLastTicks { get; }

    /// <summary>Gets the max meter resolve time in stopwatch ticks.</summary>
    public long ResolveMaxTicks { get; }

    /// <summary>Gets the last capture time in stopwatch ticks.</summary>
    public long CaptureLastTicks { get; }

    /// <summary>Gets the max capture time in stopwatch ticks.</summary>
    public long CaptureMaxTicks { get; }

    /// <summary>Gets the pitch detector profiling snapshot.</summary>
    public PitchProfilingSnapshot PitchProfile { get; }

    /// <summary>Gets the Speech Presence mode for the analysis tap.</summary>
    public AnalysisTapMode SpeechPresenceMode { get; }

    /// <summary>Gets whether Speech Presence has an upstream source.</summary>
    public bool SpeechPresenceHasSource { get; }

    /// <summary>Gets whether the external Speech Presence gate is enabled.</summary>
    public bool SpeechPresenceGateEnabled { get; }

    /// <summary>Gets the last Speech Presence gate value.</summary>
    public float SpeechPresenceGateValue { get; }
}
