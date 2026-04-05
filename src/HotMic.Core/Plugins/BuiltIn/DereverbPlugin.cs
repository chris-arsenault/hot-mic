using HotMic.Common.Configuration;
using HotMic.Core.Dsp;

namespace HotMic.Core.Plugins.BuiltIn;

/// <summary>
/// Single-channel dereverberation using frame-online recursive WPE
/// (Weighted Prediction Error) with Kalman-gain-based filter updates.
/// </summary>
public sealed class DereverbPlugin : IPlugin, IQualityConfigurablePlugin
{
    public const int ReductionIndex = 0;

    private int _fftSize = 1024;
    private int _hopSize = 256;
    private int _taps = 15;
    private int _delay = 3;
    private float _alpha = 0.99f;

    // STFT ring buffers and window
    private float[] _inputRing = Array.Empty<float>();
    private float[] _outputRing = Array.Empty<float>();
    private float[] _window = Array.Empty<float>();
    private float[] _synthesisWindow = Array.Empty<float>();
    private float[] _fftReal = Array.Empty<float>();
    private float[] _fftImag = Array.Empty<float>();
    private FastFft? _fft;
    private float _windowSum;
    private int _inputIndex;
    private int _hopCounter;

    // WPE filter state per frequency bin
    private float[][] _tapReal = Array.Empty<float[]>();
    private float[][] _tapImag = Array.Empty<float[]>();
    private float[][] _invCovReal = Array.Empty<float[]>();
    private float[][] _invCovImag = Array.Empty<float[]>();
    private float[] _power = Array.Empty<float>();

    // Circular buffer of past STFT frames
    private float[][] _frameBufReal = Array.Empty<float[]>();
    private float[][] _frameBufImag = Array.Empty<float[]>();
    private int _frameIdx;
    private int _warmupCount;

    // Scratch arrays (reused per frame, no allocations in Process)
    private float[] _winR = Array.Empty<float>();
    private float[] _winI = Array.Empty<float>();
    private float[] _numR = Array.Empty<float>();
    private float[] _numI = Array.Empty<float>();
    private float[] _kalR = Array.Empty<float>();
    private float[] _kalI = Array.Empty<float>();

    // Parameter
    private float _reduction = 0.5f;
    private float _smoothedReduction = 0.5f;

    public DereverbPlugin()
    {
        ConfigureQuality(_fftSize, _hopSize, _taps, _delay, _alpha);
        Parameters =
        [
            new PluginParameter { Index = ReductionIndex, Name = "Reduction", MinValue = 0f, MaxValue = 1f, DefaultValue = 0.5f, Unit = "%" }
        ];
    }

    public string Id => "builtin:deverb";
    public string Name => "Dereverberation";
    public bool IsBypassed { get; set; }
    public int LatencySamples => Math.Max(0, _fftSize - _hopSize);
    public IReadOnlyList<PluginParameter> Parameters { get; }

    public float Reduction => _reduction;

    public void Initialize(int sampleRate, int blockSize)
    {
        ResetState(preserveReduction: true);
    }

    public void ApplyQuality(AudioQualityProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        ConfigureQuality(profile.DereverbFftSize, profile.DereverbHopSize,
            profile.DereverbTaps, profile.DereverbDelay, profile.DereverbAlpha);
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        if (IsBypassed)
            return;

        int ringSize = _inputRing.Length;
        if (ringSize == 0)
            return;

        for (int i = 0; i < buffer.Length; i++)
        {
            _inputRing[_inputIndex] = buffer[i];
            buffer[i] = _outputRing[_inputIndex];
            _outputRing[_inputIndex] = 0f;

            _inputIndex++;
            if (_inputIndex >= ringSize)
                _inputIndex = 0;

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
            _reduction = Math.Clamp(value, 0f, 1f);
    }

    public byte[] GetState()
    {
        return BitConverter.GetBytes(_reduction);
    }

    public void SetState(byte[] state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Length >= sizeof(float))
            _reduction = BitConverter.ToSingle(state, 0);
    }

    public void Dispose() { }

    /// <summary>
    /// Configures WPE parameters directly (for testing).
    /// </summary>
    internal void ConfigureWpe(int bins, int taps, int delay, float alpha)
    {
        _fftSize = (bins - 1) * 2; // derive fftSize from bins for LatencySamples consistency
        _hopSize = 1;
        _taps = Math.Max(1, taps);
        _delay = Math.Max(1, delay);
        _alpha = Math.Clamp(alpha, 0.5f, 0.999f);

        int bufLen = _taps + _delay + 1;

        _tapReal = new float[bins][];
        _tapImag = new float[bins][];
        _invCovReal = new float[bins][];
        _invCovImag = new float[bins][];
        _power = new float[bins];
        for (int f = 0; f < bins; f++)
        {
            _tapReal[f] = new float[_taps];
            _tapImag[f] = new float[_taps];
            _invCovReal[f] = new float[_taps * _taps];
            _invCovImag[f] = new float[_taps * _taps];
            for (int i = 0; i < _taps; i++)
                _invCovReal[f][i * _taps + i] = 1f;
            _power[f] = 1e-6f;
        }

        _frameBufReal = new float[bufLen][];
        _frameBufImag = new float[bufLen][];
        for (int i = 0; i < bufLen; i++)
        {
            _frameBufReal[i] = new float[bins];
            _frameBufImag[i] = new float[bins];
        }

        _winR = new float[_taps];
        _winI = new float[_taps];
        _numR = new float[_taps];
        _numI = new float[_taps];
        _kalR = new float[_taps];
        _kalI = new float[_taps];

        _frameIdx = 0;
        _warmupCount = 0;
    }

