using System.Threading;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class ConsonantTransientPlugin : IPlugin, IAnalysisSignalConsumer, IPluginStatusProvider
{
    public const int AmountIndex = 0;
    public const int ThresholdIndex = 1;
    public const int HighCutIndex = 2;

    private float _amount = 0.6f;
    private float _fluxThresholdDb = 1f;
    private float _highCutHz = 6000f;
    private int _sampleRate;
    private string _statusMessage = string.Empty;

    private const string MissingSidechainMessage = "Missing analysis data.";
    private const float BandLowCutHz = 2000f;
    private const float TransientCeiling = 0.35f;
    private const float FluxBaselineMs = 120f;
    private const float FluxGateKneeDb = 3f;

    private float _fluxBaseline;
    private float _fluxBaselineCoeff;

    private readonly BiquadFilter _highPass = new();
    private readonly BiquadFilter _lowPass = new();
    private readonly EnvelopeFollower _fastEnv = new();
    private readonly EnvelopeFollower _slowEnv = new();

    // Metering
    private float _meterOnsetGate;
    private float _meterTransientDetected;
    private float _meterFluxDb;
    private float _meterFluxBaselineDb;
    private float _meterOnsetDb;

    public ConsonantTransientPlugin()
    {
        Parameters =
        [
            new PluginParameter { Index = AmountIndex, Name = "Amount", MinValue = 0f, MaxValue = 1f, DefaultValue = 0.6f, Unit = string.Empty },
            new PluginParameter { Index = ThresholdIndex, Name = "Threshold", MinValue = 0f, MaxValue = 6f, DefaultValue = 1f, Unit = "dB" },
            new PluginParameter { Index = HighCutIndex, Name = "High Cut", MinValue = 3000f, MaxValue = 9000f, DefaultValue = 6000f, Unit = "Hz" }
        ];
    }

    public string Id => "builtin:consonant-transient";

    public string Name => "Consonant Transient";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public AnalysisSignalMask RequiredSignals => AnalysisSignalMask.OnsetFluxHigh;

    public string StatusMessage => Volatile.Read(ref _statusMessage);

    public float Amount => _amount;
    public float Threshold => _fluxThresholdDb;
    public float HighCutHz => _highCutHz;
    public int SampleRate => _sampleRate;

    public void SetAnalysisSignalsAvailable(bool available)
    {
        Volatile.Write(ref _statusMessage, available ? string.Empty : MissingSidechainMessage);
    }

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _fastEnv.Configure(1.5f, 25f, sampleRate);
        _slowEnv.Configure(10f, 120f, sampleRate);
        _fluxBaseline = 0f;
        _fluxBaselineCoeff = DspUtils.TimeToCoefficient(FluxBaselineMs, sampleRate);
        UpdateFilter();
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return;
        }

        if (!context.TryGetAnalysisSignalSource(AnalysisSignalId.OnsetFluxHigh, out var onsetFluxSource))
        {
            return;
        }

        long baseTime = context.SampleTime;

        for (int i = 0; i < buffer.Length; i++)
        {
            float input = buffer[i];
            float band = _lowPass.Process(_highPass.Process(input));
            float fast = _fastEnv.Process(band);
            float slow = _slowEnv.Process(band);

            float transient = MathF.Max(0f, fast - slow);
            float norm = slow > 1e-6f ? transient / slow : 0f;

            float flux = onsetFluxSource.ReadSample(baseTime + i);
            _fluxBaseline += _fluxBaselineCoeff * (flux - _fluxBaseline);
            float onsetDb = flux - (_fluxBaseline + _fluxThresholdDb);
            float gate = onsetDb > 0f ? onsetDb / (onsetDb + FluxGateKneeDb) : 0f;

            float gain = 1f + _amount * gate * MathF.Min(1.5f, norm * 2f);
            float boost = band * (gain - 1f);
            boost = SoftClip(boost, TransientCeiling);
            buffer[i] = input + boost;

            // Update metering
            _meterOnsetGate = gate;
            _meterTransientDetected = gain > 1.01f ? 1f : 0f;
            _meterFluxDb = flux;
            _meterFluxBaselineDb = _fluxBaseline;
            _meterOnsetDb = onsetDb;
        }
    }

    /// <summary>Gets the current onset gate level (0-1).</summary>
    public float GetOnsetGate() => Volatile.Read(ref _meterOnsetGate);

    /// <summary>Gets the current onset flux value (dB change per frame).</summary>
    public float GetFluxDb() => Volatile.Read(ref _meterFluxDb);

    /// <summary>Gets the current flux baseline (dB change per frame).</summary>
    public float GetFluxBaselineDb() => Volatile.Read(ref _meterFluxBaselineDb);

    /// <summary>Gets the onset amount above baseline + threshold (dB).</summary>
    public float GetOnsetDb() => Volatile.Read(ref _meterOnsetDb);

    /// <summary>Gets whether a transient is currently detected (0 or 1).</summary>
    public float GetTransientDetected() => Volatile.Read(ref _meterTransientDetected);

    public void Process(Span<float> buffer)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return;
        }

        for (int i = 0; i < buffer.Length; i++)
        {
            float input = buffer[i];
            float band = _lowPass.Process(_highPass.Process(input));
            float fast = _fastEnv.Process(band);
            float slow = _slowEnv.Process(band);
            float transient = MathF.Max(0f, fast - slow);
            float norm = slow > 1e-6f ? transient / slow : 0f;
            float gain = 1f + _amount * MathF.Min(1.5f, norm * 2f);
            float boost = band * (gain - 1f);
            boost = SoftClip(boost, TransientCeiling);
            buffer[i] = input + boost;
        }
    }

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case AmountIndex:
                _amount = Math.Clamp(value, 0f, 1f);
                break;
            case ThresholdIndex:
                _fluxThresholdDb = Math.Clamp(value, 0f, 6f);
                break;
            case HighCutIndex:
                _highCutHz = Math.Clamp(value, 3000f, 9000f);
                UpdateFilter();
                break;
        }
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 3];
        Buffer.BlockCopy(BitConverter.GetBytes(_amount), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_fluxThresholdDb), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_highCutHz), 0, bytes, 8, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float) * 2)
        {
            return;
        }

        _amount = BitConverter.ToSingle(state, 0);
        _fluxThresholdDb = BitConverter.ToSingle(state, 4);
        if (state.Length >= sizeof(float) * 3)
        {
            _highCutHz = BitConverter.ToSingle(state, 8);
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

        _highPass.SetHighPass(_sampleRate, BandLowCutHz, 0.707f);
        _lowPass.SetLowPass(_sampleRate, _highCutHz, 0.707f);
    }


    private static float SoftClip(float value, float ceiling)
    {
        float scale = MathF.Max(1e-6f, ceiling);
        float normalized = value / scale;
        return MathF.Tanh(normalized) * scale;
    }
}
