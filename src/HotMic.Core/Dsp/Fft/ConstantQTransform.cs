namespace HotMic.Core.Dsp.Fft;

/// <summary>
/// Constant-Q Transform with configurable bins per octave.
/// Uses direct convolution with precomputed sparse kernels.
/// Zero allocations after Configure().
/// </summary>
public sealed class ConstantQTransform
{
    private const float TwoPi = MathF.PI * 2f;

    /// <summary>
    /// Maximum allowed window size in samples to prevent excessive latency.
    /// At 48kHz this is ~0.68 seconds, at 192kHz this is ~0.17 seconds.
    /// </summary>
    private const int MaxAllowedWindowSamples = 32768;

    // Kernel storage (all kernels concatenated)
    private float[] _kernelReal = Array.Empty<float>();
    private float[] _kernelImag = Array.Empty<float>();
    private float[] _kernelTimeReal = Array.Empty<float>();
    private float[] _kernelTimeImag = Array.Empty<float>();
    private int[] _kernelOffsets = Array.Empty<int>();
    private int[] _kernelLengths = Array.Empty<int>();
    private float[] _centerFrequencies = Array.Empty<float>();

    // Output buffers
    private float[] _outputReal = Array.Empty<float>();
    private float[] _outputImag = Array.Empty<float>();

    // Previous frame phase storage for frequency reassignment
    private float[] _prevPhase = Array.Empty<float>();
    private bool _hasPrevPhase;

    private int _binsPerOctave;
    private int _totalBins;
    private float _minFreq;
    private float _maxFreq;
    private float _sampleRate;
    private int _maxWindowLength;
    private bool _configured;

    /// <summary>
    /// Total number of CQT output bins.
    /// </summary>
    public int BinCount => _totalBins;

    /// <summary>
    /// Configured bins per octave.
    /// </summary>
    public int BinsPerOctave => _binsPerOctave;

    /// <summary>
    /// Minimum analyzed frequency in Hz.
    /// </summary>
    public float MinFrequency => _minFreq;

    /// <summary>
    /// Maximum analyzed frequency in Hz.
    /// </summary>
    public float MaxFrequency => _maxFreq;

    /// <summary>
    /// Maximum window length needed for lowest frequency bin.
    /// </summary>
    public int MaxWindowLength => _maxWindowLength;

    /// <summary>
    /// Center frequencies for each CQT bin.
    /// </summary>
    public ReadOnlySpan<float> CenterFrequencies => _centerFrequencies.AsSpan(0, _totalBins);

