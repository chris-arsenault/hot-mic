using System.Threading;
using HotMic.Core.Engine;

namespace HotMic.Core.Plugins;

public sealed class PluginChain
{
    private PluginSlot?[] _slots;
    private PluginIdIndexMap _idIndexMap = PluginIdIndexMap.Empty;
    private int _nextInstanceId;
    private readonly int _sampleRate;
    private readonly int _blockSize;
    private readonly int _sidechainCapacity;
    private readonly int[] _lastProducerScratch;
    private SidechainBus? _sidechainBus;
    private int _inputStageIndex = -1;

    public PluginChain(int sampleRate, int blockSize, int initialCapacity = 0)
    {
        _sampleRate = sampleRate;
        _blockSize = blockSize;
        _slots = initialCapacity > 0 ? new PluginSlot?[initialCapacity] : [];
        _sidechainCapacity = Math.Max(sampleRate * 2, blockSize * 4);
        _lastProducerScratch = new int[(int)SidechainSignalId.Count];
        RebuildIdIndexMap(_slots);
        RebuildSidechainBus();
        RebuildInputStageIndex(_slots);
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

    public PluginSlot?[] GetSnapshot()
    {
        return Volatile.Read(ref _slots);
    }

    public int InputStageIndex => Volatile.Read(ref _inputStageIndex);

    public bool TryGetSlotById(int instanceId, out PluginSlot? slot, out int index)
    {
        slot = null;
        index = -1;

        var map = Volatile.Read(ref _idIndexMap);
        if (!map.TryGetIndex(instanceId, out int foundIndex))
        {
            return false;
        }

        var slots = Volatile.Read(ref _slots);
        if ((uint)foundIndex >= (uint)slots.Length)
        {
            return false;
        }

        var candidate = slots[foundIndex];
        if (candidate is null || candidate.InstanceId != instanceId)
        {
            return false;
        }

        slot = candidate;
        index = foundIndex;
        return true;
    }

    public bool TryHandleCommand(int instanceId, PluginCommandType command)
    {
        if (!TryGetSlotById(instanceId, out var slot, out _))
        {
            return false;
        }

        if (slot?.Plugin is IPluginCommandHandler handler)
        {
            handler.HandleCommand(command);
            return true;
        }

        return false;
    }

    public int AddSlot(IPlugin? plugin = null, int instanceId = 0)
    {
        var oldSlots = Volatile.Read(ref _slots);
        var newSlots = new PluginSlot?[oldSlots.Length + 1];
        Array.Copy(oldSlots, newSlots, oldSlots.Length);

        int createdId = 0;
        if (plugin is not null)
        {
            var slot = CreateSlot(plugin, instanceId);
            newSlots[^1] = slot;
            createdId = slot.InstanceId;
        }

        Interlocked.Exchange(ref _slots, newSlots);
        RebuildIdIndexMap(newSlots);
        RebuildSidechainBus();
        RebuildInputStageIndex(newSlots);
        return createdId;
    }

    public int InsertSlot(int index, IPlugin? plugin = null, int instanceId = 0)
    {
        var oldSlots = Volatile.Read(ref _slots);
        if (index < 0)
        {
            index = 0;
        }
        else if (index > oldSlots.Length)
        {
            index = oldSlots.Length;
        }

        var newSlots = new PluginSlot?[oldSlots.Length + 1];
        int createdId = 0;

        for (int i = 0, j = 0; i < newSlots.Length; i++)
        {
            if (i == index)
            {
                if (plugin is not null)
                {
                    var slot = CreateSlot(plugin, instanceId);
                    newSlots[i] = slot;
                    createdId = slot.InstanceId;
                }
                j = i;
                continue;
            }

            newSlots[i] = oldSlots[j++];
        }

        Interlocked.Exchange(ref _slots, newSlots);
        RebuildIdIndexMap(newSlots);
        RebuildSidechainBus();
        RebuildInputStageIndex(newSlots);
        return createdId;
    }

    public PluginSlot? RemoveSlot(int index)
    {
        var oldSlots = Volatile.Read(ref _slots);
        if ((uint)index >= (uint)oldSlots.Length || oldSlots.Length == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var removed = oldSlots[index];
        var newSlots = new PluginSlot?[oldSlots.Length - 1];
        for (int i = 0, j = 0; i < oldSlots.Length; i++)
        {
            if (i != index)
            {
                newSlots[j++] = oldSlots[i];
            }
        }

        Interlocked.Exchange(ref _slots, newSlots);
        RebuildIdIndexMap(newSlots);
        RebuildSidechainBus();
        RebuildInputStageIndex(newSlots);
        return removed;
    }

    public PluginSlot? SetSlot(int index, IPlugin? plugin, int instanceId = 0)
    {
        var oldSlots = Volatile.Read(ref _slots);
        if ((uint)index >= (uint)oldSlots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var newSlots = new PluginSlot?[oldSlots.Length];
        Array.Copy(oldSlots, newSlots, oldSlots.Length);

        var previous = newSlots[index];
        if (plugin is null)
        {
            newSlots[index] = null;
        }
        else
        {
            newSlots[index] = CreateSlot(plugin, instanceId);
        }

        Interlocked.Exchange(ref _slots, newSlots);
        RebuildIdIndexMap(newSlots);
        RebuildSidechainBus();
        RebuildInputStageIndex(newSlots);
        return previous;
    }

    public void EnsureCapacity(int count)
    {
        var slots = Volatile.Read(ref _slots);
        if (slots.Length >= count)
        {
            return;
        }

        var newSlots = new PluginSlot?[count];
        Array.Copy(slots, newSlots, slots.Length);
        Interlocked.Exchange(ref _slots, newSlots);
        RebuildIdIndexMap(newSlots);
        RebuildSidechainBus();
        RebuildInputStageIndex(newSlots);
    }

    public void Swap(int indexA, int indexB)
    {
        var oldSlots = Volatile.Read(ref _slots);
        if ((uint)indexA >= (uint)oldSlots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(indexA));
        }
        if ((uint)indexB >= (uint)oldSlots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(indexB));
        }

        var newSlots = new PluginSlot?[oldSlots.Length];
        Array.Copy(oldSlots, newSlots, oldSlots.Length);
        (newSlots[indexA], newSlots[indexB]) = (newSlots[indexB], newSlots[indexA]);

        Interlocked.Exchange(ref _slots, newSlots);
        RebuildIdIndexMap(newSlots);
        RebuildSidechainBus();
        RebuildInputStageIndex(newSlots);
    }

    public void ReplaceAll(PluginSlot?[] newSlots)
    {
        Interlocked.Exchange(ref _slots, newSlots);
        UpdateNextInstanceId(newSlots);
        RebuildIdIndexMap(newSlots);
        RebuildSidechainBus();
        RebuildInputStageIndex(newSlots);
    }

    public PluginSlot?[] DetachAll()
    {
        var oldSlots = Volatile.Read(ref _slots);
        Interlocked.Exchange(ref _slots, Array.Empty<PluginSlot?>());
        RebuildIdIndexMap(Array.Empty<PluginSlot?>());
        RebuildSidechainBus();
        RebuildInputStageIndex(Array.Empty<PluginSlot?>());
        return oldSlots;
    }

    public int Process(Span<float> buffer, long sampleClock, int channelId, IRoutingContext routingContext)
    {
        return ProcessInternal(buffer, sampleClock, channelId, routingContext, splitIndex: -1, onSplit: null);
    }

    public int ProcessWithSplit(Span<float> buffer, long sampleClock, int channelId, IRoutingContext routingContext, int splitIndex, Action<Span<float>> onSplit)
    {
        ArgumentNullException.ThrowIfNull(onSplit);

        return ProcessInternal(buffer, sampleClock, channelId, routingContext, splitIndex, onSplit);
    }

    private int ProcessInternal(Span<float> buffer, long sampleClock, int channelId, IRoutingContext routingContext, int splitIndex, Action<Span<float>>? onSplit)
    {
        var slots = Volatile.Read(ref _slots);
        var bus = Volatile.Read(ref _sidechainBus);
        int cumulativeLatency = 0;
        var lastProducer = _lastProducerScratch;
        for (int s = 0; s < lastProducer.Length; s++)
        {
            lastProducer[s] = -1;
        }

        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot is null)
            {
                continue;
            }

            var plugin = slot.Plugin;
            var delta = slot.Delta;
            long sampleTime = sampleClock - cumulativeLatency;

            bool hasRequiredSidechain = true;
            if (plugin is ISidechainConsumer consumer)
            {
                hasRequiredSidechain = HasRequiredSignals(lastProducer, consumer.RequiredSignals);
                consumer.SetSidechainAvailable(plugin.IsBypassed ? true : hasRequiredSidechain);
            }

            bool isActive = !plugin.IsBypassed && hasRequiredSidechain;

            bool shouldCaptureDelta = isActive;

            if (shouldCaptureDelta)
            {
                delta.ProcessPre(buffer);
            }

            SidechainSignalMask producedSignals = SidechainSignalMask.None;
            if (plugin is ISidechainProducer producer && isActive)
            {
                producedSignals = producer.ProducedSignals;
            }

            SidechainSignalMask blockedSignals = SidechainSignalMask.None;
            if (plugin is ISidechainSignalBlocker blocker && isActive)
            {
                blockedSignals = blocker.BlockedSignals;
            }

            if (isActive)
            {
                var context = new PluginProcessContext(
                    _sampleRate,
                    _blockSize,
                    sampleClock,
                    sampleTime,
                    i,
                    cumulativeLatency,
                    channelId,
                    routingContext,
                    bus,
                    lastProducer[(int)SidechainSignalId.SpeechPresence],
                    lastProducer[(int)SidechainSignalId.VoicedProbability],
                    lastProducer[(int)SidechainSignalId.UnvoicedEnergy],
                    lastProducer[(int)SidechainSignalId.SibilanceEnergy],
                    producedSignals);
                plugin.Process(buffer, context);
            }

            if (shouldCaptureDelta)
            {
                delta.ProcessPost(buffer);
            }

            slot.Meter.Process(buffer, isActive);

            if (isActive)
            {
                cumulativeLatency += Math.Max(0, plugin.LatencySamples);
            }

            if (producedSignals != SidechainSignalMask.None)
            {
                UpdateLastProducer(lastProducer, producedSignals, i);
            }

            if (blockedSignals != SidechainSignalMask.None)
            {
                ApplySidechainBlocks(lastProducer, blockedSignals);
            }

            if (onSplit is not null && i == splitIndex)
            {
                onSplit(buffer);
            }
        }

