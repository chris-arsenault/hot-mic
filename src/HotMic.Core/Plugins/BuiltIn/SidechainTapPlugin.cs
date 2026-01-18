namespace HotMic.Core.Plugins.BuiltIn;

public sealed class SidechainTapPlugin : IPlugin, ISidechainProducer, ISidechainSignalBlocker
{
    public const int SpeechPresenceIndex = 0;
    public const int VoicedProbabilityIndex = 1;
    public const int UnvoicedEnergyIndex = 2;
    public const int SibilanceEnergyIndex = 3;

    private SidechainTapMode _speechMode = SidechainTapMode.Generate;
    private SidechainTapMode _voicedMode = SidechainTapMode.Generate;
    private SidechainTapMode _unvoicedMode = SidechainTapMode.Generate;
    private SidechainTapMode _sibilanceMode = SidechainTapMode.Generate;
    private SidechainSignalMask _generatedMask;
    private SidechainSignalMask _blockedMask;

    private readonly SidechainTapProcessor _processor = new();

    // Metering - expose current values from the processor
    private float _meterSpeechPresence;
    private float _meterVoicedProbability;
    private float _meterUnvoicedEnergy;
    private float _meterSibilanceEnergy;
    private int _speechHasSource;
    private int _voicedHasSource;
    private int _unvoicedHasSource;
    private int _sibilanceHasSource;

    public SidechainTapPlugin()
    {
        Parameters =
        [
            new PluginParameter { Index = SpeechPresenceIndex, Name = "Speech Presence", MinValue = 0f, MaxValue = 2f, DefaultValue = 1f, Unit = string.Empty },
            new PluginParameter { Index = VoicedProbabilityIndex, Name = "Voiced Probability", MinValue = 0f, MaxValue = 2f, DefaultValue = 1f, Unit = string.Empty },
            new PluginParameter { Index = UnvoicedEnergyIndex, Name = "Unvoiced Energy", MinValue = 0f, MaxValue = 2f, DefaultValue = 1f, Unit = string.Empty },
            new PluginParameter { Index = SibilanceEnergyIndex, Name = "Sibilance Energy", MinValue = 0f, MaxValue = 2f, DefaultValue = 1f, Unit = string.Empty }
        ];

        UpdateSignalMask();
    }

    public string Id => "builtin:sidechain-tap";

    public string Name => "Sidechain Tap";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public SidechainSignalMask ProducedSignals => _generatedMask;

    public SidechainSignalMask BlockedSignals => _blockedMask;

    public SidechainTapMode SpeechPresenceMode => _speechMode;
    public SidechainTapMode VoicedProbabilityMode => _voicedMode;
    public SidechainTapMode UnvoicedEnergyMode => _unvoicedMode;
    public SidechainTapMode SibilanceEnergyMode => _sibilanceMode;
    public bool SpeechPresenceHasSource => Volatile.Read(ref _speechHasSource) != 0;
    public bool VoicedProbabilityHasSource => Volatile.Read(ref _voicedHasSource) != 0;
    public bool UnvoicedEnergyHasSource => Volatile.Read(ref _unvoicedHasSource) != 0;
    public bool SibilanceEnergyHasSource => Volatile.Read(ref _sibilanceHasSource) != 0;
    public int SampleRate { get; private set; }

    public void Initialize(int sampleRate, int blockSize)
    {
        SampleRate = sampleRate;
        _processor.Configure(sampleRate, blockSize);
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        bool hasSpeechSource = context.TryGetSidechainSource(SidechainSignalId.SpeechPresence, out var speechSource);
        bool hasVoicedSource = context.TryGetSidechainSource(SidechainSignalId.VoicedProbability, out var voicedSource);
        bool hasUnvoicedSource = context.TryGetSidechainSource(SidechainSignalId.UnvoicedEnergy, out var unvoicedSource);
        bool hasSibilanceSource = context.TryGetSidechainSource(SidechainSignalId.SibilanceEnergy, out var sibilanceSource);

        Volatile.Write(ref _speechHasSource, hasSpeechSource ? 1 : 0);
        Volatile.Write(ref _voicedHasSource, hasVoicedSource ? 1 : 0);
        Volatile.Write(ref _unvoicedHasSource, hasUnvoicedSource ? 1 : 0);
        Volatile.Write(ref _sibilanceHasSource, hasSibilanceSource ? 1 : 0);

        if (IsBypassed)
        {
            return;
        }

        var generatedMask = _generatedMask;
        if (generatedMask != SidechainSignalMask.None)
        {
            _processor.ProcessBlock(buffer, context.SampleTime, context.SidechainWriter, generatedMask);
        }

        long sampleTime = context.SampleTime;
        int lastIndex = buffer.Length - 1;

        _meterSpeechPresence = ResolveMeterValue(_speechMode, hasSpeechSource, speechSource, _processor.GetSpeechPresence(), sampleTime, lastIndex);
        _meterVoicedProbability = ResolveMeterValue(_voicedMode, hasVoicedSource, voicedSource, _processor.GetVoicedProbability(), sampleTime, lastIndex);
        _meterUnvoicedEnergy = ResolveMeterValue(_unvoicedMode, hasUnvoicedSource, unvoicedSource, _processor.GetUnvoicedEnergy(), sampleTime, lastIndex);
        _meterSibilanceEnergy = ResolveMeterValue(_sibilanceMode, hasSibilanceSource, sibilanceSource, _processor.GetSibilanceEnergy(), sampleTime, lastIndex);
    }

