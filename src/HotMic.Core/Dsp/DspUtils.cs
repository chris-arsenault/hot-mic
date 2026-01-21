namespace HotMic.Core.Dsp;

public static class DspUtils
{
    private const float Ln10Over20 = 0.115129254f;
    private const float InvLn10Over20 = 8.68588964f;
    private const float DenormalThreshold = 1e-20f;

    public static float DbToLinear(float db)
    {
        return MathF.Exp(db * Ln10Over20);
    }

    public static float LinearToDb(float linear)
    {
        return MathF.Log(MathF.Max(linear, 1e-10f)) * InvLn10Over20;
    }

    public static float TimeToCoefficient(float timeMs, int sampleRate)
    {
        float timeSeconds = MathF.Max(0.0001f, timeMs * 0.001f);
        return 1f - MathF.Exp(-1f / (timeSeconds * sampleRate));
    }

    public static float FlushDenormal(float value)
    {
        // Avoid denormals on near-silence to prevent sporadic CPU spikes.
        return MathF.Abs(value) < DenormalThreshold ? 0f : value;
    }

    public static float ComputeBandEnergyRatio(ReadOnlySpan<float> magnitudes, float binResolutionHz, float minHz, float maxHz)
    {
        if (magnitudes.IsEmpty || binResolutionHz <= 0f || maxHz <= minHz)
        {
            return 0f;
        }

        int startBin = (int)MathF.Floor(minHz / binResolutionHz);
        int endBin = (int)MathF.Ceiling(maxHz / binResolutionHz);
        startBin = Math.Clamp(startBin, 0, magnitudes.Length - 1);
        endBin = Math.Clamp(endBin, startBin, magnitudes.Length - 1);

        double band = 0.0;
        double total = 0.0;
        for (int i = 0; i < magnitudes.Length; i++)
        {
            float mag = magnitudes[i];
            double energy = mag * mag;
            total += energy;
            if (i >= startBin && i <= endBin)
            {
                band += energy;
            }
        }

        if (total <= 1e-12)
        {
            return 0f;
        }

        return (float)(band / total);
    }
}
