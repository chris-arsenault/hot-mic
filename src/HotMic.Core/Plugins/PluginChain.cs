using System.Threading;
using HotMic.Core.Dsp;
using HotMic.Core.Metering;

namespace HotMic.Core.Plugins;

public sealed class PluginChain
{
    private IPlugin?[] _slots;
    private MeterProcessor[] _meters;
    private SpectralDeltaProcessor[] _deltaProcessors;
    private readonly int _sampleRate;

    public PluginChain(int sampleRate, int initialCapacity = 0)
    {
        _sampleRate = sampleRate;
        _slots = initialCapacity > 0 ? new IPlugin?[initialCapacity] : [];
        _meters = initialCapacity > 0 ? CreateMeters(initialCapacity) : [];
        _deltaProcessors = initialCapacity > 0 ? CreateDeltaProcessors(initialCapacity) : [];
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

    public MeterProcessor[] GetMeterSnapshot()
    {
        return Volatile.Read(ref _meters);
    }

    public SpectralDeltaProcessor[] GetDeltaSnapshot()
    {
        return Volatile.Read(ref _deltaProcessors);
    }

    public void AddSlot(IPlugin? plugin = null)
    {
        var oldSlots = Volatile.Read(ref _slots);
        var newSlots = new IPlugin?[oldSlots.Length + 1];
        Array.Copy(oldSlots, newSlots, oldSlots.Length);
        newSlots[^1] = plugin;
        var newMeters = CopyAndResizeMeters(newSlots.Length);
        var newDeltas = CopyAndResizeDeltaProcessors(newSlots.Length);
        Interlocked.Exchange(ref _deltaProcessors, newDeltas);
        Interlocked.Exchange(ref _meters, newMeters);
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
        var oldMeters = Volatile.Read(ref _meters);
        var oldDeltas = Volatile.Read(ref _deltaProcessors);
        var newMeters = CreateMeters(newSlots.Length);
        var newDeltas = CreateDeltaProcessors(newSlots.Length);
        for (int i = 0, j = 0; i < oldSlots.Length; i++)
        {
            if (i != index)
            {
                newSlots[j++] = oldSlots[i];
            }
        }
        for (int i = 0, j = 0; i < oldMeters.Length; i++)
        {
            if (i != index && j < newMeters.Length)
            {
                newMeters[j++] = oldMeters[i];
            }
        }
        for (int i = 0, j = 0; i < oldDeltas.Length; i++)
        {
            if (i != index && j < newDeltas.Length)
            {
                newDeltas[j++] = oldDeltas[i];
            }
        }
        Interlocked.Exchange(ref _deltaProcessors, newDeltas);
        Interlocked.Exchange(ref _meters, newMeters);
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
        var meters = Volatile.Read(ref _meters);
        if ((uint)index < (uint)meters.Length)
        {
            meters[index] = new MeterProcessor(_sampleRate);
        }
        var deltas = Volatile.Read(ref _deltaProcessors);
        if ((uint)index < (uint)deltas.Length)
        {
            deltas[index].Reset();
        }
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
        var newMeters = CopyAndResizeMeters(count);
        var newDeltas = CopyAndResizeDeltaProcessors(count);
        Interlocked.Exchange(ref _deltaProcessors, newDeltas);
        Interlocked.Exchange(ref _meters, newMeters);
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
        var meters = Volatile.Read(ref _meters);
        if ((uint)indexA < (uint)meters.Length && (uint)indexB < (uint)meters.Length)
        {
            (meters[indexA], meters[indexB]) = (meters[indexB], meters[indexA]);
        }
        var deltas = Volatile.Read(ref _deltaProcessors);
        if ((uint)indexA < (uint)deltas.Length && (uint)indexB < (uint)deltas.Length)
        {
            (deltas[indexA], deltas[indexB]) = (deltas[indexB], deltas[indexA]);
        }
    }

    public void ReplaceAll(IPlugin?[] newSlots)
    {
        var newMeters = CreateMeters(newSlots.Length);
        var newDeltas = CreateDeltaProcessors(newSlots.Length);
        Interlocked.Exchange(ref _deltaProcessors, newDeltas);
        Interlocked.Exchange(ref _meters, newMeters);
        Interlocked.Exchange(ref _slots, newSlots);
    }

    public void Process(Span<float> buffer)
    {
        var slots = Volatile.Read(ref _slots);
        var meters = Volatile.Read(ref _meters);
        var deltas = Volatile.Read(ref _deltaProcessors);
        int meterCount = meters.Length;
        int deltaCount = deltas.Length;

        for (int i = 0; i < slots.Length; i++)
        {
            var plugin = slots[i];
            var delta = i < deltaCount ? deltas[i] : null;

            // Short-circuit: only compute FFT if plugin is active and we have signal
            bool shouldCaptureDelta = plugin is not null
                                   && !plugin.IsBypassed
                                   && delta is not null;

            if (shouldCaptureDelta)
            {
                // Check previous meter for signal level (skip FFT if below noise floor)
                if (i > 0 && i - 1 < meterCount)
                {
                    float rms = meters[i - 1].GetRmsLevel();
                    shouldCaptureDelta = rms > 0.001f; // ~-60dB threshold
                }
            }

            if (shouldCaptureDelta)
            {
                delta!.ProcessPre(buffer);
            }

            if (plugin is not null && !plugin.IsBypassed)
            {
                plugin.Process(buffer);
            }

            if (shouldCaptureDelta)
            {
                delta!.ProcessPost(buffer);
            }

            if (plugin is not null && i < meterCount)
            {
                meters[i].Process(buffer);
            }
        }
    }

    public void ProcessMeters(Span<float> buffer)
    {
        var slots = Volatile.Read(ref _slots);
        var meters = Volatile.Read(ref _meters);
        int count = Math.Min(slots.Length, meters.Length);
        for (int i = 0; i < count; i++)
        {
            if (slots[i] is not null)
            {
                meters[i].Process(buffer);
            }
        }
    }

    private MeterProcessor[] CopyAndResizeMeters(int count)
    {
        if (count <= 0)
        {
            return [];
        }

        var newMeters = CreateMeters(count);
        var oldMeters = Volatile.Read(ref _meters);
        int copyCount = Math.Min(oldMeters.Length, newMeters.Length);
        if (copyCount > 0)
        {
            Array.Copy(oldMeters, newMeters, copyCount);
        }
        return newMeters;
    }

    private MeterProcessor[] CreateMeters(int count)
    {
        if (count <= 0)
        {
            return [];
        }

        var meters = new MeterProcessor[count];
        for (int i = 0; i < meters.Length; i++)
        {
            meters[i] = new MeterProcessor(_sampleRate);
        }

        return meters;
    }

    private SpectralDeltaProcessor[] CreateDeltaProcessors(int count)
    {
        if (count <= 0)
        {
            return [];
        }

        var processors = new SpectralDeltaProcessor[count];
        for (int i = 0; i < processors.Length; i++)
        {
            processors[i] = new SpectralDeltaProcessor(_sampleRate);
        }

        return processors;
    }

    private SpectralDeltaProcessor[] CopyAndResizeDeltaProcessors(int count)
    {
        if (count <= 0)
        {
            return [];
        }

        var newProcessors = CreateDeltaProcessors(count);
        var oldProcessors = Volatile.Read(ref _deltaProcessors);
        int copyCount = Math.Min(oldProcessors.Length, newProcessors.Length);
        if (copyCount > 0)
        {
            Array.Copy(oldProcessors, newProcessors, copyCount);
        }
        return newProcessors;
    }
}
