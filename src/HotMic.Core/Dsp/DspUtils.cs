namespace HotMic.Core.Dsp;

public static class DspUtils
{
    public static float DbToLinear(float db)
    {
        return MathF.Pow(10f, db / 20f);
    }

    public static float LinearToDb(float linear)
    {
        return 20f * MathF.Log10(linear + 1e-10f);
    }

    public static float TimeToCoefficient(float timeMs, int sampleRate)
    {
        float timeSeconds = MathF.Max(0.0001f, timeMs * 0.001f);
        return 1f - MathF.Exp(-1f / (timeSeconds * sampleRate));
    }
}