    /// <summary>
    /// Configure CQT parameters.
    /// </summary>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <param name="minHz">Minimum frequency of interest.</param>
    /// <param name="maxHz">Maximum frequency of interest.</param>
    /// <param name="binsPerOctave">Number of frequency bins per octave (12, 24, 48, 96).</param>
    /// <remarks>
    /// Minimum frequency is automatically raised if the required window size would
    /// exceed MaxAllowedWindowSamples (to prevent excessive latency and memory usage).
    /// </remarks>
    public void Configure(int sampleRate, float minHz, float maxHz, int binsPerOctave)
    {
        if (binsPerOctave < 1)
        {
            throw new ArgumentException("Bins per octave must be at least 1.", nameof(binsPerOctave));
        }

        float nyquist = sampleRate * 0.5f;

        // Q factor: constant ratio of center frequency to bandwidth
        // Q = f / Δf = 1 / (2^(1/B) - 1) where B = binsPerOctave
        float Q = 1f / (MathF.Pow(2f, 1f / binsPerOctave) - 1f);

        // Clamp min frequency to keep window sizes reasonable
        // Max window = Q * sampleRate / minFreq, so minFreq >= Q * sampleRate / maxWindow
        float minSafeHz = Q * sampleRate / MaxAllowedWindowSamples;
        minHz = Math.Clamp(minHz, Math.Max(20f, minSafeHz), nyquist - 1f);
        maxHz = Math.Clamp(maxHz, minHz + 1f, nyquist);

        _sampleRate = sampleRate;
        _minFreq = minHz;
        _maxFreq = maxHz;
        _binsPerOctave = binsPerOctave;

        // Calculate number of bins needed
        float numOctaves = MathF.Log2(maxHz / minHz);
        _totalBins = (int)MathF.Ceiling(numOctaves * binsPerOctave);
        _totalBins = Math.Max(1, _totalBins);

        // Calculate total kernel storage needed
        int totalKernelLength = 0;
        _maxWindowLength = 0;

        for (int k = 0; k < _totalBins; k++)
        {
            float freq = minHz * MathF.Pow(2f, (float)k / binsPerOctave);
            int windowLength = (int)MathF.Ceiling(Q * sampleRate / freq);
            windowLength = Math.Max(4, windowLength);
            totalKernelLength += windowLength;
            _maxWindowLength = Math.Max(_maxWindowLength, windowLength);
        }

        // Allocate storage
        if (_kernelReal.Length < totalKernelLength)
        {
            _kernelReal = new float[totalKernelLength];
            _kernelImag = new float[totalKernelLength];
            _kernelTimeReal = new float[totalKernelLength];
            _kernelTimeImag = new float[totalKernelLength];
        }

        if (_kernelOffsets.Length < _totalBins)
        {
            _kernelOffsets = new int[_totalBins];
            _kernelLengths = new int[_totalBins];
            _centerFrequencies = new float[_totalBins];
            _outputReal = new float[_totalBins];
            _outputImag = new float[_totalBins];
            _prevPhase = new float[_totalBins];
        }

        // Build kernels for each bin
        int offset = 0;
        for (int k = 0; k < _totalBins; k++)
        {
            float freq = minHz * MathF.Pow(2f, (float)k / binsPerOctave);
            _centerFrequencies[k] = freq;

            int windowLength = (int)MathF.Ceiling(Q * sampleRate / freq);
            windowLength = Math.Max(4, windowLength);

            _kernelOffsets[k] = offset;
            _kernelLengths[k] = windowLength;

            // Build windowed complex exponential kernel
            // kernel[n] = window[n] * exp(-2πi * Q * n / N) / N
            float invN = 1f / windowLength;
            float center = 0.5f * (windowLength - 1);
            for (int n = 0; n < windowLength; n++)
            {
                // Hann window
                float window = 0.5f - 0.5f * MathF.Cos(TwoPi * n / (windowLength - 1));

                // Time offset from center (for time-weighted kernel)
                float t = n - center;

                // Complex exponential at the CQT frequency
                float phase = -TwoPi * Q * n / windowLength;
                float cos = MathF.Cos(phase);
                float sin = MathF.Sin(phase);

                _kernelReal[offset + n] = window * cos * invN;
                _kernelImag[offset + n] = window * sin * invN;

                // Time-weighted kernel: τ * g_k(τ) for time reassignment
                _kernelTimeReal[offset + n] = window * t * cos * invN;
                _kernelTimeImag[offset + n] = window * t * sin * invN;
            }

            offset += windowLength;
        }

        _configured = true;
    }

    /// <summary>
    /// Compute CQT magnitudes.
    /// </summary>
    /// <param name="input">Input samples (ideally at least MaxWindowLength samples).</param>
    /// <param name="magnitudesOut">Output magnitude array (must be at least BinCount).</param>
    /// <remarks>
    /// If input is too short, output is zeroed and method returns early.
    /// This allows graceful handling during configuration changes.
    /// </remarks>
    public void Forward(ReadOnlySpan<float> input, Span<float> magnitudesOut)
    {
        if (!_configured || _totalBins == 0)
        {
            magnitudesOut.Clear();
            return;
        }

        // Guard against insufficient input - return zeros instead of crashing
        if (input.Length < _maxWindowLength)
        {
            magnitudesOut.Slice(0, Math.Min(magnitudesOut.Length, _totalBins)).Clear();
            return;
        }

        // Guard against insufficient output
        if (magnitudesOut.Length < _totalBins)
        {
            magnitudesOut.Clear();
            return;
        }

        // Compute CQT for each bin via direct convolution
        int inputLen = input.Length;

        for (int k = 0; k < _totalBins; k++)
        {
            int kernelLen = _kernelLengths[k];
            int kernelOffset = _kernelOffsets[k];

            // Use the most recent kernelLen samples from input
            int startIdx = inputLen - kernelLen;

            float sumReal = 0f;
            float sumImag = 0f;

            for (int n = 0; n < kernelLen; n++)
            {
                float sample = input[startIdx + n];
                sumReal += sample * _kernelReal[kernelOffset + n];
                sumImag += sample * _kernelImag[kernelOffset + n];
            }

            magnitudesOut[k] = MathF.Sqrt(sumReal * sumReal + sumImag * sumImag);
        }
    }

