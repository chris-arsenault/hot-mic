namespace HotMic.Core.Dsp;

/// <summary>
/// Frequency scale options for spectrogram display.
/// </summary>
public enum FrequencyScale
{
    Linear,
    Logarithmic,
    Mel,
    Erb,
    Bark
}

/// <summary>
/// Utility functions for mapping between Hz and perceptual frequency scales.
/// </summary>
public static class FrequencyScaleUtils
{
    /// <summary>
    /// Convert Hz to scaled units for the selected scale.
    /// </summary>
    public static float ToScale(FrequencyScale scale, float frequencyHz)
    {
        float f = MathF.Max(1e-3f, frequencyHz);
        return scale switch
        {
            FrequencyScale.Logarithmic => MathF.Log10(f),
            FrequencyScale.Mel => 2595f * MathF.Log10(1f + f / 700f),
            // ERB formula per vocal-spectrograph-spec.md
            FrequencyScale.Erb => 24.7f * (4.37f * f / 1000f + 1f),
            FrequencyScale.Bark => BarkScale(f),
            _ => f
        };
    }

    /// <summary>
    /// Convert scaled units to Hz for the selected scale.
    /// </summary>
    public static float FromScale(FrequencyScale scale, float scaled)
    {
        return scale switch
        {
            FrequencyScale.Logarithmic => MathF.Pow(10f, scaled),
            FrequencyScale.Mel => 700f * (MathF.Pow(10f, scaled / 2595f) - 1f),
            FrequencyScale.Erb => MathF.Max(0f, (scaled / 24.7f - 1f) * 1000f / 4.37f),
            FrequencyScale.Bark => BarkInverse(scaled),
            _ => scaled
        };
    }

    /// <summary>
    /// Normalize a frequency to 0..1 using the selected scale and range.
    /// </summary>
    public static float Normalize(FrequencyScale scale, float frequencyHz, float minHz, float maxHz)
    {
        float min = ToScale(scale, minHz);
        float max = ToScale(scale, maxHz);
        float value = ToScale(scale, frequencyHz);
        if (max <= min)
        {
            return 0f;
        }

        return Math.Clamp((value - min) / (max - min), 0f, 1f);
    }

    private static float BarkScale(float frequencyHz)
    {
        // Traunmuller approximation
        float f = frequencyHz;
        return 13f * MathF.Atan(0.00076f * f) + 3.5f * MathF.Atan(MathF.Pow(f / 7500f, 2f));
    }

    private static float BarkInverse(float bark)
    {
        // Invert Bark scale numerically (bisection). Only used on configuration changes.
        float low = 0f;
        float high = 20000f;
        for (int i = 0; i < 32; i++)
        {
            float mid = 0.5f * (low + high);
            float value = BarkScale(mid);
            if (value < bark)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return 0.5f * (low + high);
    }
}
