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
        new AnalysisTapSignalInfo(AnalysisSignalId.OnsetFluxHigh, "Onset Flux HF"),
        new AnalysisTapSignalInfo(AnalysisSignalId.PitchHz, "Pitch (Hz)"),
        new AnalysisTapSignalInfo(AnalysisSignalId.PitchConfidence, "Pitch Confidence"),
        new AnalysisTapSignalInfo(AnalysisSignalId.FormantF1Hz, "Formant F1 (Hz)"),
        new AnalysisTapSignalInfo(AnalysisSignalId.FormantF2Hz, "Formant F2 (Hz)"),
        new AnalysisTapSignalInfo(AnalysisSignalId.FormantF3Hz, "Formant F3 (Hz)"),
        new AnalysisTapSignalInfo(AnalysisSignalId.FormantConfidence, "Formant Confidence"),
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
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        int count = (int)AnalysisSignalId.Count;
        for (int i = 0; i < count; i++)
        {
            var signal = (AnalysisSignalId)i;
            bool hasSource = context.TryGetAnalysisSignalSource(signal, out var source);
            _sources[i] = source;
            Volatile.Write(ref _hasSource[i], hasSource ? 1 : 0);
        }

        if (IsBypassed)
        {
            return;
        }

        AnalysisSignalMask generatedMask = _generatedMask & context.RequestedSignals;
        if (generatedMask != AnalysisSignalMask.None)
        {
            _processor.ProcessBlock(buffer, context.SampleTime, context.AnalysisSignalWriter, generatedMask);
        }

        int lastIndex = buffer.Length - 1;
        long sampleTime = context.SampleTime;

        for (int i = 0; i < count; i++)
        {
            var signal = (AnalysisSignalId)i;
            float generated = _processor.GetLastValue(signal);
            float value = ResolveMeterValue(_modes[i], HasSource(signal), _sources[i], generated, sampleTime, lastIndex);
            Volatile.Write(ref _meterValues[i], value);
        }

        context.CopyAnalysisSignalProducers(_producerSnapshot);
        if (generatedMask != AnalysisSignalMask.None)
        {
            UpdateProducerSnapshot(_producerSnapshot, generatedMask, context.SlotIndex);
        }
        if (_blockedMask != AnalysisSignalMask.None)
        {
            ApplySignalBlocks(_producerSnapshot, _blockedMask);
        }

        context.AnalysisCapture?.Capture(buffer, context.SampleClock, context.SampleTime, context.ChannelId,
            context.AnalysisSignalBus, _producerSnapshot, AnalysisCaptureSource.Plugin);
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
