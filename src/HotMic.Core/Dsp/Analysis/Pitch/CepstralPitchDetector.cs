using System.Diagnostics;
using System.Threading;

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
    private int _profilingEnabled;
    private long _lastTotalTicks;
    private long _maxTotalTicks;

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

    internal void SetProfilingEnabled(bool enabled)
    {
        int value = enabled ? 1 : 0;
        int prior = Interlocked.Exchange(ref _profilingEnabled, value);
        if (prior != value)
        {
            ResetProfiling();
        }
    }

    internal PitchProfilingSnapshot GetProfilingSnapshot()
    {
        return new PitchProfilingSnapshot(
            PitchDetectorType.Cepstral,
            Interlocked.Read(ref _lastTotalTicks),
            Interlocked.Read(ref _maxTotalTicks),
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            _frameSize,
            _minLag,
            _maxLag);
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

        bool profilingEnabled = Volatile.Read(ref _profilingEnabled) != 0;
        long totalStart = 0;
        if (profilingEnabled)
        {
            totalStart = Stopwatch.GetTimestamp();
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
        double bestValue = double.MinValue;
        double sum = 0.0;
        int count = 0;

        for (int lag = _minLag; lag <= _maxLag; lag++)
        {
            double value = _real[lag];
            sum += value;
            count++;
            if (value > bestValue)
            {
                bestValue = value;
                bestLag = lag;
            }
        }

        double mean = count > 0 ? sum / count : 0.0;
        float cppDb = (float)((bestValue - mean) * 20.0);
        LastCpp = cppDb;

        if (profilingEnabled)
        {
            RecordProfiling(ref _lastTotalTicks, ref _maxTotalTicks, Stopwatch.GetTimestamp() - totalStart);
        }

        if (bestLag <= 0 || cppDb < _confidenceFloor)
        {
            return new PitchResult(null, 0f, false);
        }

        float frequency = _sampleRate / (float)bestLag;
        float confidence = Math.Clamp((cppDb - _confidenceFloor) / MathF.Max(1f, 20f - _confidenceFloor), 0f, 1f);
        return new PitchResult(frequency, confidence, true);
    }

    private void ResetProfiling()
    {
        Interlocked.Exchange(ref _lastTotalTicks, 0);
        Interlocked.Exchange(ref _maxTotalTicks, 0);
    }

    private static void RecordProfiling(ref long lastTicks, ref long maxTicks, long elapsedTicks)
    {
        Interlocked.Exchange(ref lastTicks, elapsedTicks);
        if (elapsedTicks <= 0)
        {
            return;
        }

        UpdateMax(ref maxTicks, elapsedTicks);
    }

    private static void UpdateMax(ref long location, long value)
    {
        long current = Interlocked.Read(ref location);
        while (value > current)
        {
            long prior = Interlocked.CompareExchange(ref location, value, current);
            if (prior == current)
            {
                break;
            }

            current = prior;
        }
    }
}
