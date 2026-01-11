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

    private float _thresholdDb = -20f;
    private float _ratio = 4f;
    private float _attackMs = 10f;
    private float _releaseMs = 100f;
    private float _makeupDb;

    private float _thresholdLinear;
    private float _makeupLinear = 1f;
    private float _attackCoeff;
    private float _releaseCoeff;
    private float _envelope;

    private int _sampleRate;
    private int _blockSize;
    private int _gainReductionBits;
    private float _inputLevel;

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

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _blockSize = blockSize;
        UpdateCoefficients();
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed)
        {
            return;
        }

        float maxReduction = 0f;
        for (int i = 0; i < buffer.Length; i++)
        {
            float input = buffer[i];
            float inputAbs = MathF.Abs(input);

            float coeff = inputAbs > _envelope ? _attackCoeff : _releaseCoeff;
            _envelope += coeff * (inputAbs - _envelope);

            float gainDb = 0f;
            if (_envelope > _thresholdLinear)
            {
                float overDb = 20f * MathF.Log10(_envelope / _thresholdLinear);
                gainDb = overDb * (1f - 1f / _ratio);
            }

            if (gainDb > maxReduction)
            {
                maxReduction = gainDb;
            }

            float gainLinear = MathF.Pow(10f, -gainDb / 20f) * _makeupLinear;
            buffer[i] = input * gainLinear;

            // Track peak input level for metering
            if (inputAbs > _inputLevel)
                _inputLevel = inputAbs;
        }

        Interlocked.Exchange(ref _gainReductionBits, BitConverter.SingleToInt32Bits(maxReduction));
    }

    /// <summary>
    /// Gets the current input level (peak) and resets it.
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

        _thresholdLinear = DspUtils.DbToLinear(_thresholdDb);
        _makeupLinear = DspUtils.DbToLinear(_makeupDb);
        _attackCoeff = DspUtils.TimeToCoefficient(_attackMs, _sampleRate);
        _releaseCoeff = DspUtils.TimeToCoefficient(_releaseMs, _sampleRate);
    }
}
