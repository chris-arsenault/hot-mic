namespace HotMic.Common.Configuration;

public sealed class AppConfig
{
    public AudioSettingsConfig AudioSettings { get; set; } = new();

    public List<ChannelConfig> Channels { get; set; } = new();

    public UiConfig Ui { get; set; } = new();

    public List<string> Vst2SearchPaths { get; set; } = new();

    public List<string> Vst3SearchPaths { get; set; } = new();

    public bool EnableVstPlugins { get; set; } = true;

    public MidiConfig Midi { get; set; } = new();
}
