using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HotMic.App.Models;
using HotMic.Common.Configuration;
using HotMic.Common.Models;
using HotMic.Core.Engine;
using HotMic.Core.Plugins;
using HotMic.Core.Plugins.BuiltIn;
using HotMic.Core.Presets;
using HotMic.Vst3;

namespace HotMic.App.ViewModels;

internal sealed class MainPluginCoordinator
{
    private const string ContainerChoiceId = "ui:container";

    private readonly ConfigManager _configManager;
    private readonly PluginPresetManager _presetManager;
    private readonly PluginWindowRouter _pluginWindowRouter;
    private readonly PluginContainerWindowManager _containerWindows;
    private readonly Func<AudioEngine> _getAudioEngine;
    private readonly Func<AppConfig> _getConfig;
    private readonly Func<AudioQualityProfile> _getQualityProfile;
    private readonly Func<int, ChannelConfig> _getOrCreateChannelConfig;
    private readonly Func<string, string> _getInputDeviceLabel;
    private readonly Action _updateDynamicWindowWidth;
    private readonly Func<int, int> _createCopyChannel;
    private readonly Action<string> _setStatusMessage;
    private readonly Func<bool> _getMeterScaleVox;
    private readonly Func<int> _getActiveChannelIndex;
    private readonly Action<string> _setActiveChannelPresetName;
    private readonly Func<IReadOnlyList<ChannelStripViewModel>> _getChannels;
    private readonly Func<IReadOnlyList<AudioDevice>> _getInputDevices;
    private readonly Func<AudioDevice?> _getSelectedOutputDevice;
    private readonly Func<bool> _getIsInitializing;
    private readonly Action<bool> _setIsInitializing;
    private readonly Action<int, string> _setChannelInputDevice;
    private readonly Action<int, InputChannelMode> _setChannelInputMode;
    private readonly Action<int, float> _setChannelInputGain;
    private PluginGraph[] _pluginGraphs = Array.Empty<PluginGraph>();
    private bool _suppressPresetUpdates;

    public int GraphCount => _pluginGraphs.Length;

    public MainPluginCoordinator(
        ConfigManager configManager,
        PluginPresetManager presetManager,
        PluginWindowRouter pluginWindowRouter,
        PluginContainerWindowManager containerWindows,
        Func<AudioEngine> getAudioEngine,
        Func<AppConfig> getConfig,
        Func<AudioQualityProfile> getQualityProfile,
        Func<int, ChannelConfig> getOrCreateChannelConfig,
        Func<string, string> getInputDeviceLabel,
        Action updateDynamicWindowWidth,
        Func<int, int> createCopyChannel,
        Action<string> setStatusMessage,
        Func<bool> getMeterScaleVox,
        Func<int> getActiveChannelIndex,
        Action<string> setActiveChannelPresetName,
        Func<IReadOnlyList<ChannelStripViewModel>> getChannels,
        Func<IReadOnlyList<AudioDevice>> getInputDevices,
        Func<AudioDevice?> getSelectedOutputDevice,
        Func<bool> getIsInitializing,
        Action<bool> setIsInitializing,
        Action<int, string> setChannelInputDevice,
        Action<int, InputChannelMode> setChannelInputMode,
        Action<int, float> setChannelInputGain)
    {
        _configManager = configManager;
        _presetManager = presetManager;
        _pluginWindowRouter = pluginWindowRouter;
        _containerWindows = containerWindows;
        _getAudioEngine = getAudioEngine;
        _getConfig = getConfig;
        _getQualityProfile = getQualityProfile;
        _getOrCreateChannelConfig = getOrCreateChannelConfig;
        _getInputDeviceLabel = getInputDeviceLabel;
        _updateDynamicWindowWidth = updateDynamicWindowWidth;
        _createCopyChannel = createCopyChannel;
        _setStatusMessage = setStatusMessage;
        _getMeterScaleVox = getMeterScaleVox;
        _getActiveChannelIndex = getActiveChannelIndex;
        _setActiveChannelPresetName = setActiveChannelPresetName;
        _getChannels = getChannels;
        _getInputDevices = getInputDevices;
        _getSelectedOutputDevice = getSelectedOutputDevice;
        _getIsInitializing = getIsInitializing;
        _setIsInitializing = setIsInitializing;
        _setChannelInputDevice = setChannelInputDevice;
        _setChannelInputMode = setChannelInputMode;
        _setChannelInputGain = setChannelInputGain;
    }

    private AudioEngine AudioEngine => _getAudioEngine();
    private AppConfig Config => _getConfig();

    public void InitializePluginGraphs()
    {
        int channelCount = AudioEngine.Channels.Count;
        if (channelCount <= 0)
        {
            _pluginGraphs = Array.Empty<PluginGraph>();
            return;
        }

        var graphs = new PluginGraph[channelCount];
        for (int i = 0; i < channelCount; i++)
        {
            graphs[i] = new PluginGraph(AudioEngine.Channels[i].PluginChain);
        }

        _pluginGraphs = graphs;
    }

    public PluginGraph? GetGraph(int channelIndex)
    {
        if ((uint)channelIndex >= (uint)_pluginGraphs.Length)
        {
            return null;
        }

        return _pluginGraphs[channelIndex];
    }

    public void SyncGraphsWithConfig()
    {
        bool changed = false;
        for (int i = 0; i < _pluginGraphs.Length; i++)
        {
            var graph = _pluginGraphs[i];
            var config = _getOrCreateChannelConfig(i);
            if (graph.SyncWithChain(config))
            {
                changed = true;
            }
        }

        if (changed)
        {
            _configManager.Save(Config);
        }
    }

    public void LoadPluginsFromConfig()
    {
        for (int i = 0; i < Config.Channels.Count; i++)
        {
            LoadChannelPlugins(i, _getOrCreateChannelConfig(i));
        }

        EnsureOutputSendPlugin();
        NormalizeOutputSendPlugins();
        AudioEngine.RebuildRoutingGraph();
    }

