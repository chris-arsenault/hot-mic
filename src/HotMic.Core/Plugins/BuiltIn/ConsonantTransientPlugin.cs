using System.Threading;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class ConsonantTransientPlugin : IPlugin, IAnalysisSignalConsumer, IPluginStatusProvider
{
    public const int AmountIndex = 0;
    public const int ThresholdIndex = 1;
    public const int HighCutIndex = 2;
    public const int ScaleIndex = 3;

    private float _amount = 0.6f;
    private float _fluxThresholdDb = 1f;
    private float _highCutHz = 6000f;
    private int _amountScaleIndex;
    private int _sampleRate;
    private string _statusMessage = string.Empty;

    private const string MissingSidechainMessage = "Missing analysis data.";
    private const float BandLowCutHz = 2000f;
    private const float TransientCeiling = 0.35f;
    private const float FluxBaselineMs = 120f;
    private const float FluxGateKneeDb = 3f;
    // Hold the onset gate to align frame-based flux detection with time-domain boosting.
    private const float GateHoldMs = 25f;

    private float _fluxBaseline;
    private float _fluxBaselineCoeff;
    private float _gateHold;
    private float _gateHoldCoeff;

    private readonly BiquadFilter _highPass = new();
    private readonly BiquadFilter _lowPass = new();
    private readonly EnvelopeFollower _fastEnv = new();
    private readonly EnvelopeFollower _slowEnv = new();

    // Metering
    private float _meterOnsetGate;
    private float _meterTransientDetected;
    private float _meterOnsetDb;
    private float _meterBoostDb;
    private float _meterTransientRatio;
    private float _meterTransientRaw;
    private float _meterFastEnv;
    private float _meterSlowEnv;
    private float _meterBaseBoostDb;
    private int _latchedOnsetGateBits;
    private int _latchedOnsetDbBits;
    private int _latchedBoostDbBits;
    private int _latchedBaseBoostDbBits;
    private int _latchedTransientRatioBits;
    private int _latchedTransientRawBits;
    private int _latchedFastEnvBits;
    private int _latchedSlowEnvBits;
    private int _latchedTransientDetectedBits;

    public ConsonantTransientPlugin()
    {
        Parameters =
        [
            new PluginParameter { Index = AmountIndex, Name = "Amount", MinValue = 0f, MaxValue = 1f, DefaultValue = 0.6f, Unit = string.Empty },
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
    public int AmountScaleIndex => _amountScaleIndex;
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
        _gateHold = 0f;
        _gateHoldCoeff = DspUtils.TimeToCoefficient(GateHoldMs, sampleRate);
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
        float maxGate = 0f;
        float maxOnsetDb = 0f;
        float maxBoostDb = 0f;
        float maxTransientRatio = 0f;
        float maxTransientRaw = 0f;
        float maxFast = 0f;
        float maxSlow = 0f;
        float maxBaseBoostDb = 0f;
        float transientDetected = 0f;

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
            float onsetDeltaDb = flux - _fluxBaseline;
            float onsetOverThresholdDb = onsetDeltaDb - _fluxThresholdDb;
            float gate = onsetOverThresholdDb > 0f ? onsetOverThresholdDb / (onsetOverThresholdDb + FluxGateKneeDb) : 0f;
            float heldGate = ApplyGateHold(gate);

            float amount = _amount;
            float baseGain = 1f + amount * heldGate;
            float baseBoostDb = DspUtils.LinearToDb(baseGain);
            float scale = EnhanceAmountScale.FromIndex(_amountScaleIndex);
            float scaledBoostDb = baseBoostDb * scale;
            float gain = DspUtils.DbToLinear(scaledBoostDb);
            float boost = band * (gain - 1f);
            boost = SoftClip(boost, TransientCeiling * scale);
            buffer[i] = input + boost;

            // Update metering (track peak within this block)
            if (gate > maxGate)
            {
                maxGate = gate;
            }

            float onsetDb = MathF.Max(0f, onsetDeltaDb);
            if (onsetDb > maxOnsetDb)
            {
                maxOnsetDb = onsetDb;
            }

            float boostDb = scaledBoostDb;
            if (boostDb > maxBoostDb)
            {
                maxBoostDb = boostDb;
            }

            if (gain > 1.01f)
            {
                transientDetected = 1f;
            }

            if (norm > maxTransientRatio)
            {
                maxTransientRatio = norm;
            }

            if (transient > maxTransientRaw)
            {
                maxTransientRaw = transient;
            }

            if (fast > maxFast)
            {
                maxFast = fast;
            }

            if (slow > maxSlow)
            {
                maxSlow = slow;
            }

            if (baseBoostDb > maxBaseBoostDb)
            {
                maxBaseBoostDb = baseBoostDb;
            }
        }

        _meterOnsetGate = maxGate;
        _meterTransientDetected = transientDetected;
        _meterOnsetDb = maxOnsetDb;
        _meterBoostDb = maxBoostDb;
        _meterTransientRatio = maxTransientRatio;
        _meterTransientRaw = maxTransientRaw;
        _meterFastEnv = maxFast;
        _meterSlowEnv = maxSlow;
        _meterBaseBoostDb = maxBaseBoostDb;

        UpdateLatchedMax(ref _latchedOnsetGateBits, maxGate);
        UpdateLatchedMax(ref _latchedOnsetDbBits, maxOnsetDb);
        UpdateLatchedMax(ref _latchedBoostDbBits, maxBoostDb);
        UpdateLatchedMax(ref _latchedBaseBoostDbBits, maxBaseBoostDb);
        UpdateLatchedMax(ref _latchedTransientRatioBits, maxTransientRatio);
        UpdateLatchedMax(ref _latchedTransientRawBits, maxTransientRaw);
        UpdateLatchedMax(ref _latchedFastEnvBits, maxFast);
        UpdateLatchedMax(ref _latchedSlowEnvBits, maxSlow);
        UpdateLatchedMax(ref _latchedTransientDetectedBits, transientDetected);
    }

    /// <summary>Gets the current onset gate level (0-1).</summary>
    public float GetOnsetGate() => Volatile.Read(ref _meterOnsetGate);

    /// <summary>Gets the onset delta above baseline (dB).</summary>
    public float GetOnsetDb() => Volatile.Read(ref _meterOnsetDb);

    /// <summary>Gets the applied boost amount (dB).</summary>
    public float GetBoostDb() => Volatile.Read(ref _meterBoostDb);

    /// <summary>Gets the transient ratio (fast-slow)/slow.</summary>
    public float GetTransientRatio() => Volatile.Read(ref _meterTransientRatio);

    /// <summary>Gets the raw transient envelope delta.</summary>
    public float GetTransientRaw() => Volatile.Read(ref _meterTransientRaw);

    /// <summary>Gets the fast envelope peak.</summary>
    public float GetFastEnvelope() => Volatile.Read(ref _meterFastEnv);

    /// <summary>Gets the slow envelope peak.</summary>
    public float GetSlowEnvelope() => Volatile.Read(ref _meterSlowEnv);

    /// <summary>Gets the pre-scale boost amount (dB).</summary>
    public float GetBaseBoostDb() => Volatile.Read(ref _meterBaseBoostDb);

    /// <summary>Gets whether a transient is currently detected (0 or 1).</summary>
    public float GetTransientDetected() => Volatile.Read(ref _meterTransientDetected);

    public float ConsumeDebugOnsetGate() => ConsumeLatched(ref _latchedOnsetGateBits);
    public float ConsumeDebugOnsetDb() => ConsumeLatched(ref _latchedOnsetDbBits);
    public float ConsumeDebugBoostDb() => ConsumeLatched(ref _latchedBoostDbBits);
    public float ConsumeDebugBaseBoostDb() => ConsumeLatched(ref _latchedBaseBoostDbBits);
    public float ConsumeDebugTransientRatio() => ConsumeLatched(ref _latchedTransientRatioBits);
    public float ConsumeDebugTransientRaw() => ConsumeLatched(ref _latchedTransientRawBits);
    public float ConsumeDebugFastEnv() => ConsumeLatched(ref _latchedFastEnvBits);
    public float ConsumeDebugSlowEnv() => ConsumeLatched(ref _latchedSlowEnvBits);
    public float ConsumeDebugTransientDetected() => ConsumeLatched(ref _latchedTransientDetectedBits);

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
            float amount = _amount;
            float transientScale = MathF.Min(1.5f, norm * 2f);
            float baseGain = 1f + amount * transientScale;
            float baseBoostDb = DspUtils.LinearToDb(baseGain);
            float scale = EnhanceAmountScale.FromIndex(_amountScaleIndex);
            float scaledBoostDb = baseBoostDb * scale;
            float gain = DspUtils.DbToLinear(scaledBoostDb);
            float boost = band * (gain - 1f);
            boost = SoftClip(boost, TransientCeiling * scale);
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
            case ScaleIndex:
                _amountScaleIndex = EnhanceAmountScale.ClampIndex(value);
                break;
        }
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 4];
        Buffer.BlockCopy(BitConverter.GetBytes(_amount), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_fluxThresholdDb), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_highCutHz), 0, bytes, 8, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_amountScaleIndex), 0, bytes, 12, 4);
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
        if (state.Length >= sizeof(float) * 4)
        {
            _amountScaleIndex = EnhanceAmountScale.ClampIndex(BitConverter.ToSingle(state, 12));
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

    private float ApplyGateHold(float value)
    {
        if (value > _gateHold)
        {
            _gateHold = value;
        }
        else
        {
            _gateHold += _gateHoldCoeff * (value - _gateHold);
        }

        return _gateHold;
    }

    private static void UpdateLatchedMax(ref int bits, float value)
    {
        int currentBits = Volatile.Read(ref bits);
        float current = BitConverter.Int32BitsToSingle(currentBits);
        if (value <= current)
        {
            return;
        }

        int nextBits = BitConverter.SingleToInt32Bits(value);
        while (true)
        {
            int observed = Interlocked.CompareExchange(ref bits, nextBits, currentBits);
            if (observed == currentBits)
            {
                return;
            }

            currentBits = observed;
            current = BitConverter.Int32BitsToSingle(currentBits);
            if (value <= current)
            {
                return;
            }
        }
    }

    private static float ConsumeLatched(ref int bits)
    {
        return BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref bits, 0));
    }
}
