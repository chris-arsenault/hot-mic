using HotMic.Core.Engine;

namespace HotMic.Core.Plugins;

public interface IPluginCommandHandler
{
    void HandleCommand(PluginCommandType command);
}
