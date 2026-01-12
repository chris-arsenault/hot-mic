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

    // Thread-safe metering
    private int _inputLevelBits;
    private int _sibilanceLevelBits;
    private int _gainReductionBits;

    // FFT spectrum analysis (for UI visualization)
    private FastFft? _fft;
    private readonly float[] _fftReal = new float[FftSize];
    private readonly float[] _fftImag = new float[FftSize];
    private readonly float[] _fftWindow = new float[FftSize];
    private readonly float[] _inputRing = new float[FftSize];
    private int _inputIndex;
    private int _sampleCounter;

    // Double-buffered spectrum for UI (thread-safe)
    private readonly float[][] _spectrumBuffers = { new float[SpectrumBins], new float[SpectrumBins] };
    private int _displayIndex;

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

    public float CenterHz => _centerHz;
    public float BandwidthHz => _bandwidthHz;
    public float ThresholdDb => _thresholdDb;
    public float ReductionDb => _reductionDb;
    public float MaxRangeDb => _maxRangeDb;
    public int SampleRate => _sampleRate;

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _gain = 1f;
        _fft = new FastFft(FftSize);

        // Hann window for FFT
        for (int i = 0; i < FftSize; i++)
        {
            _fftWindow[i] = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / (FftSize - 1));
        }

        _inputIndex = 0;
        _sampleCounter = 0;
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
        float inputPeak = 0f;
        float sibilancePeak = 0f;
        float minGain = 1f;

        for (int i = 0; i < buffer.Length; i++)
        {
            float input = buffer[i];
            inputPeak = MathF.Max(inputPeak, MathF.Abs(input));

            // Accumulate samples for spectrum analysis
            _inputRing[_inputIndex] = input;
            _inputIndex = (_inputIndex + 1) % FftSize;
            _sampleCounter++;

            // Perform FFT periodically
            if (_sampleCounter >= FftSize / 2)
            {
                _sampleCounter = 0;
                UpdateSpectrum();
            }

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
        }

        _gain = gain;

        // Update metering (thread-safe)
        UpdatePeakLevel(ref _inputLevelBits, inputPeak);
        UpdatePeakLevel(ref _sibilanceLevelBits, sibilancePeak);
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
        if (spectrum.Length < SpectrumBins) return;
        int index = Volatile.Read(ref _displayIndex);
        Array.Copy(_spectrumBuffers[index], spectrum, SpectrumBins);
    }

    public void Dispose()
    {
    }

    private void UpdateSpectrum()
    {
        if (_fft is null || _sampleRate <= 0) return;

        // Copy input ring buffer with windowing
        int start = _inputIndex;
        for (int i = 0; i < FftSize; i++)
        {
            int idx = (start + i) % FftSize;
            _fftReal[i] = _inputRing[idx] * _fftWindow[i];
            _fftImag[i] = 0f;
        }

        _fft.Forward(_fftReal, _fftImag);

        // Map FFT bins to display bins (log scale, 1kHz to 16kHz for sibilance range)
        int current = Volatile.Read(ref _displayIndex);
        int target = current == 0 ? 1 : 0;
        float[] spectrum = _spectrumBuffers[target];

        float minFreq = 1000f;
        float maxFreq = 16000f;
        int fftBins = FftSize / 2;
        float binWidth = (float)_sampleRate / FftSize;
        float normFactor = 2f / FftSize;

        for (int displayBin = 0; displayBin < SpectrumBins; displayBin++)
        {
            float t0 = displayBin / (float)SpectrumBins;
            float t1 = (displayBin + 1) / (float)SpectrumBins;
            float freq0 = minFreq * MathF.Pow(maxFreq / minFreq, t0);
            float freq1 = minFreq * MathF.Pow(maxFreq / minFreq, t1);

            int bin0 = Math.Clamp((int)(freq0 / binWidth), 0, fftBins - 1);
            int bin1 = Math.Clamp((int)(freq1 / binWidth), bin0 + 1, fftBins);

            float maxMag = 0f;
            for (int i = bin0; i < bin1; i++)
            {
                float mag = MathF.Sqrt(_fftReal[i] * _fftReal[i] + _fftImag[i] * _fftImag[i]) * normFactor;
                maxMag = MathF.Max(maxMag, mag);
            }

            // Apply decay for smooth animation
            spectrum[displayBin] = MathF.Max(spectrum[displayBin] * SpectrumDecay, maxMag);
        }

        Volatile.Write(ref _displayIndex, target);
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
