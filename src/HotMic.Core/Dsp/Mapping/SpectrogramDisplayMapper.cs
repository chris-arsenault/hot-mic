namespace HotMic.Core.Dsp.Mapping;

/// <summary>
/// Maps analysis-resolution magnitudes to display bins using an analysis layout and display scale.
/// </summary>
public sealed class SpectrogramDisplayMapper
{
    private int[] _binStart = Array.Empty<int>();
    private int[] _binEnd = Array.Empty<int>();
    private float[] _displayCenters = Array.Empty<float>();
    private float[] _analysisEdges = Array.Empty<float>();
    private int _displayBins;
    private int _analysisBins;
    private float _displayMinHz;
    private float _displayMaxHz;
    private FrequencyScale _scale;
    private float _scaledMin;
    private float _invScaledRange;
    private float _maxPos;
    private bool _configured;

    /// <summary>
    /// Number of display bins.
    /// </summary>
    public int DisplayBins => _displayBins;

    /// <summary>
    /// Number of analysis bins used for mapping.
    /// </summary>
    public int AnalysisBins => _analysisBins;

    /// <summary>
    /// Center frequencies for each display bin.
    /// </summary>
    public ReadOnlySpan<float> CenterFrequencies => _displayCenters.AsSpan(0, _displayBins);

    /// <summary>
    /// Configure mapping from analysis layout to display bins.
    /// </summary>
    public void Configure(
        SpectrogramAnalysisDescriptor analysis,
        int displayBins,
        float minFrequencyHz,
        float maxFrequencyHz,
        FrequencyScale scale)
    {
        int analysisBins = analysis.BinCount;
        if (analysisBins <= 0)
        {
            _configured = false;
            return;
        }

        _analysisBins = analysisBins;
        _displayBins = Math.Max(1, displayBins);
        _scale = scale;

        if (_binStart.Length != _displayBins)
        {
            _binStart = new int[_displayBins];
            _binEnd = new int[_displayBins];
            _displayCenters = new float[_displayBins];
        }

        EnsureAnalysisEdges(analysis.BinCentersHz.Span);

        float safeMin = MathF.Max(1f, Math.Min(minFrequencyHz, maxFrequencyHz - 1f));
        float safeMax = MathF.Max(safeMin + 1f, maxFrequencyHz);
        _displayMinHz = safeMin;
        _displayMaxHz = safeMax;

        float scaledMin = FrequencyScaleUtils.ToScale(scale, safeMin);
        float scaledMax = FrequencyScaleUtils.ToScale(scale, safeMax);
        float scaledRange = scaledMax - scaledMin;
        if (MathF.Abs(scaledRange) < 1e-6f)
        {
            scaledRange = 1f;
        }

        _scaledMin = scaledMin;
        _invScaledRange = 1f / scaledRange;
        _maxPos = Math.Max(1f, _displayBins - 1);

        for (int i = 0; i < _displayBins; i++)
        {
            float t0 = i / (float)_displayBins;
            float t1 = (i + 1) / (float)_displayBins;
            float tc = (i + 0.5f) / _displayBins;

            float scaledStart = scaledMin + scaledRange * t0;
            float scaledEnd = scaledMin + scaledRange * t1;
            float scaledCenter = scaledMin + scaledRange * tc;

            float startHz = FrequencyScaleUtils.FromScale(scale, scaledStart);
            float endHz = FrequencyScaleUtils.FromScale(scale, scaledEnd);
            float centerHz = FrequencyScaleUtils.FromScale(scale, scaledCenter);

            _displayCenters[i] = centerHz;

            float lowHz = MathF.Min(startHz, endHz);
            float highHz = MathF.Max(startHz, endHz);
            int startBin = BinIndexFromEdges(lowHz);
            int endBin = BinIndexFromEdges(highHz);
            if (startBin > endBin)
            {
                (startBin, endBin) = (endBin, startBin);
            }

            _binStart[i] = startBin;
            _binEnd[i] = endBin;
        }

        _configured = true;
    }

    /// <summary>
    /// Map analysis magnitudes into display bins using max-hold per bin.
    /// </summary>
    public void MapMax(ReadOnlySpan<float> analysisMagnitudes, Span<float> displayMagnitudes)
    {
        if (!_configured || _displayBins <= 0)
        {
            return;
        }

        int displayLen = Math.Min(displayMagnitudes.Length, _displayBins);
        int analysisLen = Math.Min(analysisMagnitudes.Length, _analysisBins);
        if (displayLen == 0 || analysisLen == 0)
        {
            return;
        }

        for (int i = 0; i < displayLen; i++)
        {
            int start = _binStart[i];
            int end = _binEnd[i];
            if (start >= analysisLen)
            {
                displayMagnitudes[i] = 0f;
                continue;
            }

            if (end >= analysisLen)
            {
                end = analysisLen - 1;
            }

            float max = 0f;
            for (int bin = start; bin <= end; bin++)
            {
                float mag = analysisMagnitudes[bin];
                if (mag > max)
                {
                    max = mag;
                }
            }

            displayMagnitudes[i] = max;
        }
    }

    /// <summary>
    /// Convert a frequency in Hz to a display-bin position.
    /// </summary>
    public float GetDisplayPosition(float frequencyHz)
    {
        if (!_configured || _displayBins <= 0)
        {
            return 0f;
        }

        float clamped = Math.Clamp(frequencyHz, _displayMinHz, _displayMaxHz);
        float scaled = FrequencyScaleUtils.ToScale(_scale, clamped);
        float norm = (scaled - _scaledMin) * _invScaledRange;
        return Math.Clamp(norm * _maxPos, 0f, _maxPos);
    }

    private void EnsureAnalysisEdges(ReadOnlySpan<float> centers)
    {
        int bins = centers.Length;
        if (bins <= 0)
        {
            return;
        }

        if (_analysisEdges.Length != bins + 1)
        {
            _analysisEdges = new float[bins + 1];
        }

        if (bins == 1)
        {
            float center = MathF.Max(0f, centers[0]);
            float half = MathF.Max(1e-3f, center * 0.5f);
            _analysisEdges[0] = MathF.Max(0f, center - half);
            _analysisEdges[1] = center + half;
            return;
        }

        float firstDelta = centers[1] - centers[0];
        _analysisEdges[0] = MathF.Max(0f, centers[0] - 0.5f * firstDelta);
        for (int i = 1; i < bins; i++)
        {
            _analysisEdges[i] = 0.5f * (centers[i - 1] + centers[i]);
        }

        float lastDelta = centers[bins - 1] - centers[bins - 2];
        _analysisEdges[bins] = centers[bins - 1] + 0.5f * lastDelta;

        for (int i = 1; i < _analysisEdges.Length; i++)
        {
            if (_analysisEdges[i] <= _analysisEdges[i - 1])
            {
                _analysisEdges[i] = _analysisEdges[i - 1] + 1e-3f;
            }
        }
    }

    private int BinIndexFromEdges(float frequencyHz)
    {
        if (_analysisBins <= 0 || _analysisEdges.Length < _analysisBins + 1)
        {
            return 0;
        }

        int upper = UpperBound(_analysisEdges, frequencyHz);
        int bin = upper - 1;
        return Math.Clamp(bin, 0, _analysisBins - 1);
    }

    private static int UpperBound(ReadOnlySpan<float> values, float target)
    {
        int low = 0;
        int high = values.Length;
        while (low < high)
        {
            int mid = low + ((high - low) >> 1);
            if (values[mid] <= target)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }
}
