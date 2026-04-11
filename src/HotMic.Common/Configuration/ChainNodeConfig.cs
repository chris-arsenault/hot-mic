using System.Text.Json.Serialization;

namespace HotMic.Common.Configuration;

/// <summary>
/// A node in the plugin chain. Either a standalone plugin or a container grouping plugins.
/// Processing order is defined by list position: iterate top-to-bottom,
/// for containers iterate children in order.
/// </summary>
[JsonDerivedType(typeof(PluginNodeConfig), "plugin")]
[JsonDerivedType(typeof(ContainerNodeConfig), "container")]
public abstract class ChainNodeConfig { }

/// <summary>
/// A single plugin in the chain.
/// </summary>
public sealed class PluginNodeConfig : ChainNodeConfig
{
    public int InstanceId { get; set; }
    public string Type { get; set; } = string.Empty;
    public bool IsBypassed { get; set; }
    public string PresetName { get; set; } = string.Empty;
    public Dictionary<string, float> Parameters { get; set; } = new();
    public byte[]? State { get; set; }
}

/// <summary>
/// A container grouping plugins. Children are processed in order.
/// </summary>
public sealed class ContainerNodeConfig : ChainNodeConfig
{
    public int ContainerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsBypassed { get; set; }
    public IList<PluginNodeConfig> Plugins { get; set; } = new List<PluginNodeConfig>();
}

/// <summary>
/// Helper methods for working with the chain node tree.
/// </summary>
public static class ChainNodeHelpers
{
    /// <summary>
    /// Flatten the node tree into an ordered list of all plugin nodes (for processing order).
    /// </summary>
    public static List<PluginNodeConfig> FlattenPlugins(IList<ChainNodeConfig> nodes)
    {
        var result = new List<PluginNodeConfig>();
        foreach (var node in nodes)
        {
            switch (node)
            {
                case PluginNodeConfig p:
                    result.Add(p);
                    break;
                case ContainerNodeConfig c:
                    result.AddRange(c.Plugins);
                    break;
            }
        }
        return result;
    }

    /// <summary>
    /// Find a plugin node by instance ID anywhere in the tree.
    /// </summary>
    public static PluginNodeConfig? FindPlugin(IList<ChainNodeConfig> nodes, int instanceId)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case PluginNodeConfig p when p.InstanceId == instanceId:
                    return p;
                case ContainerNodeConfig c:
                    foreach (var child in c.Plugins)
                    {
                        if (child.InstanceId == instanceId)
                            return child;
                    }
                    break;
            }
        }
        return null;
    }

    /// <summary>
    /// Find a container node by ID.
    /// </summary>
    public static ContainerNodeConfig? FindContainer(IList<ChainNodeConfig> nodes, int containerId)
    {
        foreach (var node in nodes)
        {
            if (node is ContainerNodeConfig c && c.ContainerId == containerId)
                return c;
        }
        return null;
    }

    /// <summary>
    /// Find the container that holds a given plugin, or null if standalone.
    /// </summary>
    public static ContainerNodeConfig? FindContainerForPlugin(IList<ChainNodeConfig> nodes, int instanceId)
    {
        foreach (var node in nodes)
        {
            if (node is ContainerNodeConfig c)
            {
                foreach (var child in c.Plugins)
                {
                    if (child.InstanceId == instanceId)
                        return c;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Remove a plugin from anywhere in the tree. Returns the removed node and true if found.
    /// </summary>
    public static bool RemovePlugin(IList<ChainNodeConfig> nodes, int instanceId, out PluginNodeConfig? removed)
    {
        removed = null;
        for (int i = 0; i < nodes.Count; i++)
        {
            switch (nodes[i])
            {
                case PluginNodeConfig p when p.InstanceId == instanceId:
                    removed = p;
                    nodes.RemoveAt(i);
                    return true;
                case ContainerNodeConfig c:
                    for (int j = 0; j < c.Plugins.Count; j++)
                    {
                        if (c.Plugins[j].InstanceId == instanceId)
                        {
                            removed = c.Plugins[j];
                            c.Plugins.RemoveAt(j);
                            return true;
                        }
                    }
                    break;
            }
        }
        return false;
    }

    /// <summary>
    /// Compute the flat processing index for a given instance ID.
    /// Returns -1 if not found.
    /// </summary>
    public static int FlatIndex(IList<ChainNodeConfig> nodes, int instanceId)
    {
        int idx = 0;
        foreach (var node in nodes)
        {
            switch (node)
            {
                case PluginNodeConfig p:
                    if (p.InstanceId == instanceId) return idx;
                    idx++;
                    break;
                case ContainerNodeConfig c:
                    foreach (var child in c.Plugins)
                    {
                        if (child.InstanceId == instanceId) return idx;
                        idx++;
                    }
                    break;
            }
        }
        return -1;
    }

}
