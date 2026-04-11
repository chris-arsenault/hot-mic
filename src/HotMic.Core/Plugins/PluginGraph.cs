using HotMic.Common;
using HotMic.Common.Configuration;
using HotMic.Core.Presets;

namespace HotMic.Core.Plugins;

/// <summary>
/// Canonical manager for plugin order and container grouping for a single channel.
/// The node tree (config.Nodes) is the single source of truth. The flat PluginChain
/// is derived from it on every mutation via FlattenToChain().
/// </summary>
public sealed class PluginGraph
{
    private readonly PluginChain _chain;
    private ChannelConfig _config;
    private readonly Dictionary<int, PluginSlot> _slotById = new();
    private int _nextInstanceId;

    public PluginGraph(PluginChain chain, ChannelConfig config)
    {
        _chain = chain ?? throw new ArgumentNullException(nameof(chain));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public PluginSlot?[] GetSlotsSnapshot() => _chain.GetSnapshot();

    /// <summary>
    /// Loads plugins from the node tree in config, creating slots via the factory.
    /// Handles migration from legacy Plugins+Containers format.
    /// </summary>
    public bool LoadFromConfig(ChannelConfig config, Func<PluginNodeConfig, PluginSlot?> slotFactory)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(slotFactory);

        _config = config;
        bool changed = false;
        var usedIds = new HashSet<int>();
        _nextInstanceId = 0;
        _slotById.Clear();

        // Assign IDs and create slots for all plugins in the tree
        foreach (var pluginNode in ChainNodeHelpers.FlattenPlugins(config.Nodes))
        {
            if (string.IsNullOrWhiteSpace(pluginNode.Type))
            {
                changed = true;
                continue;
            }

            if (pluginNode.InstanceId <= 0 || usedIds.Contains(pluginNode.InstanceId))
            {
                pluginNode.InstanceId = ++_nextInstanceId;
                changed = true;
            }
            else if (pluginNode.InstanceId > _nextInstanceId)
            {
                _nextInstanceId = pluginNode.InstanceId;
            }
            usedIds.Add(pluginNode.InstanceId);

            var slot = slotFactory(pluginNode);
            if (slot is not null)
            {
                _slotById[pluginNode.InstanceId] = slot;
            }
            else
            {
                changed = true;
            }
        }

        // Remove nodes for plugins that failed to create
        PruneFailedPlugins(config.Nodes);

        // Assign container IDs
        int nextContainerId = 0;
        foreach (var node in config.Nodes)
        {
            if (node is ContainerNodeConfig c)
            {
                if (c.ContainerId <= 0)
                {
                    c.ContainerId = ++nextContainerId;
                    changed = true;
                }
                else if (c.ContainerId > nextContainerId)
                {
                    nextContainerId = c.ContainerId;
                }
            }
        }

        FlattenToChain();
        return changed;
    }

    /// <summary>
    /// Looks up a plugin node config by instance ID in the current tree.
    /// </summary>
    public bool TryGetPluginConfig(int instanceId, out PluginNodeConfig config)
    {
        config = null!;
        if (instanceId <= 0) return false;
        config = ChainNodeHelpers.FindPlugin(_config.Nodes, instanceId)!;
        return config is not null;
    }

    /// <summary>
    /// Inserts a plugin at the given flat chain index.
    /// </summary>
    public int InsertPlugin(IPlugin plugin, int insertIndex)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        int instanceId = ++_nextInstanceId;
        var node = CreatePluginNode(instanceId, plugin);
        var slot = new PluginSlot(instanceId, plugin, _chain.SampleRate);
        _slotById[instanceId] = slot;