    /// <summary>
    /// Compute CQT with complex output (for phase information).
    /// </summary>
    /// <param name="input">Input samples.</param>
    /// <param name="realOut">Real component output.</param>
    /// <param name="imagOut">Imaginary component output.</param>
    /// <remarks>
    /// If input is too short, outputs are zeroed and method returns early.
    /// </remarks>
    public void ForwardComplex(ReadOnlySpan<float> input, Span<float> realOut, Span<float> imagOut)
    {
        if (!_configured || _totalBins == 0)
        {
            realOut.Clear();
            imagOut.Clear();
            return;
        }

        // Guard against insufficient input
        if (input.Length < _maxWindowLength)
        {
            realOut.Slice(0, Math.Min(realOut.Length, _totalBins)).Clear();
            imagOut.Slice(0, Math.Min(imagOut.Length, _totalBins)).Clear();
            return;
        }

        // Guard against insufficient output
        if (realOut.Length < _totalBins || imagOut.Length < _totalBins)
        {
            realOut.Clear();
            imagOut.Clear();
            return;
        }

        int inputLen = input.Length;

        for (int k = 0; k < _totalBins; k++)
        {
            int kernelLen = _kernelLengths[k];
            int kernelOffset = _kernelOffsets[k];
            int startIdx = inputLen - kernelLen;

            float sumReal = 0f;
            float sumImag = 0f;

            for (int n = 0; n < kernelLen; n++)
            {
                float sample = input[startIdx + n];
                sumReal += sample * _kernelReal[kernelOffset + n];
                sumImag += sample * _kernelImag[kernelOffset + n];
            }

            realOut[k] = sumReal;
            imagOut[k] = sumImag;
        }
    }

    /// <summary>
    /// Compute CQT with reassignment data.
    /// </summary>
    /// <param name="input">Input samples.</param>
    /// <param name="magnitudesOut">Output magnitude array.</param>
    /// <param name="realOut">Output real component array.</param>
    /// <param name="imagOut">Output imaginary component array.</param>
    /// <param name="timeRealOut">Output real component of time-weighted transform.</param>
    /// <param name="timeImagOut">Output imaginary component of time-weighted transform.</param>
    /// <param name="phaseDiffOut">Output phase difference from previous frame (for frequency reassignment).</param>
    /// <remarks>
    /// For CQT frequency reassignment, we compute phase difference between frames since
    /// frequency bins are log-spaced. The reassigned frequency in log space is:
    /// f_reassigned = f_k * exp(phaseDiff / (2π))
    /// </remarks>
    public void ForwardWithReassignment(
        ReadOnlySpan<float> input,
        Span<float> magnitudesOut,
        Span<float> realOut,
        Span<float> imagOut,
        Span<float> timeRealOut,
        Span<float> timeImagOut,
        Span<float> phaseDiffOut)
    {
        ForwardWithReassignment(input, magnitudesOut, realOut, imagOut, timeRealOut, timeImagOut, phaseDiffOut,
            computeTime: true, computePhase: true);
    }

