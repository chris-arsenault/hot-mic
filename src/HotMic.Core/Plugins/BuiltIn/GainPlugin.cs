using System.Threading;
using HotMic.Common.Configuration;
using HotMic.Core.Dsp;
using HotMic.Core.Plugins;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class GainPlugin : IPlugin, IQualityConfigurablePlugin
{
    public const int GainIndex = 0;
    public const int PhaseInvertIndex = 1;

    private float _gainDb;
    private float _phaseInvert; // 0 = normal, 1 = inverted
    private float _gainLinear = 1f;
    private float _phaseMultiplier = 1f;
    private float _smoothingMs = 5f;
    private int _sampleRate;
    private LinearSmoother _gainSmoother;

    private int _inputLevelBits;
    private int _outputLevelBits;

    public GainPlugin()
    {
        Parameters =
        [
            new PluginParameter { Index = GainIndex, Name = "Gain", MinValue = -24f, MaxValue = 24f, DefaultValue = 0f, Unit = "dB" },
            new PluginParameter { Index = PhaseInvertIndex, Name = "Phase", MinValue = 0f, MaxValue = 1f, DefaultValue = 0f, Unit = "" }
        ];
    }

    public string Id => "builtin:gain";

    public string Name => "Gain";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public int SampleRate => _sampleRate;

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _gainSmoother.Configure(sampleRate, _smoothingMs, _gainLinear * _phaseMultiplier);
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

        float peakIn = 0f;
        float peakOut = 0f;
        bool smoothing = _gainSmoother.IsSmoothing;
        float gain = _gainSmoother.Current;

        for (int i = 0; i < buffer.Length; i++)
        {
            if (smoothing)
            {
                gain = _gainSmoother.Next();
                smoothing = _gainSmoother.IsSmoothing;
            }

            float input = buffer[i];
            float absIn = MathF.Abs(input);
            if (absIn > peakIn) peakIn = absIn;

            float output = input * gain;
            buffer[i] = output;

            float absOut = MathF.Abs(output);
            if (absOut > peakOut) peakOut = absOut;
        }

        Interlocked.Exchange(ref _inputLevelBits, BitConverter.SingleToInt32Bits(peakIn));
        Interlocked.Exchange(ref _outputLevelBits, BitConverter.SingleToInt32Bits(peakOut));
    }

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case GainIndex:
                _gainDb = value;
                break;
            case PhaseInvertIndex:
                _phaseInvert = value >= 0.5f ? 1f : 0f;
                break;
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

    // Current parameter values for UI binding
    public float GainDb => _gainDb;
    public bool IsPhaseInverted => _phaseInvert >= 0.5f;

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 2];
        Buffer.BlockCopy(BitConverter.GetBytes(_gainDb), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_phaseInvert), 0, bytes, 4, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float))
        {
            return;
        }

        _gainDb = BitConverter.ToSingle(state, 0);
        if (state.Length >= sizeof(float) * 2)
        {
            _phaseInvert = BitConverter.ToSingle(state, 4);
        }
        UpdateCoefficients();
    }

    public void Dispose()
    {
    }

    public void ApplyQuality(AudioQualityProfile profile)
    {
        _smoothingMs = MathF.Max(1f, profile.GainSmoothingMs);
    }

    private void UpdateCoefficients()
    {
        _gainLinear = DspUtils.DbToLinear(_gainDb);
        _phaseMultiplier = _phaseInvert >= 0.5f ? -1f : 1f;
        if (_sampleRate > 0)
        {
            _gainSmoother.SetTarget(_gainLinear * _phaseMultiplier);
        }
    }
}
