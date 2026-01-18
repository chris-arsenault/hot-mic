using System.Threading;
using HotMic.Common.Configuration;
using HotMic.Core.Dsp;
using HotMic.Core.Plugins;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class NoiseGatePlugin : IPlugin, IQualityConfigurablePlugin
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
    private float _detectorAttackCoeff;
    private float _detectorReleaseCoeff;
    private float _gainAttackCoeff;
    private float _gainReleaseCoeff;
    private float _gain;
    private int _holdSamples;
    private int _holdSamplesLeft;
    private int _sampleRate;
    private bool _gateOpen;
    private int _gateOpenBits;
    private int _inputLevelBits;

    private float _detector;
    // Mutable struct: keep non-readonly so filter state persists across samples.
    private OnePoleHighPass _sidechainFilter = new();
    private float _sidechainHpfHz = 80f;
    private float _gateRatio = 3f;
    private float _maxRangeDb = 24f;
    private const float ClosedGateFloorDb = 90f; // Minimum attenuation (in dB) when the gate is closed.
    private float _closedGateFloorLinear;

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

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public int SampleRate => _sampleRate;

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _detector = 0f;
        _gain = 1f;
        _gateOpen = false;
        _holdSamplesLeft = 0;
        Interlocked.Exchange(ref _gateOpenBits, 0);
        Interlocked.Exchange(ref _inputLevelBits, 0);
        UpdateCoefficients();
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        Process(buffer);
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed)
        {
            return;
        }

        float peak = 0f;
        for (int i = 0; i < buffer.Length; i++)
        {
            float sample = buffer[i];
            // Voice-friendly detector: HPF sidechain + RMS smoothing to reduce plosive bias.
            float sidechain = _sidechainFilter.Process(sample);
            float power = sidechain * sidechain;

            float detectorCoeff = power > _detector ? _detectorAttackCoeff : _detectorReleaseCoeff;
            _detector += detectorCoeff * (power - _detector);
            float env = MathF.Sqrt(_detector + 1e-12f);

            if (_gateOpen)
            {
                if (env < _closeThresholdLinear)
                {
                    if (_holdSamples <= 0)
                    {
                        _gateOpen = false;
                    }
                    else if (_holdSamplesLeft <= 0)
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
                _gateOpen = true;
                _holdSamplesLeft = _holdSamples;
            }

            float targetGain;
            if (_gateOpen)
            {
                targetGain = 1f;
            }
            else
            {
                float envDb = DspUtils.LinearToDb(env);
                float overDb = _thresholdDb - envDb;
                float reductionDb = MathF.Min(_maxRangeDb, overDb * (1f - 1f / _gateRatio));
                targetGain = DspUtils.DbToLinear(-reductionDb);
                targetGain = MathF.Min(targetGain, _closedGateFloorLinear);
            }

            float gainCoeff = targetGain > _gain ? _gainAttackCoeff : _gainReleaseCoeff;
            _gain += gainCoeff * (targetGain - _gain);
            buffer[i] = sample * _gain;

            float absInput = MathF.Abs(sample);
            if (absInput > peak)
            {
                peak = absInput;
            }
        }

        Interlocked.Exchange(ref _gateOpenBits, _gateOpen ? 1 : 0);
        Interlocked.Exchange(ref _inputLevelBits, BitConverter.SingleToInt32Bits(peak));
    }

    /// <summary>
    /// Gets the current input level (peak) and resets it.
    /// Call this from the UI update thread at your desired meter rate.
    /// </summary>
    public float GetAndResetInputLevel()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _inputLevelBits, 0));
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

    public void ApplyQuality(AudioQualityProfile profile)
    {
        _sidechainHpfHz = MathF.Max(20f, profile.GateSidechainHpfHz);
        _gateRatio = MathF.Max(1f, profile.GateRatio);
        _maxRangeDb = MathF.Max(0f, profile.GateMaxRangeDb);
        if (_sampleRate > 0)
        {
            UpdateCoefficients();
        }
    }

    private void UpdateCoefficients()
    {
        if (_sampleRate <= 0)
        {
            return;
        }

        _thresholdLinear = DspUtils.DbToLinear(_thresholdDb);
        _closeThresholdLinear = DspUtils.DbToLinear(_thresholdDb - _hysteresisDb);
        _detectorAttackCoeff = DspUtils.TimeToCoefficient(_attackMs, _sampleRate);
        _detectorReleaseCoeff = DspUtils.TimeToCoefficient(_releaseMs, _sampleRate);
        _gainAttackCoeff = _detectorAttackCoeff;
        _gainReleaseCoeff = _detectorReleaseCoeff;
        _holdSamples = (int)(_holdMs * 0.001f * _sampleRate);
        float gateFloorDb = MathF.Max(_maxRangeDb, ClosedGateFloorDb);
        _closedGateFloorLinear = DspUtils.DbToLinear(-gateFloorDb);
        _sidechainFilter.Configure(_sidechainHpfHz, _sampleRate);
    }
}