    public void LoadChannelPlugins(int channelIndex, ChannelConfig config)
    {
        if ((uint)channelIndex >= (uint)AudioEngine.Channels.Count)
        {
            return;
        }

        var graph = GetGraph(channelIndex);
        if (graph is null)
        {
            return;
        }

        if (config.Plugins.Count == 0 &&
            !IsCustomPreset(config.PresetName))
        {
            ApplyChannelPreset(channelIndex, config.PresetName);
            return;
        }

        var strip = AudioEngine.Channels[channelIndex];
        var profile = _getQualityProfile();
        var bypassedByContainer = new HashSet<int>();
        for (int i = 0; i < config.Containers.Count; i++)
        {
            var container = config.Containers[i];
            if (!container.IsBypassed)
            {
                continue;
            }

            for (int j = 0; j < container.PluginInstanceIds.Count; j++)
            {
                bypassedByContainer.Add(container.PluginInstanceIds[j]);
            }
        }

        var oldSlots = strip.PluginChain.GetSnapshot();
        bool bypassAdjusted = false;
        bool configChanged = graph.LoadFromConfig(config, pluginConfig =>
        {
            if (string.IsNullOrWhiteSpace(pluginConfig.Type))
            {
                return null;
            }

            IPlugin? plugin = null;
            if (TryParseVstPluginType(pluginConfig.Type, out var format, out var path))
            {
                plugin = new Vst3PluginWrapper(new Vst3PluginInfo
                {
                    Name = Path.GetFileNameWithoutExtension(path),
                    Path = path,
                    Format = format
                });
            }
            else
            {
                plugin = PluginFactory.Create(pluginConfig.Type);
            }

            if (plugin is null)
            {
                return null;
            }

            if (plugin is IQualityConfigurablePlugin qualityPlugin)
            {
                qualityPlugin.ApplyQuality(profile);
            }

            plugin.Initialize(AudioEngine.SampleRate, AudioEngine.BlockSize);
            bool containerBypass = pluginConfig.InstanceId > 0 && bypassedByContainer.Contains(pluginConfig.InstanceId);
            plugin.IsBypassed = pluginConfig.IsBypassed || containerBypass;
            if (containerBypass)
            {
                if (!pluginConfig.IsBypassed)
                {
                    pluginConfig.IsBypassed = true;
                    bypassAdjusted = true;
                }
            }

            bool appliedPreset = false;
            if (pluginConfig.Parameters.Count == 0 &&
                !string.IsNullOrWhiteSpace(pluginConfig.PresetName) &&
                !IsCustomPreset(pluginConfig.PresetName) &&
                _presetManager.TryGetPreset(plugin.Id, pluginConfig.PresetName, out var preset))
            {
                pluginConfig.Parameters = ApplyPresetParameters(plugin, preset);
                appliedPreset = true;
            }

            if (!appliedPreset)
            {
                foreach (var parameter in plugin.Parameters)
                {
                    if (pluginConfig.Parameters.TryGetValue(parameter.Name, out var value))
                    {
                        plugin.SetParameter(parameter.Index, value);
                    }
                }
            }

            if (pluginConfig.State is not null && pluginConfig.State.Length > 0)
            {
                plugin.SetState(pluginConfig.State);
            }

            return new PluginSlot(pluginConfig.InstanceId, plugin, AudioEngine.SampleRate);
        });

        var newSlots = strip.PluginChain.GetSnapshot();
        QueueRemovedPlugins(oldSlots, newSlots);
        bool inputOrderChanged = NormalizeInputPluginOrder(channelIndex, config);
        if (configChanged || bypassAdjusted || inputOrderChanged)
        {
            _configManager.Save(Config);
        }

        RefreshPluginViewModels(channelIndex);
    }

    public void HandlePluginAction(int channelIndex, int pluginInstanceId, int slotIndex)
    {
        if ((uint)channelIndex >= (uint)AudioEngine.Channels.Count)
        {
            return;
        }

        var graph = GetGraph(channelIndex);
        if (graph is null)
        {
            return;
        }

        var strip = AudioEngine.Channels[channelIndex];
        bool isAddPlaceholder = pluginInstanceId <= 0;
        if (isAddPlaceholder)
        {
            var choice = _pluginWindowRouter.ShowPluginBrowser(Config);
            if (choice is null)
            {
                return;
            }

            if (choice.Id == ContainerChoiceId)
            {
                CreateContainer(channelIndex, openWindow: true);
                return;
            }

            IPlugin? newPlugin = choice.IsVst3
                ? new Vst3PluginWrapper(new Vst3PluginInfo { Name = choice.Name, Path = choice.Path, Format = choice.Format })
                : PluginFactory.Create(choice.Id);

            if (newPlugin is null)
            {
                return;
            }

            if (newPlugin is IQualityConfigurablePlugin qualityPlugin)
            {
                qualityPlugin.ApplyQuality(_getQualityProfile());
            }

            if (newPlugin is IChannelOutputPlugin && HasOutputSendPlugin())
            {
                _setStatusMessage("Only one Output Send plugin is allowed.");
                newPlugin.Dispose();
                return;
            }

            bool forceInputFirst = false;
            if (newPlugin is IChannelInputPlugin inputPlugin)
            {
                if (inputPlugin.InputKind == ChannelInputKind.Device && ChannelHasBusInputPlugin(channelIndex))
                {
                    _setStatusMessage("Bus input channels cannot add a device input plugin.");
                    newPlugin.Dispose();
                    return;
                }

                if (ChannelHasInputPlugin(channelIndex))
                {
                    _setStatusMessage("Only one input plugin is allowed per channel.");
                    newPlugin.Dispose();
                    return;
                }

                forceInputFirst = true;
            }

            newPlugin.Initialize(AudioEngine.SampleRate, AudioEngine.BlockSize);

            int insertIndex = slotIndex;
            int chainCount = strip.PluginChain.Count;
            if (insertIndex < 0)
            {
                insertIndex = 0;
            }
            else if (insertIndex > chainCount)
            {
                insertIndex = chainCount;
            }

            if (forceInputFirst)
            {
                insertIndex = 0;
            }

            int instanceId = graph.InsertPlugin(newPlugin, insertIndex);
            if (instanceId <= 0)
            {
                return;
            }

            if (newPlugin is CopyToChannelPlugin copy)
            {
                int targetChannelId = _createCopyChannel(channelIndex);
                copy.TargetChannelId = targetChannelId;
                UpdatePluginStateConfig(channelIndex, instanceId);
            }

            var config = _getOrCreateChannelConfig(channelIndex);
            MarkChannelPresetCustom(config);
            _configManager.Save(Config);
            RefreshPluginViewModels(channelIndex);
            NormalizeOutputSendPlugins();
            AudioEngine.RebuildRoutingGraph();
            _updateDynamicWindowWidth();
            return;
        }

        if (!strip.PluginChain.TryGetSlotById(pluginInstanceId, out var slot, out _)
            || slot is null)
        {
            return;
        }

        if (slot.Plugin is Vst3PluginWrapper vst3)
        {
            _pluginWindowRouter.ShowVst3Editor(vst3);
            return;
        }

        var request = new PluginWindowRequest
        {
            ChannelIndex = channelIndex,
            PluginInstanceId = pluginInstanceId,
            Plugin = slot.Plugin,
            InputDevices = _getInputDevices(),
            Channels = _getChannels(),
            SelectedOutputDevice = _getSelectedOutputDevice(),
            SampleRate = AudioEngine.SampleRate,
            GetPluginParameterValue = GetPluginParameterValue,
            ApplyPluginParameter = ApplyPluginParameter,
            SetPluginBypass = SetPluginBypass,
            RequestNoiseLearn = RequestNoiseLearn,
            SetChannelInputDevice = _setChannelInputDevice,
            SetChannelInputMode = _setChannelInputMode,
            SetChannelInputGain = _setChannelInputGain
        };

        _pluginWindowRouter.ShowPluginParameters(request);
    }

