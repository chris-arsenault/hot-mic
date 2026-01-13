namespace HotMic.Core.Dsp;

/// <summary>
/// Maps FFT magnitudes into display bins using perceptual frequency scales.
/// </summary>
public sealed class SpectrumMapper
{
    private int[] _binStart = Array.Empty<int>();
    private int[] _binEnd = Array.Empty<int>();
    private float[] _binCenters = Array.Empty<float>();

    public int BinCount => _binStart.Length;

    public ReadOnlySpan<float> CenterFrequencies => _binCenters;

    public void Configure(int fftSize, int sampleRate, int displayBins,
        float minFrequency, float maxFrequency, FrequencyScale scale)
    {
        int bins = Math.Max(8, displayBins);
        if (_binStart.Length != bins)
        {
            _binStart = new int[bins];
            _binEnd = new int[bins];
            _binCenters = new float[bins];
        }

        float nyquist = sampleRate * 0.5f;
        float minHz = Math.Clamp(minFrequency, 1f, nyquist - 1f);
        float maxHz = Math.Clamp(maxFrequency, minHz + 1f, nyquist);
        float scaledMin = FrequencyScaleUtils.ToScale(scale, minHz);
        float scaledMax = FrequencyScaleUtils.ToScale(scale, maxHz);
        float binResolution = sampleRate / (float)fftSize;

        for (int i = 0; i < bins; i++)
        {
            float t0 = i / (float)bins;
            float t1 = (i + 1) / (float)bins;
            float tc = (i + 0.5f) / bins;

            float scaledStart = scaledMin + (scaledMax - scaledMin) * t0;
            float scaledEnd = scaledMin + (scaledMax - scaledMin) * t1;
            float scaledCenter = scaledMin + (scaledMax - scaledMin) * tc;

            float startHz = FrequencyScaleUtils.FromScale(scale, scaledStart);
            float endHz = FrequencyScaleUtils.FromScale(scale, scaledEnd);
            float centerHz = FrequencyScaleUtils.FromScale(scale, scaledCenter);

            int startBin = (int)MathF.Floor(startHz / binResolution);
            int endBin = (int)MathF.Floor(endHz / binResolution);
            startBin = Math.Clamp(startBin, 0, fftSize / 2 - 1);
            endBin = Math.Clamp(Math.Max(startBin + 1, endBin), 1, fftSize / 2 - 1);

            _binStart[i] = startBin;
            _binEnd[i] = endBin;
            _binCenters[i] = centerHz;
        }
    }

    /// <summary>
    /// Map input magnitudes to display bins using max-hold per band.
    /// </summary>
    public void MapMax(ReadOnlySpan<float> magnitudes, Span<float> displayMagnitudes)
    {
        int bins = Math.Min(displayMagnitudes.Length, _binStart.Length);
        if (bins == 0)
        {
            return;
        }

        for (int i = 0; i < bins; i++)
        {
            int start = _binStart[i];
            int end = _binEnd[i];
            float max = 0f;
            for (int bin = start; bin <= end && bin < magnitudes.Length; bin++)
            {
                float mag = magnitudes[bin];
                if (mag > max)
                {
                    max = mag;
                }
            }
            displayMagnitudes[i] = max;
        }
    }
}
