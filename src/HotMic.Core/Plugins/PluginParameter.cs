namespace HotMic.Core.Plugins;

public sealed record PluginParameter
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public float MinValue { get; init; }
    public float MaxValue { get; init; }
    public float DefaultValue { get; init; }
    public string Unit { get; init; } = string.Empty;
    public Func<float, string>? FormatValue { get; init; }
}
