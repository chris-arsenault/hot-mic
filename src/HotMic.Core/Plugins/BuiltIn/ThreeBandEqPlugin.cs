using HotMic.Core.Dsp;

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
    private float _inputLevel;
    private float _outputLevel;

    // Spectrum analysis - 32 bands with peak hold
    public const int SpectrumBins = 32;
    private readonly float[] _spectrumLevels = new float[SpectrumBins];
    private readonly float[] _spectrumPeaks = new float[SpectrumBins];
    private readonly float[] _sampleBuffer = new float[512];
    private int _sampleBufferIndex;
    private const float SpectrumDecay = 0.92f;
    private const float PeakDecay = 0.985f;

    public ThreeBandEqPlugin()
    {
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
        float level = _inputLevel;
        _inputLevel = 0f;
        return level;
    }

    public float GetAndResetOutputLevel()
    {
        float level = _outputLevel;
        _outputLevel = 0f;
        return level;
    }

    /// <summary>
    /// Gets the current spectrum levels and peaks. Caller must provide arrays of size SpectrumBins.
    /// </summary>
    public void GetSpectrum(float[] levels, float[] peaks)
    {
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
        // Track input level
        float maxInput = 0f;
        for (int i = 0; i < buffer.Length; i++)
        {
            float abs = MathF.Abs(buffer[i]);
            if (abs > maxInput) maxInput = abs;
        }
        if (maxInput > _inputLevel) _inputLevel = maxInput;

        if (IsBypassed)
        {
            // Still update spectrum with input when bypassed
            UpdateSampleBuffer(buffer);
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
            if (abs > maxOutput) maxOutput = abs;
        }
        if (maxOutput > _outputLevel) _outputLevel = maxOutput;

        // Update spectrum with output
        UpdateSampleBuffer(buffer);
    }

    private void UpdateSampleBuffer(Span<float> buffer)
    {
        // Copy samples to ring buffer
        for (int i = 0; i < buffer.Length; i++)
        {
            _sampleBuffer[_sampleBufferIndex] = buffer[i];
            _sampleBufferIndex = (_sampleBufferIndex + 1) % _sampleBuffer.Length;
        }

        // Compute spectrum using simple DFT for each bin
        // Bins are logarithmically spaced from 20Hz to Nyquist
        float minFreq = 20f;
        float maxFreq = _sampleRate > 0 ? _sampleRate / 2f : 20000f;
        int bufferLen = _sampleBuffer.Length;

        for (int bin = 0; bin < SpectrumBins; bin++)
        {
            // Logarithmic frequency for this bin
            float t = bin / (float)(SpectrumBins - 1);
            float freq = minFreq * MathF.Pow(maxFreq / minFreq, t);

            // Simple Goertzel-like magnitude calculation
            float omega = 2f * MathF.PI * freq / _sampleRate;
            float cosOmega = MathF.Cos(omega);
            float sinOmega = MathF.Sin(omega);

            float real = 0f;
            float imag = 0f;
            int readIdx = _sampleBufferIndex;

            for (int i = 0; i < bufferLen; i++)
            {
                float sample = _sampleBuffer[readIdx];
                float phase = omega * i;
                real += sample * MathF.Cos(phase);
                imag += sample * MathF.Sin(phase);
                readIdx = (readIdx + 1) % bufferLen;
            }

            float magnitude = MathF.Sqrt(real * real + imag * imag) / bufferLen;

            // Apply decay to current level
            _spectrumLevels[bin] = MathF.Max(_spectrumLevels[bin] * SpectrumDecay, magnitude);

            // Update peak with slower decay
            if (magnitude > _spectrumPeaks[bin])
            {
                _spectrumPeaks[bin] = magnitude;
            }
            else
            {
                _spectrumPeaks[bin] *= PeakDecay;
            }
        }
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
}
