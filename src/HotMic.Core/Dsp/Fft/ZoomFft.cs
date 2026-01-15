namespace HotMic.Core.Dsp.Fft;

/// <summary>
/// Zoom FFT using frequency-shift and decimation for high-resolution
/// analysis of a limited frequency band. Zero allocations after Configure().
/// </summary>
public sealed class ZoomFft
{
    private const float TwoPi = MathF.PI * 2f;

    // Pre-allocated buffers
    private float[] _shiftedReal = Array.Empty<float>();
    private float[] _shiftedImag = Array.Empty<float>();
    private float[] _decimatedReal = Array.Empty<float>();
    private float[] _decimatedImag = Array.Empty<float>();
    private float[] _cosTable = Array.Empty<float>();
    private float[] _sinTable = Array.Empty<float>();
    private float[] _lowpassKernel = Array.Empty<float>();
    private float[] _filterStateReal = Array.Empty<float>();
    private float[] _filterStateImag = Array.Empty<float>();
    private float[] _window = Array.Empty<float>();
    private float[] _windowTime = Array.Empty<float>();
    private float[] _windowDerivative = Array.Empty<float>();

    // Reassignment buffers
    private float[] _timeWeightedReal = Array.Empty<float>();
    private float[] _timeWeightedImag = Array.Empty<float>();
    private float[] _derivWeightedReal = Array.Empty<float>();
    private float[] _derivWeightedImag = Array.Empty<float>();
    private float[] _timeFilterStateReal = Array.Empty<float>();
    private float[] _timeFilterStateImag = Array.Empty<float>();
    private float[] _derivFilterStateReal = Array.Empty<float>();
    private float[] _derivFilterStateImag = Array.Empty<float>();
    private float[] _timeDecimatedReal = Array.Empty<float>();
    private float[] _timeDecimatedImag = Array.Empty<float>();
    private float[] _derivDecimatedReal = Array.Empty<float>();
    private float[] _derivDecimatedImag = Array.Empty<float>();

    private FastFft? _fft;
    private int _inputSize;
    private int _fftSize;
    private int _decimationFactor;
    private int _zoomFactor;
    private float _centerFrequency;
    private float _bandwidth;
    private float _sampleRate;
    private float _normalization;
    private int _filterLength;
    private bool _configured;

    /// <summary>
    /// Number of output frequency bins (positive frequencies only).
    /// This equals FftSize/2, same as standard FFT with fftSize samples.
    /// </summary>
    public int OutputBins => _fftSize / 2;

    /// <summary>
    /// Required input size in samples. Callers must provide at least this many samples.
    /// This is fftSize * zoomFactor.
    /// </summary>
    public int RequiredInputSize => _inputSize;

    /// <summary>
    /// Frequency resolution in Hz per bin.
    /// For true zoom, this is sampleRate / (fftSize * zoomFactor) = sampleRate / inputSize.
    /// </summary>
    public float BinResolutionHz { get; private set; }

    /// <summary>
    /// The minimum frequency of the analyzed band.
    /// </summary>
    public float MinFrequency { get; private set; }

    /// <summary>
    /// The maximum frequency of the analyzed band.
    /// </summary>
    public float MaxFrequency { get; private set; }

    /// <summary>
    /// The zoom factor (how much resolution is improved vs standard FFT).
    /// </summary>
    public int ZoomFactor => _zoomFactor;

