namespace HotMic.Core.Plugins.BuiltIn;

public sealed class SidechainTapPlugin : IPlugin, ISidechainProducer
{
    public const int SpeechPresenceIndex = 0;
    public const int VoicedProbabilityIndex = 1;
    public const int UnvoicedEnergyIndex = 2;
    public const int SibilanceEnergyIndex = 3;

    private float _speechEnabled = 1f;
    private float _voicedEnabled = 1f;
    private float _unvoicedEnabled = 1f;
    private float _sibilanceEnabled = 1f;
    private SidechainSignalMask _signalMask;

    private readonly SidechainTapProcessor _processor = new();

    // Metering - expose current values from the processor
    private float _meterSpeechPresence;
    private float _meterVoicedProbability;
    private float _meterUnvoicedEnergy;
    private float _meterSibilanceEnergy;

    public SidechainTapPlugin()
    {
        Parameters =
        [
            new PluginParameter { Index = SpeechPresenceIndex, Name = "Speech Presence", MinValue = 0f, MaxValue = 1f, DefaultValue = 1f, Unit = string.Empty },
            new PluginParameter { Index = VoicedProbabilityIndex, Name = "Voiced Probability", MinValue = 0f, MaxValue = 1f, DefaultValue = 1f, Unit = string.Empty },
            new PluginParameter { Index = UnvoicedEnergyIndex, Name = "Unvoiced Energy", MinValue = 0f, MaxValue = 1f, DefaultValue = 1f, Unit = string.Empty },
            new PluginParameter { Index = SibilanceEnergyIndex, Name = "Sibilance Energy", MinValue = 0f, MaxValue = 1f, DefaultValue = 1f, Unit = string.Empty }
        ];

        UpdateSignalMask();
    }

    public string Id => "builtin:sidechain-tap";

    public string Name => "Sidechain Tap";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public SidechainSignalMask ProducedSignals => _signalMask;

    public bool SpeechPresenceEnabled => _speechEnabled >= 0.5f;
    public bool VoicedProbabilityEnabled => _voicedEnabled >= 0.5f;
    public bool UnvoicedEnergyEnabled => _unvoicedEnabled >= 0.5f;
    public bool SibilanceEnergyEnabled => _sibilanceEnabled >= 0.5f;
    public int SampleRate { get; private set; }

    public void Initialize(int sampleRate, int blockSize)
    {
        SampleRate = sampleRate;
        _processor.Configure(sampleRate, blockSize);
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        if (IsBypassed || buffer.IsEmpty || _signalMask == SidechainSignalMask.None)
        {
            return;
        }

        _processor.ProcessBlock(buffer, context.SampleTime, context.SidechainWriter, _signalMask);

        // Update metering from processor
        _meterSpeechPresence = _processor.GetSpeechPresence();
        _meterVoicedProbability = _processor.GetVoicedProbability();
        _meterUnvoicedEnergy = _processor.GetUnvoicedEnergy();
        _meterSibilanceEnergy = _processor.GetSibilanceEnergy();
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
                _speechEnabled = value;
                break;
            case VoicedProbabilityIndex:
                _voicedEnabled = value;
                break;
            case UnvoicedEnergyIndex:
                _unvoicedEnabled = value;
                break;
            case SibilanceEnergyIndex:
                _sibilanceEnabled = value;
                break;
        }

        UpdateSignalMask();
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 4];
        Buffer.BlockCopy(BitConverter.GetBytes(_speechEnabled), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_voicedEnabled), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_unvoicedEnabled), 0, bytes, 8, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_sibilanceEnabled), 0, bytes, 12, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float))
        {
            return;
        }

        _speechEnabled = BitConverter.ToSingle(state, 0);
        if (state.Length >= sizeof(float) * 2)
        {
            _voicedEnabled = BitConverter.ToSingle(state, 4);
        }
        if (state.Length >= sizeof(float) * 3)
        {
            _unvoicedEnabled = BitConverter.ToSingle(state, 8);
        }
        if (state.Length >= sizeof(float) * 4)
        {
            _sibilanceEnabled = BitConverter.ToSingle(state, 12);
        }

        UpdateSignalMask();
    }

    public void Dispose()
    {
    }

    private void UpdateSignalMask()
    {
        SidechainSignalMask mask = SidechainSignalMask.None;
        if (_speechEnabled >= 0.5f)
        {
            mask |= SidechainSignalMask.SpeechPresence;
        }
        if (_voicedEnabled >= 0.5f)
        {
            mask |= SidechainSignalMask.VoicedProbability;
        }
        if (_unvoicedEnabled >= 0.5f)
        {
            mask |= SidechainSignalMask.UnvoicedEnergy;
        }
        if (_sibilanceEnabled >= 0.5f)
        {
            mask |= SidechainSignalMask.SibilanceEnergy;
        }

        _signalMask = mask;
    }
}
