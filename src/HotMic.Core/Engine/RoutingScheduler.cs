using System.Collections.Generic;
using HotMic.Core.Plugins;

namespace HotMic.Core.Engine;

internal sealed class RoutingScheduler
{
    private readonly int _sampleRate;
    private readonly int _blockSize;

    public RoutingScheduler(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _blockSize = blockSize;
    }

    public RoutingSnapshot BuildSnapshot(ChannelStrip[] channels, InputSource?[] inputSources)
    {
        int[] processingOrder = BuildProcessingOrder(channels);
        if (processingOrder.Length != channels.Length)
        {
            processingOrder = BuildDefaultOrder(channels.Length);
        }

        return new RoutingSnapshot(channels, processingOrder, _sampleRate, _blockSize, inputSources);
    }

    public int[] BuildProcessingOrder(ChannelStrip[] channels)
    {
        int count = channels.Length;
        if (count == 0)
        {
            return Array.Empty<int>();
        }

        var edges = new bool[count, count];
        var indegree = new int[count];

        int maxDependencies = 0;
        for (int i = 0; i < count; i++)
        {
            var slots = channels[i].PluginChain.GetSnapshot();
            for (int s = 0; s < slots.Length; s++)
            {
                var slot = slots[s];
                if (slot?.Plugin is IRoutingDependencyProvider provider)
                {
                    if (provider.MaxRoutingDependencies > maxDependencies)
                    {
                        maxDependencies = provider.MaxRoutingDependencies;
                    }
                }
            }
        }

        var dependencyBuffer = maxDependencies > 0
            ? new RoutingDependency[maxDependencies]
            : Array.Empty<RoutingDependency>();

        for (int i = 0; i < count; i++)
        {
            var slots = channels[i].PluginChain.GetSnapshot();
            for (int s = 0; s < slots.Length; s++)
            {
                var slot = slots[s];
                if (slot is null)
                {
                    continue;
                }

                if (slot.Plugin is IRoutingDependencyProvider provider)
                {
                    int maxDeps = provider.MaxRoutingDependencies;
                    if (maxDeps <= 0)
                    {
                        continue;
                    }

                    if (maxDeps > dependencyBuffer.Length)
                    {
                        maxDeps = dependencyBuffer.Length;
                    }
                    if (maxDeps == 0)
                    {
                        continue;
                    }

                    int channelId = i + 1;
                    int depCount = provider.GetRoutingDependencies(channelId, dependencyBuffer.AsSpan(0, maxDeps));
                    if (depCount > maxDeps)
                    {
                        depCount = maxDeps;
                    }

                    for (int d = 0; d < depCount; d++)
                    {
                        var dependency = dependencyBuffer[d];
                        AddEdge(edges, indegree, dependency.SourceChannelId - 1, dependency.TargetChannelId - 1);
                    }
                }
            }
        }

        var queue = new Queue<int>(count);
        for (int i = 0; i < count; i++)
        {
            if (indegree[i] == 0)
            {
                queue.Enqueue(i);
            }
        }

        var order = new int[count];
        int index = 0;
        while (queue.Count > 0)
        {
            int node = queue.Dequeue();
            order[index++] = node;
            for (int j = 0; j < count; j++)
            {
                if (!edges[node, j])
                {
                    continue;
                }

                indegree[j]--;
                if (indegree[j] == 0)
                {
                    queue.Enqueue(j);
                }
            }
        }

        if (index != count)
        {
            return BuildDefaultOrder(count);
        }

        return order;
    }

    private static int[] BuildDefaultOrder(int channelCount)
    {
        var order = new int[channelCount];
        for (int i = 0; i < channelCount; i++)
        {
            order[i] = i;
        }
        return order;
    }

    private static void AddEdge(bool[,] edges, int[] indegree, int sourceIndex, int targetIndex)
    {
        int count = indegree.Length;
        if ((uint)sourceIndex >= (uint)count || (uint)targetIndex >= (uint)count || sourceIndex == targetIndex)
        {
            return;
        }

        if (!edges[sourceIndex, targetIndex])
        {
            edges[sourceIndex, targetIndex] = true;
            indegree[targetIndex]++;
        }
    }
}
