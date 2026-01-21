namespace HotMic.Core.Engine;

public enum ParameterType
{
    InputGainDb,
    OutputGainDb,
    Mute,
    Solo,
    PluginBypass,
    PluginParameter,
    PluginCommand
}

public enum PluginCommandType
{
    LearnNoiseProfile,
    ToggleNoiseLearn
}

public sealed class ParameterChange
{
    public int ChannelId { get; init; }
    public ParameterType Type { get; init; }
    public int PluginInstanceId { get; init; }
    public int ParameterIndex { get; init; }
    public float Value { get; init; }
    public PluginCommandType Command { get; init; }
}
