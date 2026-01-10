namespace HotMic.Core.Engine;

public enum ParameterType
{
    InputGainDb,
    OutputGainDb,
    Mute,
    Solo,
    PluginBypass,
    PluginParameter
}

public sealed class ParameterChange
{
    public int ChannelId { get; init; }
    public ParameterType Type { get; init; }
    public int PluginIndex { get; init; }
    public int ParameterIndex { get; init; }
    public float Value { get; init; }
}
