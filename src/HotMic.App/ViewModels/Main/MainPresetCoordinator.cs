using System;
using System.Collections.Generic;
using System.Linq;
using HotMic.Common.Configuration;
using HotMic.Core.Presets;

namespace HotMic.App.ViewModels;

internal sealed class MainPresetCoordinator
{
    private readonly MainViewModel _viewModel;
    private readonly ConfigManager _configManager;
    private readonly PluginPresetManager _presetManager;
    private readonly MainPluginCoordinator _pluginCoordinator;
    private readonly Func<AppConfig> _getConfig;
    private readonly Func<int, ChannelConfig> _getOrCreateChannelConfig;

    public MainPresetCoordinator(
        MainViewModel viewModel,
        ConfigManager configManager,
        PluginPresetManager presetManager,
        MainPluginCoordinator pluginCoordinator,
        Func<AppConfig> getConfig,
        Func<int, ChannelConfig> getOrCreateChannelConfig)
    {
        _viewModel = viewModel;
        _configManager = configManager;
        _presetManager = presetManager;
        _pluginCoordinator = pluginCoordinator;
        _getConfig = getConfig;
        _getOrCreateChannelConfig = getOrCreateChannelConfig;
    }

    public IReadOnlyList<string> GetPresetOptions() => _presetManager.GetChainPresetNames();

    public void SelectChannelPreset(int channelIndex, string presetName)
    {
        _pluginCoordinator.ApplyChannelPreset(channelIndex, presetName);
        UpdatePresetNameProperty(channelIndex);
    }

    public void SaveCurrentAsPreset(int channelIndex, string presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
        {
            return;
        }

        if (_presetManager.IsBuiltInPreset(presetName))
        {
            return;
        }

        var config = _getOrCreateChannelConfig(channelIndex);
        var graph = _pluginCoordinator.GetGraph(channelIndex);
        if (graph is null)
        {
            return;
        }

        var plugins = new List<(string pluginId, Dictionary<string, float> parameters)>();

        foreach (var pluginConfig in config.Plugins)
        {
            if (string.IsNullOrWhiteSpace(pluginConfig.Type))
            {
                continue;
            }

            var parameters = new Dictionary<string, float>(pluginConfig.Parameters, StringComparer.OrdinalIgnoreCase);
            plugins.Add((pluginConfig.Type, parameters));
        }

        var containers = graph.BuildPresetContainers();
        if (_presetManager.SaveChainPreset(presetName, plugins, containers))
        {
            config.PresetName = presetName;
            _configManager.Save(_getConfig());
            UpdatePresetNameProperty(channelIndex);
        }
    }

    public bool CanOverwritePreset(string presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
        {
            return false;
        }

        if (_presetManager.IsBuiltInPreset(presetName))
        {
            return false;
        }

        if (string.Equals(presetName, PluginPresetManager.CustomPresetName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public bool DeleteUserPreset(string presetName)
    {
        if (_presetManager.DeleteChainPreset(presetName))
        {
            var config = _getConfig();
            for (int i = 0; i < config.Channels.Count; i++)
            {
                var channelName = GetChannelPresetName(i);
                if (string.Equals(channelName, presetName, StringComparison.OrdinalIgnoreCase))
                {
                    var channelConfig = _getOrCreateChannelConfig(i);
                    channelConfig.PresetName = PluginPresetManager.CustomPresetName;
                    if (i == _viewModel.ActiveChannelIndex)
                    {
                        _viewModel.ActiveChannelPresetName = PluginPresetManager.CustomPresetName;
                    }
                }
            }

            _configManager.Save(config);
            return true;
        }

        return false;
    }

    public string GetChannelPresetName(int channelIndex)
    {
        var config = _getConfig().Channels.ElementAtOrDefault(channelIndex);
        if (config is null || _pluginCoordinator.IsCustomPreset(config.PresetName))
        {
            return PluginPresetManager.CustomPresetName;
        }

        return config.PresetName;
    }

    public void UpdatePresetNameProperty(int channelIndex)
    {
        var presetName = GetChannelPresetName(channelIndex);
        if (channelIndex == _viewModel.ActiveChannelIndex)
        {
            _viewModel.ActiveChannelPresetName = presetName;
        }
    }
}
