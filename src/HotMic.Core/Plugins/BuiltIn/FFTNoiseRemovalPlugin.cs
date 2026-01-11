using System.Threading;
using HotMic.Common.Configuration;
using HotMic.Core.Dsp;
using HotMic.Core.Plugins;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class FFTNoiseRemovalPlugin : IPlugin, IQualityConfigurablePlugin
{
    public const int ReductionIndex = 0;

    // For UI visualization - use 64 bins for display
    public const int DisplayBins = 64;

    private const float SpectrumDecay = 0.85f;
    private const float PeakDecay = 0.98f;

    // Algorithm parameters
    private const float DecisionDirectedAlpha = 0.98f;
    private const float FrequencySmoothing = 0.35f;
    private const float TimeSmoothing = 0.4f;
    private const float OverSubMax = 2.5f;

    private int _fftSize = 1024;
    private int _hopSize = 256;
    private int _noiseFramesToLearn = 50;

    private float[] _inputRing = Array.Empty<float>();
    private float[] _outputRing = Array.Empty<float>();
    private float[] _window = Array.Empty<float>();
    private float[] _synthesisWindow = Array.Empty<float>();
    private float[] _fftReal = Array.Empty<float>();
    private float[] _fftImag = Array.Empty<float>();
    private float[] _noisePsd = Array.Empty<float>();
    private float[] _prevSnr = Array.Empty<float>();
    private float[] _prevGain = Array.Empty<float>();
    private float[] _gainBuffer = Array.Empty<float>();
    private FastFft? _fft;

    // Spectrum data for UI visualization (double-buffered).
    private readonly float[][] _inputSpectrumBuffers = { new float[DisplayBins], new float[DisplayBins] };
    private readonly float[][] _outputSpectrumBuffers = { new float[DisplayBins], new float[DisplayBins] };
    private readonly float[][] _noiseSpectrumBuffers = { new float[DisplayBins], new float[DisplayBins] };
    private readonly float[][] _inputPeakBuffers = { new float[DisplayBins], new float[DisplayBins] };
    private readonly float[][] _outputPeakBuffers = { new float[DisplayBins], new float[DisplayBins] };
    private int _displayIndex;

    private float _windowSum;
    private int _inputIndex;
    private int _hopCounter;
    private bool _learning;
    private int _noiseFrames;
    private float _reduction = 0.5f;
    private float _smoothedReduction = 0.5f;
    private int _sampleRate;
    private int _hasProfileFlag;

    public FFTNoiseRemovalPlugin()
    {
        ConfigureQuality(_fftSize, _hopSize, _noiseFramesToLearn);
        Parameters =
        [
            new PluginParameter { Index = ReductionIndex, Name = "Reduction", MinValue = 0f, MaxValue = 1f, DefaultValue = 0.5f, Unit = "%" }
        ];
    }

    public string Id => "builtin:fft-noise";

    public string Name => "FFT Noise Removal";

    public bool IsBypassed { get; set; }

    public int LatencySamples => Math.Max(0, _fftSize - _hopSize);

    public IReadOnlyList<PluginParameter> Parameters { get; }

    // Property getters for UI binding
    public float Reduction => _reduction;
    public bool IsLearning => _learning;
    public int LearningProgress => _noiseFrames;
    public int LearningTotal => _noiseFramesToLearn;
    public bool HasNoiseProfile => Volatile.Read(ref _hasProfileFlag) == 1;
    public int SampleRate => _sampleRate;

    /// <summary>
    /// Gets spectrum data for UI visualization. Caller must provide arrays of size DisplayBins.
    /// </summary>
    public void GetSpectrumData(float[] inputSpectrum, float[] inputPeaks,
                                 float[] outputSpectrum, float[] outputPeaks,
                                 float[] noiseProfile)
    {
        int index = Volatile.Read(ref _displayIndex);
        Array.Copy(_inputSpectrumBuffers[index], inputSpectrum, DisplayBins);
        Array.Copy(_inputPeakBuffers[index], inputPeaks, DisplayBins);
        Array.Copy(_outputSpectrumBuffers[index], outputSpectrum, DisplayBins);
        Array.Copy(_outputPeakBuffers[index], outputPeaks, DisplayBins);
        Array.Copy(_noiseSpectrumBuffers[index], noiseProfile, DisplayBins);
    }

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        ResetState(preserveReduction: true);
    }

    public void ApplyQuality(AudioQualityProfile profile)
    {
        ConfigureQuality(profile.NoiseFftSize, profile.NoiseHopSize, profile.NoiseLearnFrames);
    }

    public void LearnNoiseProfile()
    {
        StartLearning();
    }

    public void ToggleLearning()
    {
        if (_learning)
        {
            FinalizeNoiseProfile();
        }
        else
        {
            StartLearning();
        }
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed)
        {
            return;
        }

        int ringSize = _inputRing.Length;
        if (ringSize == 0)
        {
            return;
        }

        for (int i = 0; i < buffer.Length; i++)
        {
            _inputRing[_inputIndex] = buffer[i];
            buffer[i] = _outputRing[_inputIndex];
            _outputRing[_inputIndex] = 0f;

            _inputIndex++;
            if (_inputIndex >= ringSize)
            {
                _inputIndex = 0;
            }

            _hopCounter++;
            if (_hopCounter >= _hopSize)
            {
                _hopCounter = 0;
                ProcessFrame();
            }
        }
    }

    public void SetParameter(int index, float value)
    {
        if (index == ReductionIndex)
        {
            _reduction = Math.Clamp(value, 0f, 1f);
        }
    }

    public byte[] GetState()
    {
        return BitConverter.GetBytes(_reduction);
    }

    public void SetState(byte[] state)
    {
        if (state.Length >= sizeof(float))
        {
            _reduction = BitConverter.ToSingle(state, 0);
        }
    }

    public void Dispose()
    {
    }

    private void ProcessFrame()
    {
        if (_fft is null || _fftReal.Length == 0)
        {
            return;
        }

        int start = _inputIndex;
        int fftSize = _fftSize;
        for (int i = 0; i < fftSize; i++)
        {
            int index = start + i;
            if (index >= fftSize)
            {
                index -= fftSize;
            }
            float sample = _inputRing[index] * _window[i];
            _fftReal[i] = sample;
            _fftImag[i] = 0f;
        }

        _fft.Forward(_fftReal, _fftImag);
        UpdateDisplaySpectrum(_inputSpectrumBuffers, _inputPeakBuffers, _fftReal, _fftImag);

        if (_learning)
        {
            AccumulateNoiseProfile();
        }

        int bins = _noisePsd.Length;
        _smoothedReduction = _smoothedReduction * 0.9f + _reduction * 0.1f;
        bool applyReduction = _smoothedReduction > 1e-3f && Volatile.Read(ref _hasProfileFlag) == 1;
        float noiseScale = 1f + OverSubMax * _smoothedReduction;
        float minGain = DspUtils.DbToLinear(-60f * _smoothedReduction);

        for (int i = 0; i < bins; i++)
        {
            if (!applyReduction)
            {
                _gainBuffer[i] = 1f;
                continue;
            }

            float real = _fftReal[i];
            float imag = _fftImag[i];
            float mag2 = real * real + imag * imag;
            float noise = _noisePsd[i] * noiseScale + 1e-12f;
            float snrPost = mag2 / noise;

            // Decision-directed SNR drives a Wiener-style gain with a gentle floor.
            float snrPrior = DecisionDirectedAlpha * _prevSnr[i] +
                             (1f - DecisionDirectedAlpha) * MathF.Max(snrPost - 1f, 0f);
            _prevSnr[i] = snrPrior;

            float gain = snrPrior / (1f + snrPrior);
            _gainBuffer[i] = MathF.Max(gain, minGain);
        }

        // Smooth gains across frequency to reduce musical noise.
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 1; i < bins - 1; i++)
            {
                _gainBuffer[i] = _gainBuffer[i] * (1f - FrequencySmoothing) +
                                 (_gainBuffer[i - 1] + _gainBuffer[i + 1]) * 0.5f * FrequencySmoothing;
            }
        }

        for (int i = 0; i < bins; i++)
        {
            float gain = _prevGain[i] * TimeSmoothing + _gainBuffer[i] * (1f - TimeSmoothing);
            _prevGain[i] = gain;
            _fftReal[i] *= gain;
            _fftImag[i] *= gain;
        }

        UpdateDisplaySpectrum(_outputSpectrumBuffers, _outputPeakBuffers, _fftReal, _fftImag);

        for (int i = 1; i < bins - 1; i++)
        {
            int mirror = _fftSize - i;
            _fftReal[mirror] = _fftReal[i];
            _fftImag[mirror] = -_fftImag[i];
        }

        _fft.Inverse(_fftReal, _fftImag);

        for (int i = 0; i < fftSize; i++)
        {
            int index = start + i;
            if (index >= fftSize)
            {
                index -= fftSize;
            }
            _outputRing[index] += _fftReal[i] * _synthesisWindow[i] / _windowSum;
        }

        PublishDisplayBuffers();
    }

    private void ConfigureQuality(int fftSize, int hopSize, int noiseFrames)
    {
        int size = NextPowerOfTwo(Math.Max(256, fftSize));
        int hop = Math.Clamp(hopSize, 1, size - 1);
        _fftSize = size;
        _hopSize = hop;
        _noiseFramesToLearn = Math.Max(1, noiseFrames);

        _inputRing = new float[_fftSize];
        _outputRing = new float[_fftSize];
        _window = new float[_fftSize];
        _synthesisWindow = new float[_fftSize];
        _fftReal = new float[_fftSize];
        _fftImag = new float[_fftSize];

        int bins = _fftSize / 2 + 1;
        _noisePsd = new float[bins];
        _prevSnr = new float[bins];
        _prevGain = new float[bins];
        _gainBuffer = new float[bins];

        for (int i = 0; i < _fftSize; i++)
        {
            _window[i] = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / (_fftSize - 1));
            _synthesisWindow[i] = _window[i];
        }

        _fft = new FastFft(_fftSize);
        _windowSum = CalculateWindowSum();

        ResetState(preserveReduction: true);
    }

    private void ResetState(bool preserveReduction)
    {
        _inputIndex = 0;
        _hopCounter = 0;
        _learning = false;
        _noiseFrames = 0;
        _displayIndex = 0;
        Volatile.Write(ref _hasProfileFlag, 0);

        if (!preserveReduction)
        {
            _reduction = 0.5f;
        }
        _smoothedReduction = _reduction;

        Array.Clear(_inputRing, 0, _inputRing.Length);
        Array.Clear(_outputRing, 0, _outputRing.Length);
        Array.Clear(_fftReal, 0, _fftReal.Length);
        Array.Clear(_fftImag, 0, _fftImag.Length);
        Array.Clear(_noisePsd, 0, _noisePsd.Length);
        Array.Fill(_prevSnr, 0f);
        Array.Fill(_prevGain, 1f);
        Array.Fill(_gainBuffer, 1f);
        ClearDisplayBuffers();
    }

    private void StartLearning()
    {
        _learning = true;
        _noiseFrames = 0;
        Volatile.Write(ref _hasProfileFlag, 0);
        Array.Clear(_noisePsd, 0, _noisePsd.Length);
        Array.Fill(_prevSnr, 0f);
        Array.Fill(_prevGain, 1f);
    }

    private void AccumulateNoiseProfile()
    {
        for (int i = 0; i < _noisePsd.Length; i++)
        {
            float real = _fftReal[i];
            float imag = _fftImag[i];
            _noisePsd[i] += real * real + imag * imag;
        }

        _noiseFrames++;
        if (_noiseFrames >= _noiseFramesToLearn)
        {
            FinalizeNoiseProfile();
        }
    }

    private void FinalizeNoiseProfile()
    {
        if (_noiseFrames <= 0)
        {
            _learning = false;
            return;
        }

        float inv = 1f / _noiseFrames;
        for (int i = 0; i < _noisePsd.Length; i++)
        {
            _noisePsd[i] *= inv;
        }

        UpdateNoiseProfileDisplay();
        _learning = false;
        Volatile.Write(ref _hasProfileFlag, 1);
    }

    private float CalculateWindowSum()
    {
        var overlap = new float[_fftSize];
        for (int frame = 0; frame < _fftSize; frame += _hopSize)
        {
            for (int i = 0; i < _fftSize; i++)
            {
                int index = frame + i;
                if (index >= _fftSize)
                {
                    index -= _fftSize;
                }
                overlap[index] += _window[i] * _synthesisWindow[i];
            }
        }

        float sum = 0f;
        for (int i = 0; i < overlap.Length; i++)
        {
            sum += overlap[i];
        }

        return sum / overlap.Length;
    }

    private void PublishDisplayBuffers()
    {
        int current = Volatile.Read(ref _displayIndex);
        int next = current == 0 ? 1 : 0;
        Volatile.Write(ref _displayIndex, next);
    }

    private void UpdateDisplaySpectrum(float[][] spectrumBuffers, float[][] peakBuffers, float[] fftReal, float[] fftImag)
    {
        int current = Volatile.Read(ref _displayIndex);
        int target = current == 0 ? 1 : 0;
        float[] spectrum = spectrumBuffers[target];
        float[] peaks = peakBuffers[target];

        int fftBins = _fftSize / 2 + 1;
        float minFreq = 20f;
        float maxFreq = _sampleRate > 0 ? _sampleRate / 2f : 20000f;
        float normalizationFactor = 2f / _fftSize;

        for (int displayBin = 0; displayBin < DisplayBins; displayBin++)
        {
            float t0 = displayBin / (float)DisplayBins;
            float t1 = (displayBin + 1) / (float)DisplayBins;
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
                float real = fftReal[i];
                float imag = fftImag[i];
                float mag = MathF.Sqrt(real * real + imag * imag);
                sumSq += mag * mag;
                count++;
            }

            float magnitude = count > 0 ? MathF.Sqrt(sumSq / count) * normalizationFactor : 0f;
            spectrum[displayBin] = MathF.Max(spectrum[displayBin] * SpectrumDecay, magnitude);

            if (magnitude > peaks[displayBin])
            {
                peaks[displayBin] = magnitude;
            }
            else
            {
                peaks[displayBin] *= PeakDecay;
            }
        }
    }

    private void UpdateNoiseProfileDisplay()
    {
        int current = Volatile.Read(ref _displayIndex);
        int target = current == 0 ? 1 : 0;
        float[] profile = _noiseSpectrumBuffers[target];

        int fftBins = _fftSize / 2 + 1;
        float minFreq = 20f;
        float maxFreq = _sampleRate > 0 ? _sampleRate / 2f : 20000f;
        float normalizationFactor = 2f / _fftSize;

        for (int displayBin = 0; displayBin < DisplayBins; displayBin++)
        {
            float t0 = displayBin / (float)DisplayBins;
            float t1 = (displayBin + 1) / (float)DisplayBins;
            float freq0 = minFreq * MathF.Pow(maxFreq / minFreq, t0);
            float freq1 = minFreq * MathF.Pow(maxFreq / minFreq, t1);

            int bin0 = (int)(freq0 * fftBins / maxFreq);
            int bin1 = (int)(freq1 * fftBins / maxFreq);
            bin0 = Math.Clamp(bin0, 0, fftBins - 1);
            bin1 = Math.Clamp(bin1, bin0 + 1, fftBins);

            float sumSq = 0f;
            int count = 0;
            for (int i = bin0; i < bin1 && i < _noisePsd.Length; i++)
            {
                float mag = MathF.Sqrt(_noisePsd[i]);
                sumSq += mag * mag;
                count++;
            }

            profile[displayBin] = count > 0 ? MathF.Sqrt(sumSq / count) * normalizationFactor : 0f;
        }
    }

    private void ClearDisplayBuffers()
    {
        for (int i = 0; i < _inputSpectrumBuffers.Length; i++)
        {
            Array.Clear(_inputSpectrumBuffers[i], 0, _inputSpectrumBuffers[i].Length);
            Array.Clear(_outputSpectrumBuffers[i], 0, _outputSpectrumBuffers[i].Length);
            Array.Clear(_noiseSpectrumBuffers[i], 0, _noiseSpectrumBuffers[i].Length);
            Array.Clear(_inputPeakBuffers[i], 0, _inputPeakBuffers[i].Length);
            Array.Clear(_outputPeakBuffers[i], 0, _outputPeakBuffers[i].Length);
        }
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
}
