using System.Collections.ObjectModel;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotMic.Common.Configuration;
using HotMic.Common.Models;
using HotMic.Core.Analysis;
using HotMic.Core.Engine;
using HotMic.Core.Midi;
using HotMic.Core.Plugins;
using HotMic.Core.Plugins.BuiltIn;
using HotMic.Core.Presets;
using HotMic.App.Models;
using HotMic.App.Views;
using HotMic.Vst3;

namespace HotMic.App.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private const string ContainerChoiceId = "ui:container";
    private readonly ConfigManager _configManager = new();
    private readonly DeviceManager _deviceManager = new();
    private AudioEngine _audioEngine;
    private readonly AnalysisOrchestrator _analysisOrchestrator = new();
    private MidiManager? _midiManager;
    private readonly DispatcherTimer _meterTimer;
    private AppConfig _config;
    private readonly PluginPresetManager _presetManager = PluginPresetManager.Default;
    private readonly Dictionary<int, int> _channel1PluginIndexMap = new();
    private readonly Dictionary<int, int> _channel2PluginIndexMap = new();
    private readonly Dictionary<(int ChannelIndex, int ContainerId), PluginContainerWindow> _containerWindows = new();
    private long _lastMeterUpdateTicks;
    private long _nextDebugUpdateTicks;
    private static readonly long DebugUpdateIntervalTicks = Math.Max(1, Stopwatch.Frequency / 4);
    private bool _isInitializing = true; // Skip side effects during initial config load
#pragma warning disable CS0649 // Field is reserved for future batch preset operations
    private bool _suppressPresetUpdates;
