namespace HotMic.Core.Dsp.Voice;

/// <summary>
/// Presets for formant analysis tuned to common speaker groups.
/// </summary>
public enum FormantProfile
{
    Male,
    Female,
    Child
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

    public static float GetFormantCeilingHz(FormantProfile profile) => profile switch
    {
        FormantProfile.Female => 5500f,
        FormantProfile.Child => 8000f,
        _ => 5000f
    };

    public static int GetRecommendedLpcOrder(FormantProfile profile, int maxFormants)
    {
        int baseOrder = Math.Max(2 * maxFormants, 8);
        return profile == FormantProfile.Child ? baseOrder + 2 : baseOrder;
    }

    public static string GetLabel(FormantProfile profile) => profile switch
    {
        FormantProfile.Female => "Female",
        FormantProfile.Child => "Child",
        _ => "Male"
    };
}