        return cumulativeLatency;
    }

    public void ProcessMeters(Span<float> buffer)
    {
        var slots = Volatile.Read(ref _slots);
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot is not null)
            {
                slot.Meter.Process(buffer, trackClip: false);
            }
        }
    }

    private void RebuildSidechainBus()
    {
        var slots = Volatile.Read(ref _slots);
        bool hasProducer = false;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i]?.Plugin is ISidechainProducer)
            {
                hasProducer = true;
                break;
            }
        }

        var newBus = hasProducer ? new SidechainBus(slots.Length, _sidechainCapacity) : null;
        Interlocked.Exchange(ref _sidechainBus, newBus);
    }

    private void RebuildInputStageIndex(PluginSlot?[] slots)
    {
        int index = -1;
        int priority = int.MinValue;
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot?.Plugin is not IChannelInputPlugin input)
            {
                continue;
            }

            int inputPriority = (int)input.InputKind;
            if (index < 0 || inputPriority > priority)
            {
                index = i;
                priority = inputPriority;
            }
        }

        Volatile.Write(ref _inputStageIndex, index);
    }

    private PluginSlot CreateSlot(IPlugin plugin, int instanceId)
    {
        int resolvedId = instanceId > 0 ? instanceId : NextInstanceId();
        if (resolvedId > _nextInstanceId)
        {
            _nextInstanceId = resolvedId;
        }

        return new PluginSlot(resolvedId, plugin, _sampleRate);
    }

    private int NextInstanceId()
    {
        _nextInstanceId++;
        if (_nextInstanceId <= 0)
        {
            _nextInstanceId = 1;
        }
        return _nextInstanceId;
    }

    private void UpdateNextInstanceId(PluginSlot?[] slots)
    {
        int max = _nextInstanceId;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] is { } slot && slot.InstanceId > max)
            {
                max = slot.InstanceId;
            }
        }
        _nextInstanceId = max;
    }

    private void RebuildIdIndexMap(PluginSlot?[] slots)
    {
        var map = PluginIdIndexMap.Build(slots);
        Interlocked.Exchange(ref _idIndexMap, map);
    }

    private sealed class PluginIdIndexMap
    {
        public static readonly PluginIdIndexMap Empty = new(Array.Empty<int>(), Array.Empty<int>(), 0);

        private readonly int[] _keys;
        private readonly int[] _values;
        private readonly int _mask;

        private PluginIdIndexMap(int[] keys, int[] values, int mask)
        {
            _keys = keys;
            _values = values;
            _mask = mask;
        }

        public static PluginIdIndexMap Build(PluginSlot?[] slots)
        {
            int count = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] is not null)
                {
                    count++;
                }
            }

            if (count == 0)
            {
                return Empty;
            }

            int capacity = 1;
            while (capacity < count * 2)
            {
                capacity <<= 1;
            }

            var keys = new int[capacity];
            var values = new int[capacity];
            int mask = capacity - 1;

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] is not { } slot)
                {
                    continue;
                }

                int id = slot.InstanceId;
                if (id <= 0)
                {
                    continue;
                }

                int pos = id & mask;
                while (keys[pos] != 0)
                {
                    pos = (pos + 1) & mask;
                }

                keys[pos] = id;
                values[pos] = i + 1;
            }

            return new PluginIdIndexMap(keys, values, mask);
        }

        public bool TryGetIndex(int id, out int index)
        {
            index = -1;

            if (id <= 0 || _keys.Length == 0)
            {
                return false;
            }

            int pos = id & _mask;
            for (int i = 0; i < _keys.Length; i++)
            {
                int key = _keys[pos];
                if (key == id)
                {
                    index = _values[pos] - 1;
                    return index >= 0;
                }
                if (key == 0)
                {
                    return false;
                }
                pos = (pos + 1) & _mask;
            }

            return false;
        }
    }

    private static void UpdateLastProducer(int[] lastProducer, SidechainSignalMask producedSignals, int slotIndex)
    {
        if ((producedSignals & SidechainSignalMask.SpeechPresence) != 0)
        {
            lastProducer[(int)SidechainSignalId.SpeechPresence] = slotIndex;
        }
        if ((producedSignals & SidechainSignalMask.VoicedProbability) != 0)
        {
            lastProducer[(int)SidechainSignalId.VoicedProbability] = slotIndex;
        }
        if ((producedSignals & SidechainSignalMask.UnvoicedEnergy) != 0)
        {
            lastProducer[(int)SidechainSignalId.UnvoicedEnergy] = slotIndex;
        }
        if ((producedSignals & SidechainSignalMask.SibilanceEnergy) != 0)
        {
            lastProducer[(int)SidechainSignalId.SibilanceEnergy] = slotIndex;
        }
    }

    private static void ApplySidechainBlocks(int[] lastProducer, SidechainSignalMask blockedSignals)
    {
        if ((blockedSignals & SidechainSignalMask.SpeechPresence) != 0)
        {
            lastProducer[(int)SidechainSignalId.SpeechPresence] = -1;
        }
        if ((blockedSignals & SidechainSignalMask.VoicedProbability) != 0)
        {
            lastProducer[(int)SidechainSignalId.VoicedProbability] = -1;
        }
        if ((blockedSignals & SidechainSignalMask.UnvoicedEnergy) != 0)
        {
            lastProducer[(int)SidechainSignalId.UnvoicedEnergy] = -1;
        }
        if ((blockedSignals & SidechainSignalMask.SibilanceEnergy) != 0)
        {
            lastProducer[(int)SidechainSignalId.SibilanceEnergy] = -1;
        }
    }

    private static bool HasRequiredSignals(int[] lastProducer, SidechainSignalMask requiredSignals)
    {
        if (requiredSignals == SidechainSignalMask.None)
        {
            return true;
        }
        if ((requiredSignals & SidechainSignalMask.SpeechPresence) != 0
            && lastProducer[(int)SidechainSignalId.SpeechPresence] < 0)
        {
            return false;
        }
        if ((requiredSignals & SidechainSignalMask.VoicedProbability) != 0
            && lastProducer[(int)SidechainSignalId.VoicedProbability] < 0)
        {
            return false;
        }
        if ((requiredSignals & SidechainSignalMask.UnvoicedEnergy) != 0
            && lastProducer[(int)SidechainSignalId.UnvoicedEnergy] < 0)
        {
            return false;
        }
        if ((requiredSignals & SidechainSignalMask.SibilanceEnergy) != 0
            && lastProducer[(int)SidechainSignalId.SibilanceEnergy] < 0)
        {
            return false;
        }
        return true;
    }
}
