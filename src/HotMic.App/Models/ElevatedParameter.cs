namespace HotMic.App.Models;

/// <summary>
/// Represents a plugin parameter elevated to the channel strip for quick access.
/// </summary>
public sealed class ElevatedParameter
{
    public string PluginId { get; init; } = string.Empty;
    public string PluginName { get; init; } = string.Empty;
    public int ParameterIndex { get; init; }
    public string ParameterName { get; init; } = string.Empty;
    public float MinValue { get; init; }
    public float MaxValue { get; init; } = 1f;
    public float CurrentValue { get; set; }
    public string Unit { get; init; } = string.Empty;
    public string MidiPath { get; init; } = string.Empty;

    public string DisplayValue => FormatValue();

    private string FormatValue()
    {
        if (string.IsNullOrEmpty(Unit))
        {
            return $"{CurrentValue:0.0}";
        }

        return Unit switch
        {
            "%" => $"{CurrentValue:0}%",
            "dB" => $"{CurrentValue:0.0} dB",
            "ms" => $"{CurrentValue:0} ms",
            _ => $"{CurrentValue:0.0} {Unit}"
        };
    }
}

/// <summary>
/// Defines which parameters are elevated for each plugin type.
/// </summary>
public static class ElevatedParameterDefinitions
{
    public static readonly Dictionary<string, (int Index, string Name)[]> PluginElevations = new()
    {
        ["builtin:gain"] = [(0, "Gain"), (1, "Phase")],
        ["builtin:compressor"] = [(0, "Threshold"), (1, "Ratio")],
        ["builtin:noisegate"] = [(0, "Threshold"), (4, "Release")],
        ["builtin:eq3"] = [(1, "Mid Gain"), (2, "Mid Freq")],
        ["builtin:fft-noise"] = [(0, "Reduction"), (1, "Threshold")],
        ["builtin:rnnoise"] = [(0, "Reduction"), (1, "VAD Thresh")],
        ["builtin:deepfilternet"] = [(0, "Reduction"), (1, "Atten Lim")],
        ["builtin:voice-gate"] = [(0, "Threshold"), (2, "Release")],
        ["builtin:reverb"] = [(0, "Dry/Wet"), (1, "Decay")],
    };
}