    /// <summary>
    /// Configure the zoom FFT for the specified frequency range.
    /// </summary>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <param name="fftSize">Base FFT size (should be power of 2). Output will have fftSize/2 bins.</param>
    /// <param name="minHz">Minimum frequency of interest.</param>
    /// <param name="maxHz">Maximum frequency of interest.</param>
    /// <param name="zoomFactor">Zoom factor (1, 2, 4, 8). Higher = better resolution but more latency.</param>
    /// <param name="window">Window function type to apply.</param>
    /// <remarks>
    /// True ZoomFFT works by accumulating fftSize * zoomFactor samples, then:
    /// 1. Frequency shift to move minHz to DC
    /// 2. Low-pass filter at bandwidth
    /// 3. Decimate by zoomFactor
    /// 4. FFT at fftSize
    ///
    /// This gives fftSize/2 output bins with resolution = sampleRate / (fftSize * zoomFactor).
    /// For zoomFactor=4 with fftSize=4096 at 48kHz: resolution = 2.93 Hz (vs 11.7 Hz standard).
    /// Latency increases linearly: zoomFactor=4 means 4x more samples needed = 4x latency.
    /// </remarks>
    public void Configure(int sampleRate, int fftSize, float minHz, float maxHz, int zoomFactor = 2, WindowFunction window = WindowFunction.Hann)
    {
        if (fftSize <= 0 || (fftSize & (fftSize - 1)) != 0)
        {
            throw new ArgumentException("FFT size must be a power of two.", nameof(fftSize));
        }

        // Clamp zoom factor to valid range (power of 2, 1-8)
        zoomFactor = Math.Clamp(zoomFactor, 1, 8);
        if ((zoomFactor & (zoomFactor - 1)) != 0)
        {
            // Round to nearest power of 2
            zoomFactor = zoomFactor >= 6 ? 8 : zoomFactor >= 3 ? 4 : zoomFactor >= 2 ? 2 : 1;
        }

        float nyquist = sampleRate * 0.5f;
        minHz = Math.Clamp(minHz, 1f, nyquist - 1f);
        maxHz = Math.Clamp(maxHz, minHz + 1f, nyquist);

        _sampleRate = sampleRate;
        _fftSize = fftSize;
        _zoomFactor = zoomFactor;
        _inputSize = fftSize * zoomFactor;  // True zoom: accumulate more samples
        _decimationFactor = zoomFactor;      // Decimate by zoom factor

        // Shift by minHz so that bin 0 = minHz after transform
        _centerFrequency = minHz;
        _bandwidth = maxHz - minHz;
        MinFrequency = minHz;
        MaxFrequency = maxHz;

        // True resolution improvement: sampleRate / inputSize
        BinResolutionHz = (float)sampleRate / _inputSize;

        // Allocate input processing buffers (at inputSize = fftSize * zoomFactor)
        if (_shiftedReal.Length != _inputSize)
        {
            _shiftedReal = new float[_inputSize];
            _shiftedImag = new float[_inputSize];
            _cosTable = new float[_inputSize];
            _sinTable = new float[_inputSize];
            _window = new float[_inputSize];
            _windowTime = new float[_inputSize];
            _windowDerivative = new float[_inputSize];
            _timeWeightedReal = new float[_inputSize];
            _timeWeightedImag = new float[_inputSize];
            _derivWeightedReal = new float[_inputSize];
            _derivWeightedImag = new float[_inputSize];
        }

        // Allocate FFT buffers (at fftSize after decimation)
        if (_decimatedReal.Length != _fftSize)
        {
            _decimatedReal = new float[_fftSize];
            _decimatedImag = new float[_fftSize];
            _timeDecimatedReal = new float[_fftSize];
            _timeDecimatedImag = new float[_fftSize];
            _derivDecimatedReal = new float[_fftSize];
            _derivDecimatedImag = new float[_fftSize];
            _fft = new FastFft(_fftSize);
        }

        // Build frequency shift tables (shift center frequency to DC)
        for (int i = 0; i < _inputSize; i++)
        {
            float phase = -TwoPi * _centerFrequency * i / sampleRate;
            _cosTable[i] = MathF.Cos(phase);
            _sinTable[i] = MathF.Sin(phase);
        }

        // Build window function
        WindowFunctions.Fill(_window, window);

        // Build time-weighted and derivative windows for reassignment
        float center = 0.5f * (_inputSize - 1);
        for (int i = 0; i < _inputSize; i++)
        {
            float t = i - center;
            _windowTime[i] = _window[i] * t;
        }

        for (int i = 0; i < _inputSize; i++)
        {
            float prev = i > 0 ? _window[i - 1] : _window[i];
            float next = i < _inputSize - 1 ? _window[i + 1] : _window[i];
            _windowDerivative[i] = 0.5f * (next - prev);
        }

        // Build lowpass filter (simple windowed-sinc)
        // Cutoff at the bandwidth to prevent aliasing after decimation
        float cutoffNormalized = _bandwidth / sampleRate;
        _filterLength = Math.Min(65, _inputSize / 4) | 1; // Odd length, max 65 taps
        if (_lowpassKernel.Length != _filterLength)
        {
            _lowpassKernel = new float[_filterLength];
            _filterStateReal = new float[_filterLength];
            _filterStateImag = new float[_filterLength];
            _timeFilterStateReal = new float[_filterLength];
            _timeFilterStateImag = new float[_filterLength];
            _derivFilterStateReal = new float[_filterLength];
            _derivFilterStateImag = new float[_filterLength];
        }
        BuildSincFilter(_lowpassKernel, cutoffNormalized);

        // Normalization factor
        float windowSum = 0f;
        for (int i = 0; i < _inputSize; i++)
        {
            windowSum += _window[i];
        }
        _normalization = windowSum > 1e-6f ? 2f / windowSum : 1f;

        _configured = true;
    }

