using System.Threading;
using HotMic.Core.Dsp.Mapping;
using HotMic.Core.Threading;

namespace HotMic.Core.Dsp.Analysis;

/// <summary>
/// Background FFT worker for lightweight spectrum visualizations (off audio thread).
/// </summary>
internal sealed class SpectrumDisplayWorker : IDisposable
{
    private readonly int _fftSize;
    private readonly int _displayBins;
    private readonly int _hopSize;
    private readonly float _decay;
    private readonly LockFreeRingBuffer _captureBuffer;
    private readonly SpectrumMapper _mapper = new();
    private readonly float[][] _spectrumBuffers;
    private readonly float[] _analysisBuffer;
    private readonly float[] _hopBuffer;
    private readonly float[] _fftReal;
    private readonly float[] _fftImag;
    private readonly float[] _fftWindow;
    private readonly float[] _fftMagnitudes;
    private readonly float[] _mapScratch;
    private readonly FastFft _fft;
    private readonly float _normalization;

    private Thread? _analysisThread;
    private CancellationTokenSource? _analysisCts;
    private int _displayIndex;

    public SpectrumDisplayWorker(
        int sampleRate,
        int fftSize,
        int displayBins,
        float minFrequency,
        float maxFrequency,
        float decay,
        int hopSize,
        int captureBufferSize = 0)
    {
        _fftSize = Math.Max(64, fftSize);
        _displayBins = Math.Max(8, displayBins);
        _hopSize = Math.Clamp(hopSize, 1, _fftSize);
        _decay = Math.Clamp(decay, 0f, 0.999f);

        int bufferSize = captureBufferSize > 0 ? captureBufferSize : _fftSize * 4;
        _captureBuffer = new LockFreeRingBuffer(Math.Max(bufferSize, _fftSize * 2));

        _fft = new FastFft(_fftSize);
        _analysisBuffer = new float[_fftSize];
        _hopBuffer = new float[_hopSize];
        _fftReal = new float[_fftSize];
        _fftImag = new float[_fftSize];
        _fftWindow = new float[_fftSize];
        _fftMagnitudes = new float[_fftSize / 2];
        _mapScratch = new float[_displayBins];
        _spectrumBuffers = [new float[_displayBins], new float[_displayBins]];

        _normalization = 2f / _fftSize;

        for (int i = 0; i < _fftSize; i++)
        {
            _fftWindow[i] = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / (_fftSize - 1));
        }

        _mapper.Configure(_fftSize, Math.Max(1, sampleRate), _displayBins, minFrequency, maxFrequency, FrequencyScale.Logarithmic);
        EnsureAnalysisThread();
    }

    public void Write(ReadOnlySpan<float> buffer)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        _captureBuffer.Write(buffer);
    }

    public void GetSpectrum(float[] spectrum)
    {
        if (spectrum.Length < _displayBins)
        {
            return;
        }

        int index = Volatile.Read(ref _displayIndex);
        Array.Copy(_spectrumBuffers[index], spectrum, _displayBins);
    }

    public void Dispose()
    {
        if (_analysisThread is not null)
        {
            _analysisCts?.Cancel();
            _analysisThread.Join(500);
        }

        _analysisThread = null;
        _analysisCts?.Dispose();
        _analysisCts = null;
    }

    private void EnsureAnalysisThread()
    {
        if (_analysisThread is not null)
        {
            return;
        }

        _analysisCts = new CancellationTokenSource();
        _analysisThread = new Thread(() => AnalysisLoop(_analysisCts.Token))
        {
            IsBackground = true,
            Name = "HotMic-SpectrumDisplay"
        };
        _analysisThread.Start();
    }

    private void AnalysisLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (_captureBuffer.AvailableRead < _hopSize)
            {
                Thread.Sleep(1);
                continue;
            }

            int read = _captureBuffer.Read(_hopBuffer);
            if (read < _hopSize)
            {
                Thread.Sleep(1);
                continue;
            }

            int tail = _fftSize - _hopSize;
            Array.Copy(_analysisBuffer, _hopSize, _analysisBuffer, 0, tail);
            Array.Copy(_hopBuffer, 0, _analysisBuffer, tail, _hopSize);

            ComputeSpectrum();
        }
    }

    private void ComputeSpectrum()
    {
        for (int i = 0; i < _fftSize; i++)
        {
            _fftReal[i] = _analysisBuffer[i] * _fftWindow[i];
            _fftImag[i] = 0f;
        }

        _fft.Forward(_fftReal, _fftImag);

        int half = _fftMagnitudes.Length;
        for (int i = 0; i < half; i++)
        {
            float re = _fftReal[i];
            float im = _fftImag[i];
            _fftMagnitudes[i] = MathF.Sqrt(re * re + im * im) * _normalization;
        }

        _mapper.MapMax(_fftMagnitudes, _mapScratch);

        int current = Volatile.Read(ref _displayIndex);
        int target = current == 0 ? 1 : 0;
        float[] spectrum = _spectrumBuffers[target];
        for (int i = 0; i < _displayBins; i++)
        {
            float next = _mapScratch[i];
            spectrum[i] = MathF.Max(spectrum[i] * _decay, next);
        }

        Volatile.Write(ref _displayIndex, target);
    }
}
