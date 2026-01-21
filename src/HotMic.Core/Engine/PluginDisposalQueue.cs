using System.Collections.Generic;
using HotMic.Core.Plugins;

namespace HotMic.Core.Engine;

internal sealed class PluginDisposalQueue
{
    private readonly List<PendingPluginDisposal> _pending = new();
    private readonly List<IPlugin> _disposeBuffer = new();
    private readonly object _lock = new();

    public void Queue(IPlugin plugin, long targetCallbackCount, bool outputActive)
    {
        if (!outputActive)
        {
            plugin.Dispose();
            return;
        }

        lock (_lock)
        {
            _pending.Add(new PendingPluginDisposal(plugin, targetCallbackCount));
        }
    }

    public void Drain(long callbackCount, bool force)
    {
        lock (_lock)
        {
            if (_pending.Count == 0)
            {
                return;
            }

            long effectiveCount = force ? long.MaxValue : callbackCount;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var pending = _pending[i];
                if (effectiveCount >= pending.TargetCallbackCount)
                {
                    _disposeBuffer.Add(pending.Plugin);
                    _pending.RemoveAt(i);
                }
            }
        }

        for (int i = 0; i < _disposeBuffer.Count; i++)
        {
            _disposeBuffer[i].Dispose();
        }
        _disposeBuffer.Clear();
    }

    public void DrainAll(long callbackCount)
    {
        Drain(callbackCount, force: true);
    }

    private sealed class PendingPluginDisposal
    {
        public PendingPluginDisposal(IPlugin plugin, long targetCallbackCount)
        {
            Plugin = plugin;
            TargetCallbackCount = targetCallbackCount;
        }

        public IPlugin Plugin { get; }
        public long TargetCallbackCount { get; }
    }
}
