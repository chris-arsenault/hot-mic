using HotMic.Core.Dsp;
using HotMic.Core.Plugins;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class DeEsserPlugin : IContextualPlugin, ISidechainProducer
{
    public const int CenterFreqIndex = 0;
    public const int BandwidthIndex = 1;
    public const int ThresholdIndex = 2;
    public const int ReductionIndex = 3;
    public const int MaxRangeIndex = 4;

    private const float AttackMs = 1f;
    private const float ReleaseMs = 50f;
    private const int FftSize = 512;
    public const int SpectrumBins = 48;
    private const float SpectrumDecay = 0.90f;

    private float _centerHz = 6000f;
    private float _bandwidthHz = 2000f;
    private float _thresholdDb = -30f;
    private float _reductionDb = 6f;
    private float _maxRangeDb = 10f;

    private float _gain = 1f;
    private float _gainAttackCoeff;
    private float _gainReleaseCoeff;
    private int _sampleRate;
    private float[] _sibilanceBuffer = Array.Empty<float>();

    // Thread-safe metering
    private int _inputLevelBits;
    private int _sibilanceLevelBits;
    private int _gainReductionBits;

    private SpectrumDisplayWorker? _spectrumWorker;

    private readonly BiquadFilter _bandPass = new();
    private readonly EnvelopeFollower _detector = new();
    private readonly EnvelopeFollower _inputEnv = new();

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

    public float CenterHz => _centerHz;
    public float BandwidthHz => _bandwidthHz;
    public float ThresholdDb => _thresholdDb;
    public float ReductionDb => _reductionDb;
    public float MaxRangeDb => _maxRangeDb;
    public int SampleRate => _sampleRate;

    public SidechainSignalMask ProducedSignals => SidechainSignalMask.SibilanceEnergy;

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _gain = 1f;
        _spectrumWorker?.Dispose();
        _spectrumWorker = new SpectrumDisplayWorker(sampleRate, FftSize, SpectrumBins, 1000f, 16000f, SpectrumDecay, FftSize / 2);
        EnsureSidechainBuffer(blockSize);
        UpdateFilters();
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        if (_sibilanceBuffer.Length < buffer.Length)
        {
            ProcessCore(buffer, Span<float>.Empty);
            return;
        }

        var sibilanceSpan = _sibilanceBuffer.AsSpan(0, buffer.Length);
        if (!ProcessCore(buffer, sibilanceSpan))
        {
            return;
        }

        if (!context.SidechainWriter.IsEnabled)
        {
            return;
        }

        long writeTime = context.SampleTime - LatencySamples;
        context.SidechainWriter.WriteBlock(SidechainSignalId.SibilanceEnergy, writeTime, sibilanceSpan);
    }

    public void Process(Span<float> buffer)
    {
        ProcessCore(buffer, Span<float>.Empty);
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

    public float GetAndResetInputLevel()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _inputLevelBits, 0));
    }

    public float GetAndResetSibilanceLevel()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _sibilanceLevelBits, 0));
    }

    public float GetGainReductionDb()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.CompareExchange(ref _gainReductionBits, 0, 0));
    }

    /// <summary>
    /// Gets spectrum data for UI visualization. Caller must provide array of size SpectrumBins (48).
    /// Covers 1kHz to 16kHz range (sibilance frequencies).
    /// </summary>
    public void GetSpectrum(float[] spectrum)
    {
        if (spectrum.Length < SpectrumBins)
        {
            return;
        }

        _spectrumWorker?.GetSpectrum(spectrum);
    }

    public void Dispose()
    {
        _spectrumWorker?.Dispose();
        _spectrumWorker = null;
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
        _inputEnv.Configure(AttackMs, ReleaseMs, _sampleRate);
        _gainAttackCoeff = DspUtils.TimeToCoefficient(AttackMs, _sampleRate);
        _gainReleaseCoeff = DspUtils.TimeToCoefficient(ReleaseMs, _sampleRate);
    }

    private bool ProcessCore(Span<float> buffer, Span<float> sibilanceSpan)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return false;
        }

        _spectrumWorker?.Write(buffer);

        float gain = _gain;
        float attackCoeff = _gainAttackCoeff;
        float releaseCoeff = _gainReleaseCoeff;
        float thresholdDb = _thresholdDb;
        float reductionDb = _reductionDb;
        float maxRangeDb = _maxRangeDb;
        float inputPeak = 0f;
        float sibilancePeak = 0f;
        float minGain = 1f;
        bool writeSibilance = !sibilanceSpan.IsEmpty;

        for (int i = 0; i < buffer.Length; i++)
        {
            float input = buffer[i];
            inputPeak = MathF.Max(inputPeak, MathF.Abs(input));

            float band = _bandPass.Process(input);
            float env = _detector.Process(band);
            sibilancePeak = MathF.Max(sibilancePeak, env);
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
            minGain = MathF.Min(minGain, gain);

            float processedBand = band * gain;
            buffer[i] = input - band + processedBand;

            if (writeSibilance)
            {
                float totalEnv = _inputEnv.Process(input);
                float norm = totalEnv > 1e-6f ? env / totalEnv : 0f;
                sibilanceSpan[i] = Math.Clamp(norm, 0f, 1f);
            }
        }

        _gain = gain;

        // Update metering (thread-safe)
        UpdatePeakLevel(ref _inputLevelBits, inputPeak);
        UpdatePeakLevel(ref _sibilanceLevelBits, sibilancePeak);
        float grDb = minGain < 1f ? DspUtils.LinearToDb(minGain) : 0f;
        Interlocked.Exchange(ref _gainReductionBits, BitConverter.SingleToInt32Bits(grDb));
        return true;
    }

    private void EnsureSidechainBuffer(int blockSize)
    {
        if (blockSize > 0 && _sibilanceBuffer.Length != blockSize)
        {
            _sibilanceBuffer = new float[blockSize];
        }
    }
}
