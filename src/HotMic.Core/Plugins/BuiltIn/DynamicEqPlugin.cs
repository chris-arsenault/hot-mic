using System.Threading;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class DynamicEqPlugin : IContextualPlugin, ISidechainConsumer, IPluginStatusProvider
{
    public const int LowBoostIndex = 0;
    public const int HighBoostIndex = 1;
    public const int SmoothingIndex = 2;

    private const float LowShelfHz = 220f;
    private const float HighShelfHz = 4200f;

    private float _lowBoostDb = 2f;
    private float _highBoostDb = 2f;
    private float _smoothingMs = 80f;

    private float _lowGainDb;
    private float _highGainDb;
    private float _smoothingCoeff;

    private float _lastVoiced;
    private float _lastUnvoiced;

    private int _sampleRate;
    private int _blockSize;
    private string _statusMessage = string.Empty;

    private const string MissingSidechainMessage = "Missing sidechain data.";

    private readonly BiquadFilter _lowShelf = new();
    private readonly BiquadFilter _highShelf = new();

    // Metering
    private float _meterVoicedLevel;
    private float _meterUnvoicedLevel;
    private float _meterLowGainDb;
    private float _meterHighGainDb;

    public DynamicEqPlugin()
    {
        Parameters =
        [
            new PluginParameter { Index = LowBoostIndex, Name = "Low Boost", MinValue = 0f, MaxValue = 6f, DefaultValue = 2f, Unit = "dB" },
            new PluginParameter { Index = HighBoostIndex, Name = "High Boost", MinValue = 0f, MaxValue = 6f, DefaultValue = 2f, Unit = "dB" },
            new PluginParameter { Index = SmoothingIndex, Name = "Smoothing", MinValue = 20f, MaxValue = 200f, DefaultValue = 80f, Unit = "ms" }
        ];
    }

    public string Id => "builtin:dynamic-eq";

    public string Name => "Dynamic EQ";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public SidechainSignalMask RequiredSignals => SidechainSignalMask.VoicedProbability | SidechainSignalMask.UnvoicedEnergy;

    public string StatusMessage => Volatile.Read(ref _statusMessage);

    public void SetSidechainAvailable(bool available)
    {
        Volatile.Write(ref _statusMessage, available ? string.Empty : MissingSidechainMessage);
    }

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _blockSize = blockSize;
        UpdateSmoothing();
        UpdateFilters(0f, 0f);
        _lowGainDb = 0f;
        _highGainDb = 0f;
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return;
        }

        // Apply gains from previous block.
        _lowGainDb += _smoothingCoeff * (_lastVoiced * _lowBoostDb - _lowGainDb);
        _highGainDb += _smoothingCoeff * (_lastUnvoiced * _highBoostDb - _highGainDb);
        UpdateFilters(_lowGainDb, _highGainDb);

        if (!context.TryGetSidechainSource(SidechainSignalId.VoicedProbability, out var voicedSource)
            || !context.TryGetSidechainSource(SidechainSignalId.UnvoicedEnergy, out var unvoicedSource))
        {
            return;
        }

        long baseTime = context.SampleTime;
        float voicedSum = 0f;
        float unvoicedSum = 0f;

        for (int i = 0; i < buffer.Length; i++)
        {
            float sample = buffer[i];
            float processed = _lowShelf.Process(sample);
            processed = _highShelf.Process(processed);
            buffer[i] = processed;

            voicedSum += voicedSource.ReadSample(baseTime + i);
            unvoicedSum += unvoicedSource.ReadSample(baseTime + i);
        }

        float inv = 1f / buffer.Length;
        _lastVoiced = Math.Clamp(voicedSum * inv, 0f, 1f);
        _lastUnvoiced = Math.Clamp(unvoicedSum * inv, 0f, 1f);

        // Update metering
        _meterVoicedLevel = _lastVoiced;
        _meterUnvoicedLevel = _lastUnvoiced;
        _meterLowGainDb = _lowGainDb;
        _meterHighGainDb = _highGainDb;
    }

    /// <summary>Gets the current voiced probability level (0-1).</summary>
    public float GetVoicedLevel() => Volatile.Read(ref _meterVoicedLevel);

    /// <summary>Gets the current unvoiced energy level (0-1).</summary>
    public float GetUnvoicedLevel() => Volatile.Read(ref _meterUnvoicedLevel);

    /// <summary>Gets the current low shelf gain in dB.</summary>
    public float GetLowGainDb() => Volatile.Read(ref _meterLowGainDb);

    /// <summary>Gets the current high shelf gain in dB.</summary>
    public float GetHighGainDb() => Volatile.Read(ref _meterHighGainDb);

    public void Process(Span<float> buffer)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return;
        }

        UpdateFilters(_lowGainDb, _highGainDb);
        for (int i = 0; i < buffer.Length; i++)
        {
            float sample = buffer[i];
            sample = _lowShelf.Process(sample);
            sample = _highShelf.Process(sample);
            buffer[i] = sample;
        }
    }

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case LowBoostIndex:
                _lowBoostDb = Math.Clamp(value, 0f, 6f);
                break;
            case HighBoostIndex:
                _highBoostDb = Math.Clamp(value, 0f, 6f);
                break;
            case SmoothingIndex:
                _smoothingMs = Math.Clamp(value, 20f, 200f);
                UpdateSmoothing();
                break;
        }
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 3];
        Buffer.BlockCopy(BitConverter.GetBytes(_lowBoostDb), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_highBoostDb), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_smoothingMs), 0, bytes, 8, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float) * 2)
        {
            return;
        }

        _lowBoostDb = BitConverter.ToSingle(state, 0);
        _highBoostDb = BitConverter.ToSingle(state, 4);
        if (state.Length >= sizeof(float) * 3)
        {
            _smoothingMs = BitConverter.ToSingle(state, 8);
        }

        UpdateSmoothing();
    }

    public void Dispose()
    {
    }

    private void UpdateFilters(float lowGainDb, float highGainDb)
    {
        if (_sampleRate <= 0)
        {
            return;
        }

        _lowShelf.SetLowShelf(_sampleRate, LowShelfHz, lowGainDb, 0.707f);
        _highShelf.SetHighShelf(_sampleRate, HighShelfHz, highGainDb, 0.707f);
    }

    private void UpdateSmoothing()
    {
        if (_sampleRate <= 0)
        {
            return;
        }

        float perSample = DspUtils.TimeToCoefficient(_smoothingMs, _sampleRate);
        float blockSamples = _blockSize > 0 ? _blockSize : 256f;
        _smoothingCoeff = 1f - MathF.Pow(1f - perSample, blockSamples);
    }
}
