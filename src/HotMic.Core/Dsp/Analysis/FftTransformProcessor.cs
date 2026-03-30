using HotMic.Core.Dsp.Fft;
using HotMic.Core.Dsp.Spectrogram;

namespace HotMic.Core.Dsp.Analysis;

/// <summary>
/// Computes FFT magnitudes (and optional reassignment data) from analysis buffers.
/// </summary>
public sealed class FftTransformProcessor
{
    private int _sampleRate;
    private int _fftSize;
    private float _binResolution;
    private float _normalization;
    private WindowFunction _windowFunction;

    private FastFft? _fft;
    private float[] _fftReal = Array.Empty<float>();
    private float[] _fftImag = Array.Empty<float>();
    private float[] _fftTimeReal = Array.Empty<float>();
    private float[] _fftTimeImag = Array.Empty<float>();
    private float[] _fftDerivReal = Array.Empty<float>();
    private float[] _fftDerivImag = Array.Empty<float>();
    private float[] _window = Array.Empty<float>();
    private float[] _windowTime = Array.Empty<float>();
    private float[] _windowDerivative = Array.Empty<float>();
    private float[] _magnitudes = Array.Empty<float>();

    public int FftSize => _fftSize;
    public float BinResolution => _binResolution;
    public float Normalization => _normalization;
    public float[] Magnitudes => _magnitudes;
    public float[] FftReal => _fftReal;
    public float[] FftImag => _fftImag;
    public float[] FftTimeReal => _fftTimeReal;
    public float[] FftTimeImag => _fftTimeImag;
    public float[] FftDerivReal => _fftDerivReal;
    public float[] FftDerivImag => _fftDerivImag;
    public float[] Window => _window;
    public float[] WindowTime => _windowTime;
    public float[] WindowDerivative => _windowDerivative;

    public void Configure(int sampleRate, int fftSize, WindowFunction windowFunction)
    {
        sampleRate = Math.Max(1, sampleRate);
        fftSize = Math.Max(1, fftSize);

        bool sizeChanged = fftSize != _fftSize || sampleRate != _sampleRate;
        _sampleRate = sampleRate;
        _fftSize = fftSize;
        _binResolution = sampleRate / (float)fftSize;

        if (sizeChanged)
        {
            _fft = new FastFft(fftSize);
            _fftReal = new float[fftSize];
            _fftImag = new float[fftSize];
            _fftTimeReal = new float[fftSize];
            _fftTimeImag = new float[fftSize];
            _fftDerivReal = new float[fftSize];
            _fftDerivImag = new float[fftSize];
            _window = new float[fftSize];
            _windowTime = new float[fftSize];
            _windowDerivative = new float[fftSize];
            _magnitudes = new float[fftSize / 2];
        }

        if (sizeChanged || windowFunction != _windowFunction)
        {
            _windowFunction = windowFunction;
            UpdateWindow(windowFunction);
        }
    }

    public void UpdateWindow(WindowFunction windowFunction)
    {
        _windowFunction = windowFunction;
        WindowFunctions.Fill(_window, windowFunction);
        UpdateWindowNormalization();
        UpdateReassignWindows();
    }

    public FftTransformDebug Compute(ReadOnlySpan<float> processedBuffer, bool reassignEnabled)
    {
        int size = _fftSize;
        float analysisBufMax = 0f;
        float windowMax = 0f;
        float fftRealMax = 0f;

        if (reassignEnabled)
        {
            for (int i = 0; i < size; i++)
            {
                float sample = processedBuffer[i];
                float win = _window[i];
                _fftReal[i] = sample * win;
                _fftImag[i] = 0f;
                _fftTimeReal[i] = sample * _windowTime[i];
                _fftTimeImag[i] = 0f;
                _fftDerivReal[i] = sample * _windowDerivative[i];
                _fftDerivImag[i] = 0f;
            }

            _fft?.Forward(_fftReal, _fftImag);
            _fft?.Forward(_fftTimeReal, _fftTimeImag);
            _fft?.Forward(_fftDerivReal, _fftDerivImag);
        }
        else
        {
            for (int i = 0; i < size; i++)
            {
                float sample = processedBuffer[i];
                float win = _window[i];
                _fftReal[i] = sample * win;
                _fftImag[i] = 0f;
                analysisBufMax = MathF.Max(analysisBufMax, MathF.Abs(sample));
                windowMax = MathF.Max(windowMax, MathF.Abs(win));
                fftRealMax = MathF.Max(fftRealMax, MathF.Abs(_fftReal[i]));
            }

            _fft?.Forward(_fftReal, _fftImag);
        }

        int half = size / 2;
        float fftMax = 0f;
        float normalization = _normalization;
        for (int i = 0; i < half; i++)
        {
            float re = _fftReal[i];
            float im = _fftImag[i];
            float mag = MathF.Sqrt(re * re + im * im) * normalization;
            _magnitudes[i] = mag;
            fftMax = MathF.Max(fftMax, mag);
        }

        return new FftTransformDebug(
            analysisBufMax,
            windowMax,
            fftRealMax,
            fftMax);
    }

    private void UpdateWindowNormalization()
    {
        double sum = 0.0;
        for (int i = 0; i < _window.Length; i++)
        {
            sum += _window[i];
        }

        float denom = sum > 1e-6 ? (float)sum : 1f;
        _normalization = 2f / denom;
    }

    private void UpdateReassignWindows()
    {
        if (_windowTime.Length != _fftSize || _windowDerivative.Length != _fftSize)
        {
            return;
        }

        float center = 0.5f * (_fftSize - 1);
        for (int i = 0; i < _fftSize; i++)
        {
            float t = i - center;
            _windowTime[i] = _window[i] * t;
        }

        for (int i = 0; i < _fftSize; i++)
        {
            float prev = i > 0 ? _window[i - 1] : _window[i];
            float next = i < _fftSize - 1 ? _window[i + 1] : _window[i];
            _windowDerivative[i] = 0.5f * (next - prev);
        }
    }
}

public readonly record struct FftTransformDebug(
    float AnalysisBufferMax,
    float WindowMax,
    float FftRealMax,
    float FftMax);
