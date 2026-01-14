namespace HotMic.Core.Dsp;

/// <summary>
/// Enumerates common vocal ranges for display overlays.
/// </summary>
public enum VocalRangeType
{
    Bass,
    Baritone,
    Tenor,
    Alto,
    MezzoSoprano,
    Soprano
}

/// <summary>
/// Provides vocal range metadata for spectrogram overlays.
/// </summary>
public static class VocalRangeInfo
{
    /// <summary>
    /// Gets the fundamental frequency range for the requested voice type.
    /// </summary>
    public static (float MinHz, float MaxHz) GetFundamentalRange(VocalRangeType range)
    {
        return range switch
        {
            VocalRangeType.Bass => (80f, 350f),
            VocalRangeType.Baritone => (95f, 400f),
            VocalRangeType.Tenor => (120f, 500f),
            VocalRangeType.Alto => (160f, 700f),
            VocalRangeType.MezzoSoprano => (180f, 800f),
            VocalRangeType.Soprano => (250f, 1100f),
            _ => (80f, 500f)
        };
    }

    /// <summary>
    /// Gets a short display label for the requested voice type.
    /// </summary>
    public static string GetLabel(VocalRangeType range)
    {
        return range switch
        {
            VocalRangeType.Bass => "Bass",
            VocalRangeType.Baritone => "Baritone",
            VocalRangeType.Tenor => "Tenor",
            VocalRangeType.Alto => "Alto",
            VocalRangeType.MezzoSoprano => "Mezzo",
            VocalRangeType.Soprano => "Soprano",
            _ => "Vocal"
        };
    }
}
