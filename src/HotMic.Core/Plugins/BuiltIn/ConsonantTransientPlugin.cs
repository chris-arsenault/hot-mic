using System.Threading;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class ConsonantTransientPlugin : IPlugin, IAnalysisSignalConsumer, IPluginStatusProvider
{
    public const int AmountIndex = 0;
    public const int ThresholdIndex = 1;
    public const int HighCutIndex = 2;

    private float _amount = 0.6f;
    private float _threshold = 0.15f;
    private float _highCutHz = 6000f;
    private int _sampleRate;
    private string _statusMessage = string.Empty;

    private const string MissingSidechainMessage = "Missing analysis data.";
    private const float BandLowCutHz = 2000f;
    private const float TransientCeiling = 0.35f;

    private readonly BiquadFilter _highPass = new();
    private readonly BiquadFilter _lowPass = new();
    private readonly EnvelopeFollower _fastEnv = new();
    private readonly EnvelopeFollower _slowEnv = new();

    // Metering
    private float _meterOnsetGate;
    private float _meterFastEnvelope;
    private float _meterSlowEnvelope;
    private float _meterTransientDetected;

    public ConsonantTransientPlugin()
    {
        Parameters =
        [
            new PluginParameter { Index = AmountIndex, Name = "Amount", MinValue = 0f, MaxValue = 1f, DefaultValue = 0.6f, Unit = string.Empty },
            new PluginParameter { Index = ThresholdIndex, Name = "Threshold", MinValue = 0f, MaxValue = 0.5f, DefaultValue = 0.15f, Unit = string.Empty },
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
    public float Threshold => _threshold;
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
            float gate = FluxToGate(flux);

            float gain = norm > _threshold ? 1f + _amount * gate * MathF.Min(1.5f, norm * 2f) : 1f;
            float boost = band * (gain - 1f);
            boost = SoftClip(boost, TransientCeiling);
            buffer[i] = input + boost;

            // Update metering
            _meterOnsetGate = gate;
            _meterFastEnvelope = fast;
            _meterSlowEnvelope = slow;
            _meterTransientDetected = norm > _threshold && gate > 0.1f ? 1f : 0f;
        }
    }

    /// <summary>Gets the current onset gate level (0-1).</summary>
    public float GetOnsetGate() => Volatile.Read(ref _meterOnsetGate);

    /// <summary>Gets the fast envelope level.</summary>
    public float GetFastEnvelope() => Volatile.Read(ref _meterFastEnvelope);

    /// <summary>Gets the slow envelope level.</summary>
    public float GetSlowEnvelope() => Volatile.Read(ref _meterSlowEnvelope);

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
            float gain = norm > _threshold ? 1f + _amount * MathF.Min(1.5f, norm * 2f) : 1f;
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
                _threshold = Math.Clamp(value, 0f, 0.5f);
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
        Buffer.BlockCopy(BitConverter.GetBytes(_threshold), 0, bytes, 4, 4);
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
        _threshold = BitConverter.ToSingle(state, 4);
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

    private static float FluxToGate(float flux)
    {
        if (flux <= 0f)
        {
            return 0f;
        }

        const float Knee = 0.02f;
        return flux / (flux + Knee);
    }

    private static float SoftClip(float value, float ceiling)
    {
        float scale = MathF.Max(1e-6f, ceiling);
        float normalized = value / scale;
        return MathF.Tanh(normalized) * scale;
    }
}
