namespace HotMic.Core.Dsp.Spectrogram;

/// <summary>
/// Computes spectral centroid, slope, and flux for a magnitude spectrum.
/// </summary>
public sealed class SpectralFeatureExtractor
{
    private float[] _frequencies = Array.Empty<float>();
    private float[] _fluxPrevious = Array.Empty<float>();
    private double _freqSum;
    private double _freqSumSq;
    private int _frequencyCount;

    /// <summary>
    /// Ensure internal buffers are sized for the provided bin count.
    /// </summary>
    public void EnsureCapacity(int bins)
    {
        if (bins <= 0)
        {
            return;
        }

        if (_fluxPrevious.Length < bins)
        {
            _fluxPrevious = new float[bins];
        }

        if (_frequencies.Length < bins)
        {
            _frequencies = new float[bins];
        }
    }

    /// <summary>
    /// Clear the internal flux history.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_fluxPrevious, 0, _fluxPrevious.Length);
        _frequencyCount = 0;
        _freqSum = 0.0;
        _freqSumSq = 0.0;
    }

    /// <summary>
    /// Update the frequency lookup tables used for centroid and slope calculation.
    /// </summary>
    public void UpdateFrequencies(ReadOnlySpan<float> frequencies)
    {
        EnsureCapacity(frequencies.Length);

        double sum = 0.0;
        double sumSq = 0.0;
        int count = frequencies.Length;
        for (int i = 0; i < count; i++)
        {
            float freq = frequencies[i];
            _frequencies[i] = freq;
            sum += freq;
            sumSq += freq * freq;
        }

        _frequencyCount = count;
        _freqSum = sum;
        _freqSumSq = sumSq;
    }

    /// <summary>
    /// Compute centroid, slope, and flux for the given magnitude spectrum.
    /// </summary>
    public void Compute(ReadOnlySpan<float> magnitudes, out float centroid, out float slope, out float flux)
    {
        Compute(magnitudes, Math.Min(magnitudes.Length, _frequencyCount), out centroid, out slope, out flux);
    }

    /// <summary>
    /// Compute centroid, slope, and flux with explicit bin count.
    /// </summary>
    public void Compute(ReadOnlySpan<float> magnitudes, int bins, out float centroid, out float slope, out float flux)
    {
        bins = Math.Min(bins, Math.Min(magnitudes.Length, _frequencyCount));
        if (bins <= 0)
        {
            centroid = 0f;
            slope = 0f;
            flux = 0f;
            return;
        }

        double sumMag = 0.0;
        double sumWeighted = 0.0;
        double sumDb = 0.0;
        double sumFreqDb = 0.0;
        double fluxSum = 0.0;

        for (int i = 0; i < bins; i++)
        {
            float mag = magnitudes[i];
            float freq = _frequencies[i];
            sumMag += mag;
            sumWeighted += mag * freq;

            float db = DspUtils.LinearToDb(mag);
            sumDb += db;
            sumFreqDb += db * freq;

            float diff = mag - _fluxPrevious[i];
            fluxSum += diff * diff;
            _fluxPrevious[i] = mag;
        }

        // Centroid in Hz, slope via linear regression in dB/Hz (scaled to dB/kHz), flux as mean-square change.
        centroid = sumMag > 1e-6 ? (float)(sumWeighted / sumMag) : 0f;
        double denom = bins * _freqSumSq - _freqSum * _freqSum;
        slope = Math.Abs(denom) > 1e-6
            ? (float)((bins * sumFreqDb - _freqSum * sumDb) / denom * 1000.0)
            : 0f;
        flux = (float)(fluxSum / bins);
    }
}
