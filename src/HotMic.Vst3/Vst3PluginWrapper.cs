using HotMic.Core.Plugins;

namespace HotMic.Vst3;

public sealed class Vst3PluginWrapper : IPlugin
{
    private readonly Vst3PluginInfo _info;

    public Vst3PluginWrapper(Vst3PluginInfo info)
    {
        _info = info;
    }

    public string Id => $"vst3:{_info.Path}";

    public string Name => _info.Name;

    public bool IsBypassed { get; set; }

    public IReadOnlyList<PluginParameter> Parameters { get; } = Array.Empty<PluginParameter>();

    public void Initialize(int sampleRate, int blockSize)
    {
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed)
        {
            return;
        }
    }

    public void SetParameter(int index, float value)
    {
    }

    public byte[] GetState()
    {
        return Array.Empty<byte>();
    }

    public void SetState(byte[] state)
    {
    }

    public void Dispose()
    {
    }
}
