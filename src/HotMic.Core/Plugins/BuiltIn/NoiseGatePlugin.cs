using System.Threading;
using HotMic.Core.Dsp;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class NoiseGatePlugin : IPlugin
{
    public const int ThresholdIndex = 0;
    public const int HysteresisIndex = 1;
    public const int AttackIndex = 2;
    public const int HoldIndex = 3;
    public const int ReleaseIndex = 4;

    private float _thresholdDb = -40f;
    private float _hysteresisDb = 4f;
    private float _attackMs = 1f;
    private float _holdMs = 50f;
    private float _releaseMs = 100f;

    private float _thresholdLinear;
    private float _closeThresholdLinear;
    private float _attackCoeff;
    private float _releaseCoeff;
    private float _gain;
    private int _holdSamples;
    private int _holdSamplesLeft;
    private int _sampleRate;
    private bool _gateOpen;
    private int _gateOpenBits;
    private float _inputLevel;

    private readonly EnvelopeFollower _envelope = new();

    public NoiseGatePlugin()
    {
        Parameters =
        [
            new PluginParameter { Index = ThresholdIndex, Name = "Threshold", MinValue = -80f, MaxValue = 0f, DefaultValue = -40f, Unit = "dB" },
            new PluginParameter { Index = HysteresisIndex, Name = "Hysteresis", MinValue = 0f, MaxValue = 12f, DefaultValue = 4f, Unit = "dB" },
            new PluginParameter { Index = AttackIndex, Name = "Attack", MinValue = 0.1f, MaxValue = 50f, DefaultValue = 1f, Unit = "ms" },
            new PluginParameter { Index = HoldIndex, Name = "Hold", MinValue = 0f, MaxValue = 500f, DefaultValue = 50f, Unit = "ms" },
            new PluginParameter { Index = ReleaseIndex, Name = "Release", MinValue = 10f, MaxValue = 500f, DefaultValue = 100f, Unit = "ms" }
        ];
    }

    public string Id => "builtin:noisegate";

    public string Name => "Noise Gate";

    public bool IsBypassed { get; set; }

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        UpdateCoefficients();
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed)
        {
            return;
        }

        for (int i = 0; i < buffer.Length; i++)
        {
            float sample = buffer[i];
            float env = _envelope.Process(sample);

            if (_gateOpen)
            {
                // Use lower close threshold (threshold - hysteresis) to prevent chatter
                if (env < _closeThresholdLinear)
                {
                    if (_holdSamplesLeft <= 0)
                    {
                        _holdSamplesLeft = _holdSamples;
                    }
                    else
                    {
                        _holdSamplesLeft--;
                        if (_holdSamplesLeft <= 0)
                        {
                            _gateOpen = false;
                        }
                    }
                }
                else
                {
                    _holdSamplesLeft = _holdSamples;
                }
            }
            else if (env >= _thresholdLinear)
            {
                // Gate opens at the main threshold
                _gateOpen = true;
                _holdSamplesLeft = _holdSamples;
            }

            float target = _gateOpen ? 1f : 0f;
            float coeff = target > _gain ? _attackCoeff : _releaseCoeff;
            _gain += coeff * (target - _gain);
            buffer[i] = sample * _gain;

            // Track peak input level for metering
            float absVal = MathF.Abs(sample);
            if (absVal > _inputLevel)
                _inputLevel = absVal;
        }

        Interlocked.Exchange(ref _gateOpenBits, _gateOpen ? 1 : 0);
    }

    /// <summary>
    /// Gets the current input level (peak) and resets it.
    /// Call this from the UI update thread at your desired meter rate.
    /// </summary>
    public float GetAndResetInputLevel()
    {
        float level = _inputLevel;
        _inputLevel = 0f;
        return level;
    }

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case ThresholdIndex:
                _thresholdDb = value;
                break;
            case HysteresisIndex:
                _hysteresisDb = value;
                break;
            case AttackIndex:
                _attackMs = value;
                break;
            case HoldIndex:
                _holdMs = value;
                break;
            case ReleaseIndex:
                _releaseMs = value;
                break;
        }

        UpdateCoefficients();
    }

    public bool IsGateOpen()
    {
        return Interlocked.CompareExchange(ref _gateOpenBits, 0, 0) == 1;
    }

    // Current parameter values for UI binding
    public float ThresholdDb => _thresholdDb;
    public float HysteresisDb => _hysteresisDb;
    public float AttackMs => _attackMs;
    public float HoldMs => _holdMs;
    public float ReleaseMs => _releaseMs;

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 5];
        Buffer.BlockCopy(BitConverter.GetBytes(_thresholdDb), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_hysteresisDb), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_attackMs), 0, bytes, 8, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_holdMs), 0, bytes, 12, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_releaseMs), 0, bytes, 16, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float) * 4)
        {
            return;
        }

        _thresholdDb = BitConverter.ToSingle(state, 0);
        // Handle old state format (4 floats) vs new (5 floats)
        if (state.Length >= sizeof(float) * 5)
        {
            _hysteresisDb = BitConverter.ToSingle(state, 4);
            _attackMs = BitConverter.ToSingle(state, 8);
            _holdMs = BitConverter.ToSingle(state, 12);
            _releaseMs = BitConverter.ToSingle(state, 16);
        }
        else
        {
            _attackMs = BitConverter.ToSingle(state, 4);
            _holdMs = BitConverter.ToSingle(state, 8);
            _releaseMs = BitConverter.ToSingle(state, 12);
        }
        UpdateCoefficients();
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

        _thresholdLinear = DspUtils.DbToLinear(_thresholdDb);
        // Close threshold is lower by hysteresis amount to prevent chatter
        _closeThresholdLinear = DspUtils.DbToLinear(_thresholdDb - _hysteresisDb);
        _attackCoeff = DspUtils.TimeToCoefficient(_attackMs, _sampleRate);
        _releaseCoeff = DspUtils.TimeToCoefficient(_releaseMs, _sampleRate);
        _holdSamples = (int)(_holdMs * 0.001f * _sampleRate);
        _envelope.Configure(_attackMs, _releaseMs, _sampleRate);
    }
}
