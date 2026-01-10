using System.Threading;

namespace HotMic.Core.Plugins;

public sealed class PluginChain
{
    private IPlugin?[] _slots;

    public PluginChain(int maxPlugins)
    {
        if (maxPlugins <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPlugins));
        }

        _slots = new IPlugin?[maxPlugins];
    }

    public int MaxPlugins => _slots.Length;

    public IPlugin?[] GetSnapshot()
    {
        return Volatile.Read(ref _slots);
    }

    public bool TryAdd(IPlugin plugin)
    {
        var slots = Volatile.Read(ref _slots);
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] is null)
            {
                slots[i] = plugin;
                return true;
            }
        }

        return false;
    }

    public void SetSlot(int index, IPlugin? plugin)
    {
        var slots = Volatile.Read(ref _slots);
        if ((uint)index >= (uint)slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        slots[index] = plugin;
    }

    public void Swap(int indexA, int indexB)
    {
        var slots = Volatile.Read(ref _slots);
        if ((uint)indexA >= (uint)slots.Length || (uint)indexB >= (uint)slots.Length)
        {
            throw new ArgumentOutOfRangeException();
        }

        (slots[indexA], slots[indexB]) = (slots[indexB], slots[indexA]);
    }

    public void ReplaceAll(IPlugin?[] newSlots)
    {
        if (newSlots.Length != _slots.Length)
        {
            throw new ArgumentException("Slot array length mismatch", nameof(newSlots));
        }

        Interlocked.Exchange(ref _slots, newSlots);
    }

    public void Process(Span<float> buffer)
    {
        var slots = Volatile.Read(ref _slots);
        for (int i = 0; i < slots.Length; i++)
        {
            var plugin = slots[i];
            if (plugin is null || plugin.IsBypassed)
            {
                continue;
            }

            plugin.Process(buffer);
        }
    }
}
