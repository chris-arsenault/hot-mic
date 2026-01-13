using HotMic.Vst3;

namespace HotMic.App.Models;

public enum PluginCategory
{
    Dynamics,
    Eq,
    NoiseReduction,
    Analysis,
    AiMl,
    Effects,
    Vst
}

public sealed record PluginChoice
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsVst3 { get; init; }
    public string Path { get; init; } = string.Empty;
    public VstPluginFormat Format { get; init; } = VstPluginFormat.Vst3;
    public PluginCategory Category { get; init; } = PluginCategory.Effects;
    public string Description { get; init; } = string.Empty;
}
