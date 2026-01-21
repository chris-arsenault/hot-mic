using System.Threading;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class DynamicEqPlugin : IPlugin, IAnalysisSignalConsumer, IPluginStatusProvider
{
    public const int LowBoostIndex = 0;
    public const int HighBoostIndex = 1;
    public const int SmoothingIndex = 2;
    public const int ScaleIndex = 3;

    private const float LowShelfHz = 220f;
    private const float EdgePeakHz = 3400f;
    private const float AirShelfHz = 9000f;
    private const float EdgeQ = 1.1f;
    private const float AirShelfQ = 0.707f;
    private const float AirWeight = 0.6f;

    private float _lowBoostDb = 2f;
    private float _highBoostDb = 2f;
    private float _smoothingMs = 80f;
    private int _boostScaleIndex;

    private float _lowGainDb;
    private float _edgeGainDb;
    private float _airGainDb;
    private float _smoothingCoeff;

    private float _lastVoicing;
    private float _lastFricative;

    private int _sampleRate;
    private int _blockSize;
    private string _statusMessage = string.Empty;

    private const string MissingSidechainMessage = "Missing analysis data.";

    private readonly BiquadFilter _lowShelf = new();
    private readonly BiquadFilter _edgePeak = new();
    private readonly BiquadFilter _airShelf = new();

    // Metering
    private float _meterVoicingLevel;
    private float _meterFricativeLevel;
    private float _meterLowGainDb;
    private float _meterEdgeGainDb;
    private float _meterAirGainDb;

    public DynamicEqPlugin()
    {
        Parameters =
        [
            new PluginParameter { Index = LowBoostIndex, Name = "Low Boost", MinValue = -6f, MaxValue = 6f, DefaultValue = 2f, Unit = "dB" },
            new PluginParameter { Index = HighBoostIndex, Name = "High Boost", MinValue = -6f, MaxValue = 6f, DefaultValue = 2f, Unit = "dB" },
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
            new PluginParameter { Index = SmoothingIndex, Name = "Smoothing", MinValue = 20f, MaxValue = 200f, DefaultValue = 80f, Unit = "ms" }
        ];
    }

    public string Id => "builtin:dynamic-eq";

    public string Name => "Dynamic EQ";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public AnalysisSignalMask RequiredSignals => AnalysisSignalMask.VoicingScore | AnalysisSignalMask.FricativeActivity;

    public string StatusMessage => Volatile.Read(ref _statusMessage);

    public float LowBoostDb => _lowBoostDb;
    public float HighBoostDb => _highBoostDb;
    public float SmoothingMs => _smoothingMs;
    public int BoostScaleIndex => _boostScaleIndex;
    public int SampleRate => _sampleRate;

    public void SetAnalysisSignalsAvailable(bool available)
    {
        Volatile.Write(ref _statusMessage, available ? string.Empty : MissingSidechainMessage);
    }

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _blockSize = blockSize;
        UpdateSmoothing();
        UpdateFilters(0f, 0f, 0f);
        _lowGainDb = 0f;
        _edgeGainDb = 0f;
        _airGainDb = 0f;
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return;
        }

        // Apply gains from previous block.
        float scale = EnhanceAmountScale.FromIndex(_boostScaleIndex);
        _lowGainDb += _smoothingCoeff * (_lastVoicing * _lowBoostDb * scale - _lowGainDb);
        _edgeGainDb += _smoothingCoeff * (_lastFricative * _highBoostDb * scale - _edgeGainDb);
        _airGainDb += _smoothingCoeff * (_lastFricative * _highBoostDb * scale * AirWeight - _airGainDb);
        UpdateFilters(_lowGainDb, _edgeGainDb, _airGainDb);

        if (!context.TryGetAnalysisSignalSource(AnalysisSignalId.VoicingScore, out var voicingSource)
            || !context.TryGetAnalysisSignalSource(AnalysisSignalId.FricativeActivity, out var fricativeSource))
        {
            return;
        }

        long baseTime = context.SampleTime;
        float voicingSum = 0f;
        float fricativeSum = 0f;

        for (int i = 0; i < buffer.Length; i++)
        {
            float sample = buffer[i];
            float processed = _lowShelf.Process(sample);
            processed = _edgePeak.Process(processed);
            processed = _airShelf.Process(processed);
            buffer[i] = processed;

            float voicing = voicingSource.ReadSample(baseTime + i);
            float fricative = fricativeSource.ReadSample(baseTime + i);
            // Unvoiced detector = HF energy weighted by low periodicity.
            voicingSum += voicing;
            fricativeSum += fricative * (1f - voicing);
        }

        float inv = 1f / buffer.Length;
        _lastVoicing = Math.Clamp(voicingSum * inv, 0f, 1f);
        _lastFricative = Math.Clamp(fricativeSum * inv, 0f, 1f);

        // Update metering
        _meterVoicingLevel = _lastVoicing;
        _meterFricativeLevel = _lastFricative;
        _meterLowGainDb = _lowGainDb;
        _meterEdgeGainDb = _edgeGainDb;
        _meterAirGainDb = _airGainDb;
    }

    /// <summary>Gets the current voicing score level (0-1).</summary>
    public float GetVoicingLevel() => Volatile.Read(ref _meterVoicingLevel);

    /// <summary>Gets the current fricative activity level (0-1).</summary>
    public float GetFricativeLevel() => Volatile.Read(ref _meterFricativeLevel);

    /// <summary>Gets the current low shelf gain in dB.</summary>
    public float GetLowGainDb() => Volatile.Read(ref _meterLowGainDb);

    /// <summary>Gets the current edge peak gain in dB.</summary>
    public float GetEdgeGainDb() => Volatile.Read(ref _meterEdgeGainDb);

    /// <summary>Gets the current air shelf gain in dB.</summary>
    public float GetAirGainDb() => Volatile.Read(ref _meterAirGainDb);

    public void Process(Span<float> buffer)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return;
        }

        UpdateFilters(_lowGainDb, _edgeGainDb, _airGainDb);
        for (int i = 0; i < buffer.Length; i++)
        {
            float sample = buffer[i];
            sample = _lowShelf.Process(sample);
            sample = _edgePeak.Process(sample);
            sample = _airShelf.Process(sample);
            buffer[i] = sample;
        }
    }

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case LowBoostIndex:
                _lowBoostDb = Math.Clamp(value, -6f, 6f);
                break;
            case HighBoostIndex:
                _highBoostDb = Math.Clamp(value, -6f, 6f);
                break;
            case SmoothingIndex:
                _smoothingMs = Math.Clamp(value, 20f, 200f);
                UpdateSmoothing();
                break;
            case ScaleIndex:
                _boostScaleIndex = EnhanceAmountScale.ClampIndex(value);
                break;
        }
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 4];
        Buffer.BlockCopy(BitConverter.GetBytes(_lowBoostDb), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_highBoostDb), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_smoothingMs), 0, bytes, 8, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_boostScaleIndex), 0, bytes, 12, 4);
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
        if (state.Length >= sizeof(float) * 4)
        {
            _boostScaleIndex = EnhanceAmountScale.ClampIndex(BitConverter.ToSingle(state, 12));
        }

        UpdateSmoothing();
    }

    public void Dispose()
    {
    }

    private void UpdateFilters(float lowGainDb, float edgeGainDb, float airGainDb)
    {
        if (_sampleRate <= 0)
        {
            return;
        }

        _lowShelf.SetLowShelf(_sampleRate, LowShelfHz, lowGainDb, 0.707f);
        _edgePeak.SetPeaking(_sampleRate, EdgePeakHz, edgeGainDb, EdgeQ);
        _airShelf.SetHighShelf(_sampleRate, AirShelfHz, airGainDb, AirShelfQ);
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
