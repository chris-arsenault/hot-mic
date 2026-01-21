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
/// Includes parameter index, name, min, max, default, and unit for proper UI display.
/// </summary>
public static class ElevatedParameterDefinitions
{
    public record struct ParamDef(int Index, string Name, float Min, float Max, float Default, string Unit);

    public static readonly Dictionary<string, ParamDef[]> PluginElevations = new()
    {
        ["builtin:gain"] = [new(0, "Gain", -60f, 24f, 0f, "dB"), new(1, "Phase", 0f, 1f, 0f, "")],
        ["builtin:compressor"] = [new(0, "Thresh", -60f, 0f, -20f, "dB"), new(1, "Ratio", 1f, 20f, 4f, ":1")],
        ["builtin:noisegate"] = [new(0, "Thresh", -80f, 0f, -40f, "dB"), new(4, "Release", 10f, 500f, 100f, "ms")],
        ["builtin:eq5"] = [new(4, "Lo-Mid", -12f, 12f, 0f, "dB"), new(7, "Hi-Mid", -12f, 12f, 0f, "dB")],
        ["builtin:hpf"] = [new(0, "Cutoff", 20f, 500f, 80f, "Hz"), new(1, "Slope", 0f, 3f, 1f, "")],
        ["builtin:deesser"] = [new(2, "Thresh", -60f, 0f, -20f, "dB"), new(3, "Reduce", 0f, 100f, 50f, "%")],
        ["builtin:saturation"] = [new(0, "Warmth", 0f, 100f, 50f, "%"), new(1, "Blend", 0f, 100f, 100f, "%")],
        ["builtin:limiter"] = [new(0, "Ceiling", -20f, 0f, -1f, "dB"), new(1, "Release", 10f, 500f, 100f, "ms")],
        ["builtin:fft-noise"] = [new(0, "Reduce", 0f, 100f, 50f, "%"), new(1, "Thresh", -60f, 0f, -40f, "dB")],
        ["builtin:rnnoise"] = [new(0, "Reduce", 0f, 100f, 100f, "%"), new(1, "VAD", 0f, 100f, 0f, "%")],
        ["builtin:speechdenoiser"] = [new(0, "Dry/Wet", 0f, 100f, 100f, "%"), new(1, "Atten", 0f, 100f, 100f, "dB")],
        ["builtin:voice-gate"] = [new(0, "Thresh", 0f, 100f, 50f, "%"), new(2, "Release", 10f, 500f, 150f, "ms")],
        ["builtin:reverb"] = [new(0, "D/W", 0f, 100f, 30f, "%"), new(1, "Decay", 0.1f, 10f, 1.5f, "s")],
        ["builtin:output-send"] = [new(0, "Send Mode", 0f, 2f, 2f, "")],
        ["builtin:signal-generator"] = [new(2, "Slot 1", -60f, 12f, -12f, "dB"), new(22, "Slot 2", -60f, 12f, -12f, "dB")],
    };

    /// <summary>
    /// Gets the elevated parameter definitions for a plugin, or null if not found.
    /// </summary>
    public static ParamDef[]? GetElevations(string pluginId)
    {
        return PluginElevations.TryGetValue(pluginId, out var defs) ? defs : null;
    }
}