        InsertNodeAtFlatIndex(_config.Nodes, node, insertIndex);
        FlattenToChain();
        return instanceId;
    }

    /// <summary>
    /// Inserts a plugin into a container at the container-relative index.
    /// </summary>
    public int InsertPluginIntoContainer(IPlugin plugin, int containerId, int containerIndex)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        var container = ChainNodeHelpers.FindContainer(_config.Nodes, containerId);
        if (container is null) return 0;

        int instanceId = ++_nextInstanceId;
        var node = CreatePluginNode(instanceId, plugin);
        var slot = new PluginSlot(instanceId, plugin, _chain.SampleRate);
        _slotById[instanceId] = slot;

        int idx = Math.Clamp(containerIndex, 0, container.Plugins.Count);
        container.Plugins.Insert(idx, node);
        FlattenToChain();
        return instanceId;
    }

    /// <summary>
    /// Removes a plugin from anywhere in the tree.
    /// </summary>
    public bool RemovePlugin(int instanceId, out PluginSlot? removedSlot)
    {
        removedSlot = null;
        if (instanceId <= 0) return false;

        if (!ChainNodeHelpers.RemovePlugin(_config.Nodes, instanceId, out _))
            return false;

        if (_slotById.Remove(instanceId, out var slot))
            removedSlot = slot;

        FlattenToChain();
        return true;
    }

    /// <summary>
    /// Moves a plugin to a new flat chain index.
    /// </summary>
    public bool MovePlugin(int instanceId, int targetIndex)
    {
        if (instanceId <= 0) return false;

        int currentIndex = ChainNodeHelpers.FlatIndex(_config.Nodes, instanceId);
        if (currentIndex < 0 || currentIndex == targetIndex) return false;

        // Remove from current position
        if (!ChainNodeHelpers.RemovePlugin(_config.Nodes, instanceId, out var node) || node is null)
            return false;

        // Insert at target flat index
        InsertNodeAtFlatIndex(_config.Nodes, node, targetIndex);
        FlattenToChain();
        return true;
    }

    /// <summary>
    /// Moves a plugin within its container.
    /// </summary>
    public bool MovePluginWithinContainer(int instanceId, int containerId, int targetIndex)
    {
        if (instanceId <= 0 || containerId <= 0) return false;

        var container = ChainNodeHelpers.FindContainer(_config.Nodes, containerId);
        if (container is null || container.Plugins.Count <= 1) return false;

        int fromIndex = -1;
        for (int i = 0; i < container.Plugins.Count; i++)
        {
            if (container.Plugins[i].InstanceId == instanceId)
            {
                fromIndex = i;
                break;
            }
        }
        if (fromIndex < 0) return false;

        targetIndex = Math.Clamp(targetIndex, 0, container.Plugins.Count - 1);
        if (fromIndex == targetIndex) return false;

        var plugin = container.Plugins[fromIndex];
        container.Plugins.RemoveAt(fromIndex);
        container.Plugins.Insert(targetIndex, plugin);
        FlattenToChain();
        return true;
    }

    /// <summary>
    /// Creates a new empty container.
    /// </summary>
    public int CreateContainer(string name)
    {

        int nextId = 1;
        foreach (var node in _config.Nodes)
        {
            if (node is ContainerNodeConfig c && c.ContainerId >= nextId)
                nextId = c.ContainerId + 1;
        }

        var container = new ContainerNodeConfig
        {
            ContainerId = nextId,
            Name = name ?? string.Empty,
            IsBypassed = false
        };

        _config.Nodes.Add(container);
        // No FlattenToChain needed — empty container has no audio impact
        return container.ContainerId;
    }

    /// <summary>
    /// Removes a container, promoting its children to standalone nodes.
    /// </summary>
    public bool RemoveContainer(int containerId)
    {
        if (containerId <= 0) return false;

        for (int i = 0; i < _config.Nodes.Count; i++)
        {
            if (_config.Nodes[i] is ContainerNodeConfig c && c.ContainerId == containerId)
            {
                // Replace container with its children
                _config.Nodes.RemoveAt(i);
                for (int j = c.Plugins.Count - 1; j >= 0; j--)
                {
                    _config.Nodes.Insert(i, c.Plugins[j]);
                }
                FlattenToChain();
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Toggles container bypass, applying it to all children.
    /// </summary>
    public bool SetContainerBypass(int containerId, bool bypassed)
    {
        if (containerId <= 0) return false;

        var container = ChainNodeHelpers.FindContainer(_config.Nodes, containerId);
        if (container is null) return false;

        bool changed = container.IsBypassed != bypassed;
        container.IsBypassed = bypassed;

        foreach (var child in container.Plugins)
        {
            SetPluginBypass(child.InstanceId, bypassed);
        }
        return changed;
    }

    /// <summary>
    /// Renames a container.
    /// </summary>
    public bool RenameContainer(int containerId, string newName)
    {
        if (containerId <= 0) return false;

        var container = ChainNodeHelpers.FindContainer(_config.Nodes, containerId);
        if (container is null) return false;

        container.Name = newName ?? string.Empty;
        return true;
    }

    /// <summary>
    /// Assigns a plugin to a container (removes from current container first).
    /// </summary>
    public bool AssignPluginToContainer(int instanceId, int containerId)
    {
        if (instanceId <= 0) return false;

        // Remove from current location
        if (!ChainNodeHelpers.RemovePlugin(_config.Nodes, instanceId, out var pluginNode) || pluginNode is null)
            return false;

        if (containerId <= 0)
        {
            // Ungroup: add as standalone at the end
            _config.Nodes.Add(pluginNode);
        }
        else
        {
            var container = ChainNodeHelpers.FindContainer(_config.Nodes, containerId);
            if (container is null)
            {
                // Container not found, add standalone
                _config.Nodes.Add(pluginNode);
            }
            else
            {
                container.Plugins.Add(pluginNode);
                if (container.IsBypassed)
                    SetPluginBypass(instanceId, true);
            }
        }

        FlattenToChain();
        return true;
    }

    /// <summary>
    /// Moves a container (and all its children) to a new position in the node list.
    /// </summary>
    public bool MoveContainer(int containerId, int targetIndex)
    {
        if (containerId <= 0) return false;

        // Find and remove the container
        ContainerNodeConfig? moving = null;
        int fromIndex = -1;
        for (int i = 0; i < _config.Nodes.Count; i++)
        {
            if (_config.Nodes[i] is ContainerNodeConfig c && c.ContainerId == containerId)
            {
                moving = c;
                fromIndex = i;
                break;
            }
        }
        if (moving is null || moving.Plugins.Count == 0) return false;

        // Convert target flat index to node index
        int flatCount = 0;
        int nodeInsertIndex = _config.Nodes.Count;
        for (int i = 0; i < _config.Nodes.Count; i++)
        {
            if (flatCount >= targetIndex && i != fromIndex)
            {
                nodeInsertIndex = i;
                break;
            }
            if (i == fromIndex) continue; // Skip the container being moved
            switch (_config.Nodes[i])
            {
                case PluginNodeConfig:
                    flatCount++;
                    break;
                case ContainerNodeConfig cc:
                    flatCount += cc.Plugins.Count;
                    break;
            }
        }

        _config.Nodes.RemoveAt(fromIndex);
        if (nodeInsertIndex > fromIndex) nodeInsertIndex--;
        nodeInsertIndex = Math.Clamp(nodeInsertIndex, 0, _config.Nodes.Count);
        _config.Nodes.Insert(nodeInsertIndex, moving);

        FlattenToChain();
        return true;
    }

    /// <summary>
    /// Returns containers from the node tree.
    /// </summary>
    public IReadOnlyList<ContainerNodeConfig> GetContainers()
    {
        var result = new List<ContainerNodeConfig>();
        foreach (var node in _config.Nodes)
        {
            if (node is ContainerNodeConfig c)
                result.Add(c);
        }
        return result;
    }

    public bool SetPluginBypass(int instanceId, bool bypassed)
    {
        if (instanceId <= 0) return false;

        if (_chain.TryGetSlotById(instanceId, out var slot, out _) && slot is not null)
            slot.Plugin.IsBypassed = bypassed;

        if (TryGetPluginConfig(instanceId, out var config))
            config.IsBypassed = bypassed;

        return true;
    }

    public void SetPluginParameter(int instanceId, string parameterName, float value)
    {
        if (string.IsNullOrWhiteSpace(parameterName)) return;

        if (TryGetPluginConfig(instanceId, out var config))
            config.Parameters[parameterName] = value;
    }

    public void SetPluginState(int instanceId)
    {
        if (instanceId <= 0) return;

        if (_chain.TryGetSlotById(instanceId, out var slot, out _) && slot is not null)
        {
            if (TryGetPluginConfig(instanceId, out var config))
                config.State = slot.Plugin.GetState();
        }
    }

    /// <summary>
    /// Synchronize the node tree from the current chain state.
    /// Used after external chain modifications (e.g., preset load via ReplaceAll).
    /// </summary>
    public bool SyncNodesFromChain()
    {

        var slots = _chain.GetSnapshot();
        var nodePlugins = ChainNodeHelpers.FlattenPlugins(_config.Nodes);

        // Check if chain order matches node tree flattened order
        bool inSync = slots.Length == nodePlugins.Count;
        if (inSync)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] is null || slots[i]!.InstanceId != nodePlugins[i].InstanceId)
                {
                    inSync = false;
                    break;
                }
            }
        }

        if (inSync) return false;

        // Rebuild nodes from chain — all containers lost (they'll be rebuilt by caller)
        _config.Nodes.Clear();
        foreach (var slot in slots)
        {
            if (slot is null) continue;
            _config.Nodes.Add(CreatePluginNode(slot.InstanceId, slot.Plugin));
            _slotById[slot.InstanceId] = slot;
        }
        return true;
    }

    // ---- Internal helpers ----

    /// <summary>
    /// Flatten the node tree to the PluginChain's slot array. Called after every tree mutation.
    /// </summary>
    private void FlattenToChain()
    {

        var flattened = ChainNodeHelpers.FlattenPlugins(_config.Nodes);
        var slots = new PluginSlot?[flattened.Count];
        for (int i = 0; i < flattened.Count; i++)
        {
            _slotById.TryGetValue(flattened[i].InstanceId, out var slot);
            slots[i] = slot;
        }
        _chain.ReplaceAll(slots);
    }

    /// <summary>
    /// Insert a plugin node at a given flat processing index in the node tree.
    /// </summary>
    private static void InsertNodeAtFlatIndex(IList<ChainNodeConfig> nodes, PluginNodeConfig node, int flatIndex)
    {
        if (flatIndex < 0) flatIndex = 0;

        int currentFlat = 0;
        for (int i = 0; i < nodes.Count; i++)
        {
            switch (nodes[i])
            {
                case PluginNodeConfig:
                    if (currentFlat == flatIndex)
                    {
                        nodes.Insert(i, node);
                        return;
                    }
                    currentFlat++;
                    break;
                case ContainerNodeConfig c:
                    if (currentFlat + c.Plugins.Count > flatIndex)
                    {
                        // Insert inside this container
                        int containerOffset = flatIndex - currentFlat;
                        c.Plugins.Insert(containerOffset, node);
                        return;
                    }
                    currentFlat += c.Plugins.Count;
                    break;
            }
        }
        // Past the end — append
        nodes.Add(node);
    }

    private static PluginNodeConfig CreatePluginNode(int instanceId, IPlugin plugin)
    {
        var node = new PluginNodeConfig
        {
            InstanceId = instanceId,
            Type = plugin.Id,
            IsBypassed = plugin.IsBypassed,
            PresetName = PluginPresetManager.CustomPresetName,
            State = plugin.GetState()
        };
        foreach (var p in plugin.Parameters)
            node.Parameters[p.Name] = p.DefaultValue;
        return node;
    }

    /// <summary>
    /// Remove plugin nodes that don't have a corresponding slot (factory returned null).
    /// </summary>
    private void PruneFailedPlugins(IList<ChainNodeConfig> nodes)
    {
        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            switch (nodes[i])
            {
                case PluginNodeConfig p:
                    if (!_slotById.ContainsKey(p.InstanceId))
                        nodes.RemoveAt(i);
                    break;
                case ContainerNodeConfig c:
                    for (int j = c.Plugins.Count - 1; j >= 0; j--)
                    {
                        if (!_slotById.ContainsKey(c.Plugins[j].InstanceId))
                            c.Plugins.RemoveAt(j);
                    }
                    // Remove empty containers
                    if (c.Plugins.Count == 0)
                        nodes.RemoveAt(i);
                    break;
            }
        }
    }

    // ---- Legacy compatibility ----

    /// <summary>
    /// Builds container definitions suitable for saving presets (legacy format).
    /// </summary>
    public IReadOnlyList<ChainPresetContainer> BuildPresetContainers()
    {

        var flat = ChainNodeHelpers.FlattenPlugins(_config.Nodes);
        var indexMap = new Dictionary<int, int>(flat.Count);
        for (int i = 0; i < flat.Count; i++)
            indexMap[flat[i].InstanceId] = i;

        var containers = new List<ChainPresetContainer>();
        foreach (var node in _config.Nodes)
        {
            if (node is not ContainerNodeConfig c || c.Plugins.Count == 0) continue;
            var indices = new List<int>(c.Plugins.Count);
            foreach (var child in c.Plugins)
            {
                if (indexMap.TryGetValue(child.InstanceId, out int idx))
                    indices.Add(idx);
            }
            containers.Add(new ChainPresetContainer(c.Name, indices, c.IsBypassed));
        }
        return containers;
    }

    /// <summary>
    /// Exposes the chain's SampleRate for slot creation.
    /// </summary>
    internal int SampleRate => _chain.SampleRate;
}
