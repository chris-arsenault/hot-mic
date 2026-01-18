namespace HotMic.Core.Plugins;

public interface IContextualPlugin : IPlugin
{
    void Process(Span<float> buffer, in PluginProcessContext context);
}
