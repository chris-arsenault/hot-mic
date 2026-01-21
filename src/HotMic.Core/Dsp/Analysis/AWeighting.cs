namespace HotMic.Core.Dsp.Analysis;

/// <summary>
/// A-weighting curve utilities for perceptual loudness weighting.
/// </summary>
public static class AWeighting
{
    private const float RefGainDb = 2.0f;
    private const float F1 = 20.6f;
    private const float F2 = 107.7f;
    private const float F3 = 737.9f;
    private const float F4 = 12194f;

    /// <summary>
    /// Returns A-weighting in dB for the provided frequency.
    /// </summary>
    public static float GetDb(float frequencyHz)
    {
        float f = MathF.Max(1e-3f, frequencyHz);
        float f2 = f * f;
        float f1_2 = F1 * F1;
        float f2_2 = F2 * F2;
        float f3_2 = F3 * F3;
        float f4_2 = F4 * F4;

        float numerator = f4_2 * f4_2 * f2 * f2;
        float denominator = (f2 + f1_2)
                            * MathF.Sqrt((f2 + f2_2) * (f2 + f3_2))
                            * (f2 + f4_2);
        float ra = denominator > 0f ? numerator / denominator : 0f;
        float db = 20f * MathF.Log10(MathF.Max(ra, 1e-20f)) + RefGainDb;
        return db;
    }

    /// <summary>
    /// Returns the linear weighting multiplier for the provided frequency.
    /// </summary>
    public static float GetLinearWeight(float frequencyHz)
    {
        return DspUtils.DbToLinear(GetDb(frequencyHz));
    }
}