    public void HandleContainerAction(int channelIndex, int containerId)
    {
        if (containerId <= 0)
        {
            CreateContainer(channelIndex, openWindow: true);
            return;
        }

        OpenContainerWindow(channelIndex, containerId);
    }

    public void HandleContainerPluginAction(int channelIndex, int containerId, int pluginInstanceId, int insertIndex)
    {
        if (pluginInstanceId > 0)
        {
            HandlePluginAction(channelIndex, pluginInstanceId, insertIndex);
            return;
        }

        if ((uint)channelIndex >= (uint)AudioEngine.Channels.Count)
        {
            return;
        }

        var graph = GetGraph(channelIndex);
        if (graph is null)
        {
            return;
        }

        var choice = _pluginWindowRouter.ShowPluginBrowser(Config);
        if (choice is null)
        {
            return;
        }

        if (choice.Id == ContainerChoiceId)
        {
            CreateContainer(channelIndex, openWindow: true);
            return;
        }

        IPlugin? newPlugin = choice.IsVst3
            ? new Vst3PluginWrapper(new Vst3PluginInfo { Name = choice.Name, Path = choice.Path, Format = choice.Format })
            : PluginFactory.Create(choice.Id);

        if (newPlugin is null)
        {
            return;
        }

        if (newPlugin is IQualityConfigurablePlugin qualityPlugin)
        {
            qualityPlugin.ApplyQuality(_getQualityProfile());
        }

        if (newPlugin is IChannelOutputPlugin && HasOutputSendPlugin())
        {
            _setStatusMessage("Only one Output Send plugin is allowed.");
            newPlugin.Dispose();
            return;
        }

        if (newPlugin is IChannelInputPlugin)
        {
            _setStatusMessage("Input plugins must be added to the main chain.");
            newPlugin.Dispose();
            return;
        }

        newPlugin.Initialize(AudioEngine.SampleRate, AudioEngine.BlockSize);

        int instanceId = graph.InsertPluginIntoContainer(newPlugin, containerId, insertIndex);
        if (instanceId <= 0)
        {
            return;
        }

        if (newPlugin is CopyToChannelPlugin copy)
        {
            int targetChannelId = _createCopyChannel(channelIndex);
            copy.TargetChannelId = targetChannelId;
            UpdatePluginStateConfig(channelIndex, instanceId);
        }

        var config = _getOrCreateChannelConfig(channelIndex);
        MarkChannelPresetCustom(config);
        _configManager.Save(Config);
        RefreshPluginViewModels(channelIndex);
        EnsureOutputSendPlugin();
        NormalizeOutputSendPlugins();
        AudioEngine.RebuildRoutingGraph();
        _updateDynamicWindowWidth();
    }

    public void RemovePlugin(int channelIndex, int pluginInstanceId)
    {
        if ((uint)channelIndex >= (uint)AudioEngine.Channels.Count)
        {
            return;
        }

        var strip = AudioEngine.Channels[channelIndex];
        if (strip.PluginChain.TryGetSlotById(pluginInstanceId, out var existingSlot, out _) &&
            existingSlot?.Plugin is IChannelInputPlugin input &&
            input.InputKind == ChannelInputKind.Bus)
        {
            return;
        }

        var graph = GetGraph(channelIndex);
        if (graph is null)
        {
            return;
        }

        if (!graph.RemovePlugin(pluginInstanceId, out var removedSlot))
        {
            return;
        }

        if (removedSlot is not null)
        {
            AudioEngine.QueuePluginDisposal(removedSlot.Plugin);
        }

        var config = _getOrCreateChannelConfig(channelIndex);
        MarkChannelPresetCustom(config);
        _configManager.Save(Config);
        RefreshPluginViewModels(channelIndex);
        EnsureOutputSendPlugin();
        NormalizeOutputSendPlugins();
        AudioEngine.RebuildRoutingGraph();
        _updateDynamicWindowWidth();
    }

    public void ReorderPlugins(int channelIndex, int pluginInstanceId, int toIndex)
    {
        if ((uint)channelIndex >= (uint)AudioEngine.Channels.Count)
        {
            return;
        }

        var strip = AudioEngine.Channels[channelIndex];
        if (strip.PluginChain.TryGetSlotById(pluginInstanceId, out var existingSlot, out _) &&
            existingSlot?.Plugin is IChannelInputPlugin)
        {
            return;
        }

        var graph = GetGraph(channelIndex);
        if (graph is null)
        {
            return;
        }

        var slots = strip.PluginChain.GetSnapshot();
        if (slots.Length > 0 && slots[0]?.Plugin is IChannelInputPlugin)
        {
            toIndex = Math.Max(1, toIndex);
        }

        if (!graph.MovePlugin(pluginInstanceId, toIndex))
        {
            return;
        }

        var config = _getOrCreateChannelConfig(channelIndex);
        MarkChannelPresetCustom(config);
        _configManager.Save(Config);
        RefreshPluginViewModels(channelIndex);
        AudioEngine.RebuildRoutingGraph();
    }

