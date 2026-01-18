using HotMic.Common.Configuration;
using HotMic.Core.Presets;

namespace HotMic.Core.Plugins;

/// <summary>
/// Canonical manager for plugin order and container grouping for a single channel.
/// </summary>
public sealed class PluginGraph
{
    private readonly PluginChain _chain;
    private ChannelConfig? _config;
    private readonly Dictionary<int, PluginConfig> _configById = new();
    private readonly Dictionary<int, PluginContainerConfig> _containersById = new();

    /// <summary>
    /// Creates a new graph bound to the provided plugin chain.
    /// </summary>
    /// <param name="chain">The audio plugin chain to keep in sync.</param>
    public PluginGraph(PluginChain chain)
    {
        _chain = chain ?? throw new ArgumentNullException(nameof(chain));
    }

    /// <summary>
    /// Gets the current plugin chain snapshot.
    /// </summary>
    public PluginSlot?[] GetSlotsSnapshot() => _chain.GetSnapshot();

    /// <summary>
    /// Loads plugins from config using the provided slot factory and updates the chain.
    /// Returns true if the config was normalized or modified.
    /// </summary>
    public bool LoadFromConfig(ChannelConfig config, Func<PluginConfig, PluginSlot?> slotFactory)
    {
        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }
        if (slotFactory is null)
        {
            throw new ArgumentNullException(nameof(slotFactory));
        }

        _config = config;

        bool changed = false;
        int nextInstanceId = 0;
        for (int i = 0; i < config.Plugins.Count; i++)
        {
            if (config.Plugins[i].InstanceId > nextInstanceId)
            {
                nextInstanceId = config.Plugins[i].InstanceId;
            }
        }

        var usedIds = new HashSet<int>();
        var slots = new List<PluginSlot>(config.Plugins.Count);
        var orderedConfigs = new List<PluginConfig>(config.Plugins.Count);

        for (int i = 0; i < config.Plugins.Count; i++)
        {
            var pluginConfig = config.Plugins[i];
            if (string.IsNullOrWhiteSpace(pluginConfig.Type))
            {
                changed = true;
                continue;
            }

            int instanceId = pluginConfig.InstanceId;
            if (instanceId <= 0 || usedIds.Contains(instanceId))
            {
                instanceId = ++nextInstanceId;
                pluginConfig.InstanceId = instanceId;
                changed = true;
            }
            usedIds.Add(instanceId);

            var slot = slotFactory(pluginConfig);
            if (slot is null)
            {
                changed = true;
                continue;
            }

            slots.Add(slot);
            orderedConfigs.Add(pluginConfig);
        }

        if (orderedConfigs.Count != config.Plugins.Count)
        {
            config.Plugins = orderedConfigs;
            changed = true;
        }