    /// <summary>
    /// Compute CQT with reassignment data, optionally skipping time or phase outputs.
    /// </summary>
    /// <param name="input">Input samples.</param>
    /// <param name="magnitudesOut">Output magnitude array.</param>
    /// <param name="realOut">Output real component array.</param>
    /// <param name="imagOut">Output imaginary component array.</param>
    /// <param name="timeRealOut">Output real component of time-weighted transform.</param>
    /// <param name="timeImagOut">Output imaginary component of time-weighted transform.</param>
    /// <param name="phaseDiffOut">Output phase difference from previous frame.</param>
    /// <param name="computeTime">Whether to compute time-weighted outputs.</param>
    /// <param name="computePhase">Whether to compute phase differences.</param>
    public void ForwardWithReassignment(
        ReadOnlySpan<float> input,
        Span<float> magnitudesOut,
        Span<float> realOut,
        Span<float> imagOut,
        Span<float> timeRealOut,
        Span<float> timeImagOut,
        Span<float> phaseDiffOut,
        bool computeTime,
        bool computePhase)
    {
        if (!_configured || _totalBins == 0)
        {
            magnitudesOut.Clear();
            realOut.Clear();
            imagOut.Clear();
            timeRealOut.Clear();
            timeImagOut.Clear();
            phaseDiffOut.Clear();
            _hasPrevPhase = false;
            return;
        }

        // Guard against insufficient input
        if (input.Length < _maxWindowLength)
        {
            magnitudesOut.Slice(0, Math.Min(magnitudesOut.Length, _totalBins)).Clear();
            realOut.Slice(0, Math.Min(realOut.Length, _totalBins)).Clear();
            imagOut.Slice(0, Math.Min(imagOut.Length, _totalBins)).Clear();
            timeRealOut.Slice(0, Math.Min(timeRealOut.Length, _totalBins)).Clear();
            timeImagOut.Slice(0, Math.Min(timeImagOut.Length, _totalBins)).Clear();
            phaseDiffOut.Slice(0, Math.Min(phaseDiffOut.Length, _totalBins)).Clear();
            _hasPrevPhase = false;
            return;
        }

        // Guard against insufficient output
        if (magnitudesOut.Length < _totalBins || realOut.Length < _totalBins ||
            imagOut.Length < _totalBins || timeRealOut.Length < _totalBins ||
            timeImagOut.Length < _totalBins || phaseDiffOut.Length < _totalBins)
        {
            magnitudesOut.Clear();
            realOut.Clear();
            imagOut.Clear();
            timeRealOut.Clear();
            timeImagOut.Clear();
            phaseDiffOut.Clear();
            _hasPrevPhase = false;
            return;
        }

        int inputLen = input.Length;

        for (int k = 0; k < _totalBins; k++)
        {
            int kernelLen = _kernelLengths[k];
            int kernelOffset = _kernelOffsets[k];
            int startIdx = inputLen - kernelLen;

            float sumReal = 0f;
            float sumImag = 0f;
            float sumTimeReal = 0f;
            float sumTimeImag = 0f;

            for (int n = 0; n < kernelLen; n++)
            {
                float sample = input[startIdx + n];
                sumReal += sample * _kernelReal[kernelOffset + n];
                sumImag += sample * _kernelImag[kernelOffset + n];
                if (computeTime)
                {
                    sumTimeReal += sample * _kernelTimeReal[kernelOffset + n];
                    sumTimeImag += sample * _kernelTimeImag[kernelOffset + n];
                }
            }

            realOut[k] = sumReal;
            imagOut[k] = sumImag;
            magnitudesOut[k] = MathF.Sqrt(sumReal * sumReal + sumImag * sumImag);

            if (computeTime)
            {
                timeRealOut[k] = sumTimeReal;
                timeImagOut[k] = sumTimeImag;
            }
            else
            {
                timeRealOut[k] = 0f;
                timeImagOut[k] = 0f;
            }

            if (computePhase)
            {
                // Compute phase and phase difference for frequency reassignment
                float currentPhase = MathF.Atan2(sumImag, sumReal);
                if (_hasPrevPhase)
                {
                    // Unwrap phase difference to [-π, π]
                    float diff = currentPhase - _prevPhase[k];
                    while (diff > MathF.PI) diff -= TwoPi;
                    while (diff < -MathF.PI) diff += TwoPi;
                    phaseDiffOut[k] = diff;
                }
                else
                {
                    phaseDiffOut[k] = 0f;
                }
                _prevPhase[k] = currentPhase;
            }
            else
            {
                phaseDiffOut[k] = 0f;
            }
        }

        _hasPrevPhase = computePhase;
    }

    /// <summary>
    /// Get the frequency in Hz for a given CQT bin.
    /// </summary>
    public float GetBinFrequency(int bin)
    {
        if (!_configured || bin < 0 || bin >= _totalBins)
        {
            return 0f;
        }
        return _centerFrequencies[bin];
    }

    /// <summary>
    /// Reset internal state (phase history for reassignment).
    /// </summary>
    public void Reset()
    {
        _hasPrevPhase = false;
        if (_prevPhase.Length > 0)
        {
            Array.Clear(_prevPhase);
        }
    }
}
