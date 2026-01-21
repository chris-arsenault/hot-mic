using System;
using System.Collections.Generic;
using HotMic.App.UI;
using HotMic.Common.Configuration;
using HotMic.Core.Engine;

namespace HotMic.App.ViewModels;

internal sealed class MainChannelCoordinator
{
    private readonly MainViewModel _viewModel;
    private readonly ConfigManager _configManager;
    private readonly MainPluginCoordinator _pluginCoordinator;
    private readonly Func<AudioEngine> _getAudioEngine;
    private readonly Func<AppConfig> _getConfig;
    private readonly Func<int, ChannelConfig> _getOrCreateChannelConfig;
    private Action<int>? _rebuildForChannelTopologyChange;
    private bool _suppressChannelConfig;

    public MainChannelCoordinator(
        MainViewModel viewModel,
        ConfigManager configManager,
        MainPluginCoordinator pluginCoordinator,
        Func<AudioEngine> getAudioEngine,
        Func<AppConfig> getConfig,
        Func<int, ChannelConfig> getOrCreateChannelConfig)
    {
        _viewModel = viewModel;
        _configManager = configManager;
        _pluginCoordinator = pluginCoordinator;
        _getAudioEngine = getAudioEngine;
        _getConfig = getConfig;
        _getOrCreateChannelConfig = getOrCreateChannelConfig;
    }

    public void SetRebuildHandler(Action<int> rebuildForChannelTopologyChange)
    {
        _rebuildForChannelTopologyChange = rebuildForChannelTopologyChange;
    }

    public void BuildChannelViewModels()
    {
        _viewModel.Channels.Clear();
        var config = _getConfig();
        for (int i = 0; i < config.Channels.Count; i++)
        {
            var channelConfig = config.Channels[i];
            var viewModel = CreateChannelViewModel(i, string.IsNullOrWhiteSpace(channelConfig.Name) ? $"Channel {i + 1}" : channelConfig.Name);
            _viewModel.Channels.Add(viewModel);
            int channelIndex = i;
            viewModel.PropertyChanged += (_, e) => UpdateChannelConfig(channelIndex, viewModel, e.PropertyName);
        }

        if (_viewModel.ActiveChannelIndex < 0 || _viewModel.ActiveChannelIndex >= _viewModel.Channels.Count)
        {
            _viewModel.ActiveChannelIndex = 0;
        }
    }

    public void ApplyChannelConfigToViewModels()
    {
        _suppressChannelConfig = true;
        for (int i = 0; i < _viewModel.Channels.Count; i++)
        {
            var channelConfig = _getOrCreateChannelConfig(i);
            var channelViewModel = _viewModel.Channels[i];
            channelViewModel.UpdateName(channelConfig.Name);
            channelViewModel.InputGainDb = channelConfig.InputGainDb;
            channelViewModel.OutputGainDb = channelConfig.OutputGainDb;
            channelViewModel.IsMuted = channelConfig.IsMuted;
            channelViewModel.IsSoloed = channelConfig.IsSoloed;
        }
        _suppressChannelConfig = false;
    }

    public void UpdateDynamicWindowWidth()
    {
        int channelCount = Math.Max(1, _viewModel.Channels.Count);
        if (_viewModel.IsMinimalView)
        {
            _viewModel.WindowWidth = MainLayoutMetrics.MinimalViewWidth;
            _viewModel.WindowHeight = MainLayoutMetrics.GetMinimalViewHeight(channelCount);
            return;
        }

        int maxSlots = 1;
        for (int i = 0; i < channelCount; i++)
        {
            int visibleSlots = GetVisibleSlotCount(i);
            if (visibleSlots > maxSlots)
            {
                maxSlots = visibleSlots;
            }
        }

        _viewModel.WindowWidth = MainLayoutMetrics.GetFullViewWidth(maxSlots);
        _viewModel.WindowHeight = MainLayoutMetrics.GetFullViewHeight(channelCount);
    }

    public void ApplyChannelInputGain(int channelIndex, float gainDb)
    {
        if ((uint)channelIndex >= (uint)_viewModel.Channels.Count)
        {
            return;
        }

        _viewModel.Channels[channelIndex].InputGainDb = gainDb;
    }

    public void AddChannel()
    {
        var config = _getConfig();
        int channelIndex = config.Channels.Count;
        var channelConfig = CreateDefaultChannelConfig(channelIndex + 1);
        config.Channels.Add(channelConfig);

        _getAudioEngine().EnsureChannelCount(config.Channels.Count);
        _pluginCoordinator.InitializePluginGraphs();

        var viewModel = CreateChannelViewModel(channelIndex, channelConfig.Name);
        _viewModel.Channels.Add(viewModel);
        int capturedIndex = channelIndex;
        viewModel.PropertyChanged += (_, e) => UpdateChannelConfig(capturedIndex, viewModel, e.PropertyName);

        _suppressChannelConfig = true;
        viewModel.InputGainDb = channelConfig.InputGainDb;
        viewModel.OutputGainDb = channelConfig.OutputGainDb;
        viewModel.IsMuted = channelConfig.IsMuted;
        viewModel.IsSoloed = channelConfig.IsSoloed;
        _suppressChannelConfig = false;

        _pluginCoordinator.LoadChannelPlugins(channelIndex, channelConfig);
        _pluginCoordinator.RefreshPluginViewModels(channelIndex);
        UpdateDynamicWindowWidth();
        _configManager.Save(config);

        _viewModel.ActiveChannelIndex = channelIndex;
    }