    /// <summary>
    /// Compute zoom FFT magnitudes for the configured frequency range.
    /// </summary>
    /// <param name="input">Input samples (must be at least RequiredInputSize samples).</param>
    /// <param name="magnitudesOut">Output magnitude array (must be at least OutputBins).</param>
    /// <remarks>
    /// If input is too short, output is zeroed and method returns early.
    /// This allows graceful handling during configuration changes.
    /// </remarks>
    public void Forward(ReadOnlySpan<float> input, Span<float> magnitudesOut)
    {
        if (!_configured || _fftSize == 0)
        {
            magnitudesOut.Clear();
            return;
        }

        int outputBins = OutputBins;

        // Guard against insufficient input - return zeros instead of crashing
        if (input.Length < _inputSize)
        {
            magnitudesOut.Slice(0, Math.Min(magnitudesOut.Length, outputBins)).Clear();
            return;
        }

        // Guard against insufficient output
        if (magnitudesOut.Length < outputBins)
        {
            magnitudesOut.Clear();
            return;
        }

        // Step 1: Apply window and frequency shift (complex multiplication)
        for (int i = 0; i < _inputSize; i++)
        {
            float windowed = input[i] * _window[i];
            _shiftedReal[i] = windowed * _cosTable[i];
            _shiftedImag[i] = windowed * _sinTable[i];
        }

        // Step 2: Apply lowpass filter (anti-aliasing before decimation)
        ApplyFirFilter(_shiftedReal, _filterStateReal);
        ApplyFirFilter(_shiftedImag, _filterStateImag);

        // Step 3: Decimate from inputSize to fftSize
        int offset = (_filterLength - 1) / 2; // Compensate for filter delay
        for (int i = 0; i < _fftSize; i++)
        {
            int srcIndex = Math.Min(i * _decimationFactor + offset, _inputSize - 1);
            _decimatedReal[i] = _shiftedReal[srcIndex];
            _decimatedImag[i] = _shiftedImag[srcIndex];
        }

        // Step 4: FFT at decimated rate (fftSize points)
        _fft?.Forward(_decimatedReal, _decimatedImag);

        // Step 5: Extract magnitudes (rearrange so output[0] = minHz)
        // After shifting, the spectrum is centered at DC.
        // Negative frequencies are in upper half, positive in lower half.
        int half = _fftSize / 2;
        float norm = _normalization / _decimationFactor;

        for (int i = 0; i < half; i++)
        {
            float re = _decimatedReal[i];
            float im = _decimatedImag[i];
            magnitudesOut[i] = MathF.Sqrt(re * re + im * im) * norm;
        }
    }

