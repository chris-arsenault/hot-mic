namespace HotMic.Common.Configuration;

public sealed class AudioSettingsConfig
{
    public string InputDevice1Id { get; set; } = string.Empty;
    public string InputDevice2Id { get; set; } = string.Empty;
    public string OutputDeviceId { get; set; } = string.Empty;
    public string MonitorOutputDeviceId { get; set; } = string.Empty;
    public int SampleRate { get; set; } = 48000;
    public int BufferSize { get; set; } = 256;
}
