namespace HotMic.Core.Dsp.Spectrogram;

/// <summary>
/// Immutable description of analysis-bin layout for spectrogram rendering.
/// </summary>
public sealed class SpectrogramAnalysisDescriptor
{
    /// <summary>
    /// Active analysis transform type.
    /// </summary>
    public SpectrogramTransformType TransformType { get; }

    /// <summary>
    /// Number of analysis bins per frame.
    /// </summary>
    public int BinCount { get; }

    /// <summary>
    /// Minimum analysis-bin center frequency in Hz.
    /// </summary>
    public float MinFrequencyHz { get; }

    /// <summary>
    /// Maximum analysis-bin center frequency in Hz.
    /// </summary>
    public float MaxFrequencyHz { get; }

    /// <summary>
    /// Bin resolution in Hz for linear layouts (0 for variable-spacing layouts).
    /// </summary>
    public float BinResolutionHz { get; }

    /// <summary>
    /// True for linearly spaced bins (FFT/ZoomFFT), false for variable spacing (CQT).
    /// </summary>
    public bool IsLinear { get; }

    /// <summary>
    /// Center frequencies in Hz for each analysis bin.
    /// </summary>
    public ReadOnlyMemory<float> BinCentersHz { get; }

    private SpectrogramAnalysisDescriptor(
        SpectrogramTransformType transformType,
        float[] binCentersHz,
        float minFrequencyHz,
        float maxFrequencyHz,
        float binResolutionHz,
        bool isLinear)
    {
        TransformType = transformType;
        BinCentersHz = binCentersHz;
        BinCount = binCentersHz.Length;
        MinFrequencyHz = minFrequencyHz;
        MaxFrequencyHz = maxFrequencyHz;
        BinResolutionHz = binResolutionHz;
        IsLinear = isLinear;
    }

    /// <summary>
    /// Build a descriptor for linearly spaced bins.
    /// </summary>
    public static SpectrogramAnalysisDescriptor CreateLinear(
        SpectrogramTransformType transformType,
        int binCount,
        float minFrequencyHz,
        float binResolutionHz)
    {
        binCount = Math.Max(0, binCount);
        var centers = new float[binCount];
        for (int i = 0; i < binCount; i++)
        {
            centers[i] = minFrequencyHz + i * binResolutionHz;
        }

        float maxFrequency = binCount > 0 ? centers[binCount - 1] : minFrequencyHz;
        return new SpectrogramAnalysisDescriptor(
            transformType,
            centers,
            minFrequencyHz,
            maxFrequency,
            binResolutionHz,
            isLinear: true);
    }

    /// <summary>
    /// Build a descriptor from precomputed bin centers (variable spacing).
    /// </summary>
    public static SpectrogramAnalysisDescriptor CreateFromCenters(
        SpectrogramTransformType transformType,
        ReadOnlySpan<float> centersHz)
    {
        var centers = centersHz.ToArray();
        float minFrequency = centers.Length > 0 ? centers[0] : 0f;
        float maxFrequency = centers.Length > 0 ? centers[centers.Length - 1] : minFrequency;
        return new SpectrogramAnalysisDescriptor(
            transformType,
            centers,
            minFrequency,
            maxFrequency,
            binResolutionHz: 0f,
            isLinear: false);
    }

    /// <summary>
    /// Find the analysis bin index closest to a given frequency.
    /// </summary>
    /// <param name="frequencyHz">Target frequency in Hz.</param>
    /// <returns>Bin index, clamped to valid range.</returns>
    public int FrequencyToBin(float frequencyHz)
    {
        if (BinCount <= 0)
        {
            return 0;
        }

        if (IsLinear)
        {
            // Direct calculation for linear bins
            float binFloat = (frequencyHz - MinFrequencyHz) / MathF.Max(1e-6f, BinResolutionHz);
            return Math.Clamp((int)MathF.Round(binFloat), 0, BinCount - 1);
        }
        else
        {
            // Binary search for non-linear bins (CQT)
            return BinarySearchBin(BinCentersHz.Span, frequencyHz);
        }
    }

    /// <summary>
    /// Get the peak magnitude near a target frequency, searching within a tolerance window.
    /// </summary>
    /// <param name="magnitudes">Magnitude spectrum from the active transform.</param>
    /// <param name="frequencyHz">Target frequency in Hz.</param>
    /// <param name="toleranceFraction">Search window as fraction of target frequency (default 3%).</param>
    /// <returns>Peak magnitude found in the search window, or 0 if none.</returns>
    public float GetMagnitudeNear(ReadOnlySpan<float> magnitudes, float frequencyHz, float toleranceFraction = 0.03f)
    {
        if (BinCount <= 0 || magnitudes.Length == 0)
        {
            return 0f;
        }

        float tolerance = frequencyHz * toleranceFraction;
        int startBin = FrequencyToBin(frequencyHz - tolerance);
        int endBin = FrequencyToBin(frequencyHz + tolerance);

        if (startBin > endBin)
        {
            (startBin, endBin) = (endBin, startBin);
        }

        float peak = 0f;
        int maxBin = Math.Min(magnitudes.Length, BinCount);
        for (int bin = startBin; bin <= endBin && bin < maxBin; bin++)
        {
            if (bin >= 0 && magnitudes[bin] > peak)
            {
                peak = magnitudes[bin];
            }
        }

        return peak;
    }

    /// <summary>
    /// Find the bin index and magnitude of the peak near a target frequency.
    /// </summary>
    /// <param name="magnitudes">Magnitude spectrum from the active transform.</param>
    /// <param name="frequencyHz">Target frequency in Hz.</param>
    /// <param name="toleranceFraction">Search window as fraction of target frequency (default 3%).</param>
    /// <returns>Tuple of (bin index, magnitude, actual frequency). Returns (-1, 0, 0) if not found.</returns>
    public (int bin, float magnitude, float frequency) FindPeakNear(
        ReadOnlySpan<float> magnitudes, float frequencyHz, float toleranceFraction = 0.03f)
    {
        if (BinCount <= 0 || magnitudes.Length == 0)
        {
            return (-1, 0f, 0f);
        }

        float tolerance = frequencyHz * toleranceFraction;
        int startBin = FrequencyToBin(frequencyHz - tolerance);
        int endBin = FrequencyToBin(frequencyHz + tolerance);

        if (startBin > endBin)
        {
            (startBin, endBin) = (endBin, startBin);
        }

        float peak = 0f;
        int peakBin = -1;
        int maxBin = Math.Min(magnitudes.Length, BinCount);
        var centers = BinCentersHz.Span;

        for (int bin = startBin; bin <= endBin && bin < maxBin; bin++)
        {
            if (bin >= 0 && magnitudes[bin] > peak)
            {
                peak = magnitudes[bin];
                peakBin = bin;
            }
        }

        float peakFreq = peakBin >= 0 && peakBin < centers.Length ? centers[peakBin] : 0f;
        return (peakBin, peak, peakFreq);
    }

    private static int BinarySearchBin(ReadOnlySpan<float> centers, float frequencyHz)
    {
        if (centers.Length == 0)
        {
            return 0;
        }

        int low = 0;
        int high = centers.Length - 1;

        while (low < high)
        {
            int mid = low + ((high - low) >> 1);
            if (centers[mid] < frequencyHz)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        // Check if previous bin is closer
        if (low > 0)
        {
            float distLow = MathF.Abs(centers[low] - frequencyHz);
            float distPrev = MathF.Abs(centers[low - 1] - frequencyHz);
            if (distPrev < distLow)
            {
                return low - 1;
            }
        }

        return low;
    }
}
