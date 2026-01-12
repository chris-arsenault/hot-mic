using HotMic.Core.Dsp;
using HotMic.Core.Plugins;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class HighPassFilterPlugin : IPlugin
{
    public const int CutoffIndex = 0;
    public const int SlopeIndex = 1;

    private const float MinCutoffHz = 40f;
    private const float MaxCutoffHz = 200f;

    private float _cutoffHz = 100f;
    private float _slopeDbOct = 18f;
    private int _sampleRate;
    private bool _useFirstOrder = true;

    private readonly BiquadFilter _highPass = new();
    private OnePoleHighPass _firstOrder = new();

    public HighPassFilterPlugin()
    {
        Parameters =
        [
            new PluginParameter
            {
                Index = CutoffIndex,
                Name = "Cutoff",
                MinValue = MinCutoffHz,
                MaxValue = MaxCutoffHz,
                DefaultValue = 100f,
                Unit = "Hz"
            },
            new PluginParameter
            {
                Index = SlopeIndex,
                Name = "Slope",
                MinValue = 12f,
                MaxValue = 18f,
                DefaultValue = 18f,
                Unit = "dB/oct"
            }
        ];
    }

    public string Id => "builtin:hpf";

    public string Name => "High-Pass Filter";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        UpdateFilters();
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return;
        }

        bool useFirstOrder = _useFirstOrder;
        for (int i = 0; i < buffer.Length; i++)
        {
            float sample = _highPass.Process(buffer[i]);
            if (useFirstOrder)
            {
                sample = _firstOrder.Process(sample);
            }
            buffer[i] = sample;
        }
    }

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case CutoffIndex:
                _cutoffHz = Math.Clamp(value, MinCutoffHz, MaxCutoffHz);
                break;
            case SlopeIndex:
                // Quantize to the supported slopes to avoid ambiguous values.
                _slopeDbOct = value >= 15f ? 18f : 12f;
                _useFirstOrder = _slopeDbOct >= 18f;
                break;
        }

        UpdateFilters();
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 2];
        Buffer.BlockCopy(BitConverter.GetBytes(_cutoffHz), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_slopeDbOct), 0, bytes, 4, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float))
        {
            return;
        }

        _cutoffHz = BitConverter.ToSingle(state, 0);
        if (state.Length >= sizeof(float) * 2)
        {
            _slopeDbOct = BitConverter.ToSingle(state, 4);
        }

        _useFirstOrder = _slopeDbOct >= 18f;
        UpdateFilters();
    }

    public void Dispose()
    {
    }

    private void UpdateFilters()
    {
        if (_sampleRate <= 0)
        {
            return;
        }

        _highPass.SetHighPass(_sampleRate, _cutoffHz, 0.707f);
        _firstOrder.Configure(_cutoffHz, _sampleRate);
    }
}
