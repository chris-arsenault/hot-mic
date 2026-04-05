using System.Collections.ObjectModel;
namespace HotMic.Common.Configuration;

public sealed class AppConfig
{
    public AudioSettingsConfig AudioSettings { get; set; } = new();

    public IList<ChannelConfig> Channels { get; } = new List<ChannelConfig>();

    public UiConfig Ui { get; set; } = new();

    public IList<string> Vst2SearchPaths { get; } = new List<string>();

    public IList<string> Vst3SearchPaths { get; } = new List<string>();

    public bool EnableVstPlugins { get; set; } = true;

    public MidiConfig Midi { get; set; } = new();
}
