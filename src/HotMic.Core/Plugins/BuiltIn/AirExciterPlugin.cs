using System.Threading;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class AirExciterPlugin : IPlugin, IAnalysisSignalConsumer, IPluginStatusProvider
{
    public const int DriveIndex = 0;
    public const int MixIndex = 1;
    public const int CutoffIndex = 2;

    private float _drive = 0.6f;
    private float _mix = 0.4f;
    private float _cutoffHz = 4500f;
    private int _sampleRate;
    private string _statusMessage = string.Empty;

    private const string MissingSidechainMessage = "Missing analysis data.";

    private readonly BiquadFilter _highPass = new();

    // Metering - atomic fields for thread-safe UI access
    private float _meterGateLevel;
    private float _meterHfEnergy;
    private float _meterSaturationAmount;

    public AirExciterPlugin()
    {
        Parameters =
        [
            new PluginParameter { Index = DriveIndex, Name = "Drive", MinValue = 0f, MaxValue = 1f, DefaultValue = 0.6f, Unit = string.Empty },
            new PluginParameter { Index = MixIndex, Name = "Mix", MinValue = 0f, MaxValue = 1f, DefaultValue = 0.4f, Unit = string.Empty },
            new PluginParameter { Index = CutoffIndex, Name = "Cutoff", MinValue = 3000f, MaxValue = 10000f, DefaultValue = 4500f, Unit = "Hz" }
        ];
    }

    public string Id => "builtin:air-exciter";

    public string Name => "Air Exciter";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public AnalysisSignalMask RequiredSignals => AnalysisSignalMask.VoicingScore | AnalysisSignalMask.SibilanceEnergy;

    public string StatusMessage => Volatile.Read(ref _statusMessage);

    public float Drive => _drive;
    public float Mix => _mix;
    public float CutoffHz => _cutoffHz;
    public int SampleRate => _sampleRate;

    public void SetAnalysisSignalsAvailable(bool available)
    {
        Volatile.Write(ref _statusMessage, available ? string.Empty : MissingSidechainMessage);
    }

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        UpdateFilter();
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return;
        }

        if (!context.TryGetAnalysisSignalSource(AnalysisSignalId.VoicingScore, out var voicedSource)
            || !context.TryGetAnalysisSignalSource(AnalysisSignalId.SibilanceEnergy, out var sibilanceSource))
        {
            return;
        }

        long baseTime = context.SampleTime;

        for (int i = 0; i < buffer.Length; i++)
        {
            float input = buffer[i];
            float high = _highPass.Process(input);
            float shaped = MathF.Tanh(high * (1.5f + _drive * 4f));

            float voiced = voicedSource.ReadSample(baseTime + i);
            float sib = sibilanceSource.ReadSample(baseTime + i);
            float gate = Math.Clamp(voiced * (1f - sib), 0f, 1f);

            buffer[i] = input + shaped * (_mix * gate);

            // Update metering
            _meterGateLevel = gate;
            _meterHfEnergy = MathF.Abs(high);
            _meterSaturationAmount = MathF.Abs(shaped);
        }
    }

    /// <summary>Gets the current gate level (voiced without sibilance, 0-1).</summary>
    public float GetGateLevel() => Volatile.Read(ref _meterGateLevel);

    /// <summary>Gets the current high-frequency energy level.</summary>
    public float GetHfEnergy() => Volatile.Read(ref _meterHfEnergy);

    /// <summary>Gets the current saturation output level.</summary>
    public float GetSaturationAmount() => Volatile.Read(ref _meterSaturationAmount);

    public void Process(Span<float> buffer)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return;
        }

        for (int i = 0; i < buffer.Length; i++)
        {
            float input = buffer[i];
            float high = _highPass.Process(input);
            float shaped = MathF.Tanh(high * (1.5f + _drive * 4f));
            buffer[i] = input + shaped * _mix;
        }
    }

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case DriveIndex:
                _drive = Math.Clamp(value, 0f, 1f);
                break;
            case MixIndex:
                _mix = Math.Clamp(value, 0f, 1f);
                break;
            case CutoffIndex:
                _cutoffHz = Math.Clamp(value, 3000f, 10000f);
                UpdateFilter();
                break;
        }
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 3];
        Buffer.BlockCopy(BitConverter.GetBytes(_drive), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_mix), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_cutoffHz), 0, bytes, 8, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float) * 2)
        {
            return;
        }

        _drive = BitConverter.ToSingle(state, 0);
        _mix = BitConverter.ToSingle(state, 4);
        if (state.Length >= sizeof(float) * 3)
        {
            _cutoffHz = BitConverter.ToSingle(state, 8);
        }

        UpdateFilter();
    }

    public void Dispose()
    {
    }

    private void UpdateFilter()
    {
        if (_sampleRate <= 0)
        {
            return;
        }

        _highPass.SetHighPass(_sampleRate, _cutoffHz, 0.707f);
    }
}
