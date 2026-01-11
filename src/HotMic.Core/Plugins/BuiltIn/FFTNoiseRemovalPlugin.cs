using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class FFTNoiseRemovalPlugin : IPlugin
{
    public const int ReductionIndex = 0;
    public const int SensitivityIndex = 1;

    private const int FftSize = 2048;
    private const int HopSize = FftSize / 2;
    private const int NoiseFramesToLearn = 30;

    // For UI visualization - use 64 bins for display
    public const int DisplayBins = 64;

    private readonly float[] _inputRing = new float[FftSize];
    private readonly float[] _outputRing = new float[FftSize];
    private readonly float[] _window = new float[FftSize];
    private readonly Complex32[] _fftBuffer = new Complex32[FftSize];
    private readonly float[] _noiseProfile = new float[FftSize / 2 + 1];
    private readonly float[] _frameBuffer = new float[FftSize];

    // Spectrum data for UI visualization (downsampled to DisplayBins)
    private readonly float[] _inputSpectrum = new float[DisplayBins];
    private readonly float[] _outputSpectrum = new float[DisplayBins];
    private readonly float[] _displayNoiseProfile = new float[DisplayBins];
    private readonly float[] _inputSpectrumPeaks = new float[DisplayBins];
    private readonly float[] _outputSpectrumPeaks = new float[DisplayBins];
    private const float SpectrumDecay = 0.85f;
    private const float PeakDecay = 0.98f;

    private int _inputIndex;
    private int _hopCounter;
    private bool _learning;
    private int _noiseFrames;
    private float _reduction = 0.5f;
    private float _sensitivityDb = -60f;
    private float _sensitivityLinear;
    private int _sampleRate;

    public FFTNoiseRemovalPlugin()
    {
        for (int i = 0; i < FftSize; i++)
        {
            _window[i] = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / (FftSize - 1));
        }

        Parameters =
        [
            new PluginParameter { Index = ReductionIndex, Name = "Reduction", MinValue = 0f, MaxValue = 1f, DefaultValue = 0.5f, Unit = "%" },
            new PluginParameter { Index = SensitivityIndex, Name = "Sensitivity", MinValue = -80f, MaxValue = 0f, DefaultValue = -60f, Unit = "dB" }
        ];

        UpdateSensitivity();
    }

    public string Id => "builtin:fft-noise";

    public string Name => "FFT Noise Removal";

    public bool IsBypassed { get; set; }

    public IReadOnlyList<PluginParameter> Parameters { get; }

    // Property getters for UI binding
    public float Reduction => _reduction;
    public float SensitivityDb => _sensitivityDb;
    public bool IsLearning => _learning;
    public int LearningProgress => _noiseFrames;
    public int LearningTotal => NoiseFramesToLearn;
    public bool HasNoiseProfile => !_learning && _noiseFrames >= NoiseFramesToLearn;
    public int SampleRate => _sampleRate;

    /// <summary>
    /// Gets spectrum data for UI visualization. Caller must provide arrays of size DisplayBins.
    /// </summary>
    public void GetSpectrumData(float[] inputSpectrum, float[] inputPeaks,
                                 float[] outputSpectrum, float[] outputPeaks,
                                 float[] noiseProfile)
    {
        if (inputSpectrum.Length >= DisplayBins)
            Array.Copy(_inputSpectrum, inputSpectrum, DisplayBins);
        if (inputPeaks.Length >= DisplayBins)
            Array.Copy(_inputSpectrumPeaks, inputPeaks, DisplayBins);
        if (outputSpectrum.Length >= DisplayBins)
            Array.Copy(_outputSpectrum, outputSpectrum, DisplayBins);
        if (outputPeaks.Length >= DisplayBins)
            Array.Copy(_outputSpectrumPeaks, outputPeaks, DisplayBins);
        if (noiseProfile.Length >= DisplayBins)
            Array.Copy(_displayNoiseProfile, noiseProfile, DisplayBins);
    }

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
    }

    public void LearnNoiseProfile()
    {
        _learning = true;
        _noiseFrames = 0;
        Array.Clear(_noiseProfile);
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed)
        {
            return;
        }

        for (int i = 0; i < buffer.Length; i++)
        {
            _inputRing[_inputIndex] = buffer[i];
            buffer[i] = _outputRing[_inputIndex];
            _outputRing[_inputIndex] = 0f;

            _inputIndex = (_inputIndex + 1) % FftSize;
            _hopCounter++;

            if (_hopCounter >= HopSize)
            {
                _hopCounter = 0;
                ProcessFrame();
            }
        }
    }

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case ReductionIndex:
                _reduction = Math.Clamp(value, 0f, 1f);
                break;
            case SensitivityIndex:
                _sensitivityDb = value;
                UpdateSensitivity();
                break;
        }
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 2];
        Buffer.BlockCopy(BitConverter.GetBytes(_reduction), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_sensitivityDb), 0, bytes, 4, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float) * 2)
        {
            return;
        }

        _reduction = BitConverter.ToSingle(state, 0);
        _sensitivityDb = BitConverter.ToSingle(state, 4);
        UpdateSensitivity();
    }

    public void Dispose()
    {
    }

    private void ProcessFrame()
    {
        int start = _inputIndex;
        for (int i = 0; i < FftSize; i++)
        {
            int index = (start + i) % FftSize;
            float sample = _inputRing[index] * _window[i];
            _frameBuffer[i] = sample;
            _fftBuffer[i] = new Complex32(sample, 0f);
        }

        Fourier.Forward(_fftBuffer, FourierOptions.Matlab);

        // Capture input spectrum for visualization (before noise removal)
        UpdateDisplaySpectrum(_inputSpectrum, _inputSpectrumPeaks, _fftBuffer);

        if (_learning)
        {
            AccumulateNoiseProfile();
        }

        for (int i = 0; i < _noiseProfile.Length; i++)
        {
            float real = _fftBuffer[i].Real;
            float imag = _fftBuffer[i].Imaginary;
            float magnitude = MathF.Sqrt(real * real + imag * imag);
            if (magnitude < _sensitivityLinear)
            {
                continue;
            }

            float reduced = MathF.Max(0f, magnitude - _noiseProfile[i] * _reduction);
            if (magnitude > 0f)
            {
                float scale = reduced / magnitude;
                _fftBuffer[i] = new Complex32(real * scale, imag * scale);
            }
        }

        // Capture output spectrum for visualization (after noise removal)
        UpdateDisplaySpectrum(_outputSpectrum, _outputSpectrumPeaks, _fftBuffer);

        for (int i = 1; i < _noiseProfile.Length - 1; i++)
        {
            int mirror = FftSize - i;
            _fftBuffer[mirror] = new Complex32(_fftBuffer[i].Real, -_fftBuffer[i].Imaginary);
        }

        Fourier.Inverse(_fftBuffer, FourierOptions.Matlab);

        // Note: Fourier.Inverse with FourierOptions.Matlab already divides by N,
        // so we don't divide by FftSize here
        for (int i = 0; i < FftSize; i++)
        {
            float sample = _fftBuffer[i].Real;
            int index = (start + i) % FftSize;
            _outputRing[index] += sample * _window[i];
        }
    }

    private void UpdateDisplaySpectrum(float[] spectrum, float[] peaks, Complex32[] fftData)
    {
        // Downsample FFT bins to display bins using logarithmic frequency mapping
        int fftBins = FftSize / 2 + 1;
        float minFreq = 20f;
        float maxFreq = _sampleRate > 0 ? _sampleRate / 2f : 20000f;

        // Normalization factor: divide by FftSize/2 so that full-scale audio = magnitude 1.0 = 0dB
        // This makes the spectrum display show actual signal levels in dB relative to full-scale
        const float normalizationFactor = 2f / FftSize;

        for (int displayBin = 0; displayBin < DisplayBins; displayBin++)
        {
            // Calculate frequency range for this display bin (log scale)
            float t0 = displayBin / (float)DisplayBins;
            float t1 = (displayBin + 1) / (float)DisplayBins;
            float freq0 = minFreq * MathF.Pow(maxFreq / minFreq, t0);
            float freq1 = minFreq * MathF.Pow(maxFreq / minFreq, t1);

            // Convert frequencies to FFT bin indices
            int bin0 = (int)(freq0 * fftBins / (maxFreq));
            int bin1 = (int)(freq1 * fftBins / (maxFreq));
            bin0 = Math.Clamp(bin0, 0, fftBins - 1);
            bin1 = Math.Clamp(bin1, bin0 + 1, fftBins);

            // Average magnitude in this frequency range
            float sum = 0f;
            int count = 0;
            for (int i = bin0; i < bin1 && i < fftBins; i++)
            {
                float real = fftData[i].Real;
                float imag = fftData[i].Imaginary;
                sum += MathF.Sqrt(real * real + imag * imag);
                count++;
            }

            // Normalize so full-scale audio = 1.0 (0dB)
            float magnitude = count > 0 ? (sum / count) * normalizationFactor : 0f;

            // Apply decay
            spectrum[displayBin] = MathF.Max(spectrum[displayBin] * SpectrumDecay, magnitude);

            // Update peaks
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

    private void AccumulateNoiseProfile()
    {
        for (int i = 0; i < _noiseProfile.Length; i++)
        {
            float real = _fftBuffer[i].Real;
            float imag = _fftBuffer[i].Imaginary;
            float magnitude = MathF.Sqrt(real * real + imag * imag);
            _noiseProfile[i] += magnitude;
        }

        _noiseFrames++;
        if (_noiseFrames >= NoiseFramesToLearn)
        {
            for (int i = 0; i < _noiseProfile.Length; i++)
            {
                _noiseProfile[i] /= _noiseFrames;
            }

            // Update display noise profile
            UpdateDisplayNoiseProfile();

            _learning = false;
        }
    }

    private void UpdateDisplayNoiseProfile()
    {
        // Downsample noise profile to display bins using logarithmic frequency mapping
        int fftBins = FftSize / 2 + 1;
        float minFreq = 20f;
        float maxFreq = _sampleRate > 0 ? _sampleRate / 2f : 20000f;

        // Same normalization as UpdateDisplaySpectrum so noise profile is at correct dB level
        const float normalizationFactor = 2f / FftSize;

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

            float sum = 0f;
            int count = 0;
            for (int i = bin0; i < bin1 && i < _noiseProfile.Length; i++)
            {
                sum += _noiseProfile[i];
                count++;
            }

            // Normalize to match spectrum display scale
            _displayNoiseProfile[displayBin] = count > 0 ? (sum / count) * normalizationFactor : 0f;
        }
    }

    private void UpdateSensitivity()
    {
        _sensitivityLinear = MathF.Pow(10f, _sensitivityDb / 20f);
    }
}
