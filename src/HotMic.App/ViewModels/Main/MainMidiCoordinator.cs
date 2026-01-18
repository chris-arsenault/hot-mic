using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using HotMic.Common.Configuration;
using HotMic.Core.Midi;

namespace HotMic.App.ViewModels;

internal sealed class MainMidiCoordinator : IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly ConfigManager _configManager;
    private readonly MainPluginCoordinator _pluginCoordinator;
    private readonly Func<AppConfig> _getConfig;
    private MidiManager? _midiManager;

    public MainMidiCoordinator(
        MainViewModel viewModel,
        ConfigManager configManager,
        MainPluginCoordinator pluginCoordinator,
        Func<AppConfig> getConfig)
    {
        _viewModel = viewModel;
        _configManager = configManager;
        _pluginCoordinator = pluginCoordinator;
        _getConfig = getConfig;
    }

    public void Initialize()
    {
        try
        {
            _midiManager = new MidiManager(_getConfig().Midi);
            _midiManager.BindingTriggered += OnMidiBindingTriggered;
            _midiManager.Start();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MIDI] Failed to initialize: {ex.Message}");
        }
    }

    public void StartMidiLearn(string targetPath, Action<int, int>? onLearned = null)
    {
        _midiManager?.StartLearn(targetPath, onLearned);
    }

    public void CancelMidiLearn()
    {
        _midiManager?.CancelLearn();
    }

    public bool IsMidiLearning => _midiManager?.IsLearning ?? false;

    public IReadOnlyList<string> MidiDevices => _midiManager?.GetAvailableDevices() ?? [];

    public string? CurrentMidiDevice => _midiManager?.CurrentDevice;

    public void AddMidiBinding(string targetPath, int ccNumber, int? channel, float minValue, float maxValue)
    {
        if (_midiManager == null)
        {
            return;
        }

        var binding = new MidiBinding
        {
            TargetPath = targetPath,
            CcNumber = ccNumber,
            Channel = channel,
            MinValue = minValue,
            MaxValue = maxValue
        };

        _midiManager.AddBinding(binding);
        _configManager.Save(_getConfig());
    }

    public void RemoveMidiBinding(string targetPath)
    {
        _midiManager?.RemoveBinding(targetPath);
        _configManager.Save(_getConfig());
    }

    public MidiBinding? GetMidiBinding(string targetPath)
    {
        return _midiManager?.GetBinding(targetPath);
    }

    public IReadOnlyList<MidiBinding> GetAllMidiBindings()
    {
        return _midiManager?.GetAllBindings() ?? [];
    }

    public void SetMidiEnabled(bool enabled)
    {
        var config = _getConfig();
        config.Midi.Enabled = enabled;
        _midiManager?.ApplyConfig(config.Midi);
        _configManager.Save(config);
    }

    public void SetMidiDevice(string? deviceName)
    {
        var config = _getConfig();
        config.Midi.DeviceName = deviceName;
        _midiManager?.ApplyConfig(config.Midi);
        _configManager.Save(config);
    }

    public void Dispose()
    {
        if (_midiManager is null)
        {
            return;
        }

        _midiManager.BindingTriggered -= OnMidiBindingTriggered;
        _midiManager.Dispose();
        _midiManager = null;
    }

    private void OnMidiBindingTriggered(object? sender, MidiBindingEventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            ApplyMidiBinding(e.TargetPath, e.Value);
        });
    }

    private void ApplyMidiBinding(string targetPath, float value)
    {
        var parts = targetPath.Split('.');
        if (parts.Length < 2)
        {
            return;
        }

        int channelIndex = -1;
        if (parts[0].StartsWith("channel", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(parts[0].AsSpan("channel".Length), out int parsedIndex))
        {
            channelIndex = parsedIndex - 1;
        }

        if (channelIndex < 0 || channelIndex >= _viewModel.Channels.Count)
        {
            return;
        }

        var channel = _viewModel.Channels[channelIndex];

        switch (parts[1])
        {
            case "inputGain":
                channel.InputGainDb = value;
                break;
            case "outputGain":
                channel.OutputGainDb = value;
                break;
            case "mute":
                channel.IsMuted = value >= 0.5f;
                break;
            case "plugin" when parts.Length >= 4:
                if (int.TryParse(parts[2], out int pluginInstanceId) &&
                    int.TryParse(parts[3], out int paramIndex))
                {
                    if (pluginInstanceId <= 0)
                    {
                        return;
                    }

                    _pluginCoordinator.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, "midi", value);

                    int slotIndex = MainPluginCoordinator.FindPluginSlotIndex(channel, pluginInstanceId);
                    if (slotIndex >= 0 && slotIndex < channel.PluginSlots.Count - 1)
                    {
                        var slot = channel.PluginSlots[slotIndex];
                        if (slot.ElevatedParams is { } elevParams)
                        {
                            for (int i = 0; i < elevParams.Length; i++)
                            {
                                if (elevParams[i].Index == paramIndex)
                                {
                                    slot.SetParamValueSilent(i, value);
                                    break;
                                }
                            }
                        }
                    }
                }
                break;
        }
    }
}
