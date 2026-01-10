using System.Threading;
using HotMic.Core.Dsp;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class NoiseGatePlugin : IPlugin
{
    public const int ThresholdIndex = 0;
    public const int AttackIndex = 1;
    public const int HoldIndex = 2;
    public const int ReleaseIndex = 3;

    private float _thresholdDb = -40f;
    private float _attackMs = 1f;
    private float _holdMs = 50f;
    private float _releaseMs = 100f;

    private float _thresholdLinear;
    private float _attackCoeff;
    private float _releaseCoeff;
    private float _gain;
    private int _holdSamples;
    private int _holdSamplesLeft;
    private int _sampleRate;
    private bool _gateOpen;
    private int _gateOpenBits;

    private readonly EnvelopeFollower _envelope = new();

    public NoiseGatePlugin()
    {
        Parameters =
        [
            new PluginParameter { Index = ThresholdIndex, Name = "Threshold", MinValue = -80f, MaxValue = 0f, DefaultValue = -40f, Unit = "dB" },
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
                if (env < _thresholdLinear)
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
                _gateOpen = true;
                _holdSamplesLeft = _holdSamples;
            }

            float target = _gateOpen ? 1f : 0f;
            float coeff = target > _gain ? _attackCoeff : _releaseCoeff;
            _gain += coeff * (target - _gain);
            buffer[i] = sample * _gain;
        }

        Interlocked.Exchange(ref _gateOpenBits, _gateOpen ? 1 : 0);
    }

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case ThresholdIndex:
                _thresholdDb = value;
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

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 4];
        Buffer.BlockCopy(BitConverter.GetBytes(_thresholdDb), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_attackMs), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_holdMs), 0, bytes, 8, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_releaseMs), 0, bytes, 12, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float) * 4)
        {
            return;
        }

        _thresholdDb = BitConverter.ToSingle(state, 0);
        _attackMs = BitConverter.ToSingle(state, 4);
        _holdMs = BitConverter.ToSingle(state, 8);
        _releaseMs = BitConverter.ToSingle(state, 12);
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
        _attackCoeff = DspUtils.TimeToCoefficient(_attackMs, _sampleRate);
        _releaseCoeff = DspUtils.TimeToCoefficient(_releaseMs, _sampleRate);
        _holdSamples = (int)(_holdMs * 0.001f * _sampleRate);
        _envelope.Configure(_attackMs, _releaseMs, _sampleRate);
    }
}
