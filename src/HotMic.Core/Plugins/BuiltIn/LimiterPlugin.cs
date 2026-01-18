using HotMic.Core.Dsp;
using HotMic.Core.Plugins;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class LimiterPlugin : IContextualPlugin
{
    public const int CeilingIndex = 0;
    public const int ReleaseIndex = 1;

    private float _ceilingDb = -1f;
    private float _releaseMs = 50f;
    private float _ceilingLinear = 0.891f;
    private float _releaseCoeff;
    private float _gain = 1f;
    private int _sampleRate;

    // Thread-safe metering
    private int _inputLevelBits;
    private int _outputLevelBits;
    private int _gainReductionBits;

    public LimiterPlugin()
    {
        Parameters =
        [
            new PluginParameter { Index = CeilingIndex, Name = "Ceiling", MinValue = -3f, MaxValue = 0f, DefaultValue = -1f, Unit = "dB" },
            new PluginParameter { Index = ReleaseIndex, Name = "Release", MinValue = 10f, MaxValue = 200f, DefaultValue = 50f, Unit = "ms" }
        ];
    }

    public string Id => "builtin:limiter";

    public string Name => "Limiter";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public float CeilingDb => _ceilingDb;
    public float ReleaseMs => _releaseMs;
    public int SampleRate => _sampleRate;

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _gain = 1f;
        UpdateCoefficients();
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        Process(buffer);
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return;
        }

        float gain = _gain;
        float ceiling = _ceilingLinear;
        float releaseCoeff = _releaseCoeff;
        float inputPeak = 0f;
        float outputPeak = 0f;
        float minGain = 1f;

        for (int i = 0; i < buffer.Length; i++)
        {
            float input = buffer[i];
            float abs = MathF.Abs(input);
            inputPeak = MathF.Max(inputPeak, abs);

            float targetGain = abs > ceiling ? ceiling / abs : 1f;

            if (targetGain < gain)
            {
                gain = targetGain;
            }
            else
            {
                gain += releaseCoeff * (targetGain - gain);
            }

            minGain = MathF.Min(minGain, gain);
            float output = input * gain;
            outputPeak = MathF.Max(outputPeak, MathF.Abs(output));
            buffer[i] = output;
        }

        _gain = gain;

        // Update metering (thread-safe)
        UpdatePeakLevel(ref _inputLevelBits, inputPeak);
        UpdatePeakLevel(ref _outputLevelBits, outputPeak);
        float grDb = minGain < 1f ? DspUtils.LinearToDb(minGain) : 0f;
        Interlocked.Exchange(ref _gainReductionBits, BitConverter.SingleToInt32Bits(grDb));
    }

    private static void UpdatePeakLevel(ref int levelBits, float newPeak)
    {
        int current = Interlocked.CompareExchange(ref levelBits, 0, 0);
        float currentPeak = BitConverter.Int32BitsToSingle(current);
        if (newPeak > currentPeak)
        {
            Interlocked.Exchange(ref levelBits, BitConverter.SingleToInt32Bits(newPeak));
        }
    }

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case CeilingIndex:
                _ceilingDb = Math.Clamp(value, -3f, 0f);
                break;
            case ReleaseIndex:
                _releaseMs = Math.Clamp(value, 10f, 200f);
                break;
        }

        UpdateCoefficients();
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 2];
        Buffer.BlockCopy(BitConverter.GetBytes(_ceilingDb), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_releaseMs), 0, bytes, 4, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float))
        {
            return;
        }

        _ceilingDb = BitConverter.ToSingle(state, 0);
        if (state.Length >= sizeof(float) * 2)
        {
            _releaseMs = BitConverter.ToSingle(state, 4);
        }

        UpdateCoefficients();
    }

    public float GetAndResetInputLevel()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _inputLevelBits, 0));
    }

    public float GetAndResetOutputLevel()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _outputLevelBits, 0));
    }

    public float GetGainReductionDb()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.CompareExchange(ref _gainReductionBits, 0, 0));
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

        _ceilingLinear = DspUtils.DbToLinear(_ceilingDb);
        _releaseCoeff = DspUtils.TimeToCoefficient(_releaseMs, _sampleRate);
    }
}
