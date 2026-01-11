using System.Threading;
using HotMic.Core.Dsp;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class CompressorPlugin : IPlugin
{
    public const int ThresholdIndex = 0;
    public const int RatioIndex = 1;
    public const int AttackIndex = 2;
    public const int ReleaseIndex = 3;
    public const int MakeupIndex = 4;

    private const float KneeDb = 6f;
    private const float RmsBlend = 0.7f;
    private const float ReleaseShape = 0.15f;
    private const float SidechainHpfHz = 80f;

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
    private float _envelope;
    private float _gainReductionDb;

    private int _sampleRate;
    private int _gainReductionBits;
    private int _inputLevelBits;

    private readonly OnePoleHighPass _sidechainFilter = new();

    public CompressorPlugin()
    {
        Parameters =
        [
            new PluginParameter { Index = ThresholdIndex, Name = "Threshold", MinValue = -60f, MaxValue = 0f, DefaultValue = -20f, Unit = "dB" },
            new PluginParameter { Index = RatioIndex, Name = "Ratio", MinValue = 1f, MaxValue = 20f, DefaultValue = 4f, Unit = ":1" },
            new PluginParameter { Index = AttackIndex, Name = "Attack", MinValue = 0.1f, MaxValue = 100f, DefaultValue = 10f, Unit = "ms" },
            new PluginParameter { Index = ReleaseIndex, Name = "Release", MinValue = 10f, MaxValue = 1000f, DefaultValue = 100f, Unit = "ms" },
            new PluginParameter { Index = MakeupIndex, Name = "Makeup", MinValue = 0f, MaxValue = 24f, DefaultValue = 0f, Unit = "dB" }
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

            float sidechain = _sidechainFilter.Process(input);
            float absSidechain = MathF.Abs(sidechain);
            float power = absSidechain * absSidechain;

            // RMS detector with attack/release ballistics.
            float detectorCoeff = power > _rmsPower ? _detectorAttackCoeff : _detectorReleaseCoeff;
            _rmsPower += detectorCoeff * (power - _rmsPower);
            float rms = MathF.Sqrt(_rmsPower + 1e-12f);

            float detector = absSidechain * (1f - RmsBlend) + rms * RmsBlend;
            float envCoeff = detector > _envelope ? _attackCoeff : _releaseCoeff;
            _envelope += envCoeff * (detector - _envelope);

            float envDb = DspUtils.LinearToDb(_envelope);
            float delta = envDb - _thresholdDb;

            float desiredReductionDb;
            if (delta <= -KneeDb * 0.5f)
            {
                desiredReductionDb = 0f;
            }
            else if (delta >= KneeDb * 0.5f)
            {
                desiredReductionDb = delta * (1f - 1f / _ratio);
            }
            else
            {
                float x = delta + KneeDb * 0.5f;
                desiredReductionDb = (1f - 1f / _ratio) * x * x / (2f * KneeDb);
            }

            // Program-dependent release: heavier gain reduction releases more slowly.
            float shapedRelease = _releaseCoeff / (1f + ReleaseShape * _gainReductionDb);
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

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 5];
        Buffer.BlockCopy(BitConverter.GetBytes(_thresholdDb), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_ratio), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_attackMs), 0, bytes, 8, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_releaseMs), 0, bytes, 12, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_makeupDb), 0, bytes, 16, 4);
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

        _makeupLinear = DspUtils.DbToLinear(_makeupDb);
        _attackCoeff = DspUtils.TimeToCoefficient(_attackMs, _sampleRate);
        _releaseCoeff = DspUtils.TimeToCoefficient(_releaseMs, _sampleRate);
        _detectorAttackCoeff = _attackCoeff;
        _detectorReleaseCoeff = _releaseCoeff;
        _sidechainFilter.Configure(SidechainHpfHz, _sampleRate);
    }
}
