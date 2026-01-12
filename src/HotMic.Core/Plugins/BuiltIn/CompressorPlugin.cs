using System.Threading;
using HotMic.Common.Configuration;
using HotMic.Core.Dsp;
using HotMic.Core.Plugins;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class CompressorPlugin : IPlugin, IQualityConfigurablePlugin
{
    public const int ThresholdIndex = 0;
    public const int RatioIndex = 1;
    public const int AttackIndex = 2;
    public const int ReleaseIndex = 3;
    public const int MakeupIndex = 4;
    public const int KneeIndex = 5;
    public const int DetectorIndex = 6;
    public const int SidechainIndex = 7;

    private float _kneeDb = 6f;
    private float _rmsBlend = 0.7f;
    private float _releaseShape = 0.15f;
    private float _sidechainHpfHz = 80f;

    private float _thresholdDb = -20f;
    private float _ratio = 4f;
    private float _attackMs = 10f;
    private float _releaseMs = 100f;
    private float _makeupDb;

    private float _makeupLinear = 1f;
    private float _attackCoeff;
    private float _releaseCoeff;
    private float _detectorAttackCoeff;
    private float _detectorReleaseCoeff;

    private float _rmsPower;
    private float _envelopeDb = -120f;
    private float _gainReductionDb;

    private int _sampleRate;
    private int _gainReductionBits;
    private int _inputLevelBits;
    private CompressorDetectorMode _detectorMode = CompressorDetectorMode.Blend;
    private bool _sidechainHpfEnabled = true;

    private readonly OnePoleHighPass _sidechainFilter = new();

    public CompressorPlugin()
    {
        Parameters =
        [
            new PluginParameter { Index = ThresholdIndex, Name = "Threshold", MinValue = -60f, MaxValue = 0f, DefaultValue = -20f, Unit = "dB" },
            new PluginParameter { Index = RatioIndex, Name = "Ratio", MinValue = 1f, MaxValue = 20f, DefaultValue = 4f, Unit = ":1" },
            new PluginParameter { Index = AttackIndex, Name = "Attack", MinValue = 0.1f, MaxValue = 100f, DefaultValue = 10f, Unit = "ms" },
            new PluginParameter { Index = ReleaseIndex, Name = "Release", MinValue = 10f, MaxValue = 1000f, DefaultValue = 100f, Unit = "ms" },
            new PluginParameter { Index = MakeupIndex, Name = "Makeup", MinValue = 0f, MaxValue = 24f, DefaultValue = 0f, Unit = "dB" },
            new PluginParameter { Index = KneeIndex, Name = "Knee", MinValue = 0f, MaxValue = 12f, DefaultValue = 6f, Unit = "dB" },
            new PluginParameter { Index = DetectorIndex, Name = "Detector", MinValue = 0f, MaxValue = 2f, DefaultValue = 2f, Unit = string.Empty },
            new PluginParameter { Index = SidechainIndex, Name = "Sidechain HPF", MinValue = 0f, MaxValue = 1f, DefaultValue = 1f, Unit = string.Empty }
        ];
    }

    public string Id => "builtin:compressor";

    public string Name => "Compressor";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public int SampleRate => _sampleRate;

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _rmsPower = 0f;
        _envelopeDb = -120f;
        _gainReductionDb = 0f;
        UpdateCoefficients();
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed)
        {
            return;
        }

        float maxReduction = 0f;
        float peakInput = 0f;

        for (int i = 0; i < buffer.Length; i++)
        {
            float input = buffer[i];
            float absInput = MathF.Abs(input);
            if (absInput > peakInput)
            {
                peakInput = absInput;
            }

            float sidechain = _sidechainHpfEnabled ? _sidechainFilter.Process(input) : input;
            float absSidechain = MathF.Abs(sidechain);
            float rms = absSidechain;
            if (_detectorMode != CompressorDetectorMode.Peak)
            {
                float power = absSidechain * absSidechain;
                float detectorCoeff = power > _rmsPower ? _detectorAttackCoeff : _detectorReleaseCoeff;
                _rmsPower += detectorCoeff * (power - _rmsPower);
                rms = MathF.Sqrt(_rmsPower + 1e-12f);
            }

            // Keep a full-band peak path so sidechain HPF does not null low-frequency detection.
            float detectorLinear = _detectorMode switch
            {
                CompressorDetectorMode.Rms => rms,
                CompressorDetectorMode.Peak => absInput,
                _ => absInput * (1f - _rmsBlend) + rms * _rmsBlend
            };

            float detectorDb = DspUtils.LinearToDb(detectorLinear);
            float envCoeff = detectorDb > _envelopeDb ? _attackCoeff : _releaseCoeff;
            _envelopeDb += envCoeff * (detectorDb - _envelopeDb);

            float delta = _envelopeDb - _thresholdDb;

            float desiredReductionDb;
            if (_kneeDb <= 0.001f)
            {
                desiredReductionDb = delta > 0f ? delta * (1f - 1f / _ratio) : 0f;
            }
            else if (delta <= -_kneeDb * 0.5f)
            {
                desiredReductionDb = 0f;
            }
            else if (delta >= _kneeDb * 0.5f)
            {
                desiredReductionDb = delta * (1f - 1f / _ratio);
            }
            else
            {
                float x = delta + _kneeDb * 0.5f;
                desiredReductionDb = (1f - 1f / _ratio) * x * x / (2f * _kneeDb);
            }

            // Program-dependent release: heavier gain reduction releases more slowly.
            float shapedRelease = _releaseCoeff / (1f + _releaseShape * _gainReductionDb);
            float gainCoeff = desiredReductionDb > _gainReductionDb ? _attackCoeff : shapedRelease;
            _gainReductionDb += gainCoeff * (desiredReductionDb - _gainReductionDb);

            if (_gainReductionDb > maxReduction)
            {
                maxReduction = _gainReductionDb;
            }

            float gainLinear = DspUtils.DbToLinear(-_gainReductionDb) * _makeupLinear;
            buffer[i] = input * gainLinear;
        }

        Interlocked.Exchange(ref _gainReductionBits, BitConverter.SingleToInt32Bits(maxReduction));
        Interlocked.Exchange(ref _inputLevelBits, BitConverter.SingleToInt32Bits(peakInput));
    }

    /// <summary>
    /// Gets the current input level (peak) and resets it.
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
            case RatioIndex:
                _ratio = MathF.Max(1f, value);
                break;
            case AttackIndex:
                _attackMs = value;
                break;
            case ReleaseIndex:
                _releaseMs = value;
                break;
            case MakeupIndex:
                _makeupDb = value;
                break;
            case KneeIndex:
                _kneeDb = Math.Clamp(value, 0f, 12f);
                break;
            case DetectorIndex:
                _detectorMode = value switch
                {
                    >= 1.5f => CompressorDetectorMode.Blend,
                    >= 0.5f => CompressorDetectorMode.Rms,
                    _ => CompressorDetectorMode.Peak
                };
                break;
            case SidechainIndex:
                _sidechainHpfEnabled = value >= 0.5f;
                _sidechainFilter.Reset();
                break;
        }

        UpdateCoefficients();
    }

    public float GetGainReductionDb()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.CompareExchange(ref _gainReductionBits, 0, 0));
    }

    // Current parameter values for UI binding
    public float ThresholdDb => _thresholdDb;
    public float Ratio => _ratio;
    public float AttackMs => _attackMs;
    public float ReleaseMs => _releaseMs;
    public float MakeupDb => _makeupDb;
    public float KneeDb => _kneeDb;
    public CompressorDetectorMode DetectorMode => _detectorMode;
    public bool IsSidechainHpfEnabled => _sidechainHpfEnabled;

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 8];
        Buffer.BlockCopy(BitConverter.GetBytes(_thresholdDb), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_ratio), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_attackMs), 0, bytes, 8, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_releaseMs), 0, bytes, 12, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_makeupDb), 0, bytes, 16, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_kneeDb), 0, bytes, 20, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_detectorMode), 0, bytes, 24, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_sidechainHpfEnabled ? 1f : 0f), 0, bytes, 28, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float) * 5)
        {
            return;
        }

        _thresholdDb = BitConverter.ToSingle(state, 0);
        _ratio = BitConverter.ToSingle(state, 4);
        _attackMs = BitConverter.ToSingle(state, 8);
        _releaseMs = BitConverter.ToSingle(state, 12);
        _makeupDb = BitConverter.ToSingle(state, 16);
        if (state.Length >= sizeof(float) * 6)
        {
            _kneeDb = BitConverter.ToSingle(state, 20);
        }
        if (state.Length >= sizeof(float) * 7)
        {
            float detector = BitConverter.ToSingle(state, 24);
            _detectorMode = detector switch
            {
                >= 1.5f => CompressorDetectorMode.Blend,
                >= 0.5f => CompressorDetectorMode.Rms,
                _ => CompressorDetectorMode.Peak
            };
        }
        if (state.Length >= sizeof(float) * 8)
        {
            _sidechainHpfEnabled = BitConverter.ToSingle(state, 28) >= 0.5f;
        }
        UpdateCoefficients();
    }

    public void Dispose()
    {
    }

    public void ApplyQuality(AudioQualityProfile profile)
    {
        _kneeDb = MathF.Max(0.1f, profile.CompressorKneeDb);
        _rmsBlend = Math.Clamp(profile.CompressorRmsBlend, 0f, 1f);
        _releaseShape = MathF.Max(0f, profile.CompressorReleaseShape);
        _sidechainHpfHz = MathF.Max(20f, profile.CompressorSidechainHpfHz);
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

        _makeupLinear = DspUtils.DbToLinear(_makeupDb);
        _attackCoeff = DspUtils.TimeToCoefficient(_attackMs, _sampleRate);
        _releaseCoeff = DspUtils.TimeToCoefficient(_releaseMs, _sampleRate);
        _detectorAttackCoeff = _attackCoeff;
        _detectorReleaseCoeff = _releaseCoeff;
        if (_sidechainHpfEnabled)
        {
            _sidechainFilter.Configure(_sidechainHpfHz, _sampleRate);
        }
    }
}

public enum CompressorDetectorMode
{
    Peak = 0,
    Rms = 1,
    Blend = 2
}