    /// <summary>
    /// Processes one STFT frame through the WPE core (no FFT/IFFT/overlap-add).
    /// Reads bins from specReal/specImag, writes output to outReal/outImag.
    /// </summary>
    internal void ProcessStftFrame(float[] specReal, float[] specImag,
                                    float[] outReal, float[] outImag,
                                    float reduction)
    {
        int bins = _tapReal.Length;
        int taps = _taps;
        int delay = _delay;
        int bufLen = _frameBufReal.Length;
        float alpha = _alpha;
        bool warmingUp = _warmupCount < delay + taps;

        for (int f = 0; f < bins; f++)
        {
            float yR = specReal[f];
            float yI = specImag[f];

            if (warmingUp)
            {
                _frameBufReal[_frameIdx][f] = yR;
                _frameBufImag[_frameIdx][f] = yI;
                outReal[f] = yR;
                outImag[f] = yI;
                continue;
            }

            for (int k = 0; k < taps; k++)
            {
                int fi = (_frameIdx - delay - k - 1 + bufLen) % bufLen;
                _winR[k] = _frameBufReal[fi][f];
                _winI[k] = _frameBufImag[fi][f];
            }

            float predR = 0f, predI = 0f;
            float[] tR = _tapReal[f], tI = _tapImag[f];
            for (int k = 0; k < taps; k++)
            {
                predR += tR[k] * _winR[k] - tI[k] * _winI[k];
                predI += -(tR[k] * _winI[k] + tI[k] * _winR[k]);
            }

            float zR = yR - predR;
            float zI = yI - predI;

            float zMag2 = zR * zR + zI * zI;
            _power[f] = alpha * _power[f] + (1f - alpha) * zMag2;
            float pwr = _power[f];

            float[] covR = _invCovReal[f], covI = _invCovImag[f];
            for (int i = 0; i < taps; i++)
            {
                float nR = 0f, nI = 0f;
                int row = i * taps;
                for (int j = 0; j < taps; j++)
                {
                    float pR = covR[row + j];
                    float pI = covI[row + j];
                    nR += pR * _winR[j] + pI * _winI[j];
                    nI += pI * _winR[j] - pR * _winI[j];
                }
                _numR[i] = nR;
                _numI[i] = nI;
            }

            float denR = alpha * pwr;
            float denI = 0f;
            for (int i = 0; i < taps; i++)
            {
                denR += _winR[i] * _numR[i] - _winI[i] * _numI[i];
                denI += _winR[i] * _numI[i] + _winI[i] * _numR[i];
            }

            float denMag2 = denR * denR + denI * denI;
            float invDen = 1f / MathF.Max(denMag2, 1e-12f);
            for (int i = 0; i < taps; i++)
            {
                _kalR[i] = (_numR[i] * denR + _numI[i] * denI) * invDen;
                _kalI[i] = (_numI[i] * denR - _numR[i] * denI) * invDen;
            }

            float invAlpha = 1f / alpha;
            for (int i = 0; i < taps; i++)
            {
                int row = i * taps;
                float kR = _kalR[i], kI = _kalI[i];
                for (int j = 0; j < taps; j++)
                {
                    float outerR = kR * _numR[j] + kI * _numI[j];
                    float outerI = kI * _numR[j] - kR * _numI[j];
                    covR[row + j] = (covR[row + j] - outerR) * invAlpha;
                    covI[row + j] = (covI[row + j] - outerI) * invAlpha;
                }
            }

            for (int k = 0; k < taps; k++)
            {
                tR[k] += _kalR[k] * zR + _kalI[k] * zI;
                tI[k] += _kalI[k] * zR - _kalR[k] * zI;
            }

            _frameBufReal[_frameIdx][f] = yR;
            _frameBufImag[_frameIdx][f] = yI;

            outReal[f] = yR - reduction * predR;
            outImag[f] = yI - reduction * predI;
        }

        _frameIdx = (_frameIdx + 1) % bufLen;
        if (warmingUp)
            _warmupCount++;
    }

