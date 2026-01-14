namespace HotMic.Core.Dsp.Analysis;

/// <summary>
/// Display mode for the spectral delta strip.
/// </summary>
public enum DeltaDisplayMode
{
    /// <summary>
    /// Full spectrum with linear frequency distribution (0 to Nyquist).
    /// Best for noise removal plugins that affect the full spectrum.
    /// </summary>
    FullSpectrum,

    /// <summary>
    /// Vocal range with logarithmic frequency distribution (80Hz-8kHz).
    /// Best for voice processing plugins (compression, EQ, de-essing).
    /// </summary>
    VocalRange
}

/// <summary>
/// Computes spectral delta (frequency-domain difference) before and after plugin processing.
/// Zero allocations in the audio thread - all buffers pre-allocated at construction.
/// </summary>
public sealed class SpectralDeltaProcessor
{
    private const int FftSize = 256;
    private const int NumBands = 32;
    private const int UpdateIntervalSamples = 3200; // ~15fps at 48kHz
    private const float NoiseFloorDb = -60f;
    private const float AttackCoeff = 0.3f;
    private const float ReleaseCoeff = 0.05f;

    private readonly FastFft _fft;
    private readonly int _sampleRate;

    // FFT buffers (pre-allocated)
    private readonly float[] _preReal;
    private readonly float[] _preImag;
    private readonly float[] _postReal;
    private readonly float[] _postImag;
    private readonly float[] _window;

    // Sample counter for update timing
    private int _sampleAccumulator;

    // Band magnitude storage
    private readonly float[] _preMagnitudes;
    private readonly float[] _postMagnitudes;
    private readonly float[] _smoothedDeltas;
    private readonly float[] _bandDeltas;

    // Band mapping (which FFT bins map to which display band)
    private readonly int[] _bandStartBin;
    private readonly int[] _bandEndBin;

    private DeltaDisplayMode _displayMode;
    private volatile bool _hasUpdate;
    private bool _captureThisBlock;

    /// <summary>
    /// The computed band deltas in dB. Positive = boost, negative = cut.
    /// Safe to read from UI thread.
    /// </summary>
    public float[] BandDeltas => _bandDeltas;

    /// <summary>
    /// Whether new delta data is available since last read.
    /// </summary>
    public bool HasUpdate
    {
        get => _hasUpdate;
        set => _hasUpdate = value;
    }

    /// <summary>
    /// Current display mode (affects frequency band mapping).
    /// </summary>
    public DeltaDisplayMode DisplayMode
    {
        get => _displayMode;
        set
        {
            if (_displayMode != value)
            {
                _displayMode = value;
                BuildBandMapping();
            }
        }
    }

    public SpectralDeltaProcessor(int sampleRate, DeltaDisplayMode initialMode = DeltaDisplayMode.VocalRange)
    {
        _sampleRate = sampleRate;
        _displayMode = initialMode;

        _fft = new FastFft(FftSize);

        // Pre-allocate all buffers
        _preReal = new float[FftSize];
        _preImag = new float[FftSize];
        _postReal = new float[FftSize];
        _postImag = new float[FftSize];
        _window = new float[FftSize];

        _preMagnitudes = new float[NumBands];
        _postMagnitudes = new float[NumBands];
        _smoothedDeltas = new float[NumBands];
        _bandDeltas = new float[NumBands];

        _bandStartBin = new int[NumBands];
        _bandEndBin = new int[NumBands];

        // Build Hanning window
        for (int i = 0; i < FftSize; i++)
        {
            _window[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (FftSize - 1)));
        }