    public void ReorderContainerPlugin(int channelIndex, int containerId, int pluginInstanceId, int toIndex)
    {
        if ((uint)channelIndex >= (uint)AudioEngine.Channels.Count)
        {
            return;
        }

        var strip = AudioEngine.Channels[channelIndex];
        if (strip.PluginChain.TryGetSlotById(pluginInstanceId, out var existingSlot, out _) &&
            existingSlot?.Plugin is IChannelInputPlugin)
        {
            return;
        }

        var graph = GetGraph(channelIndex);
        if (graph is null)
        {
            return;
        }

        if (!graph.MovePluginWithinContainer(pluginInstanceId, containerId, toIndex))
        {
            return;
        }

        var config = _getOrCreateChannelConfig(channelIndex);
        MarkChannelPresetCustom(config);
        _configManager.Save(Config);

        RefreshPluginViewModels(channelIndex);
        AudioEngine.RebuildRoutingGraph();
    }

    public void CreateContainer(int channelIndex, bool openWindow)
    {
        var graph = GetGraph(channelIndex);
        if (graph is null)
        {
            return;
        }

        var config = _getOrCreateChannelConfig(channelIndex);
        int containerId = graph.CreateContainer(string.Empty);
        var container = graph.GetContainers().FirstOrDefault(c => c.Id == containerId);
        if (container is not null && string.IsNullOrWhiteSpace(container.Name))
        {
            container.Name = $"Container {containerId}";
        }
        MarkChannelPresetCustom(config);
        _configManager.Save(Config);
        RefreshPluginViewModels(channelIndex);
        _updateDynamicWindowWidth();

        if (openWindow)
        {
            OpenContainerWindow(channelIndex, containerId);
        }
    }

    public void RemoveContainer(int channelIndex, int containerId)
    {
        var graph = GetGraph(channelIndex);
        if (graph is null)
        {
            return;
        }

        if (!graph.RemoveContainer(containerId))
        {
            return;
        }

        var config = _getOrCreateChannelConfig(channelIndex);
        MarkChannelPresetCustom(config);
        _configManager.Save(Config);
        _containerWindows.CloseWindow(channelIndex, containerId);
        RefreshPluginViewModels(channelIndex);
        _updateDynamicWindowWidth();
    }

    public void SetContainerBypass(int channelIndex, int containerId, bool bypassed)
    {
        var graph = GetGraph(channelIndex);
        if (graph is null)
        {
            return;
        }

        if (!graph.SetContainerBypass(containerId, bypassed))
        {
            return;
        }

        var config = _getOrCreateChannelConfig(channelIndex);
        MarkChannelPresetCustom(config);
        _configManager.Save(Config);
    }

    public void ReorderContainer(int channelIndex, int containerId, int targetChainIndex)
    {
        if ((uint)channelIndex >= (uint)AudioEngine.Channels.Count || containerId <= 0)
        {
            return;
        }

        var graph = GetGraph(channelIndex);
        if (graph is null)
        {
            return;
        }

        if (!graph.MoveContainer(containerId, targetChainIndex))
        {
            return;
        }

        var config = _getOrCreateChannelConfig(channelIndex);
        MarkChannelPresetCustom(config);
        _configManager.Save(Config);

        RefreshPluginViewModels(channelIndex);
        _updateDynamicWindowWidth();
    }

    public void OpenContainerWindow(int channelIndex, int containerId)
    {
        if ((uint)channelIndex >= (uint)AudioEngine.Channels.Count)
        {
            return;
        }

        if (_containerWindows.TryGetViewModel(channelIndex, containerId, out var existingViewModel))
        {
            _containerWindows.OpenWindow(channelIndex, containerId, existingViewModel);
            return;
        }

        var graph = GetGraph(channelIndex);
        if (graph is null)
        {
            return;
        }

        var container = graph.GetContainers().FirstOrDefault(c => c.Id == containerId);
        if (container is null)
        {
            return;
        }

        var viewModel = new PluginContainerWindowViewModel(
            channelIndex,
            containerId,
            container.Name,
            (instanceId, insertIndex) => HandleContainerPluginAction(channelIndex, containerId, instanceId, insertIndex),
            instanceId => RemovePlugin(channelIndex, instanceId),
            (instanceId, toIndex) => ReorderContainerPlugin(channelIndex, containerId, instanceId, toIndex),
            EnqueueParameterChange,
            (instanceId, bypass) => UpdatePluginBypassConfig(channelIndex, instanceId, bypass),
            _getMeterScaleVox());

        UpdateContainerWindowViewModel(viewModel, channelIndex, container);
        _containerWindows.OpenWindow(channelIndex, containerId, viewModel);
    }

    public void UpdateContainerWindowViewModel(PluginContainerWindowViewModel viewModel, int channelIndex, PluginContainerConfig container)
    {
        var strip = AudioEngine.Channels[channelIndex];
        var slots = strip.PluginChain.GetSnapshot();
        var slotInfos = new List<PluginSlotInfo>(slots.Length);
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            float latencyMs = 0f;
            string pluginId = string.Empty;
            float[] elevatedValues = [];
            int instanceId = slot?.InstanceId ?? 0;
            int copyTargetChannelId = 0;
            string displayName = slot?.Plugin.Name ?? string.Empty;

            if (slot is not null)
            {
                var plugin = slot.Plugin;
                pluginId = plugin.Id;
                if (AudioEngine.SampleRate > 0)
                {
                    latencyMs = plugin.LatencySamples * 1000f / AudioEngine.SampleRate;
                }

                var elevDefs = ElevatedParameterDefinitions.GetElevations(pluginId);
                if (elevDefs is not null)
                {
                    elevatedValues = new float[elevDefs.Length];
                    var state = plugin.GetState();
                    for (int j = 0; j < elevDefs.Length; j++)
                    {
                        var param = plugin.Parameters.FirstOrDefault(p => p.Index == elevDefs[j].Index);
                        if (param is not null && state.Length >= (elevDefs[j].Index + 1) * sizeof(float))
                        {
                            elevatedValues[j] = BitConverter.ToSingle(state, elevDefs[j].Index * sizeof(float));
                        }
                        else
                        {
                            elevatedValues[j] = elevDefs[j].Default;
                        }
                    }
                }
            }

            if (slot?.Plugin is CopyToChannelPlugin copy)
            {
                copyTargetChannelId = copy.TargetChannelId;
                if (copyTargetChannelId > 0)
                {
                    displayName = $"Copy to Ch {copyTargetChannelId}";
                }
            }

            slotInfos.Add(new PluginSlotInfo
            {
                PluginId = pluginId,
                Name = displayName,
                IsBypassed = slot?.Plugin.IsBypassed ?? false,
                LatencyMs = latencyMs,
                InstanceId = instanceId,
                ElevatedParamValues = elevatedValues,
                CopyTargetChannelId = copyTargetChannelId
            });
        }

