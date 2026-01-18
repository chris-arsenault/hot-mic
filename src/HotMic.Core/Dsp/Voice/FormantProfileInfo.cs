namespace HotMic.Core.Dsp.Voice;

/// <summary>
/// Presets for formant analysis tuned to common speaker groups.
/// </summary>
public enum FormantProfile
{
    Tenor = 0,
    Alto = 1,
    Soprano = 2,
    BassBaritone = 3,
    Male = Tenor,
    Female = Alto,
    Child = Soprano
}

public readonly record struct FormantTrackingPreset(
    float F1MinHz,
    float F1MaxHz,
    float F2MinHz,
    float F2MaxHz,
    int LpcOrder,
    float MaxDeltaF1Hz,
    float MaxDeltaF2Hz,
    float SmoothingTauMs)
{
    public float FormantCeilingHz => F2MaxHz;
}

/// <summary>
/// Reference settings for formant analysis (Praat/Burg-style defaults).
/// </summary>
public static class FormantProfileInfo
{
    /// <summary>Praat default analysis window length (seconds).</summary>
    public const float DefaultWindowSeconds = 0.025f;

    /// <summary>Praat default pre-emphasis reference (Hz).</summary>
    public const float DefaultPreEmphasisHz = 50f;

    public static FormantTrackingPreset GetTrackingPreset(FormantProfile profile) => profile switch
    {
        FormantProfile.BassBaritone => new FormantTrackingPreset(
            180f, 750f, 700f, 2200f,
            8, 120f, 200f, 25f),
        FormantProfile.Alto => new FormantTrackingPreset(
            250f, 1000f, 1000f, 3000f,
            10, 180f, 400f, 16f),
        FormantProfile.Soprano => new FormantTrackingPreset(
            300f, 1200f, 1200f, 3500f,
            12, 220f, 500f, 12f),
        _ => new FormantTrackingPreset(
            200f, 900f, 800f, 2500f,
            10, 150f, 300f, 20f)
    };

    public static float GetFormantCeilingHz(FormantProfile profile)
        => GetTrackingPreset(profile).FormantCeilingHz;

    public static int GetRecommendedLpcOrder(FormantProfile profile, int maxFormants)
    {
        return GetTrackingPreset(profile).LpcOrder;
    }

    public static string GetLabel(FormantProfile profile) => profile switch
    {
        FormantProfile.BassBaritone => "Bass/Baritone",
        FormantProfile.Alto => "Alto",
        FormantProfile.Soprano => "Soprano",
        _ => "Tenor"
    };
}