#pragma warning restore CS0649

    // 30-second rolling window for drop tracking
    private readonly Queue<(long ticks, long input1, long input2, long underflow1, long underflow2)> _dropHistory = new();
    private static readonly long ThirtySecondsInTicks = Stopwatch.Frequency * 30;

    public MainViewModel()
    {
        _config = _configManager.LoadOrDefault();
        _audioEngine = new AudioEngine(_config.AudioSettings);

        // Connect analysis orchestrator to audio engine tap
        _analysisOrchestrator.Initialize(_config.AudioSettings.SampleRate);
        _audioEngine.AnalysisTap.Orchestrator = _analysisOrchestrator;
        _analysisOrchestrator.DebugTap = _audioEngine.AnalysisTap;

        Channel1 = new ChannelStripViewModel(
            0,
            "Channel 1",
            EnqueueParameterChange,
            (instanceId, slotIndex) => HandlePluginAction(0, instanceId, slotIndex),
            instanceId => RemovePlugin(0, instanceId),
            (instanceId, toIndex) => ReorderPlugins(0, instanceId, toIndex),
            (instanceId, value) => UpdatePluginBypassConfig(0, instanceId, value),
            containerId => HandleContainerAction(0, containerId),
            containerId => RemoveContainer(0, containerId),
            (containerId, bypass) => SetContainerBypass(0, containerId, bypass));
        Channel2 = new ChannelStripViewModel(
            1,
            "Channel 2",
            EnqueueParameterChange,
            (instanceId, slotIndex) => HandlePluginAction(1, instanceId, slotIndex),
            instanceId => RemovePlugin(1, instanceId),
            (instanceId, toIndex) => ReorderPlugins(1, instanceId, toIndex),
            (instanceId, value) => UpdatePluginBypassConfig(1, instanceId, value),
            containerId => HandleContainerAction(1, containerId),
            containerId => RemoveContainer(1, containerId),
            (containerId, bypass) => SetContainerBypass(1, containerId, bypass));
        Channel1.PropertyChanged += (_, e) => UpdateChannelConfig(0, Channel1, e.PropertyName);
        Channel2.PropertyChanged += (_, e) => UpdateChannelConfig(1, Channel2, e.PropertyName);

        InputDevices = new ObservableCollection<AudioDevice>(_deviceManager.GetInputDevices());
        OutputDevices = new ObservableCollection<AudioDevice>(_deviceManager.GetOutputDevices());

        SelectedInputDevice1 = InputDevices.FirstOrDefault(d => d.Id == _config.AudioSettings.InputDevice1Id) ?? InputDevices.FirstOrDefault();
        SelectedInputDevice2 = InputDevices.FirstOrDefault(d => d.Id == _config.AudioSettings.InputDevice2Id) ?? InputDevices.FirstOrDefault();
        SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.Id == _config.AudioSettings.OutputDeviceId) ??
            OutputDevices.FirstOrDefault(d => d.Name.Contains("VB-Cable", StringComparison.OrdinalIgnoreCase));
        SelectedMonitorDevice = OutputDevices.FirstOrDefault(d => d.Id == _config.AudioSettings.MonitorOutputDeviceId);

        ToggleViewCommand = new RelayCommand(ToggleView);
        ToggleAlwaysOnTopCommand = new RelayCommand(() => AlwaysOnTop = !AlwaysOnTop);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        ApplyDeviceSelectionCommand = new RelayCommand(ApplyDeviceSelection);

        ApplyConfigToViewModels();
        if (_audioEngine.SampleRate != _config.AudioSettings.SampleRate || _audioEngine.BlockSize != _config.AudioSettings.BufferSize)
        {
            _audioEngine.Dispose();
            _audioEngine = new AudioEngine(_config.AudioSettings);
        }
        LoadPluginsFromConfig();
        UpdateDynamicWindowWidth(); // Recalculate after plugins are loaded
        _isInitializing = false; // Now safe to allow side effects from property changes
        StartEngine();

        _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _meterTimer.Tick += (_, _) => UpdateMeters();
        _meterTimer.Start();

        InitializeMidi();
    }

    private void InitializeMidi()
    {
        try
        {
            _midiManager = new MidiManager(_config.Midi);
            _midiManager.BindingTriggered += OnMidiBindingTriggered;
            _midiManager.Start();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MIDI] Failed to initialize: {ex.Message}");
        }
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
        if (parts.Length < 2) return;

        int channelIndex = parts[0] switch
        {
            "channel1" => 0,
            "channel2" => 1,
            _ => -1
        };

        if (channelIndex < 0) return;

        var channel = channelIndex == 0 ? Channel1 : Channel2;

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
                    int resolvedInstanceId = ResolvePluginInstanceId(channel, pluginInstanceId);
                    if (resolvedInstanceId <= 0)
                    {
                        return;
                    }

                    ApplyPluginParameter(channelIndex, resolvedInstanceId, paramIndex, "midi", value);

                    // Update the UI knob (slot indices in PluginSlots are 1-based, +1 for add placeholder)
                    int slotIndex = FindPluginSlotIndex(channel, resolvedInstanceId);
                    if (slotIndex >= 0 && slotIndex < channel.PluginSlots.Count - 1)
                    {
                        var slot = channel.PluginSlots[slotIndex];
                        if (slot.ElevatedParams is { } elevParams)
                        {
                            // Find which elevated index matches this parameter index
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

    private static int ResolvePluginInstanceId(ChannelStripViewModel channel, int legacyOrInstanceId)
    {
        int index = FindPluginSlotIndex(channel, legacyOrInstanceId);
        if (index >= 0)
        {
            return legacyOrInstanceId;
        }

        // Legacy path: treat value as slot index and map to current instance ID.
        if (legacyOrInstanceId >= 0 && legacyOrInstanceId < channel.PluginSlots.Count - 1)
        {
            return channel.PluginSlots[legacyOrInstanceId].InstanceId;
        }

        return 0;
    }

    private static int FindPluginSlotIndex(ChannelStripViewModel channel, int instanceId)
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

    public ChannelStripViewModel Channel1 { get; }
    public ChannelStripViewModel Channel2 { get; }

    public ObservableCollection<AudioDevice> InputDevices { get; }

    public ObservableCollection<AudioDevice> OutputDevices { get; }

    public IReadOnlyList<int> SampleRateOptions { get; } = [44100, 48000];

    public IReadOnlyList<int> BufferSizeOptions { get; } = [128, 256, 512, 1024];

    public IReadOnlyList<InputChannelMode> InputChannelOptions { get; } =
        [InputChannelMode.Sum, InputChannelMode.Left, InputChannelMode.Right];

    public IReadOnlyList<OutputRoutingMode> OutputRoutingOptions { get; } =
        [OutputRoutingMode.Split, OutputRoutingMode.Sum];

    [ObservableProperty]
    private AudioDevice? selectedInputDevice1;

    [ObservableProperty]
    private AudioDevice? selectedInputDevice2;

    [ObservableProperty]
    private AudioDevice? selectedOutputDevice;

    [ObservableProperty]
    private AudioDevice? selectedMonitorDevice;

    [ObservableProperty]
    private int selectedSampleRate;

    [ObservableProperty]
    private int selectedBufferSize;

    [ObservableProperty]
    private InputChannelMode selectedInput1Channel;

    [ObservableProperty]
    private InputChannelMode selectedInput2Channel;

    [ObservableProperty]
    private OutputRoutingMode selectedOutputRouting;

    [ObservableProperty]
    private bool isMinimalView;

    [ObservableProperty]
    private bool alwaysOnTop;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<string> debugLines = Array.Empty<string>();

    [ObservableProperty]
    private double windowX;

    [ObservableProperty]
    private double windowY;

    [ObservableProperty]
    private double windowWidth = 400; // Will be recalculated by UpdateDynamicWindowWidth

    [ObservableProperty]
    private double windowHeight = 290;

    // Mono/Stereo controls (Stereo: Ch1=Left, Ch2=Right; Mono: Sum)
    [ObservableProperty]
    private bool input1IsStereo;

    [ObservableProperty]
    private bool input2IsStereo;

    [ObservableProperty]
    private bool masterIsStereo = true;

    [ObservableProperty]
    private bool masterMuted;

    // Meter scale mode (false = default, true = vox +20dB)
    [ObservableProperty]
    private bool meterScaleVox;

    // Master meter display mode (false = dB, true = LUFS)
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
    private AudioQualityMode qualityMode;

    // Hotbar stats
    [ObservableProperty]
    private float cpuUsage;

    [ObservableProperty]
    private float latencyMs;

    [ObservableProperty]
    private long totalDrops;

    [ObservableProperty]
    private long drops30Sec;

    [ObservableProperty]
    private long input1Drops30Sec;

    [ObservableProperty]
    private long input2Drops30Sec;

    [ObservableProperty]
    private long underflow1Drops30Sec;

    [ObservableProperty]
    private long underflow2Drops30Sec;

    // Debug overlay
    [ObservableProperty]
    private bool showDebugOverlay;

    [ObservableProperty]
    private AudioEngineDiagnosticsSnapshot diagnostics;

    [ObservableProperty]
    private string channel1PresetName = PluginPresetManager.CustomPresetName;

    [ObservableProperty]
    private string channel2PresetName = PluginPresetManager.CustomPresetName;

    public string ViewToggleLabel => IsMinimalView ? "Full" : "Minimal";

    public IRelayCommand ToggleViewCommand { get; }

    public IRelayCommand ToggleAlwaysOnTopCommand { get; }

    public IRelayCommand OpenSettingsCommand { get; }

    public IRelayCommand ApplyDeviceSelectionCommand { get; }

    private void ToggleView()
    {
        IsMinimalView = !IsMinimalView;
        UpdateDynamicWindowWidth();
    }

    private void UpdateDynamicWindowWidth()
    {
        if (IsMinimalView)
        {
            WindowWidth = MinimalViewWidth;
            WindowHeight = MinimalViewHeight;
            return;
        }

        // Calculate width based on longest visible chain (containers or plugins + add placeholder).
        int channel1Slots = GetVisibleSlotCount(0);
        int channel2Slots = GetVisibleSlotCount(1);
        int maxSlots = Math.Max(channel1Slots, channel2Slots);

        double pluginAreaWidth = maxSlots * PluginSlotWidthWithSpacing;
        double calculatedWidth = FullViewBaseWidth + pluginAreaWidth;

        WindowWidth = Math.Clamp(calculatedWidth, MinFullViewWidth, MaxFullViewWidth);
        WindowHeight = FullViewHeight;
    }

    private int GetVisibleSlotCount(int channelIndex)
    {
        if ((uint)channelIndex >= (uint)_audioEngine.Channels.Count)
        {
            return 1;
        }

        var config = GetOrCreateChannelConfig(channelIndex);
        if (config.Containers.Count > 0)
        {
            return Math.Max(1, config.Containers.Count + 1);
        }

        return Math.Max(1, _audioEngine.Channels[channelIndex].PluginChain.Count + 1);
    }

    partial void OnInput1IsStereoChanged(bool value)
    {
        SelectedInput1Channel = value ? InputChannelMode.Left : InputChannelMode.Sum;
        if (_isInitializing) return;
        _audioEngine.ConfigureRouting(SelectedInput1Channel, SelectedInput2Channel, SelectedOutputRouting);
        _config.AudioSettings.Input1Channel = SelectedInput1Channel;
        _configManager.Save(_config);
    }

    partial void OnInput2IsStereoChanged(bool value)
    {
        SelectedInput2Channel = value ? InputChannelMode.Right : InputChannelMode.Sum;
        if (_isInitializing) return;
        _audioEngine.ConfigureRouting(SelectedInput1Channel, SelectedInput2Channel, SelectedOutputRouting);
        _config.AudioSettings.Input2Channel = SelectedInput2Channel;
        _configManager.Save(_config);
    }

    partial void OnMasterIsStereoChanged(bool value)
    {
        SelectedOutputRouting = value ? OutputRoutingMode.Split : OutputRoutingMode.Sum;
        if (_isInitializing) return;
        _audioEngine.ConfigureRouting(SelectedInput1Channel, SelectedInput2Channel, SelectedOutputRouting);
        _config.AudioSettings.OutputRouting = SelectedOutputRouting;
        _configManager.Save(_config);
    }

    partial void OnMasterMutedChanged(bool value)
    {
        if (_isInitializing) return;
        _audioEngine.SetMasterMute(value);
        _config.Ui.MasterMuted = value;
        _configManager.Save(_config);
    }

    private void OpenSettings()
    {
        var settingsViewModel = new SettingsViewModel(
            InputDevices,
            OutputDevices,
            SelectedInputDevice1,
            SelectedInputDevice2,
            SelectedOutputDevice,
            SelectedMonitorDevice,
            SelectedSampleRate,
            SelectedBufferSize,
            SelectedInput1Channel,
            SelectedInput2Channel,
            SelectedOutputRouting,
            _config.EnableVstPlugins,
            _config.Midi.Enabled,
            MidiDevices,
            _config.Midi.DeviceName);

        var window = new SettingsWindow(settingsViewModel)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (window.ShowDialog() == true)
        {
            // Apply settings from dialog
            SelectedInputDevice1 = settingsViewModel.SelectedInputDevice1;
            SelectedInputDevice2 = settingsViewModel.SelectedInputDevice2;
            SelectedOutputDevice = settingsViewModel.SelectedOutputDevice;
            SelectedMonitorDevice = settingsViewModel.SelectedMonitorDevice;
            SelectedSampleRate = settingsViewModel.SelectedSampleRate;
            SelectedBufferSize = settingsViewModel.SelectedBufferSize;
            SelectedInput1Channel = settingsViewModel.SelectedInput1Channel;
            SelectedInput2Channel = settingsViewModel.SelectedInput2Channel;
            SelectedOutputRouting = settingsViewModel.SelectedOutputRouting;

            // Apply VST setting
            if (_config.EnableVstPlugins != settingsViewModel.EnableVstPlugins)
            {
                _config.EnableVstPlugins = settingsViewModel.EnableVstPlugins;
                _configManager.Save(_config);
            }

            // Apply MIDI settings
            if (_config.Midi.Enabled != settingsViewModel.EnableMidi ||
                _config.Midi.DeviceName != settingsViewModel.SelectedMidiDevice)
            {
                _config.Midi.Enabled = settingsViewModel.EnableMidi;
                _config.Midi.DeviceName = settingsViewModel.SelectedMidiDevice;
                _midiManager?.ApplyConfig(_config.Midi);
                _configManager.Save(_config);
            }

            ApplyDeviceSelection();
        }
    }

    partial void OnIsMinimalViewChanged(bool value)
    {
        OnPropertyChanged(nameof(ViewToggleLabel));
        if (_isInitializing) return;
        _config.Ui.ViewMode = value ? "minimal" : "full";
        _configManager.Save(_config);
    }

    partial void OnAlwaysOnTopChanged(bool value)
    {
        if (_isInitializing) return;
        _config.Ui.AlwaysOnTop = value;
        _configManager.Save(_config);
    }

    partial void OnMeterScaleVoxChanged(bool value)
    {
        if (_isInitializing) return;
        _config.Ui.MeterScaleVox = value;
        _configManager.Save(_config);
    }

    partial void OnMasterMeterLufsChanged(bool value)
    {
        if (_isInitializing) return;
        _config.Ui.MasterMeterLufs = value;
        _configManager.Save(_config);
    }

    partial void OnQualityModeChanged(AudioQualityMode value)
    {
        if (_isInitializing) return;
        ApplyQualityMode(value);
    }

    private void EnqueueParameterChange(ParameterChange change)
    {
        _audioEngine.EnqueueParameterChange(change);
    }

    private AudioQualityProfile GetQualityProfile()
    {
        return AudioQualityProfiles.ForMode(QualityMode, SelectedSampleRate);
    }

    private void ApplyQualityMode(AudioQualityMode mode)
    {
        var profile = AudioQualityProfiles.ForMode(mode, SelectedSampleRate);
        SelectedBufferSize = profile.BufferSize;
        _config.AudioSettings.QualityMode = mode;
        _config.AudioSettings.BufferSize = SelectedBufferSize;
        _configManager.Save(_config);
        RestartAudioEngineForQuality(profile);
    }

    private void StartEngine()
    {
        if (!_audioEngine.IsVbCableInstalled())
        {
            StatusMessage = "VB-Cable not detected. Please install VB-Cable to enable output.";
            return;
        }

        StatusMessage = string.Empty;
        ApplyDeviceSelection();
    }

    private void ApplyDeviceSelection()
    {
        _audioEngine.Stop();

        if (SelectedOutputDevice is null)
        {
            StatusMessage = "Select an output device.";
            return;
        }

        if (!DeviceManager.IsVbCableDeviceName(SelectedOutputDevice.Name))
        {
            StatusMessage = "Output must be set to VB-Cable.";
            return;
        }

        var profile = GetQualityProfile();
        if (SelectedBufferSize != profile.BufferSize)
        {
            SelectedBufferSize = profile.BufferSize;
        }

        _config.AudioSettings.InputDevice1Id = SelectedInputDevice1?.Id ?? string.Empty;
        _config.AudioSettings.InputDevice2Id = SelectedInputDevice2?.Id ?? string.Empty;
        _config.AudioSettings.OutputDeviceId = SelectedOutputDevice?.Id ?? string.Empty;
        _config.AudioSettings.MonitorOutputDeviceId = SelectedMonitorDevice?.Id ?? string.Empty;
        _config.AudioSettings.SampleRate = SelectedSampleRate;
        _config.AudioSettings.BufferSize = SelectedBufferSize;
        _config.AudioSettings.QualityMode = QualityMode;
        _config.AudioSettings.Input1Channel = SelectedInput1Channel;
        _config.AudioSettings.Input2Channel = SelectedInput2Channel;
        _config.AudioSettings.OutputRouting = SelectedOutputRouting;
        _configManager.Save(_config);

        StatusMessage = string.Empty;
        _audioEngine.ConfigureDevices(_config.AudioSettings.InputDevice1Id, _config.AudioSettings.InputDevice2Id, _config.AudioSettings.OutputDeviceId, _config.AudioSettings.MonitorOutputDeviceId);
        _audioEngine.ConfigureRouting(_config.AudioSettings.Input1Channel, _config.AudioSettings.Input2Channel, _config.AudioSettings.OutputRouting);
        try
        {
            _audioEngine.Start();
            _audioEngine.SetMasterMute(MasterMuted);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Audio start failed: {ex.Message}";
        }

        if (StatusMessage.Length == 0 && (_audioEngine.SampleRate != SelectedSampleRate || _audioEngine.BlockSize != SelectedBufferSize))
        {
            StatusMessage = "Sample rate/buffer changes apply on restart.";
        }
    }

    private void RestartAudioEngineForQuality(AudioQualityProfile profile)
    {
        _audioEngine.Stop();
        var snapshots = CapturePluginSnapshots();
        _audioEngine.Dispose();
        _audioEngine = new AudioEngine(_config.AudioSettings);

        // Reconnect analysis orchestrator to new engine
        _analysisOrchestrator.Initialize(_config.AudioSettings.SampleRate);
        _analysisOrchestrator.Reset();
        _audioEngine.AnalysisTap.Orchestrator = _analysisOrchestrator;

        if (SelectedOutputDevice is null)
        {
            StatusMessage = "Select an output device.";
            return;
        }

        if (!DeviceManager.IsVbCableDeviceName(SelectedOutputDevice.Name))
        {
            StatusMessage = "Output must be set to VB-Cable.";
            return;
        }

        _audioEngine.ConfigureDevices(
            SelectedInputDevice1?.Id ?? string.Empty,
            SelectedInputDevice2?.Id ?? string.Empty,
            SelectedOutputDevice?.Id ?? string.Empty,
            SelectedMonitorDevice?.Id ?? string.Empty);
        _audioEngine.ConfigureRouting(SelectedInput1Channel, SelectedInput2Channel, SelectedOutputRouting);
        ApplyChannelStateToEngine();

        RestorePluginsFromSnapshots(snapshots, profile);

        try
        {
            _audioEngine.Start();
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Audio start failed: {ex.Message}";
        }

        RefreshPluginViewModels(0);
        RefreshPluginViewModels(1);
    }

    private PluginSlot?[][] CapturePluginSnapshots()
    {
        var snapshots = new PluginSlot?[_audioEngine.Channels.Count][];
        for (int i = 0; i < _audioEngine.Channels.Count; i++)
        {
            snapshots[i] = _audioEngine.Channels[i].PluginChain.DetachAll();
        }

        return snapshots;
    }

    private void RestorePluginsFromSnapshots(PluginSlot?[][] snapshots, AudioQualityProfile profile)
    {
        for (int channelIndex = 0; channelIndex < _audioEngine.Channels.Count; channelIndex++)
        {
            var strip = _audioEngine.Channels[channelIndex];
            var snapshot = channelIndex < snapshots.Length ? snapshots[channelIndex] : [];
            var pluginList = new List<PluginSlot>();

            foreach (var slot in snapshot)
            {
                if (slot is null)
                {
                    continue;
                }

                ReinitializePluginForQuality(slot.Plugin, profile);
                pluginList.Add(slot);
            }

            var oldSlots = strip.PluginChain.GetSnapshot();
            var newSlots = pluginList.ToArray();
            strip.PluginChain.ReplaceAll(newSlots);
            QueueRemovedPlugins(oldSlots, newSlots);
        }
    }

    private void ReinitializePluginForQuality(IPlugin plugin, AudioQualityProfile profile)
    {
        bool bypassed = plugin.IsBypassed;
        if (plugin is IQualityConfigurablePlugin qualityPlugin)
        {
            qualityPlugin.ApplyQuality(profile);
        }

        if (plugin is Vst3PluginWrapper vst3)
        {
            var state = vst3.GetState();
            vst3.Dispose();
            vst3.Initialize(_audioEngine.SampleRate, _audioEngine.BlockSize);
            if (state.Length > 0)
            {
                vst3.SetState(state);
            }
        }
        else
        {
            plugin.Initialize(_audioEngine.SampleRate, _audioEngine.BlockSize);
        }

        plugin.IsBypassed = bypassed;
    }

    private void ApplyChannelStateToEngine()
    {
        if (_audioEngine.Channels.Count < 2)
        {
            return;
        }

        var channel1 = _audioEngine.Channels[0];
        channel1.SetInputGainDb(Channel1.InputGainDb);
        channel1.SetOutputGainDb(Channel1.OutputGainDb);
        channel1.SetMuted(Channel1.IsMuted);
        channel1.SetSoloed(Channel1.IsSoloed);

        var channel2 = _audioEngine.Channels[1];
        channel2.SetInputGainDb(Channel2.InputGainDb);
        channel2.SetOutputGainDb(Channel2.OutputGainDb);
        channel2.SetMuted(Channel2.IsMuted);
        channel2.SetSoloed(Channel2.IsSoloed);

        _audioEngine.SetMasterMute(MasterMuted);
    }

    // Window size constants (must match MainRenderer layout calculations)
    // MainRenderer: PluginSlotWidth=130, MiniMeterWidth=6, PluginSlotSpacing=2
    private const double FullViewBaseWidth = 300;
    private const double PluginSlotWidthWithSpacing = 140; // Filled slot width (130) + meter (6) + spacing (2) + padding
    private const double MaxFullViewWidth = 1600;
    private const double MinFullViewWidth = 500;
    private const double FullViewHeight = 290;
    private const double MinimalViewWidth = 400;
    private const double MinimalViewHeight = 140;

    private void ApplyConfigToViewModels()
    {
        IsMinimalView = string.Equals(_config.Ui.ViewMode, "minimal", StringComparison.OrdinalIgnoreCase);
        AlwaysOnTop = _config.Ui.AlwaysOnTop;
        MeterScaleVox = _config.Ui.MeterScaleVox;
        MasterMeterLufs = _config.Ui.MasterMeterLufs;
        MasterMuted = _config.Ui.MasterMuted;
        WindowX = _config.Ui.WindowPosition.X;
        WindowY = _config.Ui.WindowPosition.Y;

        // Force correct window size based on view mode
        UpdateDynamicWindowWidth();

        var channel1Config = _config.Channels.ElementAtOrDefault(0);
        var channel2Config = _config.Channels.ElementAtOrDefault(1);

        QualityMode = _config.AudioSettings.QualityMode;
        SelectedSampleRate = SampleRateOptions.Contains(_config.AudioSettings.SampleRate)
            ? _config.AudioSettings.SampleRate
            : SampleRateOptions[0];
        var profile = AudioQualityProfiles.ForMode(QualityMode, SelectedSampleRate);
        SelectedBufferSize = BufferSizeOptions.Contains(profile.BufferSize)
            ? profile.BufferSize
            : BufferSizeOptions[1];
        _config.AudioSettings.BufferSize = SelectedBufferSize;
        SelectedInput1Channel = _config.AudioSettings.Input1Channel;
        SelectedInput2Channel = _config.AudioSettings.Input2Channel;
        SelectedOutputRouting = _config.AudioSettings.OutputRouting;

        // Initialize stereo settings from channel modes
        Input1IsStereo = _config.AudioSettings.Input1Channel == InputChannelMode.Left;
        Input2IsStereo = _config.AudioSettings.Input2Channel == InputChannelMode.Right;
        MasterIsStereo = _config.AudioSettings.OutputRouting == OutputRoutingMode.Split;

        if (channel1Config is not null)
        {
            Channel1.UpdateName(channel1Config.Name);
            Channel1.InputGainDb = channel1Config.InputGainDb;
            Channel1.OutputGainDb = channel1Config.OutputGainDb;
            Channel1.IsMuted = channel1Config.IsMuted;
            Channel1.IsSoloed = channel1Config.IsSoloed;
        }

        if (channel2Config is not null)
        {
            Channel2.UpdateName(channel2Config.Name);
            Channel2.InputGainDb = channel2Config.InputGainDb;
            Channel2.OutputGainDb = channel2Config.OutputGainDb;
            Channel2.IsMuted = channel2Config.IsMuted;
            Channel2.IsSoloed = channel2Config.IsSoloed;
        }

        // Initialize preset names
        Channel1PresetName = GetChannelPresetName(0);
        Channel2PresetName = GetChannelPresetName(1);
    }

    private void LoadPluginsFromConfig()
    {
        var channel1Config = _config.Channels.ElementAtOrDefault(0);
        var channel2Config = _config.Channels.ElementAtOrDefault(1);

        LoadChannelPlugins(0, channel1Config, Channel1);
        LoadChannelPlugins(1, channel2Config, Channel2);
    }

    private void LoadChannelPlugins(int channelIndex, ChannelConfig? config, ChannelStripViewModel viewModel)
    {
        if ((uint)channelIndex >= (uint)_audioEngine.Channels.Count)
        {
            return;
        }

        if (config is not null &&
            config.Plugins.Count == 0 &&
            !IsCustomPreset(config.PresetName))
        {
            ApplyChannelPreset(channelIndex, config.PresetName);
            return;
        }

        var strip = _audioEngine.Channels[channelIndex];
        var pluginSlots = new List<PluginSlot>();
        var profile = GetQualityProfile();
        bool assignedInstanceIds = false;
        int nextInstanceId = 0;
        HashSet<int> bypassedByContainer = new();

        if (config is not null)
        {
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

            for (int i = 0; i < config.Plugins.Count; i++)
            {
                if (config.Plugins[i].InstanceId > nextInstanceId)
                {
                    nextInstanceId = config.Plugins[i].InstanceId;
                }
            }

            for (int i = 0; i < config.Plugins.Count; i++)
            {
                var pluginConfig = config.Plugins[i];
                if (string.IsNullOrWhiteSpace(pluginConfig.Type))
                {
                    continue;
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
                    continue;
                }

                if (plugin is IQualityConfigurablePlugin qualityPlugin)
                {
                    qualityPlugin.ApplyQuality(profile);
                }

                plugin.Initialize(_audioEngine.SampleRate, _audioEngine.BlockSize);
                bool containerBypass = pluginConfig.InstanceId > 0 && bypassedByContainer.Contains(pluginConfig.InstanceId);
                plugin.IsBypassed = pluginConfig.IsBypassed || containerBypass;
                if (containerBypass)
                {
                    pluginConfig.IsBypassed = true;
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

                if (pluginConfig.InstanceId <= 0)
                {
                    pluginConfig.InstanceId = ++nextInstanceId;
                    assignedInstanceIds = true;
                }

                pluginSlots.Add(new PluginSlot(pluginConfig.InstanceId, plugin, _audioEngine.SampleRate));
            }
        }

        var oldSlots = strip.PluginChain.GetSnapshot();
        var newSlots = pluginSlots.ToArray();
        strip.PluginChain.ReplaceAll(newSlots);
        QueueRemovedPlugins(oldSlots, newSlots);
        RefreshPluginViewModels(channelIndex);

        if (config is not null)
        {
            RebuildPluginIndexMap(config, GetPluginIndexMap(channelIndex));
            bool containersChanged = NormalizeContainers(config, newSlots);
            if (assignedInstanceIds || containersChanged)
            {
                _configManager.Save(_config);
            }
            if (containersChanged)
            {
                RefreshPluginViewModels(channelIndex);
            }
        }
    }

    private static bool IsCustomPreset(string? presetName)
    {
        return string.IsNullOrWhiteSpace(presetName) ||
               presetName.Equals(PluginPresetManager.CustomPresetName, StringComparison.OrdinalIgnoreCase);
    }

    private Dictionary<string, float> ApplyPresetParameters(IPlugin plugin, PluginPreset preset)
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

    private void ApplyChannelPreset(int channelIndex, string presetName)
    {
        if ((uint)channelIndex >= (uint)_audioEngine.Channels.Count)
        {
            return;
        }

        var config = GetOrCreateChannelConfig(channelIndex);
        if (IsCustomPreset(presetName))
        {
            config.PresetName = PluginPresetManager.CustomPresetName;
            _configManager.Save(_config);
            return;
        }

        if (!_presetManager.TryGetChainPreset(presetName, out var chainPreset))
        {
            return;
        }

        var strip = _audioEngine.Channels[channelIndex];
        var profile = GetQualityProfile();
        var pluginSlots = new List<PluginSlot>(chainPreset.Entries.Count);
        var pluginConfigs = new List<PluginConfig>(chainPreset.Entries.Count);
        int nextInstanceId = 0;

        foreach (var entry in chainPreset.Entries)
        {
            IPlugin? plugin = PluginFactory.Create(entry.PluginId);
            if (plugin is null)
            {
                continue;
            }

            if (plugin is IQualityConfigurablePlugin qualityPlugin)
            {
                qualityPlugin.ApplyQuality(profile);
            }

            plugin.Initialize(_audioEngine.SampleRate, _audioEngine.BlockSize);

            if (!_presetManager.TryGetPreset(plugin.Id, entry.PresetName, out var preset))
            {
                preset = _presetManager.GetDefaultPreset(plugin);
            }

            var parameterMap = ApplyPresetParameters(plugin, preset);

            int instanceId = ++nextInstanceId;
            pluginSlots.Add(new PluginSlot(instanceId, plugin, _audioEngine.SampleRate));
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

        var oldSlots = strip.PluginChain.GetSnapshot();
        var newSlots = pluginSlots.ToArray();
        strip.PluginChain.ReplaceAll(newSlots);
        QueueRemovedPlugins(oldSlots, newSlots);
        RefreshPluginViewModels(channelIndex);
        UpdateDynamicWindowWidth();

        config.PresetName = chainPreset.Name;
        config.Plugins = pluginConfigs;
        config.Containers = containerConfigs;
        RebuildPluginIndexMap(config, GetPluginIndexMap(channelIndex));
        NormalizeContainers(config, newSlots);
        _configManager.Save(_config);
    }

    private string GetChannelPresetName(int channelIndex)
    {
        var config = _config.Channels.ElementAtOrDefault(channelIndex);
        if (config is null || IsCustomPreset(config.PresetName))
        {
            return PluginPresetManager.CustomPresetName;
        }

        return config.PresetName;
    }

    private void MarkChannelPresetCustom(ChannelConfig config)
    {
        if (_suppressPresetUpdates)
        {
            return;
        }

        if (!IsCustomPreset(config.PresetName))
        {
            config.PresetName = PluginPresetManager.CustomPresetName;
        }
    }

    private void MarkPluginPresetCustom(ChannelConfig config, int slotIndex)
    {
        if (_suppressPresetUpdates)
        {
            return;
        }

        if (slotIndex >= 0 && slotIndex < config.Plugins.Count)
        {
            config.Plugins[slotIndex].PresetName = PluginPresetManager.CustomPresetName;
        }

        config.PresetName = PluginPresetManager.CustomPresetName;
    }

    private void UpdateMeters()
    {
        if (_audioEngine.Channels.Count < 2)
        {
            return;
        }

        long nowTicks = Stopwatch.GetTimestamp();

        var channel1 = _audioEngine.Channels[0];
        var channel2 = _audioEngine.Channels[1];

        Channel1.InputPeakLevel = channel1.InputMeter.GetPeakLevel();
        Channel1.InputRmsLevel = channel1.InputMeter.GetRmsLevel();
        Channel1.OutputPeakLevel = channel1.OutputMeter.GetPeakLevel();
        Channel1.OutputRmsLevel = channel1.OutputMeter.GetRmsLevel();

        Channel2.InputPeakLevel = channel2.InputMeter.GetPeakLevel();
        Channel2.InputRmsLevel = channel2.InputMeter.GetRmsLevel();
        Channel2.OutputPeakLevel = channel2.OutputMeter.GetPeakLevel();
        Channel2.OutputRmsLevel = channel2.OutputMeter.GetRmsLevel();

        MasterLufsMomentaryLeft = _audioEngine.MasterLufsLeft.GetMomentaryLufs();
        MasterLufsShortTermLeft = _audioEngine.MasterLufsLeft.GetShortTermLufs();
        MasterLufsMomentaryRight = _audioEngine.MasterLufsRight.GetMomentaryLufs();
        MasterLufsShortTermRight = _audioEngine.MasterLufsRight.GetShortTermLufs();

        UpdatePluginMeters(Channel1, channel1);
        UpdatePluginMeters(Channel2, channel2);
        UpdateContainerMeters(Channel1, channel1);
        UpdateContainerMeters(Channel2, channel2);
        UpdateContainerWindowMeters(0, channel1);
        UpdateContainerWindowMeters(1, channel2);

        // Update hotbar stats and diagnostics
        Diagnostics = _audioEngine.GetDiagnosticsSnapshot();
        int sampleRate = Math.Max(1, _audioEngine.SampleRate);
        float baseLatencyMs = SelectedBufferSize * 1000f / sampleRate;
        int chainLatencySamples = Math.Max(GetChainLatencySamples(channel1), GetChainLatencySamples(channel2));
        float chainLatencyMs = chainLatencySamples * 1000f / sampleRate;
        LatencyMs = baseLatencyMs + chainLatencyMs;
        TotalDrops = Diagnostics.Input1DroppedSamples + Diagnostics.Input2DroppedSamples +
                     Diagnostics.OutputUnderflowSamples1 + Diagnostics.OutputUnderflowSamples2;

        // Track drops over 30-second rolling window
        _dropHistory.Enqueue((nowTicks, Diagnostics.Input1DroppedSamples, Diagnostics.Input2DroppedSamples,
                              Diagnostics.OutputUnderflowSamples1, Diagnostics.OutputUnderflowSamples2));

        // Remove entries older than 30 seconds
        long cutoffTicks = nowTicks - ThirtySecondsInTicks;
        while (_dropHistory.Count > 0 && _dropHistory.Peek().ticks < cutoffTicks)
        {
            _dropHistory.Dequeue();
        }

        // Calculate 30-second drops (current - oldest in window)
        if (_dropHistory.Count > 0)
        {
            var oldest = _dropHistory.Peek();
            Input1Drops30Sec = Diagnostics.Input1DroppedSamples - oldest.input1;
            Input2Drops30Sec = Diagnostics.Input2DroppedSamples - oldest.input2;
            Underflow1Drops30Sec = Diagnostics.OutputUnderflowSamples1 - oldest.underflow1;
            Underflow2Drops30Sec = Diagnostics.OutputUnderflowSamples2 - oldest.underflow2;
            Drops30Sec = Input1Drops30Sec + Input2Drops30Sec + Underflow1Drops30Sec + Underflow2Drops30Sec;
        }

        UpdateDebugInfo(nowTicks);
        _audioEngine.DrainPendingPluginDisposals();
        _lastMeterUpdateTicks = nowTicks;
    }

    private static void UpdatePluginMeters(ChannelStripViewModel viewModel, ChannelStrip channel)
    {
        var slots = channel.PluginChain.GetSnapshot();
        int pluginSlots = Math.Max(0, viewModel.PluginSlots.Count - 1);
        int slotCount = Math.Min(pluginSlots, slots.Length);

        for (int i = 0; i < slotCount; i++)
        {
            var slot = viewModel.PluginSlots[i];
            var chainSlot = slots[i];
            if (chainSlot is not null)
            {
                slot.OutputPeakLevel = chainSlot.Meter.GetPeakLevel();
                slot.OutputRmsLevel = chainSlot.Meter.GetRmsLevel();
            }
            else
            {
                slot.OutputPeakLevel = 0f;
                slot.OutputRmsLevel = 0f;
            }
        }

        // Update spectral delta data and sync display mode
        for (int i = 0; i < slotCount; i++)
        {
            var slot = viewModel.PluginSlots[i];
            var chainSlot = slots[i];
            if (chainSlot is null)
            {
                slot.SpectralDelta = null;
                continue;
            }

            var delta = chainSlot.Delta;

            // Sync display mode from ViewModel to processor (so V/F toggle works)
            if (delta.DisplayMode != slot.DeltaDisplayMode)
            {
                delta.DisplayMode = slot.DeltaDisplayMode;
            }

            if (delta.TryUpdate())
            {
                slot.SpectralDelta = delta.BandDeltas;
            }
        }

        for (int i = slotCount; i < pluginSlots; i++)
        {
            var slot = viewModel.PluginSlots[i];
            slot.OutputPeakLevel = 0f;
            slot.OutputRmsLevel = 0f;
            slot.SpectralDelta = null;
        }
    }

    private static void UpdateContainerMeters(ChannelStripViewModel viewModel, ChannelStrip channel)
    {
        if (viewModel.Containers.Count == 0)
        {
            return;
        }

        for (int i = 0; i < viewModel.Containers.Count; i++)
        {
            var container = viewModel.Containers[i];
            if (container.ContainerId <= 0)
            {
                container.OutputPeakLevel = 0f;
                container.OutputRmsLevel = 0f;
                continue;
            }

            var pluginIds = container.PluginInstanceIds;
            container.IsEmpty = pluginIds.Count == 0;
            if (pluginIds.Count == 0)
            {
                container.OutputPeakLevel = 0f;
                container.OutputRmsLevel = 0f;
                continue;
            }

            int lastInstanceId = pluginIds[^1];
            if (lastInstanceId > 0 &&
                channel.PluginChain.TryGetSlotById(lastInstanceId, out var slot, out _) &&
                slot is not null)
            {
                container.OutputPeakLevel = slot.Meter.GetPeakLevel();
                container.OutputRmsLevel = slot.Meter.GetRmsLevel();
            }
            else
            {
                container.OutputPeakLevel = 0f;
                container.OutputRmsLevel = 0f;
            }
        }
    }

    private void UpdateContainerWindowMeters(int channelIndex, ChannelStrip channel)
    {
        if (_containerWindows.Count == 0)
        {
            return;
        }

        foreach (var entry in _containerWindows)
        {
            if (entry.Key.ChannelIndex != channelIndex)
            {
                continue;
            }

            if (entry.Value.DataContext is not PluginContainerWindowViewModel viewModel)
            {
                continue;
            }

            int lastPluginIndex = viewModel.PluginSlots.Count - 2;
            if (lastPluginIndex < 0)
            {
                continue;
            }

            for (int i = 0; i <= lastPluginIndex; i++)
            {
                var slotVm = viewModel.PluginSlots[i];
                if (slotVm.InstanceId > 0 &&
                    channel.PluginChain.TryGetSlotById(slotVm.InstanceId, out var slot, out _) &&
                    slot is not null)
                {
                    slotVm.OutputPeakLevel = slot.Meter.GetPeakLevel();
                    slotVm.OutputRmsLevel = slot.Meter.GetRmsLevel();
                    slotVm.IsBypassed = slot.Plugin.IsBypassed;
                }
                else
                {
                    slotVm.OutputPeakLevel = 0f;
                    slotVm.OutputRmsLevel = 0f;
                }
            }
        }
    }

    private void UpdateDebugInfo(long nowTicks)
    {
        if (nowTicks < _nextDebugUpdateTicks)
        {
            return;
        }

        _nextDebugUpdateTicks = nowTicks + DebugUpdateIntervalTicks;
        var diagnostics = _audioEngine.GetDiagnosticsSnapshot();

        string outputAge = FormatAgeMs(nowTicks, diagnostics.LastOutputCallbackTicks);
        string input1Age = FormatAgeMs(nowTicks, diagnostics.LastInput1CallbackTicks);
        string input2Age = FormatAgeMs(nowTicks, diagnostics.LastInput2CallbackTicks);
        string uiAge = _lastMeterUpdateTicks == 0
            ? "n/a"
            : $"{Math.Max(0.0, (nowTicks - _lastMeterUpdateTicks) * 1000.0 / Stopwatch.Frequency):0}";

        DebugLines =
        [
            $"Audio: out={FormatFlag(diagnostics.OutputActive)} in1={FormatFlag(diagnostics.Input1Active)} in2={FormatFlag(diagnostics.Input2Active)} mon={FormatFlag(diagnostics.MonitorActive)} recov={(diagnostics.IsRecovering ? "yes" : "no")}",
            $"Callbacks(ms): out={outputAge} in1={input1Age} in2={input2Age}",
            $"Buffers: in1 {diagnostics.Input1BufferedSamples}/{diagnostics.Input1BufferCapacity} in2 {diagnostics.Input2BufferedSamples}/{diagnostics.Input2BufferCapacity} mon {diagnostics.MonitorBufferedSamples}/{diagnostics.MonitorBufferCapacity}",
            $"Drops: in1 {diagnostics.Input1DroppedSamples} in2 {diagnostics.Input2DroppedSamples} under1 {diagnostics.OutputUnderflowSamples1} under2 {diagnostics.OutputUnderflowSamples2}",
            $"Formats: in1 {diagnostics.Input1SampleRate}Hz/{diagnostics.Input1Channels}ch in2 {diagnostics.Input2SampleRate}Hz/{diagnostics.Input2Channels}ch out {SelectedSampleRate}Hz/2ch",
            $"Routing: in1 {FormatInputChannel(SelectedInput1Channel)} in2 {FormatInputChannel(SelectedInput2Channel)} out {FormatOutputRouting(SelectedOutputRouting)}  UI {uiAge}ms"
        ];
    }

    private static string FormatAgeMs(long nowTicks, long lastTicks)
    {
        if (lastTicks == 0)
        {
            return "n/a";
        }

        double ms = (nowTicks - lastTicks) * 1000.0 / Stopwatch.Frequency;
        if (ms < 0)
        {
            ms = 0;
        }

        return $"{ms:0}";
    }

    private static string FormatFlag(bool value) => value ? "on" : "off";

    private static string FormatInputChannel(InputChannelMode mode) => mode switch
    {
        InputChannelMode.Left => "L",
        InputChannelMode.Right => "R",
        _ => "Sum"
    };

    private static string FormatOutputRouting(OutputRoutingMode mode) => mode switch
    {
        OutputRoutingMode.Sum => "Sum",
        _ => "Split"
    };

    private void UpdateChannelConfig(int channelIndex, ChannelStripViewModel viewModel, string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return;
        }

        var config = GetOrCreateChannelConfig(channelIndex);
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
        }

        _configManager.Save(_config);
    }

    private void HandlePluginAction(int channelIndex, int pluginInstanceId, int slotIndex)
    {
        if ((uint)channelIndex >= (uint)_audioEngine.Channels.Count)
        {
            return;
        }

        var strip = _audioEngine.Channels[channelIndex];
        bool isAddPlaceholder = pluginInstanceId <= 0;
        if (isAddPlaceholder)
        {
            var choice = ShowPluginBrowser();
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
                qualityPlugin.ApplyQuality(GetQualityProfile());
            }

            newPlugin.Initialize(_audioEngine.SampleRate, _audioEngine.BlockSize);

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

            int instanceId = strip.PluginChain.InsertSlot(insertIndex, newPlugin);
            UpdatePluginConfig(channelIndex, insertIndex, instanceId, newPlugin);
            AssignPluginToContainer(channelIndex, instanceId);
            RefreshPluginViewModels(channelIndex);
            UpdateDynamicWindowWidth();
            return;
        }

        if (!strip.PluginChain.TryGetSlotById(pluginInstanceId, out var slot, out _)
            || slot is null)
        {
            return;
        }

        if (slot.Plugin is Vst3PluginWrapper vst3)
        {
            ShowVst3Editor(vst3);
            return;
        }

        ShowPluginParameters(channelIndex, pluginInstanceId, slot.Plugin);
    }

    private void HandleContainerAction(int channelIndex, int containerId)
    {
        if (containerId <= 0)
        {
            CreateContainer(channelIndex, openWindow: true);
            return;
        }

        OpenContainerWindow(channelIndex, containerId);
    }

    private void HandleContainerPluginAction(int channelIndex, int containerId, int pluginInstanceId, int insertIndex)
    {
        if (pluginInstanceId > 0)
        {
            HandlePluginAction(channelIndex, pluginInstanceId, insertIndex);
            return;
        }

        if ((uint)channelIndex >= (uint)_audioEngine.Channels.Count)
        {
            return;
        }

        var strip = _audioEngine.Channels[channelIndex];
        var choice = ShowPluginBrowser();
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
            qualityPlugin.ApplyQuality(GetQualityProfile());
        }

        newPlugin.Initialize(_audioEngine.SampleRate, _audioEngine.BlockSize);

        int chainCount = strip.PluginChain.Count;
        if (insertIndex < 0)
        {
            insertIndex = 0;
        }
        else if (insertIndex > chainCount)
        {
            insertIndex = chainCount;
        }

        int instanceId = strip.PluginChain.InsertSlot(insertIndex, newPlugin);
        UpdatePluginConfig(channelIndex, insertIndex, instanceId, newPlugin);
        AssignPluginToContainer(channelIndex, instanceId, containerId);
        RefreshPluginViewModels(channelIndex);
        UpdateDynamicWindowWidth();
    }

    private void RemovePlugin(int channelIndex, int pluginInstanceId)
    {
        if ((uint)channelIndex >= (uint)_audioEngine.Channels.Count)
        {
            return;
        }

        var strip = _audioEngine.Channels[channelIndex];
        if (!strip.PluginChain.TryGetSlotById(pluginInstanceId, out var slot, out int slotIndex)
            || slot is null)
        {
            return;
        }

        strip.PluginChain.RemoveSlot(slotIndex);
        _audioEngine.QueuePluginDisposal(slot.Plugin);
        RemovePluginConfig(channelIndex, pluginInstanceId);
        RemovePluginFromContainers(channelIndex, pluginInstanceId);
        RefreshPluginViewModels(channelIndex);
        UpdateDynamicWindowWidth();
    }

    private void ReorderPlugins(int channelIndex, int pluginInstanceId, int toIndex)
    {
        if ((uint)channelIndex >= (uint)_audioEngine.Channels.Count)
        {
            return;
        }

        var strip = _audioEngine.Channels[channelIndex];
        var slots = strip.PluginChain.GetSnapshot();
        if (slots.Length == 0)
        {
            return;
        }

        if (!strip.PluginChain.TryGetSlotById(pluginInstanceId, out var slot, out int fromIndex)
            || slot is null)
        {
            return;
        }

        if (toIndex < 0)
        {
            toIndex = 0;
        }
        else if (toIndex >= slots.Length)
        {
            toIndex = slots.Length - 1;
        }

        if (fromIndex == toIndex)
        {
            return;
        }

        var newSlots = new List<PluginSlot?>(slots);
        var item = newSlots[fromIndex];
        newSlots.RemoveAt(fromIndex);
        newSlots.Insert(toIndex, item);

        var newSlotsArray = newSlots.ToArray();
        strip.PluginChain.ReplaceAll(newSlotsArray);
        QueueRemovedPlugins(slots, newSlotsArray);
        MovePluginConfig(channelIndex, pluginInstanceId, toIndex);
        var config = GetOrCreateChannelConfig(channelIndex);
        if (NormalizeContainers(config, newSlotsArray))
        {
            _configManager.Save(_config);
        }
        RefreshPluginViewModels(channelIndex);
    }

    private void CreateContainer(int channelIndex, bool openWindow)
    {
        var config = GetOrCreateChannelConfig(channelIndex);
        int nextId = 1;
        for (int i = 0; i < config.Containers.Count; i++)
        {
            if (config.Containers[i].Id >= nextId)
            {
                nextId = config.Containers[i].Id + 1;
            }
        }

        var container = new PluginContainerConfig
        {
            Id = nextId,
            Name = $"Container {nextId}",
            IsBypassed = false
        };

        if (config.Containers.Count == 0)
        {
            var slots = _audioEngine.Channels[channelIndex].PluginChain.GetSnapshot();
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] is { } slot)
                {
                    container.PluginInstanceIds.Add(slot.InstanceId);
                }
            }
        }

        config.Containers.Add(container);
        NormalizeContainers(config, _audioEngine.Channels[channelIndex].PluginChain.GetSnapshot());
        MarkChannelPresetCustom(config);
        _configManager.Save(_config);
        RefreshPluginViewModels(channelIndex);
        UpdateDynamicWindowWidth();

        if (openWindow)
        {
            OpenContainerWindow(channelIndex, container.Id);
        }
    }

    private void RemoveContainer(int channelIndex, int containerId)
    {
        var config = GetOrCreateChannelConfig(channelIndex);
        int index = config.Containers.FindIndex(c => c.Id == containerId);
        if (index < 0)
        {
            return;
        }

        var removed = config.Containers[index];
        config.Containers.RemoveAt(index);

        if (config.Containers.Count > 0 && removed.PluginInstanceIds.Count > 0)
        {
            int targetIndex = Math.Clamp(index - 1, 0, config.Containers.Count - 1);
            var target = config.Containers[targetIndex];
            for (int i = 0; i < removed.PluginInstanceIds.Count; i++)
            {
                int instanceId = removed.PluginInstanceIds[i];
                if (!target.PluginInstanceIds.Contains(instanceId))
                {
                    target.PluginInstanceIds.Add(instanceId);
                }
            }
        }

        NormalizeContainers(config, _audioEngine.Channels[channelIndex].PluginChain.GetSnapshot());
        MarkChannelPresetCustom(config);
        _configManager.Save(_config);
        CloseContainerWindow(channelIndex, containerId);
        RefreshPluginViewModels(channelIndex);
        UpdateDynamicWindowWidth();
    }

    private void SetContainerBypass(int channelIndex, int containerId, bool bypassed)
    {
        var config = GetOrCreateChannelConfig(channelIndex);
        var container = config.Containers.FirstOrDefault(c => c.Id == containerId);
        if (container is null)
        {
            return;
        }

        container.IsBypassed = bypassed;
        for (int i = 0; i < container.PluginInstanceIds.Count; i++)
        {
            SetPluginBypass(channelIndex, container.PluginInstanceIds[i], bypassed);
        }

        MarkChannelPresetCustom(config);
        _configManager.Save(_config);
    }

    private void OpenContainerWindow(int channelIndex, int containerId)
    {
        if ((uint)channelIndex >= (uint)_audioEngine.Channels.Count)
        {
            return;
        }

        var key = (channelIndex, containerId);
        if (_containerWindows.TryGetValue(key, out var existing))
        {
            existing.Activate();
            return;
        }

        var config = GetOrCreateChannelConfig(channelIndex);
        var container = config.Containers.FirstOrDefault(c => c.Id == containerId);
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
            (instanceId, toIndex) => ReorderPlugins(channelIndex, instanceId, toIndex),
            EnqueueParameterChange,
            (instanceId, bypass) => UpdatePluginBypassConfig(channelIndex, instanceId, bypass));

        UpdateContainerWindowViewModel(viewModel, channelIndex, container);

        var window = new PluginContainerWindow(viewModel)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Closed += (_, _) => _containerWindows.Remove(key);
        _containerWindows[key] = window;
        window.Show();
    }

    private void CloseContainerWindow(int channelIndex, int containerId)
    {
        var key = (channelIndex, containerId);
        if (_containerWindows.TryGetValue(key, out var window))
        {
            _containerWindows.Remove(key);
            window.Close();
        }
    }

    private void UpdateContainerWindowViewModel(PluginContainerWindowViewModel viewModel, int channelIndex, PluginContainerConfig container)
    {
        var strip = _audioEngine.Channels[channelIndex];
        var slots = strip.PluginChain.GetSnapshot();
        var slotInfos = new List<PluginSlotInfo>(slots.Length);
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            float latencyMs = 0f;
            string pluginId = string.Empty;
            float[] elevatedValues = [];
            int instanceId = slot?.InstanceId ?? 0;

            if (slot is not null)
            {
                var plugin = slot.Plugin;
                pluginId = plugin.Id;
                if (_audioEngine.SampleRate > 0)
                {
                    latencyMs = plugin.LatencySamples * 1000f / _audioEngine.SampleRate;
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

            slotInfos.Add(new PluginSlotInfo
            {
                PluginId = pluginId,
                Name = slot?.Plugin.Name ?? string.Empty,
                IsBypassed = slot?.Plugin.IsBypassed ?? false,
                LatencyMs = latencyMs,
                InstanceId = instanceId,
                ElevatedParamValues = elevatedValues
            });
        }

        viewModel.UpdateName(container.Name);
        viewModel.UpdatePlugins(slotInfos, container.PluginInstanceIds);
    }

    private void UpdateOpenContainerWindows(int channelIndex, IReadOnlyList<PluginSlotInfo> slotInfos)
    {
        if (_containerWindows.Count == 0)
        {
            return;
        }

        var config = GetOrCreateChannelConfig(channelIndex);
        var keys = _containerWindows.Keys.ToArray();
        for (int i = 0; i < keys.Length; i++)
        {
            var key = keys[i];
            if (key.ChannelIndex != channelIndex)
            {
                continue;
            }

            var container = config.Containers.FirstOrDefault(c => c.Id == key.ContainerId);
            if (container is null)
            {
                CloseContainerWindow(key.ChannelIndex, key.ContainerId);
                continue;
            }

            if (_containerWindows.TryGetValue(key, out var window) && window.DataContext is PluginContainerWindowViewModel viewModel)
            {
                viewModel.UpdateName(container.Name);
                viewModel.UpdatePlugins(slotInfos, container.PluginInstanceIds);
            }
        }
    }

    private void QueueRemovedPlugins(PluginSlot?[] oldSlots, PluginSlot?[] newSlots)
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
                _audioEngine.QueuePluginDisposal(slot.Plugin);
            }
        }
    }

    private PluginChoice? ShowPluginBrowser()
    {
        var choices = new List<PluginChoice>
        {
            new() { Id = ContainerChoiceId, Name = "Plugin Container", IsVst3 = false, Category = PluginCategory.Utility, Description = "Create a visual container to group plugins on the main strip" },
            // Dynamics
            new() { Id = "builtin:gain", Name = "Gain", IsVst3 = false, Category = PluginCategory.Dynamics, Description = "Simple gain and phase control" },
            new() { Id = "builtin:compressor", Name = "Compressor", IsVst3 = false, Category = PluginCategory.Dynamics, Description = "Dynamic range compression with soft knee" },
            new() { Id = "builtin:noisegate", Name = "Noise Gate", IsVst3 = false, Category = PluginCategory.Dynamics, Description = "Removes audio below threshold" },
            new() { Id = "builtin:deesser", Name = "De-Esser", IsVst3 = false, Category = PluginCategory.Dynamics, Description = "Tames sibilance in the high band" },
            new() { Id = "builtin:limiter", Name = "Limiter", IsVst3 = false, Category = PluginCategory.Dynamics, Description = "Brick-wall peak control" },
            new() { Id = "builtin:upward-expander", Name = "Upward Expander", IsVst3 = false, Category = PluginCategory.Dynamics, Description = "Restores micro-dynamics across bands" },
            new() { Id = "builtin:consonant-transient", Name = "Consonant Transient", IsVst3 = false, Category = PluginCategory.Dynamics, Description = "Emphasizes consonant transients without harshness" },

            // EQ
            new() { Id = "builtin:hpf", Name = "High-Pass Filter", IsVst3 = false, Category = PluginCategory.Eq, Description = "Fast rumble and plosive removal" },
            new() { Id = "builtin:eq3", Name = "5-Band EQ", IsVst3 = false, Category = PluginCategory.Eq, Description = "HPF + shelves + dual mid bands" },
            new() { Id = "builtin:dynamic-eq", Name = "Dynamic EQ", IsVst3 = false, Category = PluginCategory.Eq, Description = "Voiced/unvoiced keyed tonal movement" },

            // Noise Reduction
            new() { Id = "builtin:fft-noise", Name = "FFT Noise Removal", IsVst3 = false, Category = PluginCategory.NoiseReduction, Description = "Learns and removes background noise" },

            // Analysis
            new() { Id = "builtin:freq-analyzer", Name = "Frequency Analyzer", IsVst3 = false, Category = PluginCategory.Analysis, Description = "Real-time spectrum view with tunable bins" },
            new() { Id = "builtin:vocal-spectrograph", Name = "Vocal Spectrograph", IsVst3 = false, Category = PluginCategory.Analysis, Description = "Vocal-focused spectrogram with overlays" },
            new() { Id = "builtin:signal-generator", Name = "Signal Generator", IsVst3 = false, Category = PluginCategory.Analysis, Description = "Test tones, noise, and sample playback" },
            new() { Id = "builtin:sidechain-tap", Name = "Sidechain Tap", IsVst3 = false, Category = PluginCategory.Analysis, Description = "Sidechain signal source for downstream plugins" },

            // AI/ML
            new() { Id = "builtin:rnnoise", Name = "RNNoise", IsVst3 = false, Category = PluginCategory.AiMl, Description = "Neural network noise suppression" },
            new() { Id = "builtin:speechdenoiser", Name = "Speech Denoiser", IsVst3 = false, Category = PluginCategory.AiMl, Description = "SpeechDenoiser streaming model (DFN3)" },
            new() { Id = "builtin:voice-gate", Name = "Voice Gate", IsVst3 = false, Category = PluginCategory.AiMl, Description = "AI-powered voice activity detection" },

            // Effects
            new() { Id = "builtin:saturation", Name = "Saturation", IsVst3 = false, Category = PluginCategory.Effects, Description = "Soft clipping harmonic warmth" },
            new() { Id = "builtin:reverb", Name = "Reverb", IsVst3 = false, Category = PluginCategory.Effects, Description = "Convolution reverb with IR presets" },
            new() { Id = "builtin:spectral-contrast", Name = "Spectral Contrast", IsVst3 = false, Category = PluginCategory.Effects, Description = "Enhances spectral detail via lateral inhibition" },
            new() { Id = "builtin:air-exciter", Name = "Air Exciter", IsVst3 = false, Category = PluginCategory.Effects, Description = "Keyed high-frequency excitation" },
            new() { Id = "builtin:bass-enhancer", Name = "Bass Enhancer", IsVst3 = false, Category = PluginCategory.Effects, Description = "Psychoacoustic low-end harmonics" },
            new() { Id = "builtin:room-tone", Name = "Room Tone", IsVst3 = false, Category = PluginCategory.Effects, Description = "Controlled ambience bed with ducking" },
            new() { Id = "builtin:formant-enhance", Name = "Formant Enhancer", IsVst3 = false, Category = PluginCategory.Effects, Description = "Formant-aware enhancement for vowels" }
        };

        if (_config.EnableVstPlugins)
        {
            var scanner = new Vst3Scanner();
            foreach (var vst in scanner.Scan(_config.Vst2SearchPaths, _config.Vst3SearchPaths))
            {
                string prefix = vst.Format == VstPluginFormat.Vst2 ? "vst2:" : "vst3:";
                string label = vst.Format == VstPluginFormat.Vst2 ? "VST2" : "VST3";
                choices.Add(new PluginChoice
                {
                    Id = $"{prefix}{vst.Path}",
                    Name = $"{vst.Name} ({label})",
                    Path = vst.Path,
                    IsVst3 = true,
                    Format = vst.Format,
                    Category = PluginCategory.Vst,
                    Description = $"External {label} plugin"
                });
            }
        }

        var viewModel = new PluginBrowserViewModel(choices);
        var window = new PluginBrowserWindow(viewModel)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        return window.ShowDialog() == true ? viewModel.SelectedChoice : null;
    }

    private void ShowPluginParameters(int channelIndex, int pluginInstanceId, IPlugin plugin)
    {
        // Use specialized window for noise gate
        if (plugin is NoiseGatePlugin noiseGate)
        {
            ShowNoiseGateWindow(channelIndex, pluginInstanceId, noiseGate);
            return;
        }

        // Use specialized window for compressor
        if (plugin is CompressorPlugin compressor)
        {
            ShowCompressorWindow(channelIndex, pluginInstanceId, compressor);
            return;
        }

        // Use specialized window for signal generator
        if (plugin is SignalGeneratorPlugin signalGen)
        {
            ShowSignalGeneratorWindow(channelIndex, pluginInstanceId, signalGen);
            return;
        }

        // Use specialized window for gain
        if (plugin is GainPlugin gain)
        {
            ShowGainWindow(channelIndex, pluginInstanceId, gain);
            return;
        }

        // Use specialized window for 5-band EQ
        if (plugin is FiveBandEqPlugin eq)
        {
            ShowEqWindow(channelIndex, pluginInstanceId, eq);
            return;
        }

        // Use specialized window for FFT noise removal
        if (plugin is FFTNoiseRemovalPlugin fftNoise)
        {
            ShowFFTNoiseWindow(channelIndex, pluginInstanceId, fftNoise);
            return;
        }

        // Use specialized window for Frequency Analyzer
        if (plugin is FrequencyAnalyzerPlugin analyzer)
        {
            ShowFrequencyAnalyzerWindow(channelIndex, pluginInstanceId, analyzer);
            return;
        }

        // Use specialized window for Vocal Spectrograph
        if (plugin is VocalSpectrographPlugin spectrograph)
        {
            ShowVocalSpectrographWindow(channelIndex, pluginInstanceId, spectrograph);
            return;
        }

        // Use specialized window for Voice Gate (Silero VAD)
        if (plugin is SileroVoiceGatePlugin silero)
        {
            ShowVoiceGateWindow(channelIndex, pluginInstanceId, silero);
            return;
        }

        // Use specialized window for RNNoise
        if (plugin is RNNoisePlugin rnnoise)
        {
            ShowRNNoiseWindow(channelIndex, pluginInstanceId, rnnoise);
            return;
        }

        // Use specialized window for Speech Denoiser
        if (plugin is SpeechDenoiserPlugin speechDenoiser)
        {
            ShowSpeechDenoiserWindow(channelIndex, pluginInstanceId, speechDenoiser);
            return;
        }

        // Use specialized window for Reverb
        if (plugin is ConvolutionReverbPlugin reverb)
        {
            ShowReverbWindow(channelIndex, pluginInstanceId, reverb);
            return;
        }

        // Use specialized window for Limiter
        if (plugin is LimiterPlugin limiter)
        {
            ShowLimiterWindow(channelIndex, pluginInstanceId, limiter);
            return;
        }

        // Use specialized window for De-Esser
        if (plugin is DeEsserPlugin deesser)
        {
            ShowDeEsserWindow(channelIndex, pluginInstanceId, deesser);
            return;
        }

        // Use specialized window for High-Pass Filter
        if (plugin is HighPassFilterPlugin hpf)
        {
            ShowHighPassFilterWindow(channelIndex, pluginInstanceId, hpf);
            return;
        }

        // Use specialized window for Saturation
        if (plugin is SaturationPlugin saturation)
        {
            ShowSaturationWindow(channelIndex, pluginInstanceId, saturation);
            return;
        }

        // Use specialized window for Air Exciter
        if (plugin is AirExciterPlugin airExciter)
        {
            ShowAirExciterWindow(channelIndex, pluginInstanceId, airExciter);
            return;
        }

        // Use specialized window for Bass Enhancer
        if (plugin is BassEnhancerPlugin bassEnhancer)
        {
            ShowBassEnhancerWindow(channelIndex, pluginInstanceId, bassEnhancer);
            return;
        }

        // Use specialized window for Consonant Transient
        if (plugin is ConsonantTransientPlugin consonantTransient)
        {
            ShowConsonantTransientWindow(channelIndex, pluginInstanceId, consonantTransient);
            return;
        }

        // Use specialized window for Dynamic EQ
        if (plugin is DynamicEqPlugin dynamicEq)
        {
            ShowDynamicEqWindow(channelIndex, pluginInstanceId, dynamicEq);
            return;
        }

        // Use specialized window for Formant Enhancer
        if (plugin is FormantEnhancerPlugin formantEnhancer)
        {
            ShowFormantEnhancerWindow(channelIndex, pluginInstanceId, formantEnhancer);
            return;
        }

        // Use specialized window for Room Tone
        if (plugin is RoomTonePlugin roomTone)
        {
            ShowRoomToneWindow(channelIndex, pluginInstanceId, roomTone);
            return;
        }

        // Use specialized window for Sidechain Tap
        if (plugin is SidechainTapPlugin sidechainTap)
        {
            ShowSidechainTapWindow(channelIndex, pluginInstanceId, sidechainTap);
            return;
        }

        // Use specialized window for Spectral Contrast
        if (plugin is SpectralContrastPlugin spectralContrast)
        {
            ShowSpectralContrastWindow(channelIndex, pluginInstanceId, spectralContrast);
            return;
        }

        // Use specialized window for Upward Expander
        if (plugin is UpwardExpanderPlugin upwardExpander)
        {
            ShowUpwardExpanderWindow(channelIndex, pluginInstanceId, upwardExpander);
            return;
        }

        var parameterViewModels = plugin.Parameters.Select(parameter =>
        {
            float currentValue = GetPluginParameterValue(channelIndex, pluginInstanceId, parameter.Name, parameter.DefaultValue);
            return new PluginParameterViewModel(parameter.Index, parameter.Name, parameter.MinValue, parameter.MaxValue, currentValue, parameter.Unit,
                value => ApplyPluginParameter(channelIndex, pluginInstanceId, parameter.Index, parameter.Name, value));
        }).ToList();

        Action? learnNoiseAction = plugin is FFTNoiseRemovalPlugin ? () => RequestNoiseLearn(channelIndex, pluginInstanceId) : null;
        Func<float>? vadProvider = plugin switch
        {
            RNNoisePlugin rnn => () => rnn.VadProbability,
            SileroVoiceGatePlugin vad => () => vad.VadProbability,
            _ => null
        };
        string statusMessage = plugin is IPluginStatusProvider statusProvider ? statusProvider.StatusMessage : string.Empty;

        float latencyMs = _audioEngine.SampleRate > 0
            ? plugin.LatencySamples * 1000f / _audioEngine.SampleRate
            : 0f;
        var viewModel = new PluginParametersViewModel(plugin.Name, parameterViewModels, null, null, learnNoiseAction, latencyMs, statusMessage, vadProvider);
        var window = new PluginParametersWindow(viewModel)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowNoiseGateWindow(int channelIndex, int pluginInstanceId, NoiseGatePlugin plugin)
    {
        var window = new NoiseGateWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowCompressorWindow(int channelIndex, int pluginInstanceId, CompressorPlugin plugin)
    {
        var window = new CompressorWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowSignalGeneratorWindow(int channelIndex, int pluginInstanceId, SignalGeneratorPlugin plugin)
    {
        var window = new SignalGeneratorWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters.FirstOrDefault(p => p.Index == paramIndex)?.Name ?? "";
                ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowGainWindow(int channelIndex, int pluginInstanceId, GainPlugin plugin)
    {
        var window = new GainWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowEqWindow(int channelIndex, int pluginInstanceId, FiveBandEqPlugin plugin)
    {
        var window = new EqWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowFFTNoiseWindow(int channelIndex, int pluginInstanceId, FFTNoiseRemovalPlugin plugin)
    {
        var window = new FFTNoiseWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, pluginInstanceId, bypassed),
            () => RequestNoiseLearn(channelIndex, pluginInstanceId))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowFrequencyAnalyzerWindow(int channelIndex, int pluginInstanceId, FrequencyAnalyzerPlugin plugin)
    {
        var window = new FrequencyAnalyzerWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowVocalSpectrographWindow(int channelIndex, int pluginInstanceId, VocalSpectrographPlugin plugin)
    {
        var window = new VocalSpectrographWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowVoiceGateWindow(int channelIndex, int pluginInstanceId, SileroVoiceGatePlugin plugin)
    {
        var window = new VoiceGateWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowRNNoiseWindow(int channelIndex, int pluginInstanceId, RNNoisePlugin plugin)
    {
        var window = new RNNoiseWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowSpeechDenoiserWindow(int channelIndex, int pluginInstanceId, SpeechDenoiserPlugin plugin)
    {
        var window = new SpeechDenoiserWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowReverbWindow(int channelIndex, int pluginInstanceId, ConvolutionReverbPlugin plugin)
    {
        var window = new ReverbWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowLimiterWindow(int channelIndex, int pluginInstanceId, LimiterPlugin plugin)
    {
        var window = new LimiterWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowDeEsserWindow(int channelIndex, int pluginInstanceId, DeEsserPlugin plugin)
    {
        var window = new DeEsserWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowHighPassFilterWindow(int channelIndex, int pluginInstanceId, HighPassFilterPlugin plugin)
    {
        var window = new HighPassFilterWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowSaturationWindow(int channelIndex, int pluginInstanceId, SaturationPlugin plugin)
    {
        var window = new SaturationWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowAirExciterWindow(int channelIndex, int pluginInstanceId, AirExciterPlugin plugin)
    {
        var window = new AirExciterWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowBassEnhancerWindow(int channelIndex, int pluginInstanceId, BassEnhancerPlugin plugin)
    {
        var window = new BassEnhancerWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowConsonantTransientWindow(int channelIndex, int pluginInstanceId, ConsonantTransientPlugin plugin)
    {
        var window = new ConsonantTransientWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowDynamicEqWindow(int channelIndex, int pluginInstanceId, DynamicEqPlugin plugin)
    {
        var window = new DynamicEqWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowFormantEnhancerWindow(int channelIndex, int pluginInstanceId, FormantEnhancerPlugin plugin)
    {
        var window = new FormantEnhancerWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowRoomToneWindow(int channelIndex, int pluginInstanceId, RoomTonePlugin plugin)
    {
        var window = new RoomToneWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowSidechainTapWindow(int channelIndex, int pluginInstanceId, SidechainTapPlugin plugin)
    {
        var window = new SidechainTapWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowSpectralContrastWindow(int channelIndex, int pluginInstanceId, SpectralContrastPlugin plugin)
    {
        var window = new SpectralContrastWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowUpwardExpanderWindow(int channelIndex, int pluginInstanceId, UpwardExpanderPlugin plugin)
    {
        var window = new UpwardExpanderWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void SetPluginBypass(int channelIndex, int pluginInstanceId, bool bypassed)
    {
        var channel = channelIndex == 0 ? Channel1 : Channel2;
        int slotIndex = FindPluginSlotIndex(channel, pluginInstanceId);
        if (slotIndex >= 0 && slotIndex < channel.PluginSlots.Count)
        {
            channel.PluginSlots[slotIndex].IsBypassed = bypassed;
        }
    }

    private static void ShowVst3Editor(Vst3PluginWrapper plugin)
    {
        var window = new Vst3EditorWindow(plugin)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
            Title = plugin.Name
        };
        window.Show();
    }

    private void RequestNoiseLearn(int channelIndex, int pluginInstanceId)
    {
        _audioEngine.EnqueueParameterChange(new ParameterChange
        {
            ChannelId = channelIndex,
            Type = ParameterType.PluginCommand,
            PluginInstanceId = pluginInstanceId,
            Command = PluginCommandType.ToggleNoiseLearn
        });
    }

    private void ApplyPluginParameter(int channelIndex, int pluginInstanceId, int parameterIndex, string parameterName, float value, bool markPresetDirty = true)
    {
        _audioEngine.EnqueueParameterChange(new ParameterChange
        {
            ChannelId = channelIndex,
            Type = ParameterType.PluginParameter,
            PluginInstanceId = pluginInstanceId,
            ParameterIndex = parameterIndex,
            Value = value
        });

        UpdatePluginParameterConfig(channelIndex, pluginInstanceId, parameterName, value, markPresetDirty);
        UpdatePluginStateConfig(channelIndex, pluginInstanceId);
    }

    private void RefreshPluginViewModels(int channelIndex)
    {
        var strip = _audioEngine.Channels[channelIndex];
        var slots = strip.PluginChain.GetSnapshot();
        var slotInfos = new List<PluginSlotInfo>(slots.Length);
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            float latencyMs = 0f;
            string pluginId = string.Empty;
            float[] elevatedValues = [];
            int instanceId = slot?.InstanceId ?? 0;

            if (slot is not null)
            {
                var plugin = slot.Plugin;
                pluginId = plugin.Id;
                if (_audioEngine.SampleRate > 0)
                {
                    latencyMs = plugin.LatencySamples * 1000f / _audioEngine.SampleRate;
                }

                // Get elevated parameter values
                var elevDefs = ElevatedParameterDefinitions.GetElevations(pluginId);
                if (elevDefs is not null)
                {
                    elevatedValues = new float[elevDefs.Length];
                    for (int j = 0; j < elevDefs.Length; j++)
                    {
                        // Read current parameter value from plugin state
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

            slotInfos.Add(new PluginSlotInfo
            {
                PluginId = pluginId,
                Name = slot?.Plugin.Name ?? string.Empty,
                IsBypassed = slot?.Plugin.IsBypassed ?? false,
                LatencyMs = latencyMs,
                InstanceId = instanceId,
                ElevatedParamValues = elevatedValues
            });
        }
        if (channelIndex == 0)
        {
            Channel1.UpdatePlugins(slotInfos);
            Channel1.UpdateContainers(BuildContainerInfos(channelIndex, slots));
        }
        else
        {
            Channel2.UpdatePlugins(slotInfos);
            Channel2.UpdateContainers(BuildContainerInfos(channelIndex, slots));
        }

        UpdateOpenContainerWindows(channelIndex, slotInfos);
    }

    private IReadOnlyList<PluginContainerInfo> BuildContainerInfos(int channelIndex, PluginSlot?[] slots)
    {
        if (channelIndex >= _config.Channels.Count)
        {
            return Array.Empty<PluginContainerInfo>();
        }

        var config = _config.Channels[channelIndex];
        if (config.Containers.Count == 0)
        {
            return Array.Empty<PluginContainerInfo>();
        }

        var list = new List<PluginContainerInfo>(config.Containers.Count);
        for (int i = 0; i < config.Containers.Count; i++)
        {
            var container = config.Containers[i];
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

    private static int GetChainLatencySamples(ChannelStrip channel)
    {
        var slots = channel.PluginChain.GetSnapshot();
        int latency = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot is null || slot.Plugin.IsBypassed)
            {
                continue;
            }

            latency += Math.Max(0, slot.Plugin.LatencySamples);
        }

        return latency;
    }

    private ChannelConfig GetOrCreateChannelConfig(int channelIndex)
    {
        while (_config.Channels.Count <= channelIndex)
        {
            _config.Channels.Add(new ChannelConfig { Id = _config.Channels.Count + 1, Name = $"Mic {_config.Channels.Count + 1}" });
        }

        return _config.Channels[channelIndex];
    }

    private void EnsurePluginListCapacity(ChannelConfig config, int requiredCapacity)
    {
        while (config.Plugins.Count < requiredCapacity)
        {
            config.Plugins.Add(new PluginConfig());
        }
    }

    private Dictionary<int, int> GetPluginIndexMap(int channelIndex)
    {
        return channelIndex == 0 ? _channel1PluginIndexMap : _channel2PluginIndexMap;
    }

    private int GetPluginConfigIndex(int channelIndex, int instanceId)
    {
        if (instanceId <= 0)
        {
            return -1;
        }

        var config = GetOrCreateChannelConfig(channelIndex);
        var map = GetPluginIndexMap(channelIndex);
        if (map.TryGetValue(instanceId, out int index) &&
            index >= 0 &&
            index < config.Plugins.Count &&
            config.Plugins[index].InstanceId == instanceId)
        {
            return index;
        }

        RebuildPluginIndexMap(config, map);
        return map.TryGetValue(instanceId, out index) ? index : -1;
    }

    private static void RebuildPluginIndexMap(ChannelConfig config, Dictionary<int, int> map)
    {
        map.Clear();
        for (int i = 0; i < config.Plugins.Count; i++)
        {
            int instanceId = config.Plugins[i].InstanceId;
            if (instanceId > 0 && !map.ContainsKey(instanceId))
            {
                map[instanceId] = i;
            }
        }
    }

    private bool NormalizeContainers(ChannelConfig config, PluginSlot?[] slots)
    {
        bool changed = false;
        var indexMap = new Dictionary<int, int>(slots.Length);
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] is { } slot)
            {
                indexMap[slot.InstanceId] = i;
            }
        }

        int nextContainerId = 0;
        for (int i = 0; i < config.Containers.Count; i++)
        {
            var container = config.Containers[i];
            if (container.Id > nextContainerId)
            {
                nextContainerId = container.Id;
            }
        }

        var assigned = new HashSet<int>();
        for (int i = 0; i < config.Containers.Count; i++)
        {
            var container = config.Containers[i];
            if (container.Id <= 0)
            {
                container.Id = ++nextContainerId;
                changed = true;
            }

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
                container.PluginInstanceIds.Clear();
                container.PluginInstanceIds.AddRange(ordered);
                changed = true;
            }
        }

        if (config.Containers.Count > 0)
        {
            var target = config.Containers[^1];
            bool appended = false;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] is not { } slot)
                {
                    continue;
                }

                if (assigned.Add(slot.InstanceId))
                {
                    target.PluginInstanceIds.Add(slot.InstanceId);
                    appended = true;
                }
            }

            if (appended)
            {
                target.PluginInstanceIds.Sort((a, b) => indexMap[a].CompareTo(indexMap[b]));
                changed = true;
            }
        }

        return changed;
    }

    private void AssignPluginToContainer(int channelIndex, int instanceId)
    {
        if (instanceId <= 0)
        {
            return;
        }

        var config = GetOrCreateChannelConfig(channelIndex);
        if (config.Containers.Count == 0)
        {
            return;
        }

        var container = config.Containers[^1];
        if (!container.PluginInstanceIds.Contains(instanceId))
        {
            container.PluginInstanceIds.Add(instanceId);
            NormalizeContainers(config, _audioEngine.Channels[channelIndex].PluginChain.GetSnapshot());
            MarkChannelPresetCustom(config);
            _configManager.Save(_config);
        }
    }

    private void AssignPluginToContainer(int channelIndex, int instanceId, int containerId)
    {
        if (instanceId <= 0 || containerId <= 0)
        {
            return;
        }

        var config = GetOrCreateChannelConfig(channelIndex);
        var container = config.Containers.FirstOrDefault(c => c.Id == containerId);
        if (container is null)
        {
            return;
        }

        if (!container.PluginInstanceIds.Contains(instanceId))
        {
            container.PluginInstanceIds.Add(instanceId);
            NormalizeContainers(config, _audioEngine.Channels[channelIndex].PluginChain.GetSnapshot());
            MarkChannelPresetCustom(config);
            _configManager.Save(_config);
        }
    }

    private void RemovePluginFromContainers(int channelIndex, int instanceId)
    {
        if (instanceId <= 0)
        {
            return;
        }

        var config = GetOrCreateChannelConfig(channelIndex);
        if (config.Containers.Count == 0)
        {
            return;
        }

        bool changed = false;
        for (int i = 0; i < config.Containers.Count; i++)
        {
            var container = config.Containers[i];
            if (container.PluginInstanceIds.Remove(instanceId))
            {
                changed = true;
            }
        }

        if (changed)
        {
            NormalizeContainers(config, _audioEngine.Channels[channelIndex].PluginChain.GetSnapshot());
            MarkChannelPresetCustom(config);
            _configManager.Save(_config);
        }
    }

    private void UpdatePluginConfig(int channelIndex, int slotIndex, int instanceId, IPlugin plugin)
    {
        var config = GetOrCreateChannelConfig(channelIndex);

        var pluginConfig = new PluginConfig
        {
            InstanceId = instanceId,
            Type = plugin.Id,
            IsBypassed = plugin.IsBypassed,
            PresetName = PluginPresetManager.CustomPresetName,
            Parameters = plugin.Parameters.ToDictionary(p => p.Name, p => p.DefaultValue, StringComparer.OrdinalIgnoreCase),
            State = plugin.GetState()
        };

        if (slotIndex < 0)
        {
            slotIndex = 0;
        }

        if (slotIndex >= config.Plugins.Count)
        {
            EnsurePluginListCapacity(config, slotIndex + 1);
            config.Plugins[slotIndex] = pluginConfig;
        }
        else
        {
            config.Plugins.Insert(slotIndex, pluginConfig);
        }
        RebuildPluginIndexMap(config, GetPluginIndexMap(channelIndex));
        MarkChannelPresetCustom(config);
        _configManager.Save(_config);
    }

    private void RemovePluginConfig(int channelIndex, int instanceId)
    {
        var config = GetOrCreateChannelConfig(channelIndex);
        int slotIndex = GetPluginConfigIndex(channelIndex, instanceId);
        if (slotIndex >= 0 && slotIndex < config.Plugins.Count)
        {
            config.Plugins.RemoveAt(slotIndex);
        }
        RebuildPluginIndexMap(config, GetPluginIndexMap(channelIndex));
        MarkChannelPresetCustom(config);
        _configManager.Save(_config);
    }

    private void MovePluginConfig(int channelIndex, int instanceId, int toIndex)
    {
        var config = GetOrCreateChannelConfig(channelIndex);
        int fromIndex = GetPluginConfigIndex(channelIndex, instanceId);
        if ((uint)fromIndex >= (uint)config.Plugins.Count)
        {
            return;
        }

        if (toIndex < 0)
        {
            toIndex = 0;
        }
        else if (toIndex >= config.Plugins.Count)
        {
            toIndex = config.Plugins.Count - 1;
        }

        if (fromIndex == toIndex)
        {
            return;
        }

        var item = config.Plugins[fromIndex];
        config.Plugins.RemoveAt(fromIndex);
        config.Plugins.Insert(toIndex, item);
        RebuildPluginIndexMap(config, GetPluginIndexMap(channelIndex));
        MarkChannelPresetCustom(config);
        _configManager.Save(_config);
    }

    private void UpdatePluginParameterConfig(int channelIndex, int instanceId, string parameterName, float value, bool markPresetDirty)
    {
        var config = GetOrCreateChannelConfig(channelIndex);
        int slotIndex = GetPluginConfigIndex(channelIndex, instanceId);
        if (slotIndex < 0 || slotIndex >= config.Plugins.Count)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(config.Plugins[slotIndex].Type))
        {
            return;
        }

        config.Plugins[slotIndex].Parameters[parameterName] = value;

        if (markPresetDirty)
        {
            MarkPluginPresetCustom(config, slotIndex);
        }
        _configManager.Save(_config);
    }

    private void UpdatePluginStateConfig(int channelIndex, int instanceId)
    {
        if ((uint)channelIndex >= (uint)_audioEngine.Channels.Count)
        {
            return;
        }

        var strip = _audioEngine.Channels[channelIndex];
        if (!strip.PluginChain.TryGetSlotById(instanceId, out var slot, out _)
            || slot is null)
        {
            return;
        }

        var config = GetOrCreateChannelConfig(channelIndex);
        int slotIndex = GetPluginConfigIndex(channelIndex, instanceId);
        if (slotIndex >= config.Plugins.Count)
        {
            return;
        }
        config.Plugins[slotIndex].State = slot.Plugin.GetState();
        _configManager.Save(_config);
    }

    private void UpdatePluginBypassConfig(int channelIndex, int instanceId, bool bypass)
    {
        var config = GetOrCreateChannelConfig(channelIndex);
        int slotIndex = GetPluginConfigIndex(channelIndex, instanceId);
        if (slotIndex < 0 || slotIndex >= config.Plugins.Count)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(config.Plugins[slotIndex].Type))
        {
            return;
        }

        config.Plugins[slotIndex].IsBypassed = bypass;
        MarkPluginPresetCustom(config, slotIndex);
        _configManager.Save(_config);
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

    public void OpenAnalyzerWindow()
    {
        var window = new AnalyzerWindow(_analysisOrchestrator)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    public void AddContainer(int channelIndex)
    {
        CreateContainer(channelIndex, openWindow: true);
    }

    public void Dispose()
    {
        _meterTimer.Stop();
        foreach (var window in _containerWindows.Values)
        {
            window.Close();
        }
        _containerWindows.Clear();
        _midiManager?.Dispose();
        _audioEngine.Dispose();
        _analysisOrchestrator.Dispose();
    }

    public void AddMidiBinding(string targetPath, int ccNumber, int? channel, float minValue, float maxValue)
    {
        if (_midiManager == null) return;

        var binding = new MidiBinding
        {
            TargetPath = targetPath,
            CcNumber = ccNumber,
            Channel = channel,
            MinValue = minValue,
            MaxValue = maxValue
        };

        _midiManager.AddBinding(binding);
        _configManager.Save(_config);
    }

    public void RemoveMidiBinding(string targetPath)
    {
        _midiManager?.RemoveBinding(targetPath);
        _configManager.Save(_config);
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
        _config.Midi.Enabled = enabled;
        _midiManager?.ApplyConfig(_config.Midi);
        _configManager.Save(_config);
    }

    public void SetMidiDevice(string? deviceName)
    {
        _config.Midi.DeviceName = deviceName;
        _midiManager?.ApplyConfig(_config.Midi);
        _configManager.Save(_config);
    }

    public IReadOnlyList<string> GetPresetOptions() => _presetManager.GetChainPresetNames();

    public void SelectChannelPreset(int channelIndex, string presetName)
    {
        ApplyChannelPreset(channelIndex, presetName);
        UpdatePresetNameProperty(channelIndex);
    }

    public void SaveCurrentAsPreset(int channelIndex, string presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
        {
            return;
        }

        // Cannot overwrite built-in presets
        if (_presetManager.IsBuiltInPreset(presetName))
        {
            return;
        }

        var config = GetOrCreateChannelConfig(channelIndex);
        var plugins = new List<(string pluginId, Dictionary<string, float> parameters)>();
        var containers = new List<ChainPresetContainer>();

        foreach (var pluginConfig in config.Plugins)
        {
            if (string.IsNullOrWhiteSpace(pluginConfig.Type))
            {
                continue;
            }

            var parameters = new Dictionary<string, float>(pluginConfig.Parameters, StringComparer.OrdinalIgnoreCase);
            plugins.Add((pluginConfig.Type, parameters));
        }

        if (config.Containers.Count > 0)
        {
            var indexMap = new Dictionary<int, int>();
            for (int i = 0; i < config.Plugins.Count; i++)
            {
                int instanceId = config.Plugins[i].InstanceId;
                if (instanceId > 0 && !indexMap.ContainsKey(instanceId))
                {
                    indexMap[instanceId] = i;
                }
            }

            foreach (var container in config.Containers)
            {
                var indices = new List<int>();
                foreach (var instanceId in container.PluginInstanceIds)
                {
                    if (indexMap.TryGetValue(instanceId, out int index))
                    {
                        indices.Add(index);
                    }
                }

                containers.Add(new ChainPresetContainer(container.Name, indices, container.IsBypassed));
            }
        }

        if (_presetManager.SaveChainPreset(presetName, plugins, containers))
        {
            config.PresetName = presetName;
            _configManager.Save(_config);
            UpdatePresetNameProperty(channelIndex);
        }
    }

    public bool CanOverwritePreset(string presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
        {
            return false;
        }

        // Cannot overwrite built-in presets or "Custom"
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
            // Reset any channels using this preset to Custom
            var channel1Name = GetChannelPresetName(0);
            var channel2Name = GetChannelPresetName(1);

            if (string.Equals(channel1Name, presetName, StringComparison.OrdinalIgnoreCase))
            {
                var config = GetOrCreateChannelConfig(0);
                config.PresetName = PluginPresetManager.CustomPresetName;
                Channel1PresetName = PluginPresetManager.CustomPresetName;
            }

            if (string.Equals(channel2Name, presetName, StringComparison.OrdinalIgnoreCase))
            {
                var config = GetOrCreateChannelConfig(1);
                config.PresetName = PluginPresetManager.CustomPresetName;
                Channel2PresetName = PluginPresetManager.CustomPresetName;
            }

            _configManager.Save(_config);
            return true;
        }

        return false;
    }

    private void UpdatePresetNameProperty(int channelIndex)
    {
        var presetName = GetChannelPresetName(channelIndex);
        if (channelIndex == 0)
        {
            Channel1PresetName = presetName;
        }
        else
        {
            Channel2PresetName = presetName;
        }
    }

    private float GetPluginParameterValue(int channelIndex, int instanceId, string parameterName, float fallback)
    {
        if (channelIndex >= _config.Channels.Count)
        {
            return fallback;
        }

        var channel = _config.Channels[channelIndex];
        int slotIndex = GetPluginConfigIndex(channelIndex, instanceId);
        if (slotIndex < 0 || slotIndex >= channel.Plugins.Count)
        {
            return fallback;
        }

        var pluginConfig = channel.Plugins[slotIndex];
        if (string.IsNullOrWhiteSpace(pluginConfig.Type))
        {
            return fallback;
        }

        return pluginConfig.Parameters.TryGetValue(parameterName, out var value) ? value : fallback;
    }

    private static bool TryParseVstPluginType(string type, out VstPluginFormat format, out string path)
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