    public void RemoveChannel(int channelIndex)
    {
        var config = _getConfig();
        if (config.Channels.Count <= 1)
        {
            return;
        }

        if ((uint)channelIndex >= (uint)config.Channels.Count)
        {
            return;
        }

        _pluginCoordinator.SyncGraphsWithConfig();

        int removedChannelId = channelIndex + 1;
        config.Channels.RemoveAt(channelIndex);
        ResequenceChannelIds(config);
        NormalizeChannelReferencesAfterRemoval(config, removedChannelId, config.Channels.Count);
        _configManager.Save(config);

        int nextActive = Math.Clamp(channelIndex, 0, config.Channels.Count - 1);
        if (_rebuildForChannelTopologyChange is null)
        {
            throw new InvalidOperationException("Channel rebuild handler not set.");
        }

        _rebuildForChannelTopologyChange(nextActive);
    }

    public void RenameChannel(int channelIndex, string newName)
    {
        if ((uint)channelIndex >= (uint)_viewModel.Channels.Count)
        {
            return;
        }

        var viewModel = _viewModel.Channels[channelIndex];
        viewModel.UpdateName(newName);

        var config = _getOrCreateChannelConfig(channelIndex);
        config.Name = newName;
        _configManager.Save(_getConfig());
    }

    public int CreateCopyChannel(int sourceChannelIndex)
    {
        var config = _getConfig();
        int channelIndex = config.Channels.Count;
        var channelConfig = new ChannelConfig
        {
            Id = channelIndex + 1,
            Name = $"Copy {sourceChannelIndex + 1} -> {channelIndex + 1}",
            Plugins =
            [
                new PluginConfig
                {
                    Type = "builtin:bus-input"
                }
            ]
        };

        config.Channels.Add(channelConfig);
        _getAudioEngine().EnsureChannelCount(config.Channels.Count);
        _pluginCoordinator.InitializePluginGraphs();

        var viewModel = CreateChannelViewModel(channelIndex, channelConfig.Name);
        _viewModel.Channels.Add(viewModel);
        int capturedIndex = channelIndex;
        viewModel.PropertyChanged += (_, e) => UpdateChannelConfig(capturedIndex, viewModel, e.PropertyName);

        _suppressChannelConfig = true;
        viewModel.InputGainDb = channelConfig.InputGainDb;
        viewModel.OutputGainDb = channelConfig.OutputGainDb;
        viewModel.IsMuted = channelConfig.IsMuted;
        viewModel.IsSoloed = channelConfig.IsSoloed;
        _suppressChannelConfig = false;

        _pluginCoordinator.LoadChannelPlugins(channelIndex, channelConfig);
        _pluginCoordinator.RefreshPluginViewModels(channelIndex);
        UpdateDynamicWindowWidth();
        _configManager.Save(config);

        return channelConfig.Id;
    }

    private ChannelStripViewModel CreateChannelViewModel(int channelIndex, string name)
    {
        return new ChannelStripViewModel(
            channelIndex,
            name,
            _pluginCoordinator.EnqueueParameterChange,
            _pluginCoordinator.ApplyPluginParameterByIndex,
            (instanceId, slotIndex) => _pluginCoordinator.HandlePluginAction(channelIndex, instanceId, slotIndex),
            instanceId => _pluginCoordinator.RemovePlugin(channelIndex, instanceId),
            (instanceId, toIndex) => _pluginCoordinator.ReorderPlugins(channelIndex, instanceId, toIndex),
            (instanceId, value) => _pluginCoordinator.UpdatePluginBypassConfig(channelIndex, instanceId, value),
            containerId => _pluginCoordinator.HandleContainerAction(channelIndex, containerId),
            containerId => _pluginCoordinator.RemoveContainer(channelIndex, containerId),
            (containerId, bypass) => _pluginCoordinator.SetContainerBypass(channelIndex, containerId, bypass),
            (containerId, targetIndex) => _pluginCoordinator.ReorderContainer(channelIndex, containerId, targetIndex));
    }