    /// <summary>
    /// Compute zoom FFT with reassignment data for the configured frequency range.
    /// </summary>
    /// <param name="input">Input samples (must be at least RequiredInputSize samples).</param>
    /// <param name="magnitudesOut">Output magnitude array.</param>
    /// <param name="realOut">Output real component array (for reassignment).</param>
    /// <param name="imagOut">Output imaginary component array (for reassignment).</param>
    /// <param name="timeRealOut">Output real component of time-weighted transform.</param>
    /// <param name="timeImagOut">Output imaginary component of time-weighted transform.</param>
    /// <param name="derivRealOut">Output real component of derivative-weighted transform.</param>
    /// <param name="derivImagOut">Output imaginary component of derivative-weighted transform.</param>
    public void ForwardWithReassignment(
        ReadOnlySpan<float> input,
        Span<float> magnitudesOut,
        Span<float> realOut,
        Span<float> imagOut,
        Span<float> timeRealOut,
        Span<float> timeImagOut,
        Span<float> derivRealOut,
        Span<float> derivImagOut)
    {
        if (!_configured || _fftSize == 0)
        {
            magnitudesOut.Clear();
            realOut.Clear();
            imagOut.Clear();
            timeRealOut.Clear();
            timeImagOut.Clear();
            derivRealOut.Clear();
            derivImagOut.Clear();
            return;
        }

        int outputBins = OutputBins;

        // Guard against insufficient input
        if (input.Length < _inputSize)
        {
            magnitudesOut.Slice(0, Math.Min(magnitudesOut.Length, outputBins)).Clear();
            realOut.Slice(0, Math.Min(realOut.Length, outputBins)).Clear();
            imagOut.Slice(0, Math.Min(imagOut.Length, outputBins)).Clear();
            timeRealOut.Slice(0, Math.Min(timeRealOut.Length, outputBins)).Clear();
            timeImagOut.Slice(0, Math.Min(timeImagOut.Length, outputBins)).Clear();
            derivRealOut.Slice(0, Math.Min(derivRealOut.Length, outputBins)).Clear();
            derivImagOut.Slice(0, Math.Min(derivImagOut.Length, outputBins)).Clear();
            return;
        }

        // Guard against insufficient output
        if (magnitudesOut.Length < outputBins || realOut.Length < outputBins ||
            imagOut.Length < outputBins || timeRealOut.Length < outputBins ||
            timeImagOut.Length < outputBins || derivRealOut.Length < outputBins ||
            derivImagOut.Length < outputBins)
        {
            magnitudesOut.Clear();
            realOut.Clear();
            imagOut.Clear();
            timeRealOut.Clear();
            timeImagOut.Clear();
            derivRealOut.Clear();
            derivImagOut.Clear();
            return;
        }

        // Step 1: Apply windows and frequency shift for all three transforms
        for (int i = 0; i < _inputSize; i++)
        {
            float sample = input[i];
            float cos = _cosTable[i];
            float sin = _sinTable[i];

            // Normal windowed
            float windowed = sample * _window[i];
            _shiftedReal[i] = windowed * cos;
            _shiftedImag[i] = windowed * sin;

            // Time-weighted windowed
            float timeWindowed = sample * _windowTime[i];
            _timeWeightedReal[i] = timeWindowed * cos;
            _timeWeightedImag[i] = timeWindowed * sin;

            // Derivative windowed
            float derivWindowed = sample * _windowDerivative[i];
            _derivWeightedReal[i] = derivWindowed * cos;
            _derivWeightedImag[i] = derivWindowed * sin;
        }

        // Step 2: Apply lowpass filters
        ApplyFirFilter(_shiftedReal, _filterStateReal);
        ApplyFirFilter(_shiftedImag, _filterStateImag);
        ApplyFirFilter(_timeWeightedReal, _timeFilterStateReal);
        ApplyFirFilter(_timeWeightedImag, _timeFilterStateImag);
        ApplyFirFilter(_derivWeightedReal, _derivFilterStateReal);
        ApplyFirFilter(_derivWeightedImag, _derivFilterStateImag);

        // Step 3: Decimate all transforms from inputSize to fftSize
        int offset = (_filterLength - 1) / 2;
        for (int i = 0; i < _fftSize; i++)
        {
            int srcIndex = Math.Min(i * _decimationFactor + offset, _inputSize - 1);

            _decimatedReal[i] = _shiftedReal[srcIndex];
            _decimatedImag[i] = _shiftedImag[srcIndex];

            _timeDecimatedReal[i] = _timeWeightedReal[srcIndex];
            _timeDecimatedImag[i] = _timeWeightedImag[srcIndex];

            _derivDecimatedReal[i] = _derivWeightedReal[srcIndex];
            _derivDecimatedImag[i] = _derivWeightedImag[srcIndex];
        }

        // Step 4: FFT all transforms (fftSize points)
        _fft?.Forward(_decimatedReal, _decimatedImag);
        _fft?.Forward(_timeDecimatedReal, _timeDecimatedImag);
        _fft?.Forward(_derivDecimatedReal, _derivDecimatedImag);

        // Step 5: Extract output (bin 0 corresponds to minHz)
        int half = _fftSize / 2;
        float norm = _normalization / _decimationFactor;

        for (int i = 0; i < half; i++)
        {
            float re = _decimatedReal[i];
            float im = _decimatedImag[i];
            magnitudesOut[i] = MathF.Sqrt(re * re + im * im) * norm;
            realOut[i] = re;
            imagOut[i] = im;

            timeRealOut[i] = _timeDecimatedReal[i];
            timeImagOut[i] = _timeDecimatedImag[i];

            derivRealOut[i] = _derivDecimatedReal[i];
            derivImagOut[i] = _derivDecimatedImag[i];
        }
    }

