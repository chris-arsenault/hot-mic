using System.Threading;
using HotMic.Core.Dsp;
using HotMic.Core.Threading;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class ThreeBandEqPlugin : IPlugin
{
    public const int LowGainIndex = 0;
    public const int LowFreqIndex = 1;
    public const int LowQIndex = 2;
    public const int MidGainIndex = 3;
    public const int MidFreqIndex = 4;
    public const int MidQIndex = 5;
    public const int HighGainIndex = 6;
    public const int HighFreqIndex = 7;
    public const int HighQIndex = 8;

    private const int AnalysisSize = 1024;

    private readonly BiquadFilter _low = new();
    private readonly BiquadFilter _mid = new();
    private readonly BiquadFilter _high = new();

    private float _lowGainDb;
    private float _lowFreq = 120f;
    private float _lowQ = 0.7f;
    private float _midGainDb;
    private float _midFreq = 1000f;
    private float _midQ = 0.7f;
    private float _highGainDb;
    private float _highFreq = 8000f;
    private float _highQ = 0.7f;
    private int _sampleRate;
    private int _inputLevelBits;
    private int _outputLevelBits;

    // Spectrum analysis - 32 bands with peak hold (computed on UI thread).
    public const int SpectrumBins = 32;
    private readonly float[] _spectrumLevels = new float[SpectrumBins];
    private readonly float[] _spectrumPeaks = new float[SpectrumBins];
    private const float SpectrumDecay = 0.92f;
    private const float PeakDecay = 0.985f;

    private readonly LockFreeRingBuffer _analysisBuffer = new(AnalysisSize * 4);
    private readonly float[] _analysisSamples = new float[AnalysisSize];
    private readonly float[] _fftReal = new float[AnalysisSize];
    private readonly float[] _fftImag = new float[AnalysisSize];
    private readonly float[] _window = new float[AnalysisSize];
    private readonly FastFft _fft = new(AnalysisSize);

    public ThreeBandEqPlugin()
    {
        for (int i = 0; i < AnalysisSize; i++)
        {
            _window[i] = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / (AnalysisSize - 1));
        }

        Parameters =
        [
            new PluginParameter { Index = LowGainIndex, Name = "Low Gain", MinValue = -24f, MaxValue = 24f, DefaultValue = 0f, Unit = "dB" },
            new PluginParameter { Index = LowFreqIndex, Name = "Low Freq", MinValue = 20f, MaxValue = 500f, DefaultValue = 120f, Unit = "Hz" },
            new PluginParameter { Index = LowQIndex, Name = "Low Q", MinValue = 0.2f, MaxValue = 4f, DefaultValue = 0.7f, Unit = string.Empty },
            new PluginParameter { Index = MidGainIndex, Name = "Mid Gain", MinValue = -24f, MaxValue = 24f, DefaultValue = 0f, Unit = "dB" },
            new PluginParameter { Index = MidFreqIndex, Name = "Mid Freq", MinValue = 200f, MaxValue = 5000f, DefaultValue = 1000f, Unit = "Hz" },
            new PluginParameter { Index = MidQIndex, Name = "Mid Q", MinValue = 0.2f, MaxValue = 4f, DefaultValue = 0.7f, Unit = string.Empty },
            new PluginParameter { Index = HighGainIndex, Name = "High Gain", MinValue = -24f, MaxValue = 24f, DefaultValue = 0f, Unit = "dB" },
            new PluginParameter { Index = HighFreqIndex, Name = "High Freq", MinValue = 2000f, MaxValue = 20000f, DefaultValue = 8000f, Unit = "Hz" },
            new PluginParameter { Index = HighQIndex, Name = "High Q", MinValue = 0.2f, MaxValue = 4f, DefaultValue = 0.7f, Unit = string.Empty }
        ];
    }

    public string Id => "builtin:eq3";

    public string Name => "3-Band EQ";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    // Property getters for UI binding
    public float LowGainDb => _lowGainDb;
    public float LowFreq => _lowFreq;
    public float LowQ => _lowQ;
    public float MidGainDb => _midGainDb;
    public float MidFreq => _midFreq;
    public float MidQ => _midQ;
    public float HighGainDb => _highGainDb;
    public float HighFreq => _highFreq;
    public float HighQ => _highQ;
    public int SampleRate => _sampleRate;

    public float GetAndResetInputLevel()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _inputLevelBits, 0));
    }

    public float GetAndResetOutputLevel()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _outputLevelBits, 0));
    }

    /// <summary>
    /// Gets the current spectrum levels and peaks. Caller must provide arrays of size SpectrumBins.
    /// </summary>
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
        UpdateFilters();
    }

    public void Process(Span<float> buffer)
    {
        float maxInput = 0f;
        for (int i = 0; i < buffer.Length; i++)
        {
            float abs = MathF.Abs(buffer[i]);
            if (abs > maxInput)
            {
                maxInput = abs;
            }
        }

        Interlocked.Exchange(ref _inputLevelBits, BitConverter.SingleToInt32Bits(maxInput));

        if (IsBypassed)
        {
            _analysisBuffer.Write(buffer);
            return;
        }

        float maxOutput = 0f;
        for (int i = 0; i < buffer.Length; i++)
        {
            float sample = buffer[i];
            sample = _low.Process(sample);
            sample = _mid.Process(sample);
            sample = _high.Process(sample);
            buffer[i] = sample;

            float abs = MathF.Abs(sample);
            if (abs > maxOutput)
            {
                maxOutput = abs;
            }
        }

        Interlocked.Exchange(ref _outputLevelBits, BitConverter.SingleToInt32Bits(maxOutput));
        _analysisBuffer.Write(buffer);
    }

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case LowGainIndex:
                _lowGainDb = value;
                break;
            case LowFreqIndex:
                _lowFreq = value;
                break;
            case LowQIndex:
                _lowQ = value;
                break;
            case MidGainIndex:
                _midGainDb = value;
                break;
            case MidFreqIndex:
                _midFreq = value;
                break;
            case MidQIndex:
                _midQ = value;
                break;
            case HighGainIndex:
                _highGainDb = value;
                break;
            case HighFreqIndex:
                _highFreq = value;
                break;
            case HighQIndex:
                _highQ = value;
                break;
        }

        UpdateFilters();
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 9];
        Buffer.BlockCopy(BitConverter.GetBytes(_lowGainDb), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_lowFreq), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_lowQ), 0, bytes, 8, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_midGainDb), 0, bytes, 12, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_midFreq), 0, bytes, 16, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_midQ), 0, bytes, 20, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_highGainDb), 0, bytes, 24, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_highFreq), 0, bytes, 28, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_highQ), 0, bytes, 32, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float) * 9)
        {
            return;
        }

        _lowGainDb = BitConverter.ToSingle(state, 0);
        _lowFreq = BitConverter.ToSingle(state, 4);
        _lowQ = BitConverter.ToSingle(state, 8);
        _midGainDb = BitConverter.ToSingle(state, 12);
        _midFreq = BitConverter.ToSingle(state, 16);
        _midQ = BitConverter.ToSingle(state, 20);
        _highGainDb = BitConverter.ToSingle(state, 24);
        _highFreq = BitConverter.ToSingle(state, 28);
        _highQ = BitConverter.ToSingle(state, 32);
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

        _low.SetLowShelf(_sampleRate, _lowFreq, _lowGainDb, _lowQ);
        _mid.SetPeaking(_sampleRate, _midFreq, _midGainDb, _midQ);
        _high.SetHighShelf(_sampleRate, _highFreq, _highGainDb, _highQ);
    }

    private void UpdateSpectrum()
    {
        // Run analysis on UI thread using buffered samples to keep audio callback light.
        int read = _analysisBuffer.Read(_analysisSamples);
        if (read < _analysisSamples.Length)
        {
            Array.Clear(_analysisSamples, read, _analysisSamples.Length - read);
        }

        for (int i = 0; i < AnalysisSize; i++)
        {
            _fftReal[i] = _analysisSamples[i] * _window[i];
            _fftImag[i] = 0f;
        }

        _fft.Forward(_fftReal, _fftImag);

        int fftBins = AnalysisSize / 2 + 1;
        float minFreq = 20f;
        float maxFreq = _sampleRate > 0 ? _sampleRate / 2f : 20000f;
        const float normalizationFactor = 2f / AnalysisSize;

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
