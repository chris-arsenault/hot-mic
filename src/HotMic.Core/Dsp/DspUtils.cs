namespace HotMic.Core.Dsp;

public static class DspUtils
{
    private const float Ln10Over20 = 0.115129254f;
    private const float InvLn10Over20 = 8.68588964f;

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
}
