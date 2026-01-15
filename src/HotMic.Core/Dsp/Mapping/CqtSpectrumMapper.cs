namespace HotMic.Core.Dsp.Mapping;

/// <summary>
/// Maps CQT output bins to display bins using pull-based mapping.
/// CQT bins are logarithmically spaced, so mapping depends on display scale.
/// Uses pull-based approach (iterate display bins, sample CQT bins) to avoid gaps.
/// </summary>
public sealed class CqtSpectrumMapper
{
    private int[] _displayBinStart = Array.Empty<int>();
    private int[] _displayBinEnd = Array.Empty<int>();
    private float[] _displayCenterFrequencies = Array.Empty<float>();
    private float[] _cqtFrequencies = Array.Empty<float>();
    private int _displayBins;
    private int _cqtBins;

    /// <summary>
    /// Number of display bins.
    /// </summary>
    public int DisplayBins => _displayBins;

    /// <summary>
    /// Center frequencies for each display bin.
    /// </summary>
    public ReadOnlySpan<float> CenterFrequencies => _displayCenterFrequencies.AsSpan(0, _displayBins);

    /// <summary>
    /// Configure the mapper for the given CQT and display parameters.
    /// </summary>
    /// <param name="displayBins">Number of display bins.</param>
    /// <param name="cqtFrequencies">Center frequencies from CQT.</param>
    /// <param name="minHz">Display minimum frequency.</param>
    /// <param name="maxHz">Display maximum frequency.</param>
    /// <param name="displayScale">Frequency scale for display.</param>
    public void Configure(int displayBins, ReadOnlySpan<float> cqtFrequencies,
        float minHz, float maxHz, FrequencyScale displayScale)
    {
        _displayBins = Math.Max(8, displayBins);
        _cqtBins = cqtFrequencies.Length;

        if (_displayBinStart.Length < _displayBins)
        {
            _displayBinStart = new int[_displayBins];
            _displayBinEnd = new int[_displayBins];
            _displayCenterFrequencies = new float[_displayBins];
        }

        if (_cqtFrequencies.Length < _cqtBins)
        {
            _cqtFrequencies = new float[_cqtBins];
        }

        // Copy CQT frequencies for use in MapMax
        for (int i = 0; i < _cqtBins; i++)
        {
            _cqtFrequencies[i] = cqtFrequencies[i];
        }

        float scaledMin = FrequencyScaleUtils.ToScale(displayScale, minHz);
        float scaledMax = FrequencyScaleUtils.ToScale(displayScale, maxHz);
        float scaledRange = scaledMax - scaledMin;
        if (scaledRange < 1e-6f)
        {
            scaledRange = 1f;
        }

        // Pull-based mapping: for each display bin, find which CQT bins cover it
        for (int i = 0; i < _displayBins; i++)
        {
            float t0 = i / (float)_displayBins;
            float t1 = (i + 1) / (float)_displayBins;
            float tc = (i + 0.5f) / _displayBins;

            float scaledStart = scaledMin + scaledRange * t0;
            float scaledEnd = scaledMin + scaledRange * t1;
            float scaledCenter = scaledMin + scaledRange * tc;

            float startHz = FrequencyScaleUtils.FromScale(displayScale, scaledStart);
            float endHz = FrequencyScaleUtils.FromScale(displayScale, scaledEnd);
            float centerHz = FrequencyScaleUtils.FromScale(displayScale, scaledCenter);

            _displayCenterFrequencies[i] = centerHz;

            // Find CQT bins that overlap with this display bin's frequency range
            int cqtStart = -1;
            int cqtEnd = -1;
            for (int c = 0; c < _cqtBins; c++)
            {
                float cqtFreq = _cqtFrequencies[c];
                if (cqtFreq >= startHz && cqtFreq <= endHz)
                {
                    if (cqtStart < 0)
                    {
                        cqtStart = c;
                    }
                    cqtEnd = c;
                }
            }

            // If no exact overlap, use nearest CQT bin
            if (cqtStart < 0)
            {
                float bestDist = float.MaxValue;
                int bestBin = 0;
                for (int c = 0; c < _cqtBins; c++)
                {
                    float dist = MathF.Abs(_cqtFrequencies[c] - centerHz);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestBin = c;
                    }
                }
                cqtStart = bestBin;
                cqtEnd = bestBin;
            }

            _displayBinStart[i] = cqtStart;
            _displayBinEnd[i] = cqtEnd;
        }
    }

    /// <summary>
    /// Map CQT magnitudes to display bins using max-hold per band.
    /// Pull-based approach ensures every display bin gets a value.
    /// </summary>
    public void MapMax(ReadOnlySpan<float> cqtMagnitudes, Span<float> displayMagnitudes)
    {
        int displayLen = Math.Min(displayMagnitudes.Length, _displayBins);
        int cqtLen = Math.Min(cqtMagnitudes.Length, _cqtBins);

        if (displayLen == 0 || cqtLen == 0)
        {
            return;
        }

        // Pull-based: iterate display bins, find max from corresponding CQT bins
        for (int i = 0; i < displayLen; i++)
        {
            int start = _displayBinStart[i];
            int end = _displayBinEnd[i];
            float max = 0f;

            for (int c = start; c <= end && c < cqtLen; c++)
            {
                float mag = cqtMagnitudes[c];
                if (mag > max)
                {
                    max = mag;
                }
            }

            displayMagnitudes[i] = max;
        }
    }

    /// <summary>
    /// Map CQT magnitudes directly without interpolation (nearest neighbor).
    /// </summary>
    public void MapNearest(ReadOnlySpan<float> cqtMagnitudes, Span<float> displayMagnitudes)
    {
        int displayLen = Math.Min(displayMagnitudes.Length, _displayBins);
        int cqtLen = Math.Min(cqtMagnitudes.Length, _cqtBins);

        if (displayLen == 0 || cqtLen == 0)
        {
            return;
        }

        // Use the start of each display bin's range as the nearest
        for (int i = 0; i < displayLen; i++)
        {
            int cqtBin = _displayBinStart[i];
            if (cqtBin >= 0 && cqtBin < cqtLen)
            {
                displayMagnitudes[i] = cqtMagnitudes[cqtBin];
            }
            else
            {
                displayMagnitudes[i] = 0f;
            }
        }
    }
}
