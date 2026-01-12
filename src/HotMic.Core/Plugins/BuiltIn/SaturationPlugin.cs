using HotMic.Core.Dsp;
using HotMic.Core.Plugins;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class SaturationPlugin : IPlugin
{
    public const int DriveIndex = 0;
    public const int MixIndex = 1;

    private const float SmoothingMs = 8f;

    private float _drivePct = 15f;
    private float _mixPct = 50f;
    private int _sampleRate;

    // Thread-safe metering
    private int _inputLevelBits;
    private int _outputLevelBits;

    private LinearSmoother _driveSmoother = new();
    private LinearSmoother _mixSmoother = new();

    public SaturationPlugin()
    {
        Parameters =
        [
            new PluginParameter { Index = DriveIndex, Name = "Drive", MinValue = 0f, MaxValue = 100f, DefaultValue = 15f, Unit = "%" },
            new PluginParameter { Index = MixIndex, Name = "Mix", MinValue = 0f, MaxValue = 100f, DefaultValue = 50f, Unit = "%" }
        ];
    }

    public string Id => "builtin:saturation";

    public string Name => "Saturation";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public float DrivePct => _drivePct;
    public float MixPct => _mixPct;
    public int SampleRate => _sampleRate;

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _driveSmoother.Configure(sampleRate, SmoothingMs, DrivePercentToGain(_drivePct));
        _mixSmoother.Configure(sampleRate, SmoothingMs, _mixPct / 100f);
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return;
        }

        float drive = _driveSmoother.Current;
        float mix = _mixSmoother.Current;
        bool driveSmooth = _driveSmoother.IsSmoothing;
        bool mixSmooth = _mixSmoother.IsSmoothing;
        float inputPeak = 0f;
        float outputPeak = 0f;

        for (int i = 0; i < buffer.Length; i++)
        {
            if (driveSmooth)
            {
                drive = _driveSmoother.Next();
                driveSmooth = _driveSmoother.IsSmoothing;
            }

            if (mixSmooth)
            {
                mix = _mixSmoother.Next();
                mixSmooth = _mixSmoother.IsSmoothing;
            }

            float input = buffer[i];
            inputPeak = MathF.Max(inputPeak, MathF.Abs(input));

            float wet = MathF.Tanh(input * drive);
            float output = input * (1f - mix) + wet * mix;
            outputPeak = MathF.Max(outputPeak, MathF.Abs(output));
            buffer[i] = output;
        }

        // Update metering (thread-safe)
        UpdatePeakLevel(ref _inputLevelBits, inputPeak);
        UpdatePeakLevel(ref _outputLevelBits, outputPeak);
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
            case DriveIndex:
                _drivePct = Math.Clamp(value, 0f, 100f);
                if (_sampleRate > 0)
                {
                    _driveSmoother.SetTarget(DrivePercentToGain(_drivePct));
                }
                break;
            case MixIndex:
                _mixPct = Math.Clamp(value, 0f, 100f);
                if (_sampleRate > 0)
                {
                    _mixSmoother.SetTarget(_mixPct / 100f);
                }
                break;
        }
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 2];
        Buffer.BlockCopy(BitConverter.GetBytes(_drivePct), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_mixPct), 0, bytes, 4, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float))
        {
            return;
        }

        _drivePct = BitConverter.ToSingle(state, 0);
        if (state.Length >= sizeof(float) * 2)
        {
            _mixPct = BitConverter.ToSingle(state, 4);
        }

        if (_sampleRate > 0)
        {
            _driveSmoother.SetTarget(DrivePercentToGain(_drivePct));
            _mixSmoother.SetTarget(_mixPct / 100f);
        }
    }

    public float GetAndResetInputLevel()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _inputLevelBits, 0));
    }

    public float GetAndResetOutputLevel()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _outputLevelBits, 0));
    }

    public void Dispose()
    {
    }

    private static float DrivePercentToGain(float drivePct)
    {
        return 1f + (Math.Clamp(drivePct, 0f, 100f) / 100f) * 9f;
    }
}
