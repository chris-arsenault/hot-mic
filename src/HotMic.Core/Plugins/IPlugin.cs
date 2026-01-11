namespace HotMic.Core.Plugins;

public interface IPlugin : IDisposable
{
    string Id { get; }
    string Name { get; }
    bool IsBypassed { get; set; }
    int LatencySamples { get; }

    IReadOnlyList<PluginParameter> Parameters { get; }

    void Initialize(int sampleRate, int blockSize);
    void Process(Span<float> buffer);
    void SetParameter(int index, float value);

    byte[] GetState();
    void SetState(byte[] state);
}
