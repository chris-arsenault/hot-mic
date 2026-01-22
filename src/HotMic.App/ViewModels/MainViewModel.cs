using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotMic.Common.Configuration;
using HotMic.Common.Models;
using HotMic.Core.Engine;
using HotMic.Core.Midi;
using HotMic.Core.Presets;

namespace HotMic.App.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ConfigManager _configManager = new();
    private readonly DeviceManager _deviceManager = new();
    private readonly PluginPresetManager _presetManager = PluginPresetManager.Default;
    private readonly MainWindowRouter _windowRouter = new();
    private readonly PluginWindowRouter _pluginWindowRouter = new();
    private readonly PluginContainerWindowManager _containerWindowManager = new();
    private readonly MainPluginCoordinator _pluginCoordinator;
    private readonly MainChannelCoordinator _channelCoordinator;
    private readonly MainAudioEngineCoordinator _audioEngineCoordinator;
    private readonly MainMidiCoordinator _midiCoordinator;
    private readonly MainPresetCoordinator _presetCoordinator;
    private readonly DispatcherTimer _meterTimer;
    private AudioEngine _audioEngine;
    private AppConfig _config;
    private bool _isInitializing = true;

    public MainViewModel()
    {
        _config = _configManager.LoadOrDefault();
        EnsureDefaultChannelConfig();

        _audioEngine = new AudioEngine(_config.AudioSettings, _config.Channels.Count);

        InputDevices = new ObservableCollection<AudioDevice>(_deviceManager.GetInputDevices());
        OutputDevices = new ObservableCollection<AudioDevice>(_deviceManager.GetOutputDevices());

        SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.Id == _config.AudioSettings.OutputDeviceId) ??
            OutputDevices.FirstOrDefault(d => d.Name.Contains("VB-Cable", StringComparison.OrdinalIgnoreCase));
        SelectedMonitorDevice = OutputDevices.FirstOrDefault(d => d.Id == _config.AudioSettings.MonitorOutputDeviceId);

        ToggleViewCommand = new RelayCommand(ToggleView);
        ToggleAlwaysOnTopCommand = new RelayCommand(() => AlwaysOnTop = !AlwaysOnTop);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        ApplyDeviceSelectionCommand = new RelayCommand(ApplyDeviceSelection);

        _pluginCoordinator = new MainPluginCoordinator(
            _configManager,
            _presetManager,
            _pluginWindowRouter,
            _containerWindowManager,
            () => _audioEngine,
            () => _config,
            GetQualityProfile,
            GetOrCreateChannelConfig,
            GetInputDeviceLabel,
            UpdateDynamicWindowWidth,
            CreateCopyChannel,
            value => StatusMessage = value,
            () => MeterScaleVox,
            () => ActiveChannelIndex,
            value => ActiveChannelPresetName = value,
            () => Channels,
            () => InputDevices,
            () => SelectedOutputDevice,
            () => IsInitializing,
            value => IsInitializing = value,
            (channelIndex, gain) => ApplyChannelInputGain(channelIndex, gain));

        _channelCoordinator = new MainChannelCoordinator(
            this,
            _configManager,
            _pluginCoordinator,
            () => _audioEngine,
            () => _config,
            GetOrCreateChannelConfig);

        _audioEngineCoordinator = new MainAudioEngineCoordinator(
            this,
            _configManager,
            _pluginCoordinator,
            _channelCoordinator,
            _containerWindowManager,
            () => _audioEngine,
            engine => _audioEngine = engine,
            () => _config);
        _pluginCoordinator.SetAnalysisTapSignalHandler(_audioEngineCoordinator.SetAnalysisTapRequestedSignals);

        _channelCoordinator.SetRebuildHandler(index =>
            _audioEngineCoordinator.RebuildForChannelTopologyChange(index, ApplyConfigToViewModels, UpdateDynamicWindowWidth));

        _midiCoordinator = new MainMidiCoordinator(this, _configManager, _pluginCoordinator, () => _config);
        _presetCoordinator = new MainPresetCoordinator(this, _configManager, _presetManager, _pluginCoordinator, () => _config, GetOrCreateChannelConfig);

        _channelCoordinator.BuildChannelViewModels();
        ApplyConfigToViewModels();
        _audioEngineCoordinator.EnsureEngineMatchesConfig();
        _pluginCoordinator.InitializePluginGraphs();
        _pluginCoordinator.LoadPluginsFromConfig();
        UpdateDynamicWindowWidth();
        _isInitializing = false;
        _audioEngineCoordinator.StartEngine();

        _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _meterTimer.Tick += (_, _) => _audioEngineCoordinator.UpdateMeters();
        _meterTimer.Start();

        _midiCoordinator.Initialize();
    }

    internal bool IsInitializing
    {
        get => _isInitializing;
        set => _isInitializing = value;
    }

    public ObservableCollection<ChannelStripViewModel> Channels { get; } = new();

    public ObservableCollection<AudioDevice> InputDevices { get; }

    public ObservableCollection<AudioDevice> OutputDevices { get; }

    public List<int> SampleRateOptions { get; } = [44100, 48000];

    public List<int> BufferSizeOptions { get; } = [128, 256, 512, 1024];

    [ObservableProperty]
    private AudioDevice? selectedOutputDevice;

    [ObservableProperty]
    private AudioDevice? selectedMonitorDevice;

    [ObservableProperty]
    private int selectedSampleRate;

    [ObservableProperty]
    private int selectedBufferSize;

    [ObservableProperty]
    private bool isMinimalView;

    [ObservableProperty]
    private bool alwaysOnTop;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<string> debugLines = Array.Empty<string>();

    [ObservableProperty]
    private string routingGraphOrder = "n/a";

    [ObservableProperty]
    private double windowX;

    [ObservableProperty]
    private double windowY;

    [ObservableProperty]
    private double windowWidth = 400;

    [ObservableProperty]
    private double windowHeight = 290;

    [ObservableProperty]
    private int activeChannelIndex;

    [ObservableProperty]
    private bool masterMuted;

    [ObservableProperty]
    private bool meterScaleVox;

    [ObservableProperty]
    private bool masterMeterLufs;

    [ObservableProperty]
    private float masterLufsMomentaryLeft = -70f;

    [ObservableProperty]
    private float masterLufsMomentaryRight = -70f;

    [ObservableProperty]
    private float masterLufsShortTermLeft = -70f;

    [ObservableProperty]
    private float masterLufsShortTermRight = -70f;

    [ObservableProperty]
    private float masterPeakLeft;

    [ObservableProperty]
    private float masterPeakRight;

    [ObservableProperty]
    private float masterRmsLeft;

    [ObservableProperty]
    private float masterRmsRight;

    [ObservableProperty]
    private AudioQualityMode qualityMode;

    [ObservableProperty]
    private float cpuUsage;

    [ObservableProperty]
    private float latencyMs;

    [ObservableProperty]
    private long totalDrops;

    [ObservableProperty]
    private long drops30Sec;

    [ObservableProperty]
    private long inputDrops30Sec;

    [ObservableProperty]
    private long inputUnderflowDrops30Sec;

    [ObservableProperty]
    private long outputUnderflowDrops30Sec;

    [ObservableProperty]
    private string profilingLine = "Proc: n/a";

    [ObservableProperty]
    private string worstPluginLine = "Worst: n/a";

    [ObservableProperty]
    private string analysisTapProfilingLine = "Tap: n/a";

    [ObservableProperty]
    private string analysisTapPitchProfilingLine = "Pitch: n/a";

    [ObservableProperty]
    private string analysisTapGateLine = "Gate: n/a";

    [ObservableProperty]
    private string vitalizerLine = "Vitalizer: n/a";

    [ObservableProperty]
    private bool showDebugOverlay;

    [ObservableProperty]
    private long debugOverlayCopyTicks;

    [ObservableProperty]
    private AudioEngineDiagnosticsSnapshot diagnostics;

    [ObservableProperty]
    private string activeChannelPresetName = PluginPresetManager.CustomPresetName;

    public string ViewToggleLabel => IsMinimalView ? "Full" : "Minimal";

    public IRelayCommand ToggleViewCommand { get; }

    public IRelayCommand ToggleAlwaysOnTopCommand { get; }

    public IRelayCommand OpenSettingsCommand { get; }

    public IRelayCommand ApplyDeviceSelectionCommand { get; }

    public bool IsMidiLearning => _midiCoordinator.IsMidiLearning;

    public IReadOnlyList<string> MidiDevices => _midiCoordinator.MidiDevices;

    public string? CurrentMidiDevice => _midiCoordinator.CurrentMidiDevice;

    public void StartMidiLearn(string targetPath, Action<int, int>? onLearned = null)
    {
        _midiCoordinator.StartMidiLearn(targetPath, onLearned);
    }

    public void CancelMidiLearn()
    {
        _midiCoordinator.CancelMidiLearn();
    }

    public void AddMidiBinding(string targetPath, int ccNumber, int? channel, float minValue, float maxValue)
    {
        _midiCoordinator.AddMidiBinding(targetPath, ccNumber, channel, minValue, maxValue);
    }

    public void RemoveMidiBinding(string targetPath)
    {
        _midiCoordinator.RemoveMidiBinding(targetPath);
    }

    public MidiBinding? GetMidiBinding(string targetPath)
    {
        return _midiCoordinator.GetMidiBinding(targetPath);
    }

    public IReadOnlyList<MidiBinding> GetAllMidiBindings()
    {
        return _midiCoordinator.GetAllMidiBindings();
    }

    public void SetMidiEnabled(bool enabled)
    {
        _midiCoordinator.SetMidiEnabled(enabled);
    }

    public void SetMidiDevice(string? deviceName)
    {
        _midiCoordinator.SetMidiDevice(deviceName);
    }

    public IReadOnlyList<string> GetPresetOptions() => _presetCoordinator.GetPresetOptions();

    public void SelectChannelPreset(int channelIndex, string presetName)
    {
        _presetCoordinator.SelectChannelPreset(channelIndex, presetName);
    }

    public void SaveCurrentAsPreset(int channelIndex, string presetName)
    {
        _presetCoordinator.SaveCurrentAsPreset(channelIndex, presetName);
    }

    public bool CanOverwritePreset(string presetName)
    {
        return _presetCoordinator.CanOverwritePreset(presetName);
    }

    public bool DeleteUserPreset(string presetName)
    {
        return _presetCoordinator.DeleteUserPreset(presetName);
    }

    public void OpenAnalyzerWindow()
    {
        _windowRouter.ShowAnalyzerWindow(_audioEngineCoordinator.AnalysisOrchestrator);
    }

    public void OpenAnalysisSettingsWindow()
    {
        _windowRouter.ShowAnalysisSettingsWindow(_audioEngineCoordinator.AnalysisOrchestrator);
    }

    public void OpenWaveformWindow()
    {
        _windowRouter.ShowWaveformWindow(_audioEngineCoordinator.AnalysisOrchestrator);
    }

    public void OpenSpeechCoachWindow()
    {
        _windowRouter.ShowSpeechCoachWindow(_audioEngineCoordinator.AnalysisOrchestrator);
    }

    public void ReinitializeAudioEngine()
    {
        _audioEngineCoordinator.ReinitializeAudioEngine();
    }

    public void AddChannel()
    {
        _channelCoordinator.AddChannel();
    }

    public void RemoveChannel(int channelIndex)
    {
        _channelCoordinator.RemoveChannel(channelIndex);
    }

    public void RenameChannel(int channelIndex, string newName)
    {
        _channelCoordinator.RenameChannel(channelIndex, newName);
    }

    public void RenameContainer(int channelIndex, int containerId, string newName)
    {
        _pluginCoordinator.RenameContainer(channelIndex, containerId, newName);
    }

    public void ApplyChannelInputGain(int channelIndex, float gainDb)
    {
        _channelCoordinator.ApplyChannelInputGain(channelIndex, gainDb);
    }

    public void SetInputPluginMode(int channelIndex, int pluginInstanceId, InputChannelMode mode)
    {
        _pluginCoordinator.SetInputPluginMode(channelIndex, pluginInstanceId, mode);
    }

    public void AddContainer(int channelIndex)
    {
        _pluginCoordinator.CreateContainer(channelIndex, openWindow: true);
    }

    public void UpdateWindowPosition(double x, double y)
    {
        _config.Ui.WindowPosition.X = x;
        _config.Ui.WindowPosition.Y = y;
        _configManager.Save(_config);
    }

    public void UpdateWindowSize(double width, double height)
    {
        _config.Ui.WindowSize.Width = width;
        _config.Ui.WindowSize.Height = height;
        _configManager.Save(_config);
    }

    public void Dispose()
    {
        _meterTimer.Stop();
        _containerWindowManager.CloseAll();
        _pluginCoordinator.SavePluginStates();
        _midiCoordinator.Dispose();
        _audioEngine.Dispose();
        _audioEngineCoordinator.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ToggleView()
    {
        IsMinimalView = !IsMinimalView;
        UpdateDynamicWindowWidth();
    }

    private void ApplyDeviceSelection()
    {
        _audioEngineCoordinator.ApplyDeviceSelection();
    }

    private void OpenSettings()
    {
        var settingsViewModel = new SettingsViewModel(
            OutputDevices,
            SelectedOutputDevice,
            SelectedMonitorDevice,
            SelectedSampleRate,
            SelectedBufferSize,
            _config.EnableVstPlugins,
            _config.Midi.Enabled,
            MidiDevices,
            _config.Midi.DeviceName);

        if (_windowRouter.ShowSettings(settingsViewModel))
        {
            SelectedOutputDevice = settingsViewModel.SelectedOutputDevice;
            SelectedMonitorDevice = settingsViewModel.SelectedMonitorDevice;
            SelectedSampleRate = settingsViewModel.SelectedSampleRate;
            SelectedBufferSize = settingsViewModel.SelectedBufferSize;

            if (_config.EnableVstPlugins != settingsViewModel.EnableVstPlugins)
            {
                _config.EnableVstPlugins = settingsViewModel.EnableVstPlugins;
                _configManager.Save(_config);
            }

            if (_config.Midi.Enabled != settingsViewModel.EnableMidi)
            {
                _midiCoordinator.SetMidiEnabled(settingsViewModel.EnableMidi);
            }

            if (_config.Midi.DeviceName != settingsViewModel.SelectedMidiDevice)
            {
                _midiCoordinator.SetMidiDevice(settingsViewModel.SelectedMidiDevice);
            }

            ApplyDeviceSelection();
        }
    }

    private void ApplyConfigToViewModels()
    {
        IsMinimalView = string.Equals(_config.Ui.ViewMode, "minimal", StringComparison.OrdinalIgnoreCase);
        AlwaysOnTop = _config.Ui.AlwaysOnTop;
        MeterScaleVox = _config.Ui.MeterScaleVox;
        MasterMeterLufs = _config.Ui.MasterMeterLufs;
        MasterMuted = _config.Ui.MasterMuted;
        WindowX = _config.Ui.WindowPosition.X;
        WindowY = _config.Ui.WindowPosition.Y;

        UpdateDynamicWindowWidth();

        QualityMode = _config.AudioSettings.QualityMode;
        SelectedSampleRate = SampleRateOptions.Contains(_config.AudioSettings.SampleRate)
            ? _config.AudioSettings.SampleRate
            : SampleRateOptions[0];
        var profile = AudioQualityProfiles.ForMode(QualityMode, SelectedSampleRate);
        int configBufferSize = _config.AudioSettings.BufferSize;
        int resolvedBufferSize = BufferSizeOptions.Contains(configBufferSize)
            ? configBufferSize
            : BufferSizeOptions.Contains(profile.BufferSize)
                ? profile.BufferSize
                : BufferSizeOptions[1];
        SelectedBufferSize = resolvedBufferSize;
        _config.AudioSettings.BufferSize = SelectedBufferSize;

        _channelCoordinator.ApplyChannelConfigToViewModels();

        if (ActiveChannelIndex < 0 || ActiveChannelIndex >= Channels.Count)
        {
            ActiveChannelIndex = 0;
        }
        ActiveChannelPresetName = _presetCoordinator.GetChannelPresetName(ActiveChannelIndex);
    }

    private void UpdateDynamicWindowWidth()
    {
        _channelCoordinator.UpdateDynamicWindowWidth();
    }

    private void EnsureDefaultChannelConfig()
    {
        if (_config.Channels.Count != 0)
        {
            return;
        }

        _config.Channels.Add(new ChannelConfig
        {
            Id = 1,
            Name = "Mic 1",
            Plugins =
            [
                new PluginConfig
                {
                    Type = "builtin:input"
                },
                new PluginConfig
                {
                    Type = "builtin:output-send"
                }
            ]
        });
    }

    private AudioQualityProfile GetQualityProfile()
    {
        return AudioQualityProfiles.ForMode(QualityMode, SelectedSampleRate);
    }

    private ChannelConfig GetOrCreateChannelConfig(int channelIndex)
    {
        while (_config.Channels.Count <= channelIndex)
        {
            _config.Channels.Add(new ChannelConfig
            {
                Id = _config.Channels.Count + 1,
                Name = $"Channel {_config.Channels.Count + 1}",
                Plugins =
                [
                    new PluginConfig
                    {
                        Type = "builtin:input"
                    }
                ]
            });
        }

        return _config.Channels[channelIndex];
    }

    private string GetInputDeviceLabel(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return "No Input";
        }

        var device = InputDevices.FirstOrDefault(d => d.Id == deviceId);
        return device?.Name ?? "Unknown";
    }

    private int CreateCopyChannel(int sourceChannelIndex)
    {
        return _channelCoordinator.CreateCopyChannel(sourceChannelIndex);
    }

    partial void OnMasterMutedChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        _audioEngineCoordinator.SetMasterMute(value);
    }

    partial void OnIsMinimalViewChanged(bool value)
    {
        OnPropertyChanged(nameof(ViewToggleLabel));
        if (_isInitializing)
        {
            return;
        }

        _config.Ui.ViewMode = value ? "minimal" : "full";
        _configManager.Save(_config);
    }

    partial void OnAlwaysOnTopChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        _config.Ui.AlwaysOnTop = value;
        _configManager.Save(_config);
    }

    partial void OnMeterScaleVoxChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        _config.Ui.MeterScaleVox = value;
        _configManager.Save(_config);
        _containerWindowManager.UpdateMeterScale(value);
    }

    partial void OnMasterMeterLufsChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        _config.Ui.MasterMeterLufs = value;
        _configManager.Save(_config);
    }

    partial void OnActiveChannelIndexChanged(int value)
    {
        if (value < 0 || value >= Channels.Count)
        {
            return;
        }

        ActiveChannelPresetName = _presetCoordinator.GetChannelPresetName(value);
    }

    partial void OnQualityModeChanged(AudioQualityMode value)
    {
        if (_isInitializing)
        {
            return;
        }

        _audioEngineCoordinator.ApplyQualityMode(value);
    }
}
