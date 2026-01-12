using HotMic.Core.Dsp;
using HotMic.Core.Plugins;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class HighPassFilterPlugin : IPlugin
{
    public const int CutoffIndex = 0;
    public const int SlopeIndex = 1;

    private const float MinCutoffHz = 40f;
    private const float MaxCutoffHz = 200f;
    private const int FftSize = 256;
    private const int SpectrumBins = 32;
    private const float SpectrumDecay = 0.92f;

    private float _cutoffHz = 100f;
    private float _slopeDbOct = 18f;
    private int _sampleRate;
    private bool _useFirstOrder = true;

    // Thread-safe metering
    private int _inputLevelBits;
    private int _outputLevelBits;

    // FFT spectrum analysis
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

    public float CutoffHz => _cutoffHz;
    public float SlopeDbOct => _slopeDbOct;
    public int SampleRate => _sampleRate;

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
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

        bool useFirstOrder = _useFirstOrder;
        float inputPeak = 0f;
        float outputPeak = 0f;

        for (int i = 0; i < buffer.Length; i++)
        {
            float input = buffer[i];
            inputPeak = MathF.Max(inputPeak, MathF.Abs(input));

            // Accumulate samples for spectrum analysis
            _inputRing[_inputIndex] = input;
            _inputIndex = (_inputIndex + 1) % FftSize;
            _sampleCounter++;

            // Perform FFT periodically (every FftSize/2 samples for overlap)
            if (_sampleCounter >= FftSize / 2)
            {
                _sampleCounter = 0;
                UpdateSpectrum();
            }

            float sample = _highPass.Process(input);
            if (useFirstOrder)
            {
                sample = _firstOrder.Process(sample);
            }
            outputPeak = MathF.Max(outputPeak, MathF.Abs(sample));
            buffer[i] = sample;
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

    public float GetAndResetInputLevel()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _inputLevelBits, 0));
    }

    public float GetAndResetOutputLevel()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _outputLevelBits, 0));
    }

    /// <summary>
    /// Gets spectrum data for UI visualization. Caller must provide array of size SpectrumBins (32).
    /// Values are linear magnitudes (0-1 range, approximately).
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

    private void UpdateFilters()
    {
        if (_sampleRate <= 0)
        {
            return;
        }

        _highPass.SetHighPass(_sampleRate, _cutoffHz, 0.707f);
        _firstOrder.Configure(_cutoffHz, _sampleRate);
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

        // Map FFT bins to display bins (log scale, 20Hz to 500Hz for HPF display)
        int current = Volatile.Read(ref _displayIndex);
        int target = current == 0 ? 1 : 0;
        float[] spectrum = _spectrumBuffers[target];

        float minFreq = 20f;
        float maxFreq = 500f;
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
}
