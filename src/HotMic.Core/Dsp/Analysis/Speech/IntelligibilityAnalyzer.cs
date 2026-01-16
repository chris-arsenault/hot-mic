namespace HotMic.Core.Dsp.Analysis.Speech;

/// <summary>
/// Computes simplified intelligibility metrics based on modulation depth.
/// Inspired by STI (Speech Transmission Index) but simplified for real-time use.
/// Key insight: speech intelligibility depends on preserving amplitude modulations in the 2-8 Hz range.
/// </summary>
public sealed class IntelligibilityAnalyzer
{
    private const int EnvelopeBufferSize = 256; // ~5 seconds at 50Hz envelope rate
    private const float EnvelopeSampleRate = 50f; // Hz (one sample per ~20ms)

    private readonly float[] _energyEnvelope = new float[EnvelopeBufferSize];
    private int _envelopeIndex;
    private int _envelopeCount;

    // For downsampling to envelope rate
    private float _energyAccumulator;
    private int _sampleCount;
    private int _targetSamplesPerEnvelope;

    // FFT for modulation spectrum (simple DFT for small buffer)
    private readonly float[] _modulationSpectrum = new float[EnvelopeBufferSize / 2];

    /// <summary>
    /// Configure the analyzer for the given analysis rate.
    /// </summary>
    /// <param name="hopSize">Hop size in samples.</param>
    /// <param name="sampleRate">Audio sample rate.</param>
    public void Configure(int hopSize, int sampleRate)
    {
        float frameRate = sampleRate / (float)hopSize;
        _targetSamplesPerEnvelope = Math.Max(1, (int)(frameRate / EnvelopeSampleRate));
        Reset();
    }

    /// <summary>
    /// Process a frame's energy and accumulate for modulation analysis.
    /// </summary>
    /// <param name="energyDb">Frame energy in dB.</param>
    public void Process(float energyDb)
    {
        // Convert dB to linear for envelope
        float linearEnergy = MathF.Pow(10f, energyDb / 20f);

        _energyAccumulator += linearEnergy;
        _sampleCount++;

        // Downsample to envelope rate
        if (_sampleCount >= _targetSamplesPerEnvelope)
        {
            float avgEnergy = _energyAccumulator / _sampleCount;
            _energyEnvelope[_envelopeIndex] = avgEnergy;
            _envelopeIndex = (_envelopeIndex + 1) % EnvelopeBufferSize;
            _envelopeCount = Math.Min(_envelopeCount + 1, EnvelopeBufferSize);

            _energyAccumulator = 0f;
            _sampleCount = 0;
        }
    }

    /// <summary>
    /// Compute intelligibility metrics.
    /// </summary>
    /// <param name="modulationIndex">Output: modulation depth index (0-1, higher = better).</param>
    /// <param name="speechBandEnergy">Output: energy in speech modulation band 2-8 Hz (0-1).</param>
    /// <param name="intelligibilityScore">Output: overall intelligibility estimate (0-100).</param>
    public void Compute(
        out float modulationIndex,
        out float speechBandEnergy,
        out float intelligibilityScore)
    {
        modulationIndex = 0f;
        speechBandEnergy = 0f;
        intelligibilityScore = 0f;

        if (_envelopeCount < 32)
        {
            return;
        }

        // Compute modulation spectrum via simple DFT
        ComputeModulationSpectrum();

        // Find energy in speech-relevant modulation band (2-8 Hz)
        // Bin resolution = EnvelopeSampleRate / EnvelopeBufferSize
        float binResHz = EnvelopeSampleRate / EnvelopeBufferSize;
        int minBin = (int)(2f / binResHz);
        int maxBin = (int)(8f / binResHz);
        minBin = Math.Max(1, minBin);
        maxBin = Math.Min(_modulationSpectrum.Length - 1, maxBin);

        float speechBandSum = 0f;
        float totalSum = 0f;

        for (int i = 1; i < _modulationSpectrum.Length; i++)
        {
            float mag = _modulationSpectrum[i];
            totalSum += mag;

            if (i >= minBin && i <= maxBin)
            {
                speechBandSum += mag;
            }
        }

        // Speech band energy ratio
        speechBandEnergy = totalSum > 1e-6f ? speechBandSum / totalSum : 0f;

        // Compute modulation index (depth of modulation)
        // Higher modulation depth in speech band = better intelligibility
        float dcComponent = _modulationSpectrum[0];
        modulationIndex = dcComponent > 1e-6f
            ? MathF.Min(speechBandSum / dcComponent, 1f)
            : 0f;

        // Overall intelligibility score
        // Combine modulation index and speech band energy
        // Good intelligibility: high modulation depth concentrated in 2-8 Hz band
        float modScore = modulationIndex * 100f;
        float bandScore = speechBandEnergy * 100f;
        intelligibilityScore = modScore * 0.6f + bandScore * 0.4f;
        intelligibilityScore = MathF.Min(intelligibilityScore, 100f);
    }

    private void ComputeModulationSpectrum()
    {
        int n = _envelopeCount;
        if (n < 32)
        {
            return;
        }

        // Use available envelope data
        int fftSize = Math.Min(n, EnvelopeBufferSize);
        int halfSize = fftSize / 2;

        // Simple DFT (not performance-critical at 50 Hz sample rate)
        for (int k = 0; k < halfSize; k++)
        {
            float real = 0f;
            float imag = 0f;
            float w = -2f * MathF.PI * k / fftSize;

            for (int i = 0; i < fftSize; i++)
            {
                int idx = (_envelopeIndex - fftSize + i + EnvelopeBufferSize) % EnvelopeBufferSize;
                float sample = _energyEnvelope[idx];
                float angle = w * i;
                real += sample * MathF.Cos(angle);
                imag += sample * MathF.Sin(angle);
            }

            _modulationSpectrum[k] = MathF.Sqrt(real * real + imag * imag) / fftSize;
        }
    }

    /// <summary>
    /// Get the modulation spectrum for display purposes.
    /// </summary>
    /// <param name="spectrum">Output array to copy spectrum into.</param>
    /// <returns>Number of bins copied.</returns>
    public int GetModulationSpectrum(float[] spectrum)
    {
        int count = Math.Min(spectrum.Length, _modulationSpectrum.Length);
        Array.Copy(_modulationSpectrum, spectrum, count);
        return count;
    }

    /// <summary>
    /// Reset the analyzer state.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_energyEnvelope, 0, _energyEnvelope.Length);
        Array.Clear(_modulationSpectrum, 0, _modulationSpectrum.Length);
        _envelopeIndex = 0;
        _envelopeCount = 0;
        _energyAccumulator = 0f;
        _sampleCount = 0;
    }
}
