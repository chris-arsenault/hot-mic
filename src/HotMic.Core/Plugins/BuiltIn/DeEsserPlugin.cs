using HotMic.Core.Dsp;
using HotMic.Core.Plugins;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class DeEsserPlugin : IPlugin
{
    public const int CenterFreqIndex = 0;
    public const int BandwidthIndex = 1;
    public const int ThresholdIndex = 2;
    public const int ReductionIndex = 3;
    public const int MaxRangeIndex = 4;

    private const float AttackMs = 1f;
    private const float ReleaseMs = 50f;

    private float _centerHz = 6000f;
    private float _bandwidthHz = 2000f;
    private float _thresholdDb = -30f;
    private float _reductionDb = 6f;
    private float _maxRangeDb = 10f;

    private float _gain = 1f;
    private float _gainAttackCoeff;
    private float _gainReleaseCoeff;
    private int _sampleRate;

    private readonly BiquadFilter _bandPass = new();
    private readonly EnvelopeFollower _detector = new();

    public DeEsserPlugin()
    {
        Parameters =
        [
            new PluginParameter { Index = CenterFreqIndex, Name = "Center Freq", MinValue = 4000f, MaxValue = 9000f, DefaultValue = 6000f, Unit = "Hz" },
            new PluginParameter { Index = BandwidthIndex, Name = "Bandwidth", MinValue = 1000f, MaxValue = 4000f, DefaultValue = 2000f, Unit = "Hz" },
            new PluginParameter { Index = ThresholdIndex, Name = "Threshold", MinValue = -40f, MaxValue = 0f, DefaultValue = -30f, Unit = "dB" },
            new PluginParameter { Index = ReductionIndex, Name = "Reduction", MinValue = 0f, MaxValue = 12f, DefaultValue = 6f, Unit = "dB" },
            new PluginParameter { Index = MaxRangeIndex, Name = "Max Range", MinValue = 0f, MaxValue = 20f, DefaultValue = 10f, Unit = "dB" }
        ];
    }

    public string Id => "builtin:deesser";

    public string Name => "De-Esser";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _gain = 1f;
        UpdateFilters();
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return;
        }

        float gain = _gain;
        float attackCoeff = _gainAttackCoeff;
        float releaseCoeff = _gainReleaseCoeff;
        float thresholdDb = _thresholdDb;
        float reductionDb = _reductionDb;
        float maxRangeDb = _maxRangeDb;

        for (int i = 0; i < buffer.Length; i++)
        {
            float input = buffer[i];
            float band = _bandPass.Process(input);
            float env = _detector.Process(band);
            float envDb = DspUtils.LinearToDb(env);

            float targetReduction = 0f;
            if (envDb > thresholdDb)
            {
                float overDb = envDb - thresholdDb;
                targetReduction = MathF.Min(maxRangeDb, MathF.Min(reductionDb, overDb));
            }

            float targetGain = DspUtils.DbToLinear(-targetReduction);
            float coeff = targetGain < gain ? attackCoeff : releaseCoeff;
            gain += coeff * (targetGain - gain);

            float processedBand = band * gain;
            buffer[i] = input - band + processedBand;
        }

        _gain = gain;
    }

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case CenterFreqIndex:
                _centerHz = Math.Clamp(value, 4000f, 9000f);
                UpdateFilters();
                break;
            case BandwidthIndex:
                _bandwidthHz = Math.Clamp(value, 1000f, 4000f);
                UpdateFilters();
                break;
            case ThresholdIndex:
                _thresholdDb = value;
                break;
            case ReductionIndex:
                _reductionDb = Math.Clamp(value, 0f, 12f);
                break;
            case MaxRangeIndex:
                _maxRangeDb = Math.Clamp(value, 0f, 20f);
                break;
        }
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 5];
        Buffer.BlockCopy(BitConverter.GetBytes(_centerHz), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_bandwidthHz), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_thresholdDb), 0, bytes, 8, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_reductionDb), 0, bytes, 12, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_maxRangeDb), 0, bytes, 16, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float) * 3)
        {
            return;
        }

        _centerHz = BitConverter.ToSingle(state, 0);
        _bandwidthHz = BitConverter.ToSingle(state, 4);
        _thresholdDb = BitConverter.ToSingle(state, 8);
        if (state.Length >= sizeof(float) * 4)
        {
            _reductionDb = BitConverter.ToSingle(state, 12);
        }
        if (state.Length >= sizeof(float) * 5)
        {
            _maxRangeDb = BitConverter.ToSingle(state, 16);
        }

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

        float q = Math.Clamp(_centerHz / MathF.Max(1f, _bandwidthHz), 0.2f, 12f);
        _bandPass.SetBandPass(_sampleRate, _centerHz, q);
        _detector.Configure(AttackMs, ReleaseMs, _sampleRate);
        _gainAttackCoeff = DspUtils.TimeToCoefficient(AttackMs, _sampleRate);
        _gainReleaseCoeff = DspUtils.TimeToCoefficient(ReleaseMs, _sampleRate);
    }
}
