using System.Threading;
using HotMic.Common.Configuration;
using HotMic.Core.Dsp;
using HotMic.Core.Plugins;
using HotMic.Core.Threading;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class FiveBandEqPlugin : IPlugin, IQualityConfigurablePlugin
{
    public const int HpfFreqIndex = 0;
    public const int LowShelfGainIndex = 1;
    public const int LowShelfFreqIndex = 2;
    public const int Mid1GainIndex = 3;
    public const int Mid1FreqIndex = 4;
    public const int Mid1QIndex = 5;
    public const int Mid2GainIndex = 6;
    public const int Mid2FreqIndex = 7;
    public const int Mid2QIndex = 8;
    public const int HighShelfGainIndex = 9;
    public const int HighShelfFreqIndex = 10;

    private const int DefaultAnalysisSize = 1024;
    private const float DefaultSmoothingMs = 15f;
    private const int DefaultCoefficientUpdateStride = 16; // Throttle coefficient updates to avoid per-sample trig work.
    private const float ShelfQ = 0.7f;

    private readonly BiquadFilter _hpf = new();
    private readonly BiquadFilter _lowShelf = new();
    private readonly BiquadFilter _mid1 = new();
    private readonly BiquadFilter _mid2 = new();
    private readonly BiquadFilter _highShelf = new();

    private float _hpfFreq = 80f;
    private float _lowShelfGainDb = 3f;
    private float _lowShelfFreq = 120f;
    private float _mid1GainDb = -3f;
    private float _mid1Freq = 300f;
    private float _mid1Q = 1.0f;
    private float _mid2GainDb = 3f;
    private float _mid2Freq = 3000f;
    private float _mid2Q = 1.0f;
    private float _highShelfGainDb = 2f;
    private float _highShelfFreq = 10000f;

    private int _sampleRate;
    private int _inputLevelBits;
    private int _outputLevelBits;
    private int _coeffUpdateCounter;
    private bool _filtersDirty;
    private int _analysisSize = DefaultAnalysisSize;
    private float _parameterSmoothingMs = DefaultSmoothingMs;
    private int _coefficientUpdateStride = DefaultCoefficientUpdateStride;

    private LinearSmoother _hpfFreqSmoother;
    private LinearSmoother _lowShelfGainSmoother;
    private LinearSmoother _lowShelfFreqSmoother;
    private LinearSmoother _mid1GainSmoother;
    private LinearSmoother _mid1FreqSmoother;
    private LinearSmoother _mid1QSmoother;
    private LinearSmoother _mid2GainSmoother;
    private LinearSmoother _mid2FreqSmoother;
    private LinearSmoother _mid2QSmoother;
    private LinearSmoother _highShelfGainSmoother;
    private LinearSmoother _highShelfFreqSmoother;

    // Spectrum analysis - 32 bands with peak hold (computed on UI thread).
    public const int SpectrumBins = 32;
    private readonly float[] _spectrumLevels = new float[SpectrumBins];
    private readonly float[] _spectrumPeaks = new float[SpectrumBins];
    private const float SpectrumDecay = 0.92f;
    private const float PeakDecay = 0.985f;

    private LockFreeRingBuffer _analysisBuffer;
    private float[] _analysisSamples = Array.Empty<float>();
    private float[] _fftReal = Array.Empty<float>();
    private float[] _fftImag = Array.Empty<float>();
    private float[] _window = Array.Empty<float>();
    private FastFft? _fft;

    public FiveBandEqPlugin()
    {
        _analysisBuffer = new LockFreeRingBuffer(DefaultAnalysisSize * 4);
        ConfigureAnalysis(_analysisSize);

        Parameters =
        [
            new PluginParameter { Index = HpfFreqIndex, Name = "HPF Freq", MinValue = 40f, MaxValue = 200f, DefaultValue = 80f, Unit = "Hz" },
            new PluginParameter { Index = LowShelfGainIndex, Name = "Low Shelf Gain", MinValue = -24f, MaxValue = 24f, DefaultValue = 3f, Unit = "dB" },
            new PluginParameter { Index = LowShelfFreqIndex, Name = "Low Shelf Freq", MinValue = 60f, MaxValue = 300f, DefaultValue = 120f, Unit = "Hz" },
            new PluginParameter { Index = Mid1GainIndex, Name = "Low-Mid Gain", MinValue = -24f, MaxValue = 24f, DefaultValue = -3f, Unit = "dB" },
            new PluginParameter { Index = Mid1FreqIndex, Name = "Low-Mid Freq", MinValue = 150f, MaxValue = 800f, DefaultValue = 300f, Unit = "Hz" },
            new PluginParameter { Index = Mid1QIndex, Name = "Low-Mid Q", MinValue = 0.5f, MaxValue = 4f, DefaultValue = 1f, Unit = string.Empty },
            new PluginParameter { Index = Mid2GainIndex, Name = "High-Mid Gain", MinValue = -24f, MaxValue = 24f, DefaultValue = 3f, Unit = "dB" },
            new PluginParameter { Index = Mid2FreqIndex, Name = "High-Mid Freq", MinValue = 1000f, MaxValue = 6000f, DefaultValue = 3000f, Unit = "Hz" },
            new PluginParameter { Index = Mid2QIndex, Name = "High-Mid Q", MinValue = 0.5f, MaxValue = 4f, DefaultValue = 1f, Unit = string.Empty },
            new PluginParameter { Index = HighShelfGainIndex, Name = "High Shelf Gain", MinValue = -24f, MaxValue = 24f, DefaultValue = 2f, Unit = "dB" },
            new PluginParameter { Index = HighShelfFreqIndex, Name = "High Shelf Freq", MinValue = 6000f, MaxValue = 16000f, DefaultValue = 10000f, Unit = "Hz" }
        ];
    }

    public string Id => "builtin:eq3";

    public string Name => "5-Band EQ";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public float HpfFreq => _hpfFreq;
    public float LowShelfGainDb => _lowShelfGainDb;
    public float LowShelfFreq => _lowShelfFreq;
    public float Mid1GainDb => _mid1GainDb;
    public float Mid1Freq => _mid1Freq;
    public float Mid1Q => _mid1Q;
    public float Mid2GainDb => _mid2GainDb;
    public float Mid2Freq => _mid2Freq;
    public float Mid2Q => _mid2Q;
    public float HighShelfGainDb => _highShelfGainDb;
    public float HighShelfFreq => _highShelfFreq;
    public int SampleRate => _sampleRate;

    public float GetAndResetInputLevel()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _inputLevelBits, 0));
    }

    public float GetAndResetOutputLevel()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _outputLevelBits, 0));
    }

    public void GetSpectrum(float[] levels, float[] peaks)
    {
        UpdateSpectrum();
        if (levels.Length >= SpectrumBins && peaks.Length >= SpectrumBins)
        {
            Array.Copy(_spectrumLevels, levels, SpectrumBins);
            Array.Copy(_spectrumPeaks, peaks, SpectrumBins);
        }
    }

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        ConfigureAnalysis(_analysisSize);
        ConfigureSmoothers();
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        Process(buffer);
    }

    public void Process(Span<float> buffer)
    {
        float maxInput = 0f;
        bool bypassed = IsBypassed;
        bool smoothingActive = AnySmootherActive();
        bool smoothingWasActive = smoothingActive;

        if (_filtersDirty && !smoothingActive)
        {
            UpdateFiltersFromSmoothers();
            _filtersDirty = false;
        }
        else if (_filtersDirty)
        {
            _filtersDirty = false;
            _coeffUpdateCounter = 0;
        }

        float maxOutput = 0f;
        int updateCounter = _coeffUpdateCounter;

        for (int i = 0; i < buffer.Length; i++)
        {
            float sample = buffer[i];
            float abs = MathF.Abs(sample);
            if (abs > maxInput)
            {
                maxInput = abs;
            }

            if (smoothingActive)
            {
                AdvanceSmoothers();
                if (updateCounter <= 0)
                {
                    UpdateFiltersFromSmoothers();
                    updateCounter = _coefficientUpdateStride;
                }
                updateCounter--;
            }

            if (!bypassed)
            {
                sample = _hpf.Process(sample);
                sample = _lowShelf.Process(sample);
                sample = _mid1.Process(sample);
                sample = _mid2.Process(sample);
                sample = _highShelf.Process(sample);
                buffer[i] = sample;

                float absOut = MathF.Abs(sample);
                if (absOut > maxOutput)
                {
                    maxOutput = absOut;
                }
            }
        }

        if (smoothingWasActive && !AnySmootherActive())
        {
            UpdateFiltersFromSmoothers();
            updateCounter = 0;
        }

        _coeffUpdateCounter = updateCounter;

        Interlocked.Exchange(ref _inputLevelBits, BitConverter.SingleToInt32Bits(maxInput));
        if (!bypassed)
        {
            Interlocked.Exchange(ref _outputLevelBits, BitConverter.SingleToInt32Bits(maxOutput));
        }

        _analysisBuffer.Write(buffer);
    }

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case HpfFreqIndex:
                _hpfFreq = value;
                _hpfFreqSmoother.SetTarget(value);
                break;
            case LowShelfGainIndex:
                _lowShelfGainDb = value;
                _lowShelfGainSmoother.SetTarget(value);
                break;
            case LowShelfFreqIndex:
                _lowShelfFreq = value;
                _lowShelfFreqSmoother.SetTarget(value);
                break;
            case Mid1GainIndex:
                _mid1GainDb = value;
                _mid1GainSmoother.SetTarget(value);
                break;
            case Mid1FreqIndex:
                _mid1Freq = value;
                _mid1FreqSmoother.SetTarget(value);
                break;
            case Mid1QIndex:
                _mid1Q = value;
                _mid1QSmoother.SetTarget(value);
                break;
            case Mid2GainIndex:
                _mid2GainDb = value;
                _mid2GainSmoother.SetTarget(value);
                break;
            case Mid2FreqIndex:
                _mid2Freq = value;
                _mid2FreqSmoother.SetTarget(value);
                break;
            case Mid2QIndex:
                _mid2Q = value;
                _mid2QSmoother.SetTarget(value);
                break;
            case HighShelfGainIndex:
                _highShelfGainDb = value;
                _highShelfGainSmoother.SetTarget(value);
                break;
            case HighShelfFreqIndex:
                _highShelfFreq = value;
                _highShelfFreqSmoother.SetTarget(value);
                break;
        }

        MarkFiltersDirty();
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 11];
        Buffer.BlockCopy(BitConverter.GetBytes(_hpfFreq), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_lowShelfGainDb), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_lowShelfFreq), 0, bytes, 8, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_mid1GainDb), 0, bytes, 12, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_mid1Freq), 0, bytes, 16, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_mid1Q), 0, bytes, 20, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_mid2GainDb), 0, bytes, 24, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_mid2Freq), 0, bytes, 28, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_mid2Q), 0, bytes, 32, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_highShelfGainDb), 0, bytes, 36, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_highShelfFreq), 0, bytes, 40, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float) * 6)
        {
            return;
        }

        if (state.Length == sizeof(float) * 9)
        {
            // Legacy 3-band EQ state: map low/mid/high to shelf/mid/shelf defaults.
            _hpfFreq = 40f;
            _lowShelfGainDb = BitConverter.ToSingle(state, 0);
            _lowShelfFreq = BitConverter.ToSingle(state, 4);
            _mid1GainDb = BitConverter.ToSingle(state, 12);
            _mid1Freq = BitConverter.ToSingle(state, 16);
            _mid1Q = BitConverter.ToSingle(state, 20);
            _mid2GainDb = 0f;
            _highShelfGainDb = BitConverter.ToSingle(state, 24);
            _highShelfFreq = BitConverter.ToSingle(state, 28);
            ConfigureSmoothers();
            return;
        }

        _hpfFreq = BitConverter.ToSingle(state, 0);
        _lowShelfGainDb = BitConverter.ToSingle(state, 4);
        _lowShelfFreq = BitConverter.ToSingle(state, 8);
        _mid1GainDb = BitConverter.ToSingle(state, 12);
        _mid1Freq = BitConverter.ToSingle(state, 16);
        _mid1Q = BitConverter.ToSingle(state, 20);
        if (state.Length >= sizeof(float) * 9)
        {
            _mid2GainDb = BitConverter.ToSingle(state, 24);
            _mid2Freq = BitConverter.ToSingle(state, 28);
            _mid2Q = BitConverter.ToSingle(state, 32);
        }
        if (state.Length >= sizeof(float) * 11)
        {
            _highShelfGainDb = BitConverter.ToSingle(state, 36);
            _highShelfFreq = BitConverter.ToSingle(state, 40);
        }

        ConfigureSmoothers();
    }

    public void Dispose()
    {
    }

    public void ApplyQuality(AudioQualityProfile profile)
    {
        _analysisSize = Math.Max(256, NextPowerOfTwo(profile.EqAnalysisSize));
        _parameterSmoothingMs = MathF.Max(1f, profile.EqSmoothingMs);
        _coefficientUpdateStride = Math.Max(1, profile.EqCoefficientUpdateStride);
        ConfigureAnalysis(_analysisSize);
        ConfigureSmoothers();
    }

    private void UpdateFiltersFromSmoothers()
    {
        if (_sampleRate <= 0)
        {
            return;
        }

        _hpf.SetHighPass(_sampleRate, _hpfFreqSmoother.Current, 0.707f);
        _lowShelf.SetLowShelf(_sampleRate, _lowShelfFreqSmoother.Current, _lowShelfGainSmoother.Current, ShelfQ);
        _mid1.SetPeaking(_sampleRate, _mid1FreqSmoother.Current, _mid1GainSmoother.Current, _mid1QSmoother.Current);
        _mid2.SetPeaking(_sampleRate, _mid2FreqSmoother.Current, _mid2GainSmoother.Current, _mid2QSmoother.Current);
        _highShelf.SetHighShelf(_sampleRate, _highShelfFreqSmoother.Current, _highShelfGainSmoother.Current, ShelfQ);
    }

    private void ConfigureSmoothers()
    {
        if (_sampleRate <= 0)
        {
            return;
        }

        _hpfFreqSmoother.Configure(_sampleRate, _parameterSmoothingMs, _hpfFreq);
        _lowShelfGainSmoother.Configure(_sampleRate, _parameterSmoothingMs, _lowShelfGainDb);
        _lowShelfFreqSmoother.Configure(_sampleRate, _parameterSmoothingMs, _lowShelfFreq);
        _mid1GainSmoother.Configure(_sampleRate, _parameterSmoothingMs, _mid1GainDb);
        _mid1FreqSmoother.Configure(_sampleRate, _parameterSmoothingMs, _mid1Freq);
        _mid1QSmoother.Configure(_sampleRate, _parameterSmoothingMs, _mid1Q);
        _mid2GainSmoother.Configure(_sampleRate, _parameterSmoothingMs, _mid2GainDb);
        _mid2FreqSmoother.Configure(_sampleRate, _parameterSmoothingMs, _mid2Freq);
        _mid2QSmoother.Configure(_sampleRate, _parameterSmoothingMs, _mid2Q);
        _highShelfGainSmoother.Configure(_sampleRate, _parameterSmoothingMs, _highShelfGainDb);
        _highShelfFreqSmoother.Configure(_sampleRate, _parameterSmoothingMs, _highShelfFreq);

        UpdateFiltersFromSmoothers();
        _filtersDirty = false;
        _coeffUpdateCounter = 0;
    }

    private void ConfigureAnalysis(int analysisSize)
    {
        int size = Math.Max(256, NextPowerOfTwo(analysisSize));
        if (_analysisSamples.Length == size)
        {
            _analysisBuffer.Clear();
            return;
        }

        _analysisBuffer = new LockFreeRingBuffer(size * 4);
        _analysisSamples = new float[size];
        _fftReal = new float[size];
        _fftImag = new float[size];
        _window = new float[size];
        for (int i = 0; i < size; i++)
        {
            _window[i] = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / (size - 1));
        }

        _fft = new FastFft(size);
        _analysisSize = size;
    }

    private static int NextPowerOfTwo(int value)
    {
        int power = 1;
        while (power < value)
        {
            power <<= 1;
        }

        return power;
    }

    private void MarkFiltersDirty()
    {
        _filtersDirty = true;
        _coeffUpdateCounter = 0;
    }

    private bool AnySmootherActive()
    {
        return _hpfFreqSmoother.IsSmoothing ||
               _lowShelfGainSmoother.IsSmoothing ||
               _lowShelfFreqSmoother.IsSmoothing ||
               _mid1GainSmoother.IsSmoothing ||
               _mid1FreqSmoother.IsSmoothing ||
               _mid1QSmoother.IsSmoothing ||
               _mid2GainSmoother.IsSmoothing ||
               _mid2FreqSmoother.IsSmoothing ||
               _mid2QSmoother.IsSmoothing ||
               _highShelfGainSmoother.IsSmoothing ||
               _highShelfFreqSmoother.IsSmoothing;
    }

    private void AdvanceSmoothers()
    {
        if (_hpfFreqSmoother.IsSmoothing) _hpfFreqSmoother.Next();
        if (_lowShelfGainSmoother.IsSmoothing) _lowShelfGainSmoother.Next();
        if (_lowShelfFreqSmoother.IsSmoothing) _lowShelfFreqSmoother.Next();
        if (_mid1GainSmoother.IsSmoothing) _mid1GainSmoother.Next();
        if (_mid1FreqSmoother.IsSmoothing) _mid1FreqSmoother.Next();
        if (_mid1QSmoother.IsSmoothing) _mid1QSmoother.Next();
        if (_mid2GainSmoother.IsSmoothing) _mid2GainSmoother.Next();
        if (_mid2FreqSmoother.IsSmoothing) _mid2FreqSmoother.Next();
        if (_mid2QSmoother.IsSmoothing) _mid2QSmoother.Next();
        if (_highShelfGainSmoother.IsSmoothing) _highShelfGainSmoother.Next();
        if (_highShelfFreqSmoother.IsSmoothing) _highShelfFreqSmoother.Next();
    }

    private void UpdateSpectrum()
    {
        // Run analysis on UI thread using buffered samples to keep audio callback light.
        int analysisSize = _analysisSamples.Length;
        if (analysisSize == 0 || _fft is null)
        {
            return;
        }

        int read = _analysisBuffer.Read(_analysisSamples);
        if (read < _analysisSamples.Length)
        {
            Array.Clear(_analysisSamples, read, _analysisSamples.Length - read);
        }

        for (int i = 0; i < analysisSize; i++)
        {
            _fftReal[i] = _analysisSamples[i] * _window[i];
            _fftImag[i] = 0f;
        }

        _fft.Forward(_fftReal, _fftImag);

        int fftBins = analysisSize / 2 + 1;
        float minFreq = 20f;
        float maxFreq = _sampleRate > 0 ? _sampleRate / 2f : 20000f;
        float normalizationFactor = 2f / analysisSize;

        for (int displayBin = 0; displayBin < SpectrumBins; displayBin++)
        {
            float t0 = displayBin / (float)SpectrumBins;
            float t1 = (displayBin + 1) / (float)SpectrumBins;
            float freq0 = minFreq * MathF.Pow(maxFreq / minFreq, t0);
            float freq1 = minFreq * MathF.Pow(maxFreq / minFreq, t1);

            int bin0 = (int)(freq0 * fftBins / maxFreq);
            int bin1 = (int)(freq1 * fftBins / maxFreq);
            bin0 = Math.Clamp(bin0, 0, fftBins - 1);
            bin1 = Math.Clamp(bin1, bin0 + 1, fftBins);

            float sumSq = 0f;
            int count = 0;
            for (int i = bin0; i < bin1 && i < fftBins; i++)
            {
                float real = _fftReal[i];
                float imag = _fftImag[i];
                float mag = MathF.Sqrt(real * real + imag * imag);
                sumSq += mag * mag;
                count++;
            }

            float magnitude = count > 0 ? MathF.Sqrt(sumSq / count) * normalizationFactor : 0f;

            _spectrumLevels[displayBin] = MathF.Max(_spectrumLevels[displayBin] * SpectrumDecay, magnitude);

            if (magnitude > _spectrumPeaks[displayBin])
            {
                _spectrumPeaks[displayBin] = magnitude;
            }
            else
            {
                _spectrumPeaks[displayBin] *= PeakDecay;
            }
        }
    }
}
