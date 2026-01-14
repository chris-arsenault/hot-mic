namespace HotMic.Core.Dsp.Analysis.Pitch;

/// <summary>
/// Cepstrum-based pitch detector with CPP (cepstral peak prominence) reporting.
/// </summary>
public sealed class CepstralPitchDetector
{
    private int _sampleRate;
    private int _frameSize;
    private int _minLag;
    private int _maxLag;
    private float _confidenceFloor;

    private FastFft? _fft;
    private float[] _real = Array.Empty<float>();
    private float[] _imag = Array.Empty<float>();
    private float[] _window = Array.Empty<float>();

    public CepstralPitchDetector(int sampleRate, int frameSize, float minFrequency, float maxFrequency, float confidenceFloor = 2f)
    {
        Configure(sampleRate, frameSize, minFrequency, maxFrequency, confidenceFloor);
    }

    /// <summary>
    /// Gets the most recent cepstral peak prominence (CPP) value in dB.
    /// </summary>
    public float LastCpp { get; private set; }

    /// <summary>
    /// Updates the detector configuration for the current sample rate and analysis window.
    /// </summary>
    public void Configure(int sampleRate, int frameSize, float minFrequency, float maxFrequency, float confidenceFloor)
    {
        _sampleRate = Math.Max(1, sampleRate);
        _frameSize = Math.Max(64, frameSize);
        _confidenceFloor = Math.Clamp(confidenceFloor, 0.5f, 20f);

        float maxFreq = Math.Clamp(maxFrequency, 40f, _sampleRate * 0.45f);
        float minFreq = Math.Clamp(minFrequency, 20f, maxFreq - 1f);
        _minLag = Math.Max(2, (int)(_sampleRate / maxFreq));
        _maxLag = Math.Min(_frameSize - 2, (int)(_sampleRate / minFreq));

        if (_real.Length != _frameSize)
        {
            _real = new float[_frameSize];
            _imag = new float[_frameSize];
            _window = new float[_frameSize];
        }

        if (_fft is null || _fft.Size != _frameSize)
        {
            _fft = new FastFft(_frameSize);
        }

        WindowFunctions.Fill(_window, WindowFunction.Hann);
    }

    /// <summary>
    /// Detects pitch for the provided frame (length must be >= configured frame size).
    /// </summary>
    public PitchResult Detect(ReadOnlySpan<float> frame)
    {
        if (frame.Length < _frameSize || _maxLag <= _minLag)
        {
            LastCpp = 0f;
            return new PitchResult(null, 0f, false);
        }

        for (int i = 0; i < _frameSize; i++)
        {
            _real[i] = frame[i] * _window[i];
            _imag[i] = 0f;
        }

        _fft?.Forward(_real, _imag);

        for (int i = 0; i < _frameSize; i++)
        {
            float re = _real[i];
            float im = _imag[i];
            float mag = MathF.Sqrt(re * re + im * im);
            _real[i] = MathF.Log10(mag + 1e-12f);
            _imag[i] = 0f;
        }

        _fft?.Inverse(_real, _imag);

        int bestLag = -1;
        float bestValue = float.MinValue;
        float sum = 0f;
        int count = 0;

        for (int lag = _minLag; lag <= _maxLag; lag++)
        {
            float value = _real[lag];
            sum += value;
            count++;
            if (value > bestValue)
            {
                bestValue = value;
                bestLag = lag;
            }
        }

        float mean = count > 0 ? sum / count : 0f;
        float cppDb = (bestValue - mean) * 20f;
        LastCpp = cppDb;

        if (bestLag <= 0 || cppDb < _confidenceFloor)
        {
            return new PitchResult(null, 0f, false);
        }

        float frequency = _sampleRate / (float)bestLag;
        float confidence = Math.Clamp((cppDb - _confidenceFloor) / MathF.Max(1f, 20f - _confidenceFloor), 0f, 1f);
        return new PitchResult(frequency, confidence, true);
    }
}