        viewModel.UpdateName(container.Name);
        viewModel.MeterScaleVox = _getMeterScaleVox();
        viewModel.UpdatePlugins(slotInfos, container.PluginInstanceIds);
    }

    public void UpdateOpenContainerWindows(int channelIndex, IReadOnlyList<PluginSlotInfo> slotInfos)
    {
        var windows = _containerWindows.GetWindowSnapshot();
        if (windows.Count == 0)
        {
            return;
        }

        var graph = GetGraph(channelIndex);
        if (graph is null)
        {
            return;
        }

        var containers = graph.GetContainers();
        for (int i = 0; i < windows.Count; i++)
        {
            var entry = windows[i];
            if (entry.ChannelIndex != channelIndex)
            {
                continue;
            }

            var container = containers.FirstOrDefault(c => c.Id == entry.ContainerId);
            if (container is null)
            {
                _containerWindows.CloseWindow(entry.ChannelIndex, entry.ContainerId);
                continue;
            }

            entry.ViewModel.UpdateName(container.Name);
            entry.ViewModel.UpdatePlugins(slotInfos, container.PluginInstanceIds);
        }
    }

    public void QueueRemovedPlugins(PluginSlot?[] oldSlots, PluginSlot?[] newSlots)
    {
        if (oldSlots.Length == 0)
        {
            return;
        }

        var newSet = new HashSet<IPlugin>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < newSlots.Length; i++)
        {
            if (newSlots[i] is { } slot)
            {
                newSet.Add(slot.Plugin);
            }
        }

        for (int i = 0; i < oldSlots.Length; i++)
        {
            if (oldSlots[i] is { } slot && !newSet.Contains(slot.Plugin))
            {
                AudioEngine.QueuePluginDisposal(slot.Plugin);
            }
        }
    }

    public void ApplyPluginParameter(int channelIndex, int pluginInstanceId, int parameterIndex, string parameterName, float value)
    {
        AudioEngine.EnqueueParameterChange(new ParameterChange
        {
            ChannelId = channelIndex,
            Type = ParameterType.PluginParameter,
            PluginInstanceId = pluginInstanceId,
            ParameterIndex = parameterIndex,
            Value = value
        });

        UpdatePluginParameterConfig(channelIndex, pluginInstanceId, parameterName, value, markPresetDirty: true);
        UpdatePluginStateConfig(channelIndex, pluginInstanceId);

        var strip = AudioEngine.Channels.ElementAtOrDefault(channelIndex);
        if (strip is not null && strip.PluginChain.TryGetSlotById(pluginInstanceId, out var slot, out _) &&
            slot?.Plugin is IChannelOutputPlugin)
        {
            RefreshPluginViewModels(channelIndex);
        }
    }

    public void SetPluginBypass(int channelIndex, int pluginInstanceId, bool bypassed)
    {
        var channels = _getChannels();
        if ((uint)channelIndex >= (uint)channels.Count)
        {
            return;
        }

        var channel = channels[channelIndex];
        int slotIndex = FindPluginSlotIndex(channel, pluginInstanceId);
        if (slotIndex >= 0 && slotIndex < channel.PluginSlots.Count)
        {
            channel.PluginSlots[slotIndex].SetBypassSilent(bypassed);
        }
    }

    public void RequestNoiseLearn(int channelIndex, int pluginInstanceId)
    {
        AudioEngine.EnqueueParameterChange(new ParameterChange
        {
            ChannelId = channelIndex,
            Type = ParameterType.PluginCommand,
            PluginInstanceId = pluginInstanceId,
            Command = PluginCommandType.ToggleNoiseLearn
        });
    }

    public void RefreshPluginViewModels(int channelIndex)
    {
        var strip = AudioEngine.Channels[channelIndex];
        var slots = strip.PluginChain.GetSnapshot();
        var slotInfos = new List<PluginSlotInfo>(slots.Length);
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            float latencyMs = 0f;
            string pluginId = string.Empty;
            float[] elevatedValues = [];
            int instanceId = slot?.InstanceId ?? 0;
            int copyTargetChannelId = 0;
            string displayName = slot?.Plugin.Name ?? string.Empty;

            if (slot is not null)
            {
                var plugin = slot.Plugin;
                pluginId = plugin.Id;
                if (AudioEngine.SampleRate > 0)
                {
                    latencyMs = plugin.LatencySamples * 1000f / AudioEngine.SampleRate;
                }

                if (plugin is InputPlugin)
                {
                    string deviceLabel = _getInputDeviceLabel(_getOrCreateChannelConfig(channelIndex).InputDeviceId);
                    displayName = $"Input ({deviceLabel})";
                }
                else if (plugin is BusInputPlugin)
                {
                    displayName = "Bus Input";
                }
                else if (plugin is OutputSendPlugin send)
                {
                    string modeLabel = send.Mode switch
                    {
                        OutputSendMode.Left => "Left",
                        OutputSendMode.Right => "Right",
                        _ => "Both"
                    };
                    displayName = $"Output Send ({modeLabel})";
                }

                var elevDefs = ElevatedParameterDefinitions.GetElevations(pluginId);
                if (elevDefs is not null)
                {
                    elevatedValues = new float[elevDefs.Length];
                    for (int j = 0; j < elevDefs.Length; j++)
                    {
                        var state = plugin.GetState();
                        var param = plugin.Parameters.FirstOrDefault(p => p.Index == elevDefs[j].Index);
                        if (param is not null && state.Length >= (elevDefs[j].Index + 1) * sizeof(float))
                        {
                            elevatedValues[j] = BitConverter.ToSingle(state, elevDefs[j].Index * sizeof(float));
                        }
                        else
                        {
                            elevatedValues[j] = elevDefs[j].Default;
                        }
                    }
                }
            }

            if (slot?.Plugin is CopyToChannelPlugin copy)
            {
                copyTargetChannelId = copy.TargetChannelId;
                if (copyTargetChannelId > 0)
                {
                    displayName = $"Copy to Ch {copyTargetChannelId}";
                }
            }

            slotInfos.Add(new PluginSlotInfo
            {
                PluginId = pluginId,
                Name = displayName,
                IsBypassed = slot?.Plugin.IsBypassed ?? false,
                LatencyMs = latencyMs,
                InstanceId = instanceId,
                ElevatedParamValues = elevatedValues,
                CopyTargetChannelId = copyTargetChannelId
            });
        }

        var channels = _getChannels();
        if ((uint)channelIndex < (uint)channels.Count)
        {
            var viewModel = channels[channelIndex];
            viewModel.UpdatePlugins(slotInfos);
            viewModel.UpdateContainers(BuildContainerInfos(channelIndex));
        }

        UpdateOpenContainerWindows(channelIndex, slotInfos);
    }

    public IReadOnlyList<PluginContainerInfo> BuildContainerInfos(int channelIndex)
    {
        var graph = GetGraph(channelIndex);
        if (graph is null)
        {
            return Array.Empty<PluginContainerInfo>();
        }

        var containers = graph.GetContainers();
        if (containers.Count == 0)
        {
            return Array.Empty<PluginContainerInfo>();
        }

        var list = new List<PluginContainerInfo>(containers.Count);
        for (int i = 0; i < containers.Count; i++)
        {
            var container = containers[i];
            list.Add(new PluginContainerInfo
            {
                ContainerId = container.Id,
                Name = container.Name,
                IsBypassed = container.IsBypassed,
                PluginInstanceIds = container.PluginInstanceIds.ToArray()
            });
        }

        return list;
    }

    public void UpdatePluginParameterConfig(int channelIndex, int instanceId, string parameterName, float value, bool markPresetDirty)
    {
        var graph = GetGraph(channelIndex);
        if (graph is null)
        {
            return;
        }

        graph.SetPluginParameter(instanceId, parameterName, value);

        if (markPresetDirty)
        {
            MarkPluginPresetCustom(channelIndex, instanceId);
        }
        _configManager.Save(Config);
    }

    public void UpdatePluginStateConfig(int channelIndex, int instanceId)
    {
        var graph = GetGraph(channelIndex);
        if (graph is null)
        {
            return;
        }

        graph.SetPluginState(instanceId);
        _configManager.Save(Config);

        var strip = AudioEngine.Channels.ElementAtOrDefault(channelIndex);
        if (strip is not null &&
            strip.PluginChain.TryGetSlotById(instanceId, out var slot, out _) &&
            slot?.Plugin is IRoutingDependencyProvider)
        {
            AudioEngine.RebuildRoutingGraph();
        }
    }

    public void UpdatePluginBypassConfig(int channelIndex, int instanceId, bool bypass)
    {
        var graph = GetGraph(channelIndex);
        if (graph is null)
        {
            return;
        }

        graph.SetPluginBypass(instanceId, bypass);
        MarkPluginPresetCustom(channelIndex, instanceId);
        _configManager.Save(Config);

        var strip = AudioEngine.Channels.ElementAtOrDefault(channelIndex);
        if (strip is not null &&
            strip.PluginChain.TryGetSlotById(instanceId, out var slot, out _) &&
            slot?.Plugin is IChannelOutputPlugin)
        {
            NormalizeOutputSendPlugins();
        }
    }

    public float GetPluginParameterValue(int channelIndex, int instanceId, string parameterName, float fallback)
    {
        var graph = GetGraph(channelIndex);
        if (graph is null || !graph.TryGetPluginConfig(instanceId, out var pluginConfig))
        {
            return fallback;
        }

        return pluginConfig.Parameters.TryGetValue(parameterName, out var value) ? value : fallback;
    }

    public void EnsureOutputSendPlugin()
    {
        if (HasOutputSendPlugin())
        {
            return;
        }

        if (AudioEngine.Channels.Count == 0)
        {
            return;
        }

        int channelIndex = 0;
        var graph = GetGraph(channelIndex);
        if (graph is null)
        {
            return;
        }

        var outputSend = new OutputSendPlugin();
        outputSend.Initialize(AudioEngine.SampleRate, AudioEngine.BlockSize);

        int insertIndex = AudioEngine.Channels[channelIndex].PluginChain.Count;
        int instanceId = graph.InsertPlugin(outputSend, insertIndex);
        if (instanceId <= 0)
        {
            return;
        }

        var config = _getOrCreateChannelConfig(channelIndex);
        MarkChannelPresetCustom(config);
        _configManager.Save(Config);
        RefreshPluginViewModels(channelIndex);
        _updateDynamicWindowWidth();
    }

    public void NormalizeOutputSendPlugins()
    {
        bool foundActive = false;
        bool changed = false;
        PluginSlot? firstOutputSend = null;
        int firstChannelIndex = -1;
        PluginGraph? firstGraph = null;
        ChannelConfig? firstConfig = null;

        for (int i = 0; i < AudioEngine.Channels.Count; i++)
        {
            var graph = GetGraph(i);
            var config = _getOrCreateChannelConfig(i);
            var slots = AudioEngine.Channels[i].PluginChain.GetSnapshot();
            for (int j = 0; j < slots.Length; j++)
            {
                var slot = slots[j];
                if (slot?.Plugin is not IChannelOutputPlugin)
                {
                    continue;
                }

                if (firstOutputSend is null)
                {
                    firstOutputSend = slot;
                    firstChannelIndex = i;
                    firstGraph = graph;
                    firstConfig = config;
                }

                if (!foundActive && !slot.Plugin.IsBypassed)
                {
                    foundActive = true;
                    continue;
                }

                if (!slot.Plugin.IsBypassed)
                {
                    slot.Plugin.IsBypassed = true;
                    if (graph is not null)
                    {
                        graph.SetPluginBypass(slot.InstanceId, true);
                    }

                    AudioEngine.EnqueueParameterChange(new ParameterChange
                    {
                        ChannelId = i,
                        Type = ParameterType.PluginBypass,
                        PluginInstanceId = slot.InstanceId,
                        Value = 1f
                    });

                    MarkChannelPresetCustom(config);
                    changed = true;
                }
            }
        }

        if (!foundActive && firstOutputSend is not null && firstOutputSend.Plugin.IsBypassed)
        {
            firstOutputSend.Plugin.IsBypassed = false;
            if (firstGraph is not null)
            {
                firstGraph.SetPluginBypass(firstOutputSend.InstanceId, false);
            }

            AudioEngine.EnqueueParameterChange(new ParameterChange
            {
                ChannelId = firstChannelIndex,
                Type = ParameterType.PluginBypass,
                PluginInstanceId = firstOutputSend.InstanceId,
                Value = 0f
            });

            if (firstConfig is not null)
            {
                MarkChannelPresetCustom(firstConfig);
            }
            changed = true;
        }

        if (changed)
        {
            _configManager.Save(Config);
        }
    }

    public bool HasOutputSendPlugin()
    {
        for (int i = 0; i < AudioEngine.Channels.Count; i++)
        {
            var slots = AudioEngine.Channels[i].PluginChain.GetSnapshot();
            for (int j = 0; j < slots.Length; j++)
            {
                if (slots[j]?.Plugin is IChannelOutputPlugin)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public bool ChannelHasInputPlugin(int channelIndex)
    {
        if ((uint)channelIndex >= (uint)AudioEngine.Channels.Count)
        {
            return false;
        }

        var slots = AudioEngine.Channels[channelIndex].PluginChain.GetSnapshot();
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i]?.Plugin is IChannelInputPlugin)
            {
                return true;
            }
        }

        return false;
    }

    public bool ChannelHasBusInputPlugin(int channelIndex)
    {
        if ((uint)channelIndex >= (uint)AudioEngine.Channels.Count)
        {
            return false;
        }

        var slots = AudioEngine.Channels[channelIndex].PluginChain.GetSnapshot();
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i]?.Plugin is IChannelInputPlugin input && input.InputKind == ChannelInputKind.Bus)
            {
                return true;
            }
        }

        return false;
    }

    public bool NormalizeInputPluginOrder(int channelIndex, ChannelConfig config)
    {
        var graph = GetGraph(channelIndex);
        if (graph is null)
        {
            return false;
        }

        var strip = AudioEngine.Channels[channelIndex];
        var slots = strip.PluginChain.GetSnapshot();
        if (slots.Length == 0)
        {
            return false;
        }

        int selectedIndex = -1;
        int selectedPriority = int.MinValue;
        int selectedInstanceId = 0;
        var toRemove = new List<int>();
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot is null)
            {
                continue;
            }

            if (slot.Plugin is not IChannelInputPlugin input)
            {
                continue;
            }

            int priority = (int)input.InputKind;
            if (selectedIndex < 0 || priority > selectedPriority)
            {
                if (selectedIndex >= 0)
                {
                    toRemove.Add(selectedInstanceId);
                }

                selectedIndex = i;
                selectedPriority = priority;
                selectedInstanceId = slot.InstanceId;
            }
            else
            {
                toRemove.Add(slot.InstanceId);
            }
        }

        bool changed = false;
        if (selectedIndex < 0)
        {
            return false;
        }

        for (int i = 0; i < toRemove.Count; i++)
        {
            if (graph.RemovePlugin(toRemove[i], out var removedSlot) && removedSlot is not null)
            {
                AudioEngine.QueuePluginDisposal(removedSlot.Plugin);
                changed = true;
            }
        }

        if (selectedIndex != 0)
        {
            graph.MovePlugin(selectedInstanceId, 0);
            changed = true;
        }

        if (changed)
        {
            graph.SyncWithChain(config);
        }

        return changed;
    }

    public void ApplyChannelPreset(int channelIndex, string presetName)
    {
        if ((uint)channelIndex >= (uint)AudioEngine.Channels.Count)
        {
            return;
        }

        var config = _getOrCreateChannelConfig(channelIndex);
        if (IsCustomPreset(presetName))
        {
            config.PresetName = PluginPresetManager.CustomPresetName;
            _configManager.Save(Config);
            return;
        }

        if (!_presetManager.TryGetChainPreset(presetName, out var chainPreset))
        {
            return;
        }

        AudioEngine.BeginPresetLoad();
        bool wasInitializing = _getIsInitializing();
        _setIsInitializing(true);
        try
        {
            var strip = AudioEngine.Channels[channelIndex];
            var oldSlots = strip.PluginChain.GetSnapshot();
            PluginSlot? preservedInputSlot = null;
            int preservedPriority = int.MinValue;
            for (int i = 0; i < oldSlots.Length; i++)
            {
                var slot = oldSlots[i];
                if (slot?.Plugin is not IChannelInputPlugin input)
                {
                    continue;
                }

                int priority = (int)input.InputKind;
                if (preservedInputSlot is null || priority > preservedPriority)
                {
                    preservedInputSlot = slot;
                    preservedPriority = priority;
                }
            }

            var profile = _getQualityProfile();
            int extraSlotCount = preservedInputSlot is null ? 0 : 1;
            var pluginSlots = new List<PluginSlot>(chainPreset.Entries.Count + extraSlotCount);
            var pluginConfigs = new List<PluginConfig>(chainPreset.Entries.Count);
            int nextInstanceId = 0;

            if (preservedInputSlot is not null)
            {
                pluginSlots.Add(preservedInputSlot);
                nextInstanceId = Math.Max(nextInstanceId, preservedInputSlot.InstanceId);
            }

            foreach (var entry in chainPreset.Entries)
            {
                if (preservedInputSlot is not null &&
                    (string.Equals(entry.PluginId, "builtin:input", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(entry.PluginId, "builtin:bus-input", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                IPlugin? plugin = PluginFactory.Create(entry.PluginId);
                if (plugin is null)
                {
                    continue;
                }

                if (plugin is IQualityConfigurablePlugin qualityPlugin)
                {
                    qualityPlugin.ApplyQuality(profile);
                }

                plugin.Initialize(AudioEngine.SampleRate, AudioEngine.BlockSize);

                if (!_presetManager.TryGetPreset(plugin.Id, entry.PresetName, out var preset))
                {
                    preset = _presetManager.GetDefaultPreset(plugin);
                }

                var parameterMap = ApplyPresetParameters(plugin, preset);

                int instanceId = ++nextInstanceId;
                pluginSlots.Add(new PluginSlot(instanceId, plugin, AudioEngine.SampleRate));
                pluginConfigs.Add(new PluginConfig
                {
                    InstanceId = instanceId,
                    Type = plugin.Id,
                    IsBypassed = plugin.IsBypassed,
                    PresetName = entry.PresetName,
                    Parameters = parameterMap,
                    State = plugin.GetState()
                });
            }

            var containerConfigs = new List<PluginContainerConfig>();
            if (chainPreset.Containers.Count > 0)
            {
                int nextContainerId = 0;
                for (int i = 0; i < chainPreset.Containers.Count; i++)
                {
                    var container = chainPreset.Containers[i];
                    var ids = new List<int>();
                    for (int j = 0; j < container.PluginIndices.Count; j++)
                    {
                        int index = container.PluginIndices[j];
                        if ((uint)index < (uint)pluginConfigs.Count)
                        {
                            ids.Add(pluginConfigs[index].InstanceId);
                        }
                    }

                    containerConfigs.Add(new PluginContainerConfig
                    {
                        Id = ++nextContainerId,
                        Name = container.Name,
                        IsBypassed = container.IsBypassed,
                        PluginInstanceIds = ids
                    });
                }
            }

            if (containerConfigs.Count > 0)
            {
                var bypassed = new HashSet<int>();
                for (int i = 0; i < containerConfigs.Count; i++)
                {
                    if (!containerConfigs[i].IsBypassed)
                    {
                        continue;
                    }

                    for (int j = 0; j < containerConfigs[i].PluginInstanceIds.Count; j++)
                    {
                        bypassed.Add(containerConfigs[i].PluginInstanceIds[j]);
                    }
                }

                if (bypassed.Count > 0)
                {
                    for (int i = 0; i < pluginSlots.Count; i++)
                    {
                        var slot = pluginSlots[i];
                        if (bypassed.Contains(slot.InstanceId))
                        {
                            slot.Plugin.IsBypassed = true;
                        }
                    }

                    for (int i = 0; i < pluginConfigs.Count; i++)
                    {
                        if (bypassed.Contains(pluginConfigs[i].InstanceId))
                        {
                            pluginConfigs[i].IsBypassed = true;
                        }
                    }
                }
            }

            var newSlots = pluginSlots.ToArray();
            strip.PluginChain.ReplaceAll(newSlots);
            QueueRemovedPlugins(oldSlots, newSlots);

            config.PresetName = chainPreset.Name;
            config.Plugins = pluginConfigs;
            config.Containers = containerConfigs;
            GetGraph(channelIndex)?.SyncWithChain(config);
            NormalizeInputPluginOrder(channelIndex, config);
            _configManager.Save(Config);

            EnsureOutputSendPlugin();
            NormalizeOutputSendPlugins();
            AudioEngine.RebuildRoutingGraph();
            RefreshPluginViewModels(channelIndex);
            _updateDynamicWindowWidth();
        }
        finally
        {
            _setIsInitializing(wasInitializing);
            AudioEngine.EndPresetLoad();
        }
    }

    public bool IsCustomPreset(string? presetName)
    {
        return string.IsNullOrWhiteSpace(presetName) ||
               presetName.Equals(PluginPresetManager.CustomPresetName, StringComparison.OrdinalIgnoreCase);
    }

    public Dictionary<string, float> ApplyPresetParameters(IPlugin plugin, PluginPreset preset)
    {
        var parameters = new Dictionary<string, float>(plugin.Parameters.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in plugin.Parameters)
        {
            if (preset.Parameters.TryGetValue(parameter.Name, out var value))
            {
                plugin.SetParameter(parameter.Index, value);
                parameters[parameter.Name] = value;
            }
            else
            {
                parameters[parameter.Name] = parameter.DefaultValue;
            }
        }
        return parameters;
    }

    public void MarkChannelPresetCustom(ChannelConfig config)
    {
        if (_suppressPresetUpdates)
        {
            return;
        }

        if (!IsCustomPreset(config.PresetName))
        {
            config.PresetName = PluginPresetManager.CustomPresetName;
        }

        int index = Config.Channels.IndexOf(config);
        if (index == _getActiveChannelIndex())
        {
            _setActiveChannelPresetName(PluginPresetManager.CustomPresetName);
        }
    }

    public void MarkPluginPresetCustom(int channelIndex, int instanceId)
    {
        if (_suppressPresetUpdates)
        {
            return;
        }

        var config = _getOrCreateChannelConfig(channelIndex);
        var graph = GetGraph(channelIndex);
        if (graph is not null && graph.TryGetPluginConfig(instanceId, out var pluginConfig))
        {
            pluginConfig.PresetName = PluginPresetManager.CustomPresetName;
        }

        config.PresetName = PluginPresetManager.CustomPresetName;
        if (channelIndex == _getActiveChannelIndex())
        {
            _setActiveChannelPresetName(PluginPresetManager.CustomPresetName);
        }
    }

    public static int FindPluginSlotIndex(ChannelStripViewModel channel, int instanceId)
    {
        int count = Math.Max(0, channel.PluginSlots.Count - 1);
        for (int i = 0; i < count; i++)
        {
            if (channel.PluginSlots[i].InstanceId == instanceId)
            {
                return i;
            }
        }

        return -1;
    }

    public void EnqueueParameterChange(ParameterChange change)
    {
        AudioEngine.EnqueueParameterChange(change);
    }

    public bool TryParseVstPluginType(string type, out VstPluginFormat format, out string path)
    {
        if (type.StartsWith("vst3:", StringComparison.OrdinalIgnoreCase))
        {
            format = VstPluginFormat.Vst3;
            path = type["vst3:".Length..];
            return !string.IsNullOrWhiteSpace(path);
        }

        if (type.StartsWith("vst2:", StringComparison.OrdinalIgnoreCase))
        {
            format = VstPluginFormat.Vst2;
            path = type["vst2:".Length..];
            return !string.IsNullOrWhiteSpace(path);
        }

        format = VstPluginFormat.Vst3;
        path = string.Empty;
        return false;
    }
}
