namespace HotMic.Core.Plugins.BuiltIn;

internal static class EnhanceAmountScale
{
    private static readonly string[] Labels = ["x1", "x2", "x5", "x10"];

    public static float FromIndex(int index)
    {
        return index switch
        {
            1 => 2f,
            2 => 5f,
            3 => 10f,
            _ => 1f
        };
    }

    public static int ClampIndex(float value)
    {
        int index = (int)MathF.Round(value);
        return Math.Clamp(index, 0, 3);
    }

    public static string FormatLabel(float value)
    {
        int index = ClampIndex(value);
        return Labels[index];
    }
}
