using System.Threading;
using HotMic.Core.Dsp;
using HotMic.Core.Dsp.Filters;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class UpwardExpanderPlugin : IContextualPlugin, ISidechainConsumer, IPluginStatusProvider
{
    public const int AmountIndex = 0;
    public const int ThresholdIndex = 1;
    public const int LowSplitIndex = 2;
    public const int HighSplitIndex = 3;
    public const int AttackIndex = 4;
    public const int ReleaseIndex = 5;
    public const int GateStrengthIndex = 6;

    private const float MaxRatioAdd = 0.3f;
    private const float MaxBoostDb = 6f;

    private float _amountPct = 20f;
    private float _thresholdDb = -35f;
    private float _lowSplitHz = 200f;
    private float _highSplitHz = 3500f;
    private float _attackMs = 8f;
    private float _releaseMs = 120f;
    private float _gateStrength = 0.8f;

    private float _attackCoeff;
    private float _releaseCoeff;
    private float _ratio = 1.06f;

    private float _lowGain = 1f;
    private float _midGain = 1f;
    private float _highGain = 1f;

    private int _sampleRate;
    private string _statusMessage = string.Empty;

    private const string MissingSidechainMessage = "Missing sidechain data.";

    private readonly BiquadFilter _lowPass = new();
    private readonly BiquadFilter _highPass = new();
    private readonly EnvelopeFollower _lowEnv = new();
    private readonly EnvelopeFollower _midEnv = new();
    private readonly EnvelopeFollower _highEnv = new();

    // Metering
    private float _meterLowLevel;
    private float _meterMidLevel;
    private float _meterHighLevel;
    private float _meterLowGainDb;
    private float _meterMidGainDb;
    private float _meterHighGainDb;
    private float _meterSpeechPresence;

    public UpwardExpanderPlugin()
    {
        Parameters =
        [
            new PluginParameter { Index = AmountIndex, Name = "Amount", MinValue = 0f, MaxValue = 100f, DefaultValue = 20f, Unit = "%" },
            new PluginParameter { Index = ThresholdIndex, Name = "Threshold", MinValue = -60f, MaxValue = -10f, DefaultValue = -35f, Unit = "dB" },
            new PluginParameter { Index = LowSplitIndex, Name = "Low Split", MinValue = 80f, MaxValue = 400f, DefaultValue = 200f, Unit = "Hz" },
            new PluginParameter { Index = HighSplitIndex, Name = "High Split", MinValue = 1500f, MaxValue = 8000f, DefaultValue = 3500f, Unit = "Hz" },
            new PluginParameter { Index = AttackIndex, Name = "Attack", MinValue = 2f, MaxValue = 50f, DefaultValue = 8f, Unit = "ms" },
            new PluginParameter { Index = ReleaseIndex, Name = "Release", MinValue = 30f, MaxValue = 300f, DefaultValue = 120f, Unit = "ms" },
            new PluginParameter { Index = GateStrengthIndex, Name = "Gate Strength", MinValue = 0f, MaxValue = 1f, DefaultValue = 0.8f, Unit = string.Empty }
        ];
    }

    public string Id => "builtin:upward-expander";

    public string Name => "Upward Expander";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public SidechainSignalMask RequiredSignals => SidechainSignalMask.SpeechPresence;

    public string StatusMessage => Volatile.Read(ref _statusMessage);

    public float AmountPct => _amountPct;
    public float ThresholdDb => _thresholdDb;
    public float LowSplitHz => _lowSplitHz;
    public float HighSplitHz => _highSplitHz;
    public float AttackMs => _attackMs;
    public float ReleaseMs => _releaseMs;
    public float GateStrength => _gateStrength;
    public int SampleRate => _sampleRate;

    public void SetSidechainAvailable(bool available)
    {
        Volatile.Write(ref _statusMessage, available ? string.Empty : MissingSidechainMessage);
    }

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        UpdateCoefficients();
        UpdateFilters();
        _lowGain = 1f;
        _midGain = 1f;
        _highGain = 1f;
        _lowEnv.Reset();
        _midEnv.Reset();
        _highEnv.Reset();
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return;
        }

        if (!context.TryGetSidechainSource(SidechainSignalId.SpeechPresence, out var speechSource))
        {
            return;
        }

        long baseTime = context.SampleTime;

        for (int i = 0; i < buffer.Length; i++)
        {
            float input = buffer[i];

            float low = _lowPass.Process(input);
            float high = _highPass.Process(input);
            float mid = input - low - high;

            float presence = speechSource.ReadSample(baseTime + i);
            float gate = 1f - _gateStrength + _gateStrength * presence;

            float lowEnv = _lowEnv.Process(low);
            float midEnv = _midEnv.Process(mid);
            float highEnv = _highEnv.Process(high);

            _lowGain = UpdateGain(lowEnv, _lowGain, gate);
            _midGain = UpdateGain(midEnv, _midGain, gate);
            _highGain = UpdateGain(highEnv, _highGain, gate);

            buffer[i] = low * _lowGain + mid * _midGain + high * _highGain;

            // Update metering
            _meterLowLevel = lowEnv;
            _meterMidLevel = midEnv;
            _meterHighLevel = highEnv;
            _meterLowGainDb = DspUtils.LinearToDb(_lowGain);
            _meterMidGainDb = DspUtils.LinearToDb(_midGain);
            _meterHighGainDb = DspUtils.LinearToDb(_highGain);
            _meterSpeechPresence = presence;
        }
    }

    /// <summary>Gets the low band envelope level.</summary>
    public float GetLowLevel() => Volatile.Read(ref _meterLowLevel);

    /// <summary>Gets the mid band envelope level.</summary>
    public float GetMidLevel() => Volatile.Read(ref _meterMidLevel);

    /// <summary>Gets the high band envelope level.</summary>
    public float GetHighLevel() => Volatile.Read(ref _meterHighLevel);

    /// <summary>Gets the low band gain in dB.</summary>
    public float GetLowGainDb() => Volatile.Read(ref _meterLowGainDb);

    /// <summary>Gets the mid band gain in dB.</summary>
    public float GetMidGainDb() => Volatile.Read(ref _meterMidGainDb);

    /// <summary>Gets the high band gain in dB.</summary>
    public float GetHighGainDb() => Volatile.Read(ref _meterHighGainDb);

    /// <summary>Gets the current speech presence (0-1).</summary>
    public float GetSpeechPresence() => Volatile.Read(ref _meterSpeechPresence);

    public void Process(Span<float> buffer)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return;
        }

        for (int i = 0; i < buffer.Length; i++)
        {
            float input = buffer[i];
            float low = _lowPass.Process(input);
            float high = _highPass.Process(input);
            float mid = input - low - high;

            _lowGain = UpdateGain(_lowEnv.Process(low), _lowGain, 1f);
            _midGain = UpdateGain(_midEnv.Process(mid), _midGain, 1f);
            _highGain = UpdateGain(_highEnv.Process(high), _highGain, 1f);

            buffer[i] = low * _lowGain + mid * _midGain + high * _highGain;
        }
    }

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case AmountIndex:
                _amountPct = Math.Clamp(value, 0f, 100f);
                UpdateCoefficients();
                break;
            case ThresholdIndex:
                _thresholdDb = value;
                break;
            case LowSplitIndex:
                _lowSplitHz = Math.Clamp(value, 80f, 400f);
                UpdateFilters();
                break;
            case HighSplitIndex:
                _highSplitHz = Math.Clamp(value, 1500f, 8000f);
                UpdateFilters();
                break;
            case AttackIndex:
                _attackMs = Math.Clamp(value, 2f, 50f);
                UpdateCoefficients();
                break;
            case ReleaseIndex:
                _releaseMs = Math.Clamp(value, 30f, 300f);
                UpdateCoefficients();
                break;
            case GateStrengthIndex:
                _gateStrength = Math.Clamp(value, 0f, 1f);
                break;
        }
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 7];
        Buffer.BlockCopy(BitConverter.GetBytes(_amountPct), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_thresholdDb), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_lowSplitHz), 0, bytes, 8, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_highSplitHz), 0, bytes, 12, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_attackMs), 0, bytes, 16, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_releaseMs), 0, bytes, 20, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_gateStrength), 0, bytes, 24, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float) * 2)
        {
            return;
        }

        _amountPct = BitConverter.ToSingle(state, 0);
        _thresholdDb = BitConverter.ToSingle(state, 4);
        if (state.Length >= sizeof(float) * 3)
        {
            _lowSplitHz = BitConverter.ToSingle(state, 8);
        }
        if (state.Length >= sizeof(float) * 4)
        {
            _highSplitHz = BitConverter.ToSingle(state, 12);
        }
        if (state.Length >= sizeof(float) * 5)
        {
            _attackMs = BitConverter.ToSingle(state, 16);
        }
        if (state.Length >= sizeof(float) * 6)
        {
            _releaseMs = BitConverter.ToSingle(state, 20);
        }
        if (state.Length >= sizeof(float) * 7)
        {
            _gateStrength = BitConverter.ToSingle(state, 24);
        }

        UpdateCoefficients();
        UpdateFilters();
    }

    public void Dispose()
    {
    }

    private void UpdateCoefficients()
    {
        if (_sampleRate <= 0)
        {
            return;
        }

        _attackCoeff = DspUtils.TimeToCoefficient(_attackMs, _sampleRate);
        _releaseCoeff = DspUtils.TimeToCoefficient(_releaseMs, _sampleRate);
        _ratio = 1f + (_amountPct / 100f) * MaxRatioAdd;
    }

    private void UpdateFilters()
    {
        if (_sampleRate <= 0)
        {
            return;
        }

        _lowPass.SetLowPass(_sampleRate, _lowSplitHz, 0.707f);
        _highPass.SetHighPass(_sampleRate, _highSplitHz, 0.707f);
    }

    private float UpdateGain(float env, float current, float gate)
    {
        float levelDb = DspUtils.LinearToDb(env);
        float gainDb = 0f;
        if (levelDb > _thresholdDb)
        {
            gainDb = MathF.Min(MaxBoostDb, (levelDb - _thresholdDb) * (_ratio - 1f));
        }

        float target = DspUtils.DbToLinear(gainDb);
        target = 1f + (target - 1f) * gate;
        float coeff = target > current ? _attackCoeff : _releaseCoeff;
        return current + coeff * (target - current);
    }
}
