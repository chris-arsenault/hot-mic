using System.Threading;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class BassEnhancerPlugin : IPlugin, IAnalysisSignalConsumer, IPluginStatusProvider
{
    public const int AmountIndex = 0;
    public const int DriveIndex = 1;
    public const int MixIndex = 2;
    public const int CenterIndex = 3;
    public const int ScaleIndex = 4;

    private float _amount = 0.4f;
    private float _drive = 0.5f;
    private float _mix = 0.5f;
    private float _centerHz = 110f;
    private int _amountScaleIndex;

    private int _sampleRate;
    private string _statusMessage = string.Empty;

    private const string MissingSidechainMessage = "Missing analysis data.";

    private readonly BiquadFilter _bandPass = new();
    private readonly BiquadFilter _highPass = new();

    // Metering
    private float _meterVoicedGate;
    private float _meterBassEnergy;
    private float _meterHarmonicAmount;

    public BassEnhancerPlugin()
    {
        Parameters =
        [
            new PluginParameter { Index = AmountIndex, Name = "Amount", MinValue = 0f, MaxValue = 1f, DefaultValue = 0.4f, Unit = string.Empty },
            new PluginParameter
            {
                Index = ScaleIndex,
                Name = "Scale",
                MinValue = 0f,
                MaxValue = 3f,
                DefaultValue = 0f,
                Unit = string.Empty,
                FormatValue = EnhanceAmountScale.FormatLabel
            },
            new PluginParameter { Index = DriveIndex, Name = "Drive", MinValue = 0f, MaxValue = 1f, DefaultValue = 0.5f, Unit = string.Empty },
            new PluginParameter { Index = MixIndex, Name = "Mix", MinValue = 0f, MaxValue = 1f, DefaultValue = 0.5f, Unit = string.Empty },
            new PluginParameter { Index = CenterIndex, Name = "Center", MinValue = 70f, MaxValue = 180f, DefaultValue = 110f, Unit = "Hz" }
        ];
    }

    public string Id => "builtin:bass-enhancer";

    public string Name => "Bass Enhancer";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public AnalysisSignalMask RequiredSignals => AnalysisSignalMask.VoicingScore;

    public string StatusMessage => Volatile.Read(ref _statusMessage);

    public float Amount => _amount;
    public float Drive => _drive;
    public float Mix => _mix;
    public float CenterHz => _centerHz;
    public int AmountScaleIndex => _amountScaleIndex;
    public int SampleRate => _sampleRate;

    public void SetAnalysisSignalsAvailable(bool available)
    {
        Volatile.Write(ref _statusMessage, available ? string.Empty : MissingSidechainMessage);
    }

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        UpdateFilters();
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return;
        }

        if (!context.TryGetAnalysisSignalSource(AnalysisSignalId.VoicingScore, out var voicedSource))
        {
            return;
        }

        long baseTime = context.SampleTime;

        for (int i = 0; i < buffer.Length; i++)
        {
            float input = buffer[i];
            float low = _bandPass.Process(input);
            float shaped = MathF.Tanh(low * (1f + _drive * 6f)) - low;
            float harmonic = _highPass.Process(shaped);

            float gate = voicedSource.ReadSample(baseTime + i);

            float amount = _amount * EnhanceAmountScale.FromIndex(_amountScaleIndex);
            float wet = harmonic * amount * gate;
            buffer[i] = input * (1f - _mix) + (input + wet) * _mix;

            // Update metering
            _meterVoicedGate = gate;
            _meterBassEnergy = MathF.Abs(low);
            _meterHarmonicAmount = MathF.Abs(harmonic);
        }
    }

    /// <summary>Gets the current voiced gate level (0-1).</summary>
    public float GetVoicedGate() => Volatile.Read(ref _meterVoicedGate);

    /// <summary>Gets the current bass band energy.</summary>
    public float GetBassEnergy() => Volatile.Read(ref _meterBassEnergy);

    /// <summary>Gets the current harmonic generation amount.</summary>
    public float GetHarmonicAmount() => Volatile.Read(ref _meterHarmonicAmount);

    public void Process(Span<float> buffer)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return;
        }

        for (int i = 0; i < buffer.Length; i++)
        {
            float input = buffer[i];
            float low = _bandPass.Process(input);
            float shaped = MathF.Tanh(low * (1f + _drive * 6f)) - low;
            float harmonic = _highPass.Process(shaped);
            float amount = _amount * EnhanceAmountScale.FromIndex(_amountScaleIndex);
            float wet = harmonic * amount;
            buffer[i] = input * (1f - _mix) + (input + wet) * _mix;
        }
    }

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case AmountIndex:
                _amount = Math.Clamp(value, 0f, 1f);
                break;
            case DriveIndex:
                _drive = Math.Clamp(value, 0f, 1f);
                break;
            case MixIndex:
                _mix = Math.Clamp(value, 0f, 1f);
                break;
            case CenterIndex:
                _centerHz = Math.Clamp(value, 70f, 180f);
                UpdateFilters();
                break;
            case ScaleIndex:
                _amountScaleIndex = EnhanceAmountScale.ClampIndex(value);
                break;
        }
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 5];
        Buffer.BlockCopy(BitConverter.GetBytes(_amount), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_drive), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_mix), 0, bytes, 8, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_centerHz), 0, bytes, 12, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_amountScaleIndex), 0, bytes, 16, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float) * 2)
        {
            return;
        }

        _amount = BitConverter.ToSingle(state, 0);
        _drive = BitConverter.ToSingle(state, 4);
        if (state.Length >= sizeof(float) * 3)
        {
            _mix = BitConverter.ToSingle(state, 8);
        }
        if (state.Length >= sizeof(float) * 4)
        {
            _centerHz = BitConverter.ToSingle(state, 12);
        }
        if (state.Length >= sizeof(float) * 5)
        {
            _amountScaleIndex = EnhanceAmountScale.ClampIndex(BitConverter.ToSingle(state, 16));
        }

        UpdateFilters();
    }

    public void Dispose()
    {
    }

    private void UpdateFilters()
    {
        if (_sampleRate <= 0)
        {
            return;
        }

        _bandPass.SetBandPass(_sampleRate, _centerHz, 0.8f);
        _highPass.SetHighPass(_sampleRate, _centerHz * 1.8f, 0.707f);
    }

}