    /// <summary>Gets the current speech presence level (0-1).</summary>
    public float GetSpeechPresence() => Volatile.Read(ref _meterSpeechPresence);

    /// <summary>Gets the current voiced probability (0-1).</summary>
    public float GetVoicedProbability() => Volatile.Read(ref _meterVoicedProbability);

    /// <summary>Gets the current unvoiced energy (0-1).</summary>
    public float GetUnvoicedEnergy() => Volatile.Read(ref _meterUnvoicedEnergy);

    /// <summary>Gets the current sibilance energy (0-1).</summary>
    public float GetSibilanceEnergy() => Volatile.Read(ref _meterSibilanceEnergy);

    public void Process(Span<float> buffer)
    {
        // Tap does not modify audio; this path is kept for interface parity.
    }

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case SpeechPresenceIndex:
                _speechMode = ModeFromValue(value);
                break;
            case VoicedProbabilityIndex:
                _voicedMode = ModeFromValue(value);
                break;
            case UnvoicedEnergyIndex:
                _unvoicedMode = ModeFromValue(value);
                break;
            case SibilanceEnergyIndex:
                _sibilanceMode = ModeFromValue(value);
                break;
        }

        UpdateSignalMask();
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 4];
        Buffer.BlockCopy(BitConverter.GetBytes(ValueFromMode(_speechMode)), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(ValueFromMode(_voicedMode)), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(ValueFromMode(_unvoicedMode)), 0, bytes, 8, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(ValueFromMode(_sibilanceMode)), 0, bytes, 12, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float))
        {
            return;
        }

        _speechMode = ModeFromValue(BitConverter.ToSingle(state, 0));
        if (state.Length >= sizeof(float) * 2)
        {
            _voicedMode = ModeFromValue(BitConverter.ToSingle(state, 4));
        }
        if (state.Length >= sizeof(float) * 3)
        {
            _unvoicedMode = ModeFromValue(BitConverter.ToSingle(state, 8));
        }
        if (state.Length >= sizeof(float) * 4)
        {
            _sibilanceMode = ModeFromValue(BitConverter.ToSingle(state, 12));
        }

        UpdateSignalMask();
    }

    public void Dispose()
    {
    }

    private void UpdateSignalMask()
    {
        SidechainSignalMask generated = SidechainSignalMask.None;
        SidechainSignalMask blocked = SidechainSignalMask.None;

        UpdateModeMasks(_speechMode, SidechainSignalMask.SpeechPresence, ref generated, ref blocked);
        UpdateModeMasks(_voicedMode, SidechainSignalMask.VoicedProbability, ref generated, ref blocked);
        UpdateModeMasks(_unvoicedMode, SidechainSignalMask.UnvoicedEnergy, ref generated, ref blocked);
        UpdateModeMasks(_sibilanceMode, SidechainSignalMask.SibilanceEnergy, ref generated, ref blocked);

        _generatedMask = generated;
        _blockedMask = blocked;
    }

    private static void UpdateModeMasks(SidechainTapMode mode, SidechainSignalMask signal, ref SidechainSignalMask generated, ref SidechainSignalMask blocked)
    {
        switch (mode)
        {
            case SidechainTapMode.Generate:
                generated |= signal;
                break;
            case SidechainTapMode.Disabled:
                blocked |= signal;
                break;
        }
    }

    private static SidechainTapMode ModeFromValue(float value)
    {
        if (value < 0.5f)
        {
            return SidechainTapMode.Disabled;
        }
        if (value < 1.5f)
        {
            return SidechainTapMode.Generate;
        }

        return SidechainTapMode.UseExisting;
    }

    private static float ValueFromMode(SidechainTapMode mode)
    {
        return mode switch
        {
            SidechainTapMode.Disabled => 0f,
            SidechainTapMode.Generate => 1f,
            SidechainTapMode.UseExisting => 2f,
            _ => 1f
        };
    }

    private static float ResolveMeterValue(SidechainTapMode mode, bool hasSource, in SidechainSource source, float generatedValue, long sampleTime, int lastIndex)
    {
        if (sampleTime < 0 || lastIndex < 0)
        {
            return 0f;
        }

        float value = mode switch
        {
            SidechainTapMode.Generate => generatedValue,
            SidechainTapMode.UseExisting => hasSource ? source.ReadSample(sampleTime + lastIndex) : 0f,
            _ => 0f
        };

        return Math.Clamp(value, 0f, 1f);
    }
}