    private void UpdateChannelConfig(int channelIndex, ChannelStripViewModel viewModel, string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName) || _suppressChannelConfig)
        {
            return;
        }

        var config = _getOrCreateChannelConfig(channelIndex);
        bool changed = true;
        switch (propertyName)
        {
            case nameof(ChannelStripViewModel.InputGainDb):
                config.InputGainDb = viewModel.InputGainDb;
                break;
            case nameof(ChannelStripViewModel.OutputGainDb):
                config.OutputGainDb = viewModel.OutputGainDb;
                break;
            case nameof(ChannelStripViewModel.IsMuted):
                config.IsMuted = viewModel.IsMuted;
                break;
            case nameof(ChannelStripViewModel.IsSoloed):
                config.IsSoloed = viewModel.IsSoloed;
                break;
            case nameof(ChannelStripViewModel.Name):
                config.Name = viewModel.Name;
                break;
            default:
                changed = false;
                break;
        }

        if (changed)
        {
            _configManager.Save(_getConfig());
        }
    }

    private int GetVisibleSlotCount(int channelIndex)
    {
        if ((uint)channelIndex >= (uint)_getAudioEngine().Channels.Count)
        {
            return 1;
        }

        var strip = _getAudioEngine().Channels[channelIndex];
        var slots = strip.PluginChain.GetSnapshot();
        var config = _getOrCreateChannelConfig(channelIndex);
        if (config.Containers.Count == 0)
        {
            return Math.Max(1, slots.Length + 1);
        }

        var containerIndexByPluginId = new Dictionary<int, int>();
        for (int i = 0; i < config.Containers.Count; i++)
        {
            var container = config.Containers[i];
            var pluginIds = container.PluginInstanceIds;
            for (int j = 0; j < pluginIds.Count; j++)
            {
                int instanceId = pluginIds[j];
                if (instanceId > 0 && !containerIndexByPluginId.ContainsKey(instanceId))
                {
                    containerIndexByPluginId[instanceId] = i;
                }
            }
        }

        var countedContainers = new HashSet<int>();
        int visibleCount = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] is not { } slot)
            {
                continue;
            }

            if (containerIndexByPluginId.TryGetValue(slot.InstanceId, out int containerIndex))
            {
                if (countedContainers.Add(containerIndex))
                {
                    visibleCount++;
                }
            }
            else
            {
                visibleCount++;
            }
        }

        for (int i = 0; i < config.Containers.Count; i++)
        {
            if (config.Containers[i].PluginInstanceIds.Count == 0)
            {
                visibleCount++;
            }
        }

        return Math.Max(1, visibleCount + 1);
    }

    private static ChannelConfig CreateDefaultChannelConfig(int id)
    {
        return new ChannelConfig
        {
            Id = id,
            Name = $"Channel {id}",
            Plugins =
            [
                new PluginConfig
                {
                    Type = "builtin:input"
                }
            ]
        };
    }

    private static void ResequenceChannelIds(AppConfig config)
    {
        for (int i = 0; i < config.Channels.Count; i++)
        {
            config.Channels[i].Id = i + 1;
        }
    }

    private static void NormalizeChannelReferencesAfterRemoval(AppConfig config, int removedChannelId, int newChannelCount)
    {
        if (newChannelCount < 1)
        {
            return;
        }

        for (int i = 0; i < config.Channels.Count; i++)
        {
            var channel = config.Channels[i];
            for (int j = 0; j < channel.Plugins.Count; j++)
            {
                var pluginConfig = channel.Plugins[j];
                if (string.Equals(pluginConfig.Type, "builtin:copy", StringComparison.OrdinalIgnoreCase))
                {
                    if (pluginConfig.State is { Length: >= 4 } state)
                    {
                        int target = BitConverter.ToInt32(state, 0);
                        int remapped = RemapChannelId(target, removedChannelId, newChannelCount);
                        if (remapped != target)
                        {
                            pluginConfig.State = BitConverter.GetBytes(remapped);
                        }
                    }
                    continue;
                }

                if (string.Equals(pluginConfig.Type, "builtin:merge", StringComparison.OrdinalIgnoreCase))
                {
                    bool changed = false;
                    for (int sourceIndex = 1; sourceIndex <= 16; sourceIndex++)
                    {
                        string key = $"Source {sourceIndex}";
                        if (!pluginConfig.Parameters.TryGetValue(key, out var value))
                        {
                            continue;
                        }

                        int channelId = (int)MathF.Round(value);
                        int remapped = RemapChannelId(channelId, removedChannelId, newChannelCount);
                        if (remapped != channelId)
                        {
                            pluginConfig.Parameters[key] = remapped;
                            changed = true;
                        }
                    }

                    if (changed && pluginConfig.State is { Length: > 0 })
                    {
                        pluginConfig.State = null;
                    }
                }
            }
        }
    }

    private static int RemapChannelId(int channelId, int removedChannelId, int newChannelCount)
    {
        if (channelId <= 0)
        {
            return channelId;
        }

        int remapped = channelId > removedChannelId ? channelId - 1 : channelId;
        if (remapped > newChannelCount)
        {
            remapped = newChannelCount;
        }

        return remapped < 1 ? 1 : remapped;
    }
}
