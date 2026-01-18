using System.Threading;
using HotMic.Core.Analysis;
using HotMic.Core.Engine;

namespace HotMic.Core.Plugins;

public sealed class PluginChain
{
    private PluginSlot?[] _slots;
    private PluginIdIndexMap _idIndexMap = PluginIdIndexMap.Empty;
    private int _nextInstanceId;
    private readonly int _sampleRate;
    private readonly int _blockSize;
    private readonly int _analysisSignalCapacity;
    private readonly int[] _lastProducerScratch;
    private AnalysisSignalBus? _analysisSignalBus;
    private AnalysisSignalMask[] _downstreamRequiredSignals = Array.Empty<AnalysisSignalMask>();
    private AnalysisSignalMask _visualRequestedSignals;
    private AnalysisCaptureLink? _analysisCapture;
    private int _inputStageIndex = -1;

    public PluginChain(int sampleRate, int blockSize, int initialCapacity = 0)
    {
        _sampleRate = sampleRate;
        _blockSize = blockSize;
        _slots = initialCapacity > 0 ? new PluginSlot?[initialCapacity] : [];
        _analysisSignalCapacity = Math.Max(sampleRate * 2, blockSize * 4);
        _lastProducerScratch = new int[(int)AnalysisSignalId.Count];
        RebuildIdIndexMap(_slots);
        RebuildAnalysisSignalBus();
        RebuildDownstreamRequiredSignals(_slots);
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

    public void SetAnalysisCaptureLink(AnalysisCaptureLink? captureLink)
    {
        Volatile.Write(ref _analysisCapture, captureLink);
    }

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
        RebuildAnalysisSignalBus();
        RebuildDownstreamRequiredSignals(newSlots);
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
        RebuildAnalysisSignalBus();
        RebuildDownstreamRequiredSignals(newSlots);
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
        RebuildAnalysisSignalBus();
        RebuildDownstreamRequiredSignals(newSlots);
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
        RebuildAnalysisSignalBus();
        RebuildDownstreamRequiredSignals(newSlots);
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
        RebuildAnalysisSignalBus();
        RebuildDownstreamRequiredSignals(newSlots);
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
        RebuildAnalysisSignalBus();
        RebuildDownstreamRequiredSignals(newSlots);
        RebuildInputStageIndex(newSlots);
    }

    public void ReplaceAll(PluginSlot?[] newSlots)
    {
        Interlocked.Exchange(ref _slots, newSlots);
        UpdateNextInstanceId(newSlots);
        RebuildIdIndexMap(newSlots);
        RebuildAnalysisSignalBus();
        RebuildDownstreamRequiredSignals(newSlots);
        RebuildInputStageIndex(newSlots);
    }

    public PluginSlot?[] DetachAll()
    {
        var oldSlots = Volatile.Read(ref _slots);
        Interlocked.Exchange(ref _slots, Array.Empty<PluginSlot?>());
        RebuildIdIndexMap(Array.Empty<PluginSlot?>());
        RebuildAnalysisSignalBus();
        RebuildDownstreamRequiredSignals(Array.Empty<PluginSlot?>());
        RebuildInputStageIndex(Array.Empty<PluginSlot?>());
        return oldSlots;
    }

    public void SetVisualRequestedSignals(AnalysisSignalMask requestedSignals)
    {
        Volatile.Write(ref _visualRequestedSignals, requestedSignals);
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
        var bus = Volatile.Read(ref _analysisSignalBus);
        var downstreamRequiredSignals = UpdateDownstreamRequiredSignals(slots);
        var visualRequestedSignals = Volatile.Read(ref _visualRequestedSignals);
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

            bool hasRequiredSignals = true;
            if (plugin is IAnalysisSignalConsumer consumer)
            {
                hasRequiredSignals = HasRequiredSignals(lastProducer, consumer.RequiredSignals);
                consumer.SetAnalysisSignalsAvailable(plugin.IsBypassed ? true : hasRequiredSignals);
            }

            bool isActive = !plugin.IsBypassed && hasRequiredSignals;

            bool shouldCaptureDelta = isActive;

            if (shouldCaptureDelta)
            {
                delta.ProcessPre(buffer);
            }

            AnalysisSignalMask requestedSignals = AnalysisSignalMask.None;
            if (isActive)
            {
                if (i < downstreamRequiredSignals.Length)
                {
                    requestedSignals = downstreamRequiredSignals[i] | visualRequestedSignals;
                }
                else
                {
                    requestedSignals = visualRequestedSignals;
                }
            }

            AnalysisSignalMask producedSignals = AnalysisSignalMask.None;
            if (plugin is IAnalysisSignalProducer producer && isActive)
            {
                producedSignals = producer.ProducedSignals & requestedSignals;
            }

            AnalysisSignalMask blockedSignals = AnalysisSignalMask.None;
            if (plugin is IAnalysisSignalBlocker blocker && isActive)
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
                    Volatile.Read(ref _analysisCapture),
                    bus,
                    lastProducer,
                    producedSignals,
                    requestedSignals);
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