        BuildBandMapping();
    }

    /// <summary>
    /// Capture pre-plugin spectrum from the current buffer.
    /// Call this BEFORE plugin.Process().
    /// </summary>
    public void ProcessPre(ReadOnlySpan<float> buffer)
    {
        _sampleAccumulator += buffer.Length;

        // Check if it's time to capture a new snapshot
        if (_sampleAccumulator >= UpdateIntervalSamples)
        {
            // Capture pre-plugin FFT from current buffer
            CaptureSpectrum(buffer, _preReal, _preImag, _preMagnitudes);
            _captureThisBlock = true;
        }
    }

    /// <summary>
    /// Capture post-plugin spectrum and compute delta.
    /// Call this AFTER plugin.Process().
    /// </summary>
    public void ProcessPost(ReadOnlySpan<float> buffer)
    {
        if (_captureThisBlock)
        {
            // Capture post-plugin FFT from the same buffer (now modified by plugin)
            CaptureSpectrum(buffer, _postReal, _postImag, _postMagnitudes);
            ComputeDelta();

            _captureThisBlock = false;
            _sampleAccumulator = 0;
            _hasUpdate = true;
        }
    }

    /// <summary>
    /// Reset all state (call when plugin changes or is removed).
    /// </summary>
    public void Reset()
    {
        _sampleAccumulator = 0;
        _captureThisBlock = false;
        _hasUpdate = false;

        Array.Clear(_preMagnitudes);
        Array.Clear(_postMagnitudes);
        Array.Clear(_smoothedDeltas);
        Array.Clear(_bandDeltas);
    }

    private void CaptureSpectrum(ReadOnlySpan<float> buffer, float[] real, float[] imag, float[] magnitudes)
    {
        // Copy buffer to FFT arrays with windowing
        int copyLen = Math.Min(buffer.Length, FftSize);
        for (int i = 0; i < copyLen; i++)
        {
            real[i] = buffer[i] * _window[i];
            imag[i] = 0f;
        }
        // Zero-pad if buffer is smaller than FFT size
        for (int i = copyLen; i < FftSize; i++)
        {
            real[i] = 0f;
            imag[i] = 0f;
        }

        _fft.Forward(real, imag);
        ComputeBandMagnitudes(real, imag, magnitudes);
    }

    private void ComputeDelta()
    {
        for (int i = 0; i < NumBands; i++)
        {
            float preDb = MagnitudeToDb(_preMagnitudes[i]);
            float postDb = MagnitudeToDb(_postMagnitudes[i]);
            float delta = postDb - preDb;

            // Clamp to reasonable range
            delta = Math.Clamp(delta, -24f, 24f);

            // Apply smoothing (fast attack, slow release)
            float coeff = MathF.Abs(delta) > MathF.Abs(_smoothedDeltas[i]) ? AttackCoeff : ReleaseCoeff;
            _smoothedDeltas[i] += coeff * (delta - _smoothedDeltas[i]);

            // Copy to output
            _bandDeltas[i] = _smoothedDeltas[i];
        }
    }

    private void ComputeBandMagnitudes(float[] real, float[] imag, float[] magnitudes)
    {
        for (int band = 0; band < NumBands; band++)
        {
            int startBin = _bandStartBin[band];
            int endBin = _bandEndBin[band];

            float sumMag = 0f;
            int count = 0;

            for (int bin = startBin; bin <= endBin && bin < FftSize / 2; bin++)
            {
                float mag = MathF.Sqrt(real[bin] * real[bin] + imag[bin] * imag[bin]);
                sumMag += mag;
                count++;
            }

            magnitudes[band] = count > 0 ? sumMag / count : 0f;
        }
    }

    private static float MagnitudeToDb(float magnitude)
    {
        if (magnitude <= 1e-10f) return NoiseFloorDb;
        return 20f * MathF.Log10(magnitude);
    }

    private void BuildBandMapping()
    {
        int nyquistBin = FftSize / 2;
        float nyquistHz = _sampleRate / 2f;

        if (_displayMode == DeltaDisplayMode.FullSpectrum)
        {
            // Linear distribution across full spectrum
            float binsPerBand = (float)nyquistBin / NumBands;
            for (int band = 0; band < NumBands; band++)
            {
                _bandStartBin[band] = (int)(band * binsPerBand);
                _bandEndBin[band] = (int)((band + 1) * binsPerBand) - 1;
                if (_bandEndBin[band] < _bandStartBin[band])
                    _bandEndBin[band] = _bandStartBin[band];
            }
        }
        else // VocalRange - logarithmic 80Hz to 8kHz
        {
            float minFreq = 80f;
            float maxFreq = Math.Min(8000f, nyquistHz);
            float logMin = MathF.Log10(minFreq);
            float logMax = MathF.Log10(maxFreq);
            float logRange = logMax - logMin;

            for (int band = 0; band < NumBands; band++)
            {
                float logStart = logMin + (band * logRange / NumBands);
                float logEnd = logMin + ((band + 1) * logRange / NumBands);

                float freqStart = MathF.Pow(10f, logStart);
                float freqEnd = MathF.Pow(10f, logEnd);

                _bandStartBin[band] = (int)(freqStart / nyquistHz * nyquistBin);
                _bandEndBin[band] = (int)(freqEnd / nyquistHz * nyquistBin);

                // Ensure at least one bin per band
                if (_bandEndBin[band] < _bandStartBin[band])
                    _bandEndBin[band] = _bandStartBin[band];

                // Clamp to valid range
                _bandStartBin[band] = Math.Clamp(_bandStartBin[band], 0, nyquistBin - 1);
                _bandEndBin[band] = Math.Clamp(_bandEndBin[band], 0, nyquistBin - 1);
            }
        }
    }
}
