using System.Threading;

namespace HotMic.Core.Plugins;

public sealed class PluginChain
{
    private IPlugin?[] _slots;

    public PluginChain(int initialCapacity = 0)
    {
        _slots = initialCapacity > 0 ? new IPlugin?[initialCapacity] : [];
    }

    public int Count => Volatile.Read(ref _slots).Length;

    public int ActiveCount
    {
        get
        {
            var slots = Volatile.Read(ref _slots);
            int count = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] is not null)
                {
                    count++;
                }
            }
            return count;
        }
    }

    public IPlugin?[] GetSnapshot()
    {
        return Volatile.Read(ref _slots);
    }

    public void AddSlot(IPlugin? plugin = null)
    {
        var oldSlots = Volatile.Read(ref _slots);
        var newSlots = new IPlugin?[oldSlots.Length + 1];
        Array.Copy(oldSlots, newSlots, oldSlots.Length);
        newSlots[^1] = plugin;
        Interlocked.Exchange(ref _slots, newSlots);
    }

    public void RemoveSlot(int index)
    {
        var oldSlots = Volatile.Read(ref _slots);
        if ((uint)index >= (uint)oldSlots.Length || oldSlots.Length == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var newSlots = new IPlugin?[oldSlots.Length - 1];
        for (int i = 0, j = 0; i < oldSlots.Length; i++)
        {
            if (i != index)
            {
                newSlots[j++] = oldSlots[i];
            }
        }
        Interlocked.Exchange(ref _slots, newSlots);
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

    public void EnsureCapacity(int count)
    {
        var slots = Volatile.Read(ref _slots);
        if (slots.Length >= count)
        {
            return;
        }

        var newSlots = new IPlugin?[count];
        Array.Copy(slots, newSlots, slots.Length);
        Interlocked.Exchange(ref _slots, newSlots);
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