    /// <summary>
    /// Get the frequency in Hz for a given output bin.
    /// </summary>
    public float GetBinFrequency(int bin)
    {
        if (!_configured || bin < 0 || bin >= OutputBins)
        {
            return 0f;
        }

        // After frequency shift by minHz, bin 0 corresponds to minHz
        // Bin i corresponds to minHz + i * binResolution
        // BinResolutionHz = sampleRate / inputSize (true zoom resolution)
        return MinFrequency + bin * BinResolutionHz;
    }

    /// <summary>
    /// Reset internal filter state.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_filterStateReal);
        Array.Clear(_filterStateImag);
        Array.Clear(_timeFilterStateReal);
        Array.Clear(_timeFilterStateImag);
        Array.Clear(_derivFilterStateReal);
        Array.Clear(_derivFilterStateImag);
    }

    private void ApplyFirFilter(float[] data, float[] state)
    {
        int filterLen = _filterLength;
        int dataLen = data.Length;

        // Simple convolution with FIR kernel
        // This is O(N*M) but filterLen is small (<=65)
        for (int i = dataLen - 1; i >= 0; i--)
        {
            float sum = 0f;
            for (int j = 0; j < filterLen; j++)
            {
                int idx = i - j;
                float sample = idx >= 0 ? data[idx] : state[filterLen - 1 + idx];
                sum += sample * _lowpassKernel[j];
            }
            data[i] = sum;
        }

        // Update state for next call (not needed for single-shot, but keeps API consistent)
        int stateLen = Math.Min(filterLen - 1, dataLen);
        for (int i = 0; i < stateLen; i++)
        {
            state[i] = data[dataLen - stateLen + i];
        }
    }

    private static void BuildSincFilter(float[] kernel, float cutoff)
    {
        int len = kernel.Length;
        int center = len / 2;
        float sum = 0f;

        for (int i = 0; i < len; i++)
        {
            float x = i - center;
            float sinc;
            if (MathF.Abs(x) < 1e-6f)
            {
                sinc = 1f;
            }
            else
            {
                float arg = MathF.PI * 2f * cutoff * x;
                sinc = MathF.Sin(arg) / (MathF.PI * x);
            }

            // Apply Blackman window to sinc
            float window = 0.42f - 0.5f * MathF.Cos(TwoPi * i / (len - 1))
                                 + 0.08f * MathF.Cos(2f * TwoPi * i / (len - 1));
            kernel[i] = sinc * window;
            sum += kernel[i];
        }

        // Normalize
        if (sum > 1e-6f)
        {
            float inv = 1f / sum;
            for (int i = 0; i < len; i++)
            {
                kernel[i] *= inv;
            }
        }
    }
}