    private void ProcessFrame()
    {
        if (_fft is null || _fftReal.Length == 0)
            return;

        int fftSize = _fftSize;
        int start = _inputIndex;
        int bins = _fftSize / 2 + 1;

        // --- Forward STFT ---
        for (int i = 0; i < fftSize; i++)
        {
            int index = start + i;
            if (index >= fftSize)
                index -= fftSize;
            _fftReal[i] = _inputRing[index] * _window[i];
            _fftImag[i] = 0f;
        }
        _fft.Forward(_fftReal, _fftImag);

        // --- Smooth reduction parameter ---
        _smoothedReduction = _smoothedReduction * 0.9f + _reduction * 0.1f;

        // --- WPE core ---
        ProcessStftFrame(_fftReal, _fftImag, _fftReal, _fftImag, _smoothedReduction);

        // --- Hermitian symmetry ---
        for (int i = 1; i < bins - 1; i++)
        {
            int mirror = _fftSize - i;
            _fftReal[mirror] = _fftReal[i];
            _fftImag[mirror] = -_fftImag[i];
        }

        // --- Inverse STFT ---
        _fft.Inverse(_fftReal, _fftImag);

        // --- Overlap-add ---
        for (int i = 0; i < fftSize; i++)
        {
            int index = start + i;
            if (index >= fftSize)
                index -= fftSize;
            _outputRing[index] += _fftReal[i] * _synthesisWindow[i] / _windowSum;
        }
    }

    private void ConfigureQuality(int fftSize, int hopSize, int taps, int delay, float alpha)
    {
        int size = NextPowerOfTwo(Math.Max(256, fftSize));
        _fftSize = size;
        _hopSize = Math.Clamp(hopSize, 1, size - 1);
        _taps = Math.Max(1, taps);
        _delay = Math.Max(1, delay);
        _alpha = Math.Clamp(alpha, 0.5f, 0.999f);

        int bins = _fftSize / 2 + 1;
        int bufLen = _taps + _delay + 1;

        // STFT buffers
        _inputRing = new float[_fftSize];
        _outputRing = new float[_fftSize];
        _window = new float[_fftSize];
        _synthesisWindow = new float[_fftSize];
        _fftReal = new float[_fftSize];
        _fftImag = new float[_fftSize];

        // Hann window
        for (int i = 0; i < _fftSize; i++)
        {
            _window[i] = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / (_fftSize - 1));
            _synthesisWindow[i] = _window[i];
        }
        _fft = new FastFft(_fftSize);
        _windowSum = CalculateWindowSum();

        // WPE state
        _tapReal = new float[bins][];
        _tapImag = new float[bins][];
        _invCovReal = new float[bins][];
        _invCovImag = new float[bins][];
        _power = new float[bins];
        for (int f = 0; f < bins; f++)
        {
            _tapReal[f] = new float[_taps];
            _tapImag[f] = new float[_taps];
            _invCovReal[f] = new float[_taps * _taps];
            _invCovImag[f] = new float[_taps * _taps];
            // Initialize inverse covariance to identity
            for (int i = 0; i < _taps; i++)
                _invCovReal[f][i * _taps + i] = 1f;
            _power[f] = 1e-6f;
        }

        // Frame buffer
        _frameBufReal = new float[bufLen][];
        _frameBufImag = new float[bufLen][];
        for (int i = 0; i < bufLen; i++)
        {
            _frameBufReal[i] = new float[bins];
            _frameBufImag[i] = new float[bins];
        }

        // Scratch arrays
        _winR = new float[_taps];
        _winI = new float[_taps];
        _numR = new float[_taps];
        _numI = new float[_taps];
        _kalR = new float[_taps];
        _kalI = new float[_taps];

        ResetState(preserveReduction: true);
    }

    private void ResetState(bool preserveReduction)
    {
        _inputIndex = 0;
        _hopCounter = 0;
        _frameIdx = 0;
        _warmupCount = 0;

        if (!preserveReduction)
            _reduction = 0.5f;
        _smoothedReduction = _reduction;

        Array.Clear(_inputRing);
        Array.Clear(_outputRing);
        Array.Clear(_fftReal);
        Array.Clear(_fftImag);

        int bins = _fftSize / 2 + 1;
        for (int f = 0; f < bins && f < _tapReal.Length; f++)
        {
            Array.Clear(_tapReal[f]);
            Array.Clear(_tapImag[f]);
            Array.Clear(_invCovReal[f]);
            Array.Clear(_invCovImag[f]);
            for (int i = 0; i < _taps; i++)
                _invCovReal[f][i * _taps + i] = 1f;
            _power[f] = 1e-6f;
        }

        for (int i = 0; i < _frameBufReal.Length; i++)
        {
            Array.Clear(_frameBufReal[i]);
            Array.Clear(_frameBufImag[i]);
        }
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
                    index -= _fftSize;
                overlap[index] += _window[i] * _synthesisWindow[i];
            }
        }

        float sum = 0f;
        for (int i = 0; i < overlap.Length; i++)
            sum += overlap[i];
        return sum / overlap.Length;
    }

    private static int NextPowerOfTwo(int value)
    {
        int power = 1;
        while (power < value)
            power <<= 1;
        return power;
    }
}