            if (producedSignals != AnalysisSignalMask.None)
            {
                UpdateLastProducer(lastProducer, producedSignals, i);
            }

            if (blockedSignals != AnalysisSignalMask.None)
            {
                ApplySignalBlocks(lastProducer, blockedSignals);
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

    private void RebuildAnalysisSignalBus()
    {
        var slots = Volatile.Read(ref _slots);
        bool hasProducer = false;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i]?.Plugin is IAnalysisSignalProducer)
            {
                hasProducer = true;
                break;
            }
        }

        var newBus = hasProducer ? new AnalysisSignalBus(slots.Length, _analysisSignalCapacity) : null;
        Interlocked.Exchange(ref _analysisSignalBus, newBus);
    }

    private void RebuildDownstreamRequiredSignals(PluginSlot?[] slots)
    {
        if (slots.Length == 0)
        {
            Interlocked.Exchange(ref _downstreamRequiredSignals, Array.Empty<AnalysisSignalMask>());
            return;
        }

        var required = new AnalysisSignalMask[slots.Length];
        AnalysisSignalMask downstream = AnalysisSignalMask.None;

        for (int i = slots.Length - 1; i >= 0; i--)
        {
            required[i] = downstream;

            var slot = slots[i];
            if (slot is null || slot.Plugin.IsBypassed)
            {
                continue;
            }

            if (slot.Plugin is IAnalysisSignalConsumer consumer)
            {
                downstream |= consumer.RequiredSignals;
            }

            if (slot.Plugin is IAnalysisSignalBlocker blocker && blocker.BlockedSignals != AnalysisSignalMask.None)
            {
                downstream &= ~blocker.BlockedSignals;
            }
        }

        Interlocked.Exchange(ref _downstreamRequiredSignals, required);
    }

    private AnalysisSignalMask[] UpdateDownstreamRequiredSignals(PluginSlot?[] slots)
    {
        var required = Volatile.Read(ref _downstreamRequiredSignals);
        if (required.Length != slots.Length)
        {
            return required;
        }

        AnalysisSignalMask downstream = AnalysisSignalMask.None;
        for (int i = slots.Length - 1; i >= 0; i--)
        {
            required[i] = downstream;

            var slot = slots[i];
            if (slot is null || slot.Plugin.IsBypassed)
            {
                continue;
            }

            if (slot.Plugin is IAnalysisSignalConsumer consumer)
            {
                downstream |= consumer.RequiredSignals;
            }

            if (slot.Plugin is IAnalysisSignalBlocker blocker && blocker.BlockedSignals != AnalysisSignalMask.None)
            {
                downstream &= ~blocker.BlockedSignals;
            }
        }

        return required;
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

    private static void UpdateLastProducer(int[] lastProducer, AnalysisSignalMask producedSignals, int slotIndex)
    {
        if (producedSignals == AnalysisSignalMask.None)
        {
            return;
        }

        int mask = (int)producedSignals;
        int count = lastProducer.Length;
        for (int i = 0; i < count; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                lastProducer[i] = slotIndex;
            }
        }
    }

    private static void ApplySignalBlocks(int[] lastProducer, AnalysisSignalMask blockedSignals)
    {
        if (blockedSignals == AnalysisSignalMask.None)
        {
            return;
        }

        int mask = (int)blockedSignals;
        int count = lastProducer.Length;
        for (int i = 0; i < count; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                lastProducer[i] = -1;
            }
        }
    }

    private static bool HasRequiredSignals(int[] lastProducer, AnalysisSignalMask requiredSignals)
    {
        if (requiredSignals == AnalysisSignalMask.None)
        {
            return true;
        }

        int mask = (int)requiredSignals;
        int count = lastProducer.Length;
        for (int i = 0; i < count; i++)
        {
            if ((mask & (1 << i)) != 0 && lastProducer[i] < 0)
            {
                return false;
            }
        }

        return true;
    }
}