        _chain.ReplaceAll(slots.ToArray());
        changed |= SyncWithChain(config);
        return changed;
    }

    /// <summary>
    /// Synchronizes config ordering and container state from the current chain snapshot.
    /// Returns true if the config was normalized or modified.
    /// </summary>
    public bool SyncWithChain(ChannelConfig config)
    {
        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        _config = config;
        bool configChanged = SyncConfigOrder();
        bool containersChanged = NormalizeContainers();
        return configChanged || containersChanged;
    }

    /// <summary>
    /// Attempts to resolve a plugin config by instance id.
    /// </summary>
    public bool TryGetPluginConfig(int instanceId, out PluginConfig config)
    {
        config = null!;
        if (instanceId <= 0)
        {
            return false;
        }

        if (_configById.TryGetValue(instanceId, out var found) && found is not null)
        {
            config = found;
            return true;
        }

        if (_config is null)
        {
            return false;
        }

        SyncConfigOrder();
        if (_configById.TryGetValue(instanceId, out var refreshed) && refreshed is not null)
        {
            config = refreshed;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Inserts a plugin into the chain at the requested index and updates config.
    /// </summary>
    public int InsertPlugin(IPlugin plugin, int insertIndex)
    {
        if (plugin is null)
        {
            throw new ArgumentNullException(nameof(plugin));
        }

        int instanceId = _chain.InsertSlot(insertIndex, plugin);
        if (instanceId <= 0)
        {
            return 0;
        }

        SyncConfigOrder();
        NormalizeContainers();
        return instanceId;
    }

    /// <summary>
    /// Inserts a plugin into the specified container at the container-relative index.
    /// </summary>
    public int InsertPluginIntoContainer(IPlugin plugin, int containerId, int containerIndex)
    {
        if (plugin is null)
        {
            throw new ArgumentNullException(nameof(plugin));
        }

        int insertIndex = ResolveContainerInsertIndex(containerId, containerIndex);
        int instanceId = InsertPlugin(plugin, insertIndex);
        if (instanceId <= 0)
        {
            return 0;
        }

        AssignPluginToContainer(instanceId, containerId);
        return instanceId;
    }

    /// <summary>
    /// Removes the plugin with the given instance id from the chain and config.
    /// </summary>
    public bool RemovePlugin(int instanceId, out PluginSlot? removedSlot)
    {
        removedSlot = null;
        if (instanceId <= 0)
        {
            return false;
        }

        if (!_chain.TryGetSlotById(instanceId, out var slot, out int slotIndex) || slot is null)
        {
            return false;
        }

        removedSlot = _chain.RemoveSlot(slotIndex);
        if (_config is null)
        {
            return true;
        }

        _configById.Remove(instanceId);
        for (int i = 0; i < _config.Containers.Count; i++)
        {
            _config.Containers[i].PluginInstanceIds.Remove(instanceId);
        }

        SyncConfigOrder();
        NormalizeContainers();
        return true;
    }

    /// <summary>
    /// Moves the plugin with the given instance id to the requested chain index.
    /// </summary>
    public bool MovePlugin(int instanceId, int targetIndex)
    {
        if (instanceId <= 0)
        {
            return false;
        }

        var slots = _chain.GetSnapshot();
        if (slots.Length == 0)
        {
            return false;
        }

        if (!_chain.TryGetSlotById(instanceId, out var slot, out int fromIndex) || slot is null)
        {
            return false;
        }

        if (targetIndex < 0)
        {
            targetIndex = 0;
        }
        else if (targetIndex >= slots.Length)
        {
            targetIndex = slots.Length - 1;
        }

        if (fromIndex == targetIndex)
        {
            return false;
        }

        var reordered = new List<PluginSlot?>(slots);
        var item = reordered[fromIndex];
        reordered.RemoveAt(fromIndex);
        reordered.Insert(targetIndex, item);

        _chain.ReplaceAll(reordered.ToArray());
        SyncConfigOrder();
        NormalizeContainers();
        return true;
    }

    /// <summary>
    /// Moves the plugin within its container relative order without changing other plugins.
    /// </summary>
    public bool MovePluginWithinContainer(int instanceId, int containerId, int targetIndex)
    {
        if (_config is null || instanceId <= 0 || containerId <= 0)
        {
            return false;
        }

        var container = FindContainer(containerId);
        if (container is null)
        {
            return false;
        }

        var pluginIds = container.PluginInstanceIds;
        int count = pluginIds.Count;
        if (count <= 1)
        {
            return false;
        }

        int fromIndex = pluginIds.IndexOf(instanceId);
        if (fromIndex < 0)
        {
            return false;
        }

        if (targetIndex < 0)
        {
            targetIndex = 0;
        }
        else if (targetIndex >= count)
        {
            targetIndex = count - 1;
        }

        if (fromIndex == targetIndex)
        {
            return false;
        }

        var reorderedIds = new List<int>(pluginIds);
        reorderedIds.RemoveAt(fromIndex);
        reorderedIds.Insert(targetIndex, instanceId);

        var slots = _chain.GetSnapshot();
        var slotById = new Dictionary<int, PluginSlot>(slots.Length);
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] is { } slot)
            {
                slotById[slot.InstanceId] = slot;
            }
        }

        var containerIdSet = new HashSet<int>(pluginIds);
        int reorderPos = 0;
        var newSlots = new PluginSlot?[slots.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot is not null && containerIdSet.Contains(slot.InstanceId))
            {
                int nextId = reorderedIds[reorderPos++];
                newSlots[i] = slotById[nextId];
            }
            else
            {
                newSlots[i] = slot;
            }
        }

        container.PluginInstanceIds = reorderedIds;
        _chain.ReplaceAll(newSlots);
        SyncConfigOrder();
        NormalizeContainers();
        return true;
    }

    /// <summary>
    /// Creates a new container with the provided name and returns its id.
    /// </summary>
    public int CreateContainer(string name)
    {
        if (_config is null)
        {
            throw new InvalidOperationException("Graph is not initialized.");
        }

        int nextId = 1;
        for (int i = 0; i < _config.Containers.Count; i++)
        {
            if (_config.Containers[i].Id >= nextId)
            {
                nextId = _config.Containers[i].Id + 1;
            }
        }

        var container = new PluginContainerConfig
        {
            Id = nextId,
            Name = name ?? string.Empty,
            IsBypassed = false
        };

        _config.Containers.Add(container);
        NormalizeContainers();
        return container.Id;
    }

    /// <summary>
    /// Removes the container with the given id.
    /// </summary>
    public bool RemoveContainer(int containerId)
    {
        if (_config is null || containerId <= 0)
        {
            return false;
        }

        int index = _config.Containers.FindIndex(c => c.Id == containerId);
        if (index < 0)
        {
            return false;
        }

        _config.Containers.RemoveAt(index);
        NormalizeContainers();
        return true;
    }

    /// <summary>
    /// Updates container bypass state and applies it to child plugins.
    /// </summary>
    public bool SetContainerBypass(int containerId, bool bypassed)
    {
        if (_config is null || containerId <= 0)
        {
            return false;
        }

        var container = FindContainer(containerId);
        if (container is null)
        {
            return false;
        }

        bool changed = container.IsBypassed != bypassed;
        container.IsBypassed = bypassed;

        for (int i = 0; i < container.PluginInstanceIds.Count; i++)
        {
            SetPluginBypass(container.PluginInstanceIds[i], bypassed);
        }

        return changed;
    }

    /// <summary>
    /// Assigns a plugin to the requested container (or clears assignment if containerId is 0).
    /// </summary>
    public bool AssignPluginToContainer(int instanceId, int containerId)
    {
        if (_config is null || instanceId <= 0)
        {
            return false;
        }

        bool changed = false;
        for (int i = 0; i < _config.Containers.Count; i++)
        {
            if (_config.Containers[i].PluginInstanceIds.Remove(instanceId))
            {
                changed = true;
            }
        }

        if (containerId > 0 && FindContainer(containerId) is { } container)
        {
            if (!container.PluginInstanceIds.Contains(instanceId))
            {
                container.PluginInstanceIds.Add(instanceId);
                changed = true;
            }

            if (container.IsBypassed)
            {
                SetPluginBypass(instanceId, true);
            }
        }

        if (changed)
        {
            NormalizeContainers();
        }

        return changed;
    }

    /// <summary>
    /// Moves a container's plugins as a block to a new chain index.
    /// </summary>
    public bool MoveContainer(int containerId, int targetIndex)
    {
        if (_config is null || containerId <= 0)
        {
            return false;
        }

        var container = FindContainer(containerId);
        if (container is null || container.PluginInstanceIds.Count == 0)
        {
            return false;
        }

        var slots = _chain.GetSnapshot();
        if (slots.Length == 0)
        {
            return false;
        }

        if (targetIndex < 0)
        {
            targetIndex = 0;
        }
        else if (targetIndex > slots.Length)
        {
            targetIndex = slots.Length;
        }

        var movingIds = new HashSet<int>(container.PluginInstanceIds);
        var moving = new List<PluginSlot?>(container.PluginInstanceIds.Count);
        var remaining = new List<PluginSlot?>(slots.Length);
        int movingBeforeTarget = 0;

        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot is null)
            {
                continue;
            }

            if (movingIds.Contains(slot.InstanceId))
            {
                moving.Add(slot);
                if (i < targetIndex)
                {
                    movingBeforeTarget++;
                }
            }
            else
            {
                remaining.Add(slot);
            }
        }

        if (moving.Count == 0)
        {
            return false;
        }

        int insertIndex = targetIndex - movingBeforeTarget;
        if (insertIndex < 0)
        {
            insertIndex = 0;
        }
        else if (insertIndex > remaining.Count)
        {
            insertIndex = remaining.Count;
        }

        remaining.InsertRange(insertIndex, moving);
        _chain.ReplaceAll(remaining.ToArray());
        SyncConfigOrder();
        NormalizeContainers();
        return true;
    }

    /// <summary>
    /// Returns the ordered list of container configs.
    /// </summary>
    public IReadOnlyList<PluginContainerConfig> GetContainers()
    {
        IReadOnlyList<PluginContainerConfig>? containers = _config?.Containers;
        return containers ?? Array.Empty<PluginContainerConfig>();
    }

    /// <summary>
    /// Updates a plugin bypass state in both chain and config.
    /// </summary>
    public bool SetPluginBypass(int instanceId, bool bypassed)
    {
        if (instanceId <= 0)
        {
            return false;
        }

        if (_chain.TryGetSlotById(instanceId, out var slot, out _) && slot is not null)
        {
            slot.Plugin.IsBypassed = bypassed;
        }

        if (TryGetPluginConfig(instanceId, out var config))
        {
            config.IsBypassed = bypassed;
        }

        return true;
    }

    /// <summary>
    /// Updates a plugin parameter value in config.
    /// </summary>
    public void SetPluginParameter(int instanceId, string parameterName, float value)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
        {
            return;
        }

        if (TryGetPluginConfig(instanceId, out var config))
        {
            config.Parameters[parameterName] = value;
        }
    }

    /// <summary>
    /// Updates a plugin serialized state in config.
    /// </summary>
    public void SetPluginState(int instanceId)
    {
        if (instanceId <= 0)
        {
            return;
        }

        if (_chain.TryGetSlotById(instanceId, out var slot, out _) && slot is not null)
        {
            if (TryGetPluginConfig(instanceId, out var config))
            {
                config.State = slot.Plugin.GetState();
            }
        }
    }

    /// <summary>
    /// Builds container definitions suitable for saving presets.
    /// </summary>
    public IReadOnlyList<ChainPresetContainer> BuildPresetContainers()
    {
        if (_config is null || _config.Containers.Count == 0)
        {
            return Array.Empty<ChainPresetContainer>();
        }

        var indexMap = new Dictionary<int, int>();
        for (int i = 0; i < _config.Plugins.Count; i++)
        {
            int instanceId = _config.Plugins[i].InstanceId;
            if (instanceId > 0 && !indexMap.ContainsKey(instanceId))
            {
                indexMap[instanceId] = i;
            }
        }

        var containers = new List<ChainPresetContainer>(_config.Containers.Count);
        for (int i = 0; i < _config.Containers.Count; i++)
        {
            var container = _config.Containers[i];
            var indices = new List<int>();
            for (int j = 0; j < container.PluginInstanceIds.Count; j++)
            {
                if (indexMap.TryGetValue(container.PluginInstanceIds[j], out int idx))
                {
                    indices.Add(idx);
                }
            }

            containers.Add(new ChainPresetContainer(container.Name, indices, container.IsBypassed));
        }

        return containers;
    }

    private bool SyncConfigOrder()
    {
        if (_config is null)
        {
            return false;
        }

        var slots = _chain.GetSnapshot();
        var configLookup = BuildConfigLookup(_config);

        var ordered = new List<PluginConfig>(slots.Length);
        bool created = false;

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] is not { } slot)
            {
                continue;
            }

            if (configLookup.TryGetValue(slot.InstanceId, out var existing))
            {
                ordered.Add(existing);
            }
            else
            {
                ordered.Add(CreateConfigFromSlot(slot));
                created = true;
            }
        }

        bool changed = created || ordered.Count != _config.Plugins.Count;
        if (!changed)
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                if (!ReferenceEquals(_config.Plugins[i], ordered[i]))
                {
                    changed = true;
                    break;
                }
            }
        }

        if (changed)
        {
            _config.Plugins = ordered;
        }

        _configById.Clear();
        for (int i = 0; i < ordered.Count; i++)
        {
            int instanceId = ordered[i].InstanceId;
            if (instanceId > 0 && !_configById.ContainsKey(instanceId))
            {
                _configById[instanceId] = ordered[i];
            }
        }

        return changed;
    }

    private static Dictionary<int, PluginConfig> BuildConfigLookup(ChannelConfig config)
    {
        var lookup = new Dictionary<int, PluginConfig>();
        for (int i = 0; i < config.Plugins.Count; i++)
        {
            var pluginConfig = config.Plugins[i];
            if (pluginConfig.InstanceId <= 0 || string.IsNullOrWhiteSpace(pluginConfig.Type))
            {
                continue;
            }

            if (!lookup.ContainsKey(pluginConfig.InstanceId))
            {
                lookup[pluginConfig.InstanceId] = pluginConfig;
            }
        }

        return lookup;
    }

    private bool NormalizeContainers()
    {
        if (_config is null)
        {
            return false;
        }

        var slots = _chain.GetSnapshot();
        var indexMap = new Dictionary<int, int>(slots.Length);
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] is { } slot)
            {
                indexMap[slot.InstanceId] = i;
            }
        }

        _containersById.Clear();
        bool changed = false;
        int nextContainerId = 0;
        for (int i = 0; i < _config.Containers.Count; i++)
        {
            var container = _config.Containers[i];
            if (container.Id > nextContainerId)
            {
                nextContainerId = container.Id;
            }
        }

        var assigned = new HashSet<int>();
        for (int i = 0; i < _config.Containers.Count; i++)
        {
            var container = _config.Containers[i];
            if (container.Id <= 0 || _containersById.ContainsKey(container.Id))
            {
                container.Id = ++nextContainerId;
                changed = true;
            }

            _containersById[container.Id] = container;

            var ordered = new List<int>();
            for (int j = 0; j < container.PluginInstanceIds.Count; j++)
            {
                int instanceId = container.PluginInstanceIds[j];
                if (!indexMap.ContainsKey(instanceId))
                {
                    changed = true;
                    continue;
                }
                if (!assigned.Add(instanceId))
                {
                    changed = true;
                    continue;
                }

                ordered.Add(instanceId);
            }

            ordered.Sort((a, b) => indexMap[a].CompareTo(indexMap[b]));

            if (!ordered.SequenceEqual(container.PluginInstanceIds))
            {
                container.PluginInstanceIds = ordered;
                changed = true;
            }
        }

        return changed;
    }

    private static PluginConfig CreateConfigFromSlot(PluginSlot slot)
    {
        return new PluginConfig
        {
            InstanceId = slot.InstanceId,
            Type = slot.Plugin.Id,
            IsBypassed = slot.Plugin.IsBypassed,
            PresetName = PluginPresetManager.CustomPresetName,
            Parameters = slot.Plugin.Parameters.ToDictionary(p => p.Name, p => p.DefaultValue, StringComparer.OrdinalIgnoreCase),
            State = slot.Plugin.GetState()
        };
    }

    private PluginContainerConfig? FindContainer(int containerId)
    {
        if (containerId <= 0)
        {
            return null;
        }

        if (_containersById.TryGetValue(containerId, out var container))
        {
            return container;
        }

        if (_config is null)
        {
            return null;
        }

        NormalizeContainers();
        return _containersById.TryGetValue(containerId, out container) ? container : null;
    }

    private int ResolveContainerInsertIndex(int containerId, int containerIndex)
    {
        if (_config is null || containerId <= 0)
        {
            return _chain.Count;
        }

        var container = FindContainer(containerId);
        if (container is null || container.PluginInstanceIds.Count == 0)
        {
            return _chain.Count;
        }

        var slots = _chain.GetSnapshot();
        var indexMap = new Dictionary<int, int>(slots.Length);
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] is { } slot)
            {
                indexMap[slot.InstanceId] = i;
            }
        }

        var pluginIds = container.PluginInstanceIds;
        int count = pluginIds.Count;
        if (containerIndex <= 0)
        {
            return indexMap.TryGetValue(pluginIds[0], out int idx) ? idx : _chain.Count;
        }

        if (containerIndex >= count)
        {
            return indexMap.TryGetValue(pluginIds[count - 1], out int idx) ? idx + 1 : _chain.Count;
        }

        return indexMap.TryGetValue(pluginIds[containerIndex], out int target) ? target : _chain.Count;
    }
}
