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
    private PluginGraph[] _pluginGraphs = Array.Empty<PluginGraph>();
    private readonly Dictionary<(int ChannelIndex, int ContainerId), PluginContainerWindow> _containerWindows = new();
    private long _lastMeterUpdateTicks;
    private long _nextDebugUpdateTicks;
    private static readonly long DebugUpdateIntervalTicks = Math.Max(1, Stopwatch.Frequency / 4);
    private bool _isInitializing = true; // Skip side effects during initial config load
    private bool _suppressChannelConfig;
#pragma warning disable CS0649 // Field is reserved for future batch preset operations
    private bool _suppressPresetUpdates;
#pragma warning restore CS0649

    // 30-second rolling window for drop tracking
    private readonly Queue<(long ticks, long inputDrops, long outputUnderflow)> _dropHistory = new();
    private static readonly long ThirtySecondsInTicks = Stopwatch.Frequency * 30;

    public MainViewModel()
    {
        _config = _configManager.LoadOrDefault();
        if (_config.Channels.Count == 0)
        {
            _config.Channels.Add(new ChannelConfig
            {
                Id = 1,
                Name = "Mic 1",
                InputChannel = InputChannelMode.Sum,
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

        _audioEngine = new AudioEngine(_config.AudioSettings, _config.Channels.Count);

        // Connect analysis orchestrator to audio engine tap
        _analysisOrchestrator.Initialize(_config.AudioSettings.SampleRate);
        _audioEngine.AnalysisTap.Orchestrator = _analysisOrchestrator;
        _analysisOrchestrator.DebugTap = _audioEngine.AnalysisTap;

        BuildChannelViewModels();

        InputDevices = new ObservableCollection<AudioDevice>(_deviceManager.GetInputDevices());
        OutputDevices = new ObservableCollection<AudioDevice>(_deviceManager.GetOutputDevices());

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
            _audioEngine = new AudioEngine(_config.AudioSettings, _config.Channels.Count);
        }
        InitializePluginGraphs();
        LoadPluginsFromConfig();
        UpdateDynamicWindowWidth(); // Recalculate after plugins are loaded
        _isInitializing = false; // Now safe to allow side effects from property changes
        StartEngine();

        _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _meterTimer.Tick += (_, _) => UpdateMeters();
        _meterTimer.Start();

        InitializeMidi();
    }

    private void BuildChannelViewModels()
    {
        Channels.Clear();
        for (int i = 0; i < _config.Channels.Count; i++)
        {
            var config = _config.Channels[i];
            var viewModel = CreateChannelViewModel(i, string.IsNullOrWhiteSpace(config.Name) ? $"Channel {i + 1}" : config.Name);
            Channels.Add(viewModel);
            int channelIndex = i;
            viewModel.PropertyChanged += (_, e) => UpdateChannelConfig(channelIndex, viewModel, e.PropertyName);
        }

        if (ActiveChannelIndex < 0 || ActiveChannelIndex >= Channels.Count)
        {
            ActiveChannelIndex = 0;
        }
    }

    private ChannelStripViewModel CreateChannelViewModel(int channelIndex, string name)
    {
        return new ChannelStripViewModel(
            channelIndex,
            name,
            EnqueueParameterChange,
            (instanceId, slotIndex) => HandlePluginAction(channelIndex, instanceId, slotIndex),
            instanceId => RemovePlugin(channelIndex, instanceId),
            (instanceId, toIndex) => ReorderPlugins(channelIndex, instanceId, toIndex),
            (instanceId, value) => UpdatePluginBypassConfig(channelIndex, instanceId, value),
            containerId => HandleContainerAction(channelIndex, containerId),
            containerId => RemoveContainer(channelIndex, containerId),
            (containerId, bypass) => SetContainerBypass(channelIndex, containerId, bypass),
            (containerId, targetIndex) => ReorderContainer(channelIndex, containerId, targetIndex));
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

        int channelIndex = -1;
        if (parts[0].StartsWith("channel", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(parts[0].AsSpan("channel".Length), out int parsedIndex))
        {
            channelIndex = parsedIndex - 1;
        }

        if (channelIndex < 0 || channelIndex >= Channels.Count)
        {
            return;
        }

        var channel = Channels[channelIndex];

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

                    ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, "midi", value);

                    // Update the UI knob (slot indices in PluginSlots are 1-based, +1 for add placeholder)
                    int slotIndex = FindPluginSlotIndex(channel, pluginInstanceId);
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

    public ObservableCollection<ChannelStripViewModel> Channels { get; } = new();

    public ObservableCollection<AudioDevice> InputDevices { get; }

    public ObservableCollection<AudioDevice> OutputDevices { get; }

    public IReadOnlyList<int> SampleRateOptions { get; } = [44100, 48000];

    public IReadOnlyList<int> BufferSizeOptions { get; } = [128, 256, 512, 1024];

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
    private double windowX;

    [ObservableProperty]
    private double windowY;

    [ObservableProperty]
    private double windowWidth = 400; // Will be recalculated by UpdateDynamicWindowWidth

    [ObservableProperty]
    private double windowHeight = 290;

    [ObservableProperty]
    private int activeChannelIndex;

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
    private float masterPeakLeft;

    [ObservableProperty]
    private float masterPeakRight;

    [ObservableProperty]
    private float masterRmsLeft;

    [ObservableProperty]
    private float masterRmsRight;

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
    private long inputDrops30Sec;

    [ObservableProperty]
    private long outputUnderflowDrops30Sec;

    // Debug overlay
    [ObservableProperty]
    private bool showDebugOverlay;

    [ObservableProperty]
    private AudioEngineDiagnosticsSnapshot diagnostics;

    [ObservableProperty]
    private string activeChannelPresetName = PluginPresetManager.CustomPresetName;

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
        int channelCount = Math.Max(1, Channels.Count);
        if (IsMinimalView)
        {
            WindowWidth = MinimalViewWidth;
            double rowsHeight = MinimalRowHeight * channelCount + MinimalRowSpacing * Math.Max(0, channelCount - 1);
            WindowHeight = FullViewTitleBarHeight + MinimalViewPadding + rowsHeight + MinimalViewPadding;
            return;
        }

        // Calculate width based on longest visible chain (containers or plugins + add placeholder).
        int maxSlots = 1;
        for (int i = 0; i < channelCount; i++)
        {
            int visibleSlots = GetVisibleSlotCount(i);
            if (visibleSlots > maxSlots)
            {
                maxSlots = visibleSlots;
            }
        }

        double pluginAreaWidth = maxSlots * PluginSlotWidthWithSpacing;
        double calculatedWidth = FullViewBaseWidth + pluginAreaWidth;

        WindowWidth = Math.Clamp(calculatedWidth, MinFullViewWidth, MaxFullViewWidth);
        double channelAreaHeight = FullViewChannelHeight * channelCount + FullViewChannelSpacing * Math.Max(0, channelCount - 1);
        double addChannelAreaHeight = FullViewChannelSpacing + FullViewAddChannelHeight;
        WindowHeight = FullViewTitleBarHeight + FullViewHotbarHeight + FullViewPadding + channelAreaHeight + addChannelAreaHeight + FullViewPadding;
    }

    private int GetVisibleSlotCount(int channelIndex)
    {
        if ((uint)channelIndex >= (uint)_audioEngine.Channels.Count)
        {
            return 1;
        }

        var strip = _audioEngine.Channels[channelIndex];
        var slots = strip.PluginChain.GetSnapshot();
        var config = GetOrCreateChannelConfig(channelIndex);
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
            OutputDevices,
            SelectedOutputDevice,
            SelectedMonitorDevice,
            SelectedSampleRate,
            SelectedBufferSize,
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
            SelectedOutputDevice = settingsViewModel.SelectedOutputDevice;
            SelectedMonitorDevice = settingsViewModel.SelectedMonitorDevice;
            SelectedSampleRate = settingsViewModel.SelectedSampleRate;
            SelectedBufferSize = settingsViewModel.SelectedBufferSize;

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

        foreach (var entry in _containerWindows)
        {
            if (entry.Value.DataContext is PluginContainerWindowViewModel viewModel)
            {
                viewModel.MeterScaleVox = value;
            }
        }
    }

    partial void OnMasterMeterLufsChanged(bool value)
    {
        if (_isInitializing) return;
        _config.Ui.MasterMeterLufs = value;
        _configManager.Save(_config);
    }

    partial void OnActiveChannelIndexChanged(int value)
    {
        if (value < 0 || value >= Channels.Count)
        {
            return;
        }

        ActiveChannelPresetName = GetChannelPresetName(value);
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

        _config.AudioSettings.OutputDeviceId = SelectedOutputDevice?.Id ?? string.Empty;
        _config.AudioSettings.MonitorOutputDeviceId = SelectedMonitorDevice?.Id ?? string.Empty;
        _config.AudioSettings.SampleRate = SelectedSampleRate;
        _config.AudioSettings.BufferSize = SelectedBufferSize;
        _config.AudioSettings.QualityMode = QualityMode;
        _configManager.Save(_config);

        StatusMessage = string.Empty;
        _audioEngine.ConfigureOutputDevices(_config.AudioSettings.OutputDeviceId, _config.AudioSettings.MonitorOutputDeviceId);
        _audioEngine.EnsureChannelCount(Math.Max(1, Channels.Count));
        if (_pluginGraphs.Length != _audioEngine.Channels.Count)
        {
            InitializePluginGraphs();
        }

        ApplyChannelInputsToEngine();
        _audioEngine.RebuildRoutingGraph();
        ApplyChannelStateToEngine();
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
        _audioEngine = new AudioEngine(_config.AudioSettings, Math.Max(1, _config.Channels.Count));

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

        _audioEngine.ConfigureOutputDevices(
            SelectedOutputDevice?.Id ?? string.Empty,
            SelectedMonitorDevice?.Id ?? string.Empty);
        _audioEngine.EnsureChannelCount(Math.Max(1, Channels.Count));
        ApplyChannelInputsToEngine();
        _audioEngine.RebuildRoutingGraph();
        ApplyChannelStateToEngine();

        RestorePluginsFromSnapshots(snapshots, profile);
        InitializePluginGraphs();
        SyncGraphsWithConfig();
        NormalizeOutputSendPlugins();

        try
        {
            _audioEngine.Start();
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Audio start failed: {ex.Message}";
        }

        for (int i = 0; i < _audioEngine.Channels.Count; i++)
        {
            RefreshPluginViewModels(i);
        }
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
        int count = Math.Min(_audioEngine.Channels.Count, Channels.Count);
        for (int i = 0; i < count; i++)
        {
            var channel = _audioEngine.Channels[i];
            var viewModel = Channels[i];
            channel.SetInputGainDb(viewModel.InputGainDb);
            channel.SetOutputGainDb(viewModel.OutputGainDb);
            channel.SetMuted(viewModel.IsMuted);
            channel.SetSoloed(viewModel.IsSoloed);
        }

        _audioEngine.SetMasterMute(MasterMuted);
    }

    private void ApplyChannelInputsToEngine()
    {
        bool changed = false;
        int count = Math.Min(_audioEngine.Channels.Count, Channels.Count);
        _suppressChannelConfig = true;

        for (int i = 0; i < count; i++)
        {
            var viewModel = Channels[i];
            var config = GetOrCreateChannelConfig(i);
            string resolved = _audioEngine.ConfigureChannelInput(i, viewModel.InputDeviceId, viewModel.InputChannelMode);
            if (!string.Equals(resolved, viewModel.InputDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                viewModel.InputDeviceId = resolved;
                viewModel.InputDeviceLabel = GetInputDeviceLabel(resolved);
                config.InputDeviceId = resolved;
                changed = true;
            }
            else
            {
                viewModel.InputDeviceLabel = GetInputDeviceLabel(resolved);
            }
        }

        _suppressChannelConfig = false;
        if (changed)
        {
            _configManager.Save(_config);
        }
    }

    private void InitializePluginGraphs()
    {
        int channelCount = _audioEngine.Channels.Count;
        if (channelCount <= 0)
        {
            _pluginGraphs = Array.Empty<PluginGraph>();
            return;
        }

        var graphs = new PluginGraph[channelCount];
        for (int i = 0; i < channelCount; i++)
        {
            graphs[i] = new PluginGraph(_audioEngine.Channels[i].PluginChain);
        }

        _pluginGraphs = graphs;
    }

    private PluginGraph? GetGraph(int channelIndex)
    {
        if ((uint)channelIndex >= (uint)_pluginGraphs.Length)
        {
            return null;
        }

        return _pluginGraphs[channelIndex];
    }

    private void SyncGraphsWithConfig()
    {
        bool changed = false;
        for (int i = 0; i < _pluginGraphs.Length; i++)
        {
            var graph = _pluginGraphs[i];
            var config = GetOrCreateChannelConfig(i);
            if (graph.SyncWithChain(config))
            {
                changed = true;
            }
        }

        if (changed)
        {
            _configManager.Save(_config);
        }
    }

    // Window size constants (must match MainRenderer layout calculations)
    // MainRenderer: ChannelHeaderWidth=90, PluginSlotWidth=130, MiniMeterWidth=6, PluginSlotSpacing=2
    private const double FullViewBaseWidth = 236;
    private const double PluginSlotWidthWithSpacing = 140; // Filled slot width (130) + meter (6) + spacing (2) + padding
    private const double MaxFullViewWidth = 1600;
    private const double MinFullViewWidth = 500;
    private const double FullViewTitleBarHeight = 36;
    private const double FullViewHotbarHeight = 24;
    private const double FullViewPadding = 10;
    private const double FullViewChannelHeight = 102;
    private const double FullViewChannelSpacing = 6;
    private const double FullViewAddChannelHeight = 26;
    private const double MinimalViewWidth = 400;
    private const double MinimalViewPadding = 10;
    private const double MinimalRowHeight = 40;
    private const double MinimalRowSpacing = 4;

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

        QualityMode = _config.AudioSettings.QualityMode;
        SelectedSampleRate = SampleRateOptions.Contains(_config.AudioSettings.SampleRate)
            ? _config.AudioSettings.SampleRate
            : SampleRateOptions[0];
        var profile = AudioQualityProfiles.ForMode(QualityMode, SelectedSampleRate);
        SelectedBufferSize = BufferSizeOptions.Contains(profile.BufferSize)
            ? profile.BufferSize
            : BufferSizeOptions[1];
        _config.AudioSettings.BufferSize = SelectedBufferSize;

        _suppressChannelConfig = true;
        for (int i = 0; i < Channels.Count; i++)
        {
            var channelConfig = GetOrCreateChannelConfig(i);
            var channelViewModel = Channels[i];
            channelViewModel.UpdateName(channelConfig.Name);
            channelViewModel.InputGainDb = channelConfig.InputGainDb;
            channelViewModel.OutputGainDb = channelConfig.OutputGainDb;
            channelViewModel.IsMuted = channelConfig.IsMuted;
            channelViewModel.IsSoloed = channelConfig.IsSoloed;
            channelViewModel.InputDeviceId = channelConfig.InputDeviceId;
            channelViewModel.InputChannelMode = channelConfig.InputChannel;
            channelViewModel.InputDeviceLabel = GetInputDeviceLabel(channelConfig.InputDeviceId);
        }
        _suppressChannelConfig = false;

        if (ActiveChannelIndex < 0 || ActiveChannelIndex >= Channels.Count)
        {
            ActiveChannelIndex = 0;
        }
        ActiveChannelPresetName = GetChannelPresetName(ActiveChannelIndex);
    }

    private void LoadPluginsFromConfig()
    {
        for (int i = 0; i < _config.Channels.Count; i++)
        {
            LoadChannelPlugins(i, GetOrCreateChannelConfig(i));
        }

        EnsureOutputSendPlugin();
        NormalizeOutputSendPlugins();
        _audioEngine.RebuildRoutingGraph();
    }

    private void LoadChannelPlugins(int channelIndex, ChannelConfig config)
    {
        if ((uint)channelIndex >= (uint)_audioEngine.Channels.Count)
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

        var strip = _audioEngine.Channels[channelIndex];
        var profile = GetQualityProfile();
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

            plugin.Initialize(_audioEngine.SampleRate, _audioEngine.BlockSize);
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

            return new PluginSlot(pluginConfig.InstanceId, plugin, _audioEngine.SampleRate);
        });

        var newSlots = strip.PluginChain.GetSnapshot();
        QueueRemovedPlugins(oldSlots, newSlots);
        bool inputOrderChanged = NormalizeInputPluginOrder(channelIndex, config);
        if (configChanged || bypassAdjusted || inputOrderChanged)
        {
            _configManager.Save(_config);
        }

        RefreshPluginViewModels(channelIndex);
    }

    private bool NormalizeInputPluginOrder(int channelIndex, ChannelConfig config)
    {
        var graph = GetGraph(channelIndex);
        if (graph is null)
        {
            return false;
        }

        var strip = _audioEngine.Channels[channelIndex];
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
                _audioEngine.QueuePluginDisposal(removedSlot.Plugin);
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

    private bool HasOutputSendPlugin()
    {
        for (int i = 0; i < _audioEngine.Channels.Count; i++)
        {
            var slots = _audioEngine.Channels[i].PluginChain.GetSnapshot();
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

    private bool ChannelHasInputPlugin(int channelIndex)
    {
        if ((uint)channelIndex >= (uint)_audioEngine.Channels.Count)
        {
            return false;
        }

        var slots = _audioEngine.Channels[channelIndex].PluginChain.GetSnapshot();
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i]?.Plugin is IChannelInputPlugin)
            {
                return true;
            }
        }

        return false;
    }

    private bool ChannelHasBusInputPlugin(int channelIndex)
    {
        if ((uint)channelIndex >= (uint)_audioEngine.Channels.Count)
        {
            return false;
        }

        var slots = _audioEngine.Channels[channelIndex].PluginChain.GetSnapshot();
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i]?.Plugin is IChannelInputPlugin input && input.InputKind == ChannelInputKind.Bus)
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureOutputSendPlugin()
    {
        if (HasOutputSendPlugin())
        {
            return;
        }

        if (_audioEngine.Channels.Count == 0)
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
        outputSend.Initialize(_audioEngine.SampleRate, _audioEngine.BlockSize);

        int insertIndex = _audioEngine.Channels[channelIndex].PluginChain.Count;
        int instanceId = graph.InsertPlugin(outputSend, insertIndex);
        if (instanceId <= 0)
        {
            return;
        }

        var config = GetOrCreateChannelConfig(channelIndex);
        MarkChannelPresetCustom(config);
        _configManager.Save(_config);
        RefreshPluginViewModels(channelIndex);
        UpdateDynamicWindowWidth();
    }

    private void NormalizeOutputSendPlugins()
    {
        bool foundActive = false;
        bool changed = false;
        PluginSlot? firstOutputSend = null;
        int firstChannelIndex = -1;
        PluginGraph? firstGraph = null;
        ChannelConfig? firstConfig = null;

        for (int i = 0; i < _audioEngine.Channels.Count; i++)
        {
            var graph = GetGraph(i);
            var config = GetOrCreateChannelConfig(i);
            var slots = _audioEngine.Channels[i].PluginChain.GetSnapshot();
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

                    _audioEngine.EnqueueParameterChange(new ParameterChange
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

            _audioEngine.EnqueueParameterChange(new ParameterChange
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
            _configManager.Save(_config);
        }
    }

    private int CreateCopyChannel(int sourceChannelIndex)
    {
        int channelIndex = _config.Channels.Count;
        var config = new ChannelConfig
        {
            Id = channelIndex + 1,
            Name = $"Copy {sourceChannelIndex + 1} -> {channelIndex + 1}",
            InputChannel = InputChannelMode.Sum,
            InputDeviceId = string.Empty,
            Plugins =
            [
                new PluginConfig
                {
                    Type = "builtin:bus-input"
                }
            ]
        };

        _config.Channels.Add(config);
        _audioEngine.EnsureChannelCount(_config.Channels.Count);
        InitializePluginGraphs();

        var viewModel = CreateChannelViewModel(channelIndex, config.Name);
        Channels.Add(viewModel);
        int capturedIndex = channelIndex;
        viewModel.PropertyChanged += (_, e) => UpdateChannelConfig(capturedIndex, viewModel, e.PropertyName);

        _suppressChannelConfig = true;
        viewModel.InputGainDb = config.InputGainDb;
        viewModel.OutputGainDb = config.OutputGainDb;
        viewModel.IsMuted = config.IsMuted;
        viewModel.IsSoloed = config.IsSoloed;
        viewModel.InputDeviceId = config.InputDeviceId;
        viewModel.InputChannelMode = config.InputChannel;
        viewModel.InputDeviceLabel = GetInputDeviceLabel(config.InputDeviceId);
        _suppressChannelConfig = false;

        LoadChannelPlugins(channelIndex, config);
        RefreshPluginViewModels(channelIndex);
        UpdateDynamicWindowWidth();
        _configManager.Save(_config);

        return config.Id;
    }

    private static ChannelConfig CreateDefaultChannelConfig(int id)
    {
        return new ChannelConfig
        {
            Id = id,
            Name = $"Channel {id}",
            InputChannel = InputChannelMode.Sum,
            Plugins =
            [
                new PluginConfig
                {
                    Type = "builtin:input"
                }
            ]
        };
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

        var profile = GetQualityProfile();
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

        var newSlots = pluginSlots.ToArray();
        strip.PluginChain.ReplaceAll(newSlots);
        QueueRemovedPlugins(oldSlots, newSlots);

        config.PresetName = chainPreset.Name;
        config.Plugins = pluginConfigs;
        config.Containers = containerConfigs;
        GetGraph(channelIndex)?.SyncWithChain(config);
        NormalizeInputPluginOrder(channelIndex, config);
        _configManager.Save(_config);

        EnsureOutputSendPlugin();
        NormalizeOutputSendPlugins();
        _audioEngine.RebuildRoutingGraph();
        RefreshPluginViewModels(channelIndex);
        UpdateDynamicWindowWidth();
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

        int index = _config.Channels.IndexOf(config);
        if (index == ActiveChannelIndex)
        {
            ActiveChannelPresetName = PluginPresetManager.CustomPresetName;
        }
    }

    private void MarkPluginPresetCustom(int channelIndex, int instanceId)
    {
        if (_suppressPresetUpdates)
        {
            return;
        }

        var config = GetOrCreateChannelConfig(channelIndex);
        var graph = GetGraph(channelIndex);
        if (graph is not null && graph.TryGetPluginConfig(instanceId, out var pluginConfig))
        {
            pluginConfig.PresetName = PluginPresetManager.CustomPresetName;
        }

        config.PresetName = PluginPresetManager.CustomPresetName;
        if (channelIndex == ActiveChannelIndex)
        {
            ActiveChannelPresetName = PluginPresetManager.CustomPresetName;
        }
    }

    private void UpdateMeters()
    {
        int channelCount = Math.Min(_audioEngine.Channels.Count, Channels.Count);
        if (channelCount == 0)
        {
            return;
        }

        long nowTicks = Stopwatch.GetTimestamp();

        for (int i = 0; i < channelCount; i++)
        {
            var channel = _audioEngine.Channels[i];
            var viewModel = Channels[i];
            viewModel.InputPeakLevel = channel.InputMeter.GetPeakLevel();
            viewModel.InputRmsLevel = channel.InputMeter.GetRmsLevel();
            viewModel.OutputPeakLevel = channel.OutputMeter.GetPeakLevel();
            viewModel.OutputRmsLevel = channel.OutputMeter.GetRmsLevel();

            UpdatePluginMeters(viewModel, channel);
            UpdateContainerMeters(viewModel, channel);
            UpdateContainerWindowMeters(i, channel);
        }

        MasterLufsMomentaryLeft = _audioEngine.MasterLufsLeft.GetMomentaryLufs();
        MasterLufsShortTermLeft = _audioEngine.MasterLufsLeft.GetShortTermLufs();
        MasterLufsMomentaryRight = _audioEngine.MasterLufsRight.GetMomentaryLufs();
        MasterLufsShortTermRight = _audioEngine.MasterLufsRight.GetShortTermLufs();

        UpdateMasterMeterLevels();

        Diagnostics = _audioEngine.GetDiagnosticsSnapshot();
        int sampleRate = Math.Max(1, _audioEngine.SampleRate);
        float baseLatencyMs = SelectedBufferSize * 1000f / sampleRate;
        int chainLatencySamples = GetOutputChainLatencySamples();
        float chainLatencyMs = chainLatencySamples * 1000f / sampleRate;
        LatencyMs = baseLatencyMs + chainLatencyMs;

        long inputDrops = 0;
        var inputs = Diagnostics.Inputs;
        for (int i = 0; i < inputs.Count; i++)
        {
            inputDrops += inputs[i].DroppedSamples;
        }

        TotalDrops = inputDrops + Diagnostics.OutputUnderflowSamples;

        _dropHistory.Enqueue((nowTicks, inputDrops, Diagnostics.OutputUnderflowSamples));

        long cutoffTicks = nowTicks - ThirtySecondsInTicks;
        while (_dropHistory.Count > 0 && _dropHistory.Peek().ticks < cutoffTicks)
        {
            _dropHistory.Dequeue();
        }

        if (_dropHistory.Count > 0)
        {
            var oldest = _dropHistory.Peek();
            InputDrops30Sec = inputDrops - oldest.inputDrops;
            OutputUnderflowDrops30Sec = Diagnostics.OutputUnderflowSamples - oldest.outputUnderflow;
            Drops30Sec = InputDrops30Sec + OutputUnderflowDrops30Sec;
        }

        UpdateDebugInfo(nowTicks);
        _audioEngine.DrainPendingPluginDisposals();
        _lastMeterUpdateTicks = nowTicks;
    }

    private void UpdateMasterMeterLevels()
    {
        float peak = 0f;
        float rms = 0f;
        OutputSendMode mode = OutputSendMode.Both;

        if (TryGetOutputSendSource(out int channelIndex, out mode))
        {
            var channel = _audioEngine.Channels[channelIndex];
            peak = channel.OutputMeter.GetPeakLevel();
            rms = channel.OutputMeter.GetRmsLevel();
        }

        if (MasterMuted)
        {
            peak = 0f;
            rms = 0f;
        }

        switch (mode)
        {
            case OutputSendMode.Left:
                MasterPeakLeft = peak;
                MasterPeakRight = 0f;
                MasterRmsLeft = rms;
                MasterRmsRight = 0f;
                break;
            case OutputSendMode.Right:
                MasterPeakLeft = 0f;
                MasterPeakRight = peak;
                MasterRmsLeft = 0f;
                MasterRmsRight = rms;
                break;
            case OutputSendMode.Both:
            default:
                MasterPeakLeft = peak;
                MasterPeakRight = peak;
                MasterRmsLeft = rms;
                MasterRmsRight = rms;
                break;
        }
    }

    private bool TryGetOutputSendSource(out int channelIndex, out OutputSendMode mode)
    {
        for (int i = 0; i < _audioEngine.Channels.Count; i++)
        {
            var slots = _audioEngine.Channels[i].PluginChain.GetSnapshot();
            for (int j = 0; j < slots.Length; j++)
            {
                var slot = slots[j];
                if (slot?.Plugin is IChannelOutputPlugin send && !slot.Plugin.IsBypassed)
                {
                    channelIndex = i;
                    mode = send.OutputMode;
                    return true;
                }
            }
        }

        channelIndex = -1;
        mode = OutputSendMode.Both;
        return false;
    }

    private int GetOutputChainLatencySamples()
    {
        if (TryGetOutputSendSource(out int channelIndex, out _))
        {
            return GetChainLatencySamples(_audioEngine.Channels[channelIndex]);
        }

        int max = 0;
        for (int i = 0; i < _audioEngine.Channels.Count; i++)
        {
            max = Math.Max(max, GetChainLatencySamples(_audioEngine.Channels[i]));
        }

        return max;
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
                slot.SetBypassSilent(chainSlot.Plugin.IsBypassed);
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
                    slotVm.SetBypassSilent(slot.Plugin.IsBypassed);

                    var delta = slot.Delta;
                    if (delta.DisplayMode != slotVm.DeltaDisplayMode)
                    {
                        delta.DisplayMode = slotVm.DeltaDisplayMode;
                    }

                    if (delta.TryUpdate())
                    {
                        slotVm.SpectralDelta = delta.BandDeltas;
                    }
                }
                else
                {
                    slotVm.OutputPeakLevel = 0f;
                    slotVm.OutputRmsLevel = 0f;
                    slotVm.SpectralDelta = null;
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
        string uiAge = _lastMeterUpdateTicks == 0
            ? "n/a"
            : $"{Math.Max(0.0, (nowTicks - _lastMeterUpdateTicks) * 1000.0 / Stopwatch.Frequency):0}";

        var inputs = diagnostics.Inputs;
        string inputStatus = inputs.Count == 0
            ? "none"
            : string.Join(" ", inputs.Select(i => $"ch{i.ChannelId + 1}:{FormatFlag(i.IsActive)}"));
        string inputAges = inputs.Count == 0
            ? "n/a"
            : string.Join(" ", inputs.Select(i => $"ch{i.ChannelId + 1}={FormatAgeMs(nowTicks, i.LastCallbackTicks)}"));
        string bufferSummary = inputs.Count == 0
            ? "n/a"
            : string.Join(" ", inputs.Select(i => $"ch{i.ChannelId + 1} {i.BufferedSamples}/{i.BufferCapacity}"));

        long inputDrops = 0;
        long inputUnderflows = 0;
        for (int i = 0; i < inputs.Count; i++)
        {
            inputDrops += inputs[i].DroppedSamples;
            inputUnderflows += inputs[i].UnderflowSamples;
        }

        DebugLines =
        [
            $"Audio: out={FormatFlag(diagnostics.OutputActive)} mon={FormatFlag(diagnostics.MonitorActive)} inputs={inputStatus} recov={(diagnostics.IsRecovering ? "yes" : "no")}",
            $"Callbacks(ms): out={outputAge} in={inputAges}",
            $"Buffers: {bufferSummary} mon {diagnostics.MonitorBufferedSamples}/{diagnostics.MonitorBufferCapacity}",
            $"Drops: in {inputDrops} under {inputUnderflows} out {diagnostics.OutputUnderflowSamples}",
            $"Formats: out {SelectedSampleRate}Hz/2ch",
            $"UI {uiAge}ms"
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

    private string GetInputDeviceLabel(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return "No Input";
        }

        var device = InputDevices.FirstOrDefault(d => d.Id == deviceId);
        return device?.Name ?? "Unknown";
    }

    private void ApplyChannelInput(int channelIndex, ChannelStripViewModel viewModel, ChannelConfig config)
    {
        string resolved = _audioEngine.ConfigureChannelInput(channelIndex, viewModel.InputDeviceId, viewModel.InputChannelMode);
        if (!string.Equals(resolved, viewModel.InputDeviceId, StringComparison.OrdinalIgnoreCase))
        {
            _suppressChannelConfig = true;
            viewModel.InputDeviceId = resolved;
            viewModel.InputDeviceLabel = GetInputDeviceLabel(resolved);
            _suppressChannelConfig = false;
            config.InputDeviceId = resolved;
        }
        else
        {
            viewModel.InputDeviceLabel = GetInputDeviceLabel(resolved);
        }
    }

    private void UpdateChannelConfig(int channelIndex, ChannelStripViewModel viewModel, string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName) || _suppressChannelConfig)
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
            case nameof(ChannelStripViewModel.InputDeviceId):
                config.InputDeviceId = viewModel.InputDeviceId;
                if (!_isInitializing)
                {
                    ApplyChannelInput(channelIndex, viewModel, config);
                    RefreshPluginViewModels(channelIndex);
                }
                else
                {
                    viewModel.InputDeviceLabel = GetInputDeviceLabel(viewModel.InputDeviceId);
                }
                break;
            case nameof(ChannelStripViewModel.InputChannelMode):
                config.InputChannel = viewModel.InputChannelMode;
                if (!_isInitializing)
                {
                    ApplyChannelInput(channelIndex, viewModel, config);
                }
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

        var graph = GetGraph(channelIndex);
        if (graph is null)
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

            if (newPlugin is IChannelOutputPlugin && HasOutputSendPlugin())
            {
                StatusMessage = "Only one Output Send plugin is allowed.";
                newPlugin.Dispose();
                return;
            }

            bool forceInputFirst = false;
            if (newPlugin is IChannelInputPlugin inputPlugin)
            {
                if (inputPlugin.InputKind == ChannelInputKind.Device && ChannelHasBusInputPlugin(channelIndex))
                {
                    StatusMessage = "Bus input channels cannot add an Input Source plugin.";
                    newPlugin.Dispose();
                    return;
                }

                if (ChannelHasInputPlugin(channelIndex))
                {
                    StatusMessage = "Only one Input Source plugin is allowed per channel.";
                    newPlugin.Dispose();
                    return;
                }

                forceInputFirst = true;
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
                int targetChannelId = CreateCopyChannel(channelIndex);
                copy.TargetChannelId = targetChannelId;
                UpdatePluginStateConfig(channelIndex, instanceId);
            }

            var config = GetOrCreateChannelConfig(channelIndex);
            MarkChannelPresetCustom(config);
            _configManager.Save(_config);
            RefreshPluginViewModels(channelIndex);
            NormalizeOutputSendPlugins();
            _audioEngine.RebuildRoutingGraph();
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

        var graph = GetGraph(channelIndex);
        if (graph is null)
        {
            return;
        }

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

        if (newPlugin is IChannelOutputPlugin && HasOutputSendPlugin())
        {
            StatusMessage = "Only one Output Send plugin is allowed.";
            newPlugin.Dispose();
            return;
        }

        if (newPlugin is IChannelInputPlugin)
        {
            StatusMessage = "Input Source plugins must be added to the main chain.";
            newPlugin.Dispose();
            return;
        }

        newPlugin.Initialize(_audioEngine.SampleRate, _audioEngine.BlockSize);

        int instanceId = graph.InsertPluginIntoContainer(newPlugin, containerId, insertIndex);
        if (instanceId <= 0)
        {
            return;
        }

        if (newPlugin is CopyToChannelPlugin copy)
        {
            int targetChannelId = CreateCopyChannel(channelIndex);
            copy.TargetChannelId = targetChannelId;
            UpdatePluginStateConfig(channelIndex, instanceId);
        }

        var config = GetOrCreateChannelConfig(channelIndex);
        MarkChannelPresetCustom(config);
        _configManager.Save(_config);
        RefreshPluginViewModels(channelIndex);
        EnsureOutputSendPlugin();
        NormalizeOutputSendPlugins();
        _audioEngine.RebuildRoutingGraph();
        UpdateDynamicWindowWidth();
    }

    private void RemovePlugin(int channelIndex, int pluginInstanceId)
    {
        if ((uint)channelIndex >= (uint)_audioEngine.Channels.Count)
        {
            return;
        }

        var strip = _audioEngine.Channels[channelIndex];
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
            _audioEngine.QueuePluginDisposal(removedSlot.Plugin);
        }

        var config = GetOrCreateChannelConfig(channelIndex);
        MarkChannelPresetCustom(config);
        _configManager.Save(_config);
        RefreshPluginViewModels(channelIndex);
        EnsureOutputSendPlugin();
        NormalizeOutputSendPlugins();
        _audioEngine.RebuildRoutingGraph();
        UpdateDynamicWindowWidth();
    }

    private void ReorderPlugins(int channelIndex, int pluginInstanceId, int toIndex)
    {
        if ((uint)channelIndex >= (uint)_audioEngine.Channels.Count)
        {
            return;
        }

        var strip = _audioEngine.Channels[channelIndex];
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

        var config = GetOrCreateChannelConfig(channelIndex);
        MarkChannelPresetCustom(config);
        _configManager.Save(_config);
        RefreshPluginViewModels(channelIndex);
        _audioEngine.RebuildRoutingGraph();
    }

    private void ReorderContainerPlugin(int channelIndex, int containerId, int pluginInstanceId, int toIndex)
    {
        if ((uint)channelIndex >= (uint)_audioEngine.Channels.Count)
        {
            return;
        }

        var strip = _audioEngine.Channels[channelIndex];
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

        var config = GetOrCreateChannelConfig(channelIndex);
        MarkChannelPresetCustom(config);
        _configManager.Save(_config);

        RefreshPluginViewModels(channelIndex);
        _audioEngine.RebuildRoutingGraph();
    }

    private void CreateContainer(int channelIndex, bool openWindow)
    {
        var graph = GetGraph(channelIndex);
        if (graph is null)
        {
            return;
        }

        var config = GetOrCreateChannelConfig(channelIndex);
        int containerId = graph.CreateContainer(string.Empty);
        var container = graph.GetContainers().FirstOrDefault(c => c.Id == containerId);
        if (container is not null && string.IsNullOrWhiteSpace(container.Name))
        {
            container.Name = $"Container {containerId}";
        }
        MarkChannelPresetCustom(config);
        _configManager.Save(_config);
        RefreshPluginViewModels(channelIndex);
        UpdateDynamicWindowWidth();

        if (openWindow)
        {
            OpenContainerWindow(channelIndex, containerId);
        }
    }

    private void RemoveContainer(int channelIndex, int containerId)
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

        var config = GetOrCreateChannelConfig(channelIndex);
        MarkChannelPresetCustom(config);
        _configManager.Save(_config);
        CloseContainerWindow(channelIndex, containerId);
        RefreshPluginViewModels(channelIndex);
        UpdateDynamicWindowWidth();
    }

    private void SetContainerBypass(int channelIndex, int containerId, bool bypassed)
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

        var config = GetOrCreateChannelConfig(channelIndex);
        MarkChannelPresetCustom(config);
        _configManager.Save(_config);
    }

    private void ReorderContainer(int channelIndex, int containerId, int targetChainIndex)
    {
        if ((uint)channelIndex >= (uint)_audioEngine.Channels.Count || containerId <= 0)
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

        var config = GetOrCreateChannelConfig(channelIndex);
        MarkChannelPresetCustom(config);
        _configManager.Save(_config);

        RefreshPluginViewModels(channelIndex);
        UpdateDynamicWindowWidth();
    }

    private void OpenContainerWindow(int channelIndex, int containerId)
    {
        if ((uint)channelIndex >= (uint)_audioEngine.Channels.Count)
        {
            return;
        }

        var graph = GetGraph(channelIndex);
        if (graph is null)
        {
            return;
        }

        var key = (channelIndex, containerId);
        if (_containerWindows.TryGetValue(key, out var existing))
        {
            existing.Activate();
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
            MeterScaleVox);

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
            int copyTargetChannelId = 0;
            string displayName = slot?.Plugin.Name ?? string.Empty;

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
        viewModel.MeterScaleVox = MeterScaleVox;
        viewModel.UpdatePlugins(slotInfos, container.PluginInstanceIds);
    }

    private void UpdateOpenContainerWindows(int channelIndex, IReadOnlyList<PluginSlotInfo> slotInfos)
    {
        if (_containerWindows.Count == 0)
        {
            return;
        }

        var graph = GetGraph(channelIndex);
        if (graph is null)
        {
            return;
        }

        var containers = graph.GetContainers();
        var keys = _containerWindows.Keys.ToArray();
        for (int i = 0; i < keys.Length; i++)
        {
            var key = keys[i];
            if (key.ChannelIndex != channelIndex)
            {
                continue;
            }

            var container = containers.FirstOrDefault(c => c.Id == key.ContainerId);
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
            new() { Id = "builtin:input", Name = "Input Source", IsVst3 = false, Category = PluginCategory.Routing, Description = "Read a microphone input into this channel" },
            new() { Id = "builtin:copy", Name = "Copy to Channel", IsVst3 = false, Category = PluginCategory.Routing, Description = "Duplicate audio + sidechain into a new channel" },
            new() { Id = "builtin:merge", Name = "Merge", IsVst3 = false, Category = PluginCategory.Routing, Description = "Merge 2-N channels into the current chain with alignment" },
            new() { Id = "builtin:output-send", Name = "Output Send", IsVst3 = false, Category = PluginCategory.Routing, Description = "Send this chain to the main output (left/right/both)" },
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
        // Routing plugins with no UI - just inline controls
        if (plugin is BusInputPlugin or CopyToChannelPlugin or MergePlugin)
        {
            return;
        }

        // Use specialized window for Input Source
        if (plugin is InputPlugin inputPlugin)
        {
            ShowInputSourceWindow(channelIndex, pluginInstanceId, inputPlugin);
            return;
        }

        // Use specialized window for Output Send
        if (plugin is OutputSendPlugin outputSend)
        {
            ShowOutputSendWindow(channelIndex, pluginInstanceId, outputSend);
            return;
        }

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
            return new PluginParameterViewModel(
                parameter.Index,
                parameter.Name,
                parameter.MinValue,
                parameter.MaxValue,
                currentValue,
                parameter.Unit,
                value => ApplyPluginParameter(channelIndex, pluginInstanceId, parameter.Index, parameter.Name, value),
                parameter.FormatValue);
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

    private void ShowInputSourceWindow(int channelIndex, int pluginInstanceId, InputPlugin plugin)
    {
        if ((uint)channelIndex >= (uint)Channels.Count)
            return;

        var channel = Channels[channelIndex];
        var devices = InputDevices
            .Select(d => new UI.PluginComponents.InputSourceDevice(d.Id, d.Name))
            .ToList();

        var window = new InputSourceWindow(
            devices,
            () => channel.InputDeviceId,
            () => channel.InputChannelMode,
            () => channel.InputGainDb,
            () => channel.InputPeakLevel,
            () => plugin.IsBypassed,
            deviceId => SetChannelInputDevice(channelIndex, deviceId),
            mode =>
            {
                channel.InputChannelMode = mode;
                _audioEngine.ConfigureChannelInput(channelIndex, channel.InputDeviceId, mode);
                var config = GetOrCreateChannelConfig(channelIndex);
                config.InputChannel = mode;
                _configManager.Save(_config);
            },
            gainDb =>
            {
                channel.InputGainDb = gainDb;
            },
            bypassed => SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowOutputSendWindow(int channelIndex, int pluginInstanceId, OutputSendPlugin plugin)
    {
        if ((uint)channelIndex >= (uint)Channels.Count)
            return;

        var channel = Channels[channelIndex];
        string outputDeviceName = SelectedOutputDevice?.Name ?? "No Output";

        var window = new OutputSendWindow(
            plugin,
            outputDeviceName,
            () => channel.OutputPeakLevel,
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
        if ((uint)channelIndex >= (uint)Channels.Count)
        {
            return;
        }

        var channel = Channels[channelIndex];
        int slotIndex = FindPluginSlotIndex(channel, pluginInstanceId);
        if (slotIndex >= 0 && slotIndex < channel.PluginSlots.Count)
        {
            channel.PluginSlots[slotIndex].SetBypassSilent(bypassed);
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

        var strip = _audioEngine.Channels.ElementAtOrDefault(channelIndex);
        if (strip is not null && strip.PluginChain.TryGetSlotById(pluginInstanceId, out var slot, out _) &&
            slot?.Plugin is IChannelOutputPlugin)
        {
            RefreshPluginViewModels(channelIndex);
        }
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
            int copyTargetChannelId = 0;
            string displayName = slot?.Plugin.Name ?? string.Empty;

            if (slot is not null)
            {
                var plugin = slot.Plugin;
                pluginId = plugin.Id;
                if (_audioEngine.SampleRate > 0)
                {
                    latencyMs = plugin.LatencySamples * 1000f / _audioEngine.SampleRate;
                }

                if (plugin is InputPlugin)
                {
                    string deviceLabel = GetInputDeviceLabel(GetOrCreateChannelConfig(channelIndex).InputDeviceId);
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
        if ((uint)channelIndex < (uint)Channels.Count)
        {
            var viewModel = Channels[channelIndex];
            viewModel.UpdatePlugins(slotInfos);
            viewModel.UpdateContainers(BuildContainerInfos(channelIndex));
        }

        UpdateOpenContainerWindows(channelIndex, slotInfos);
    }

    private IReadOnlyList<PluginContainerInfo> BuildContainerInfos(int channelIndex)
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
            _config.Channels.Add(CreateDefaultChannelConfig(_config.Channels.Count + 1));
        }

        return _config.Channels[channelIndex];
    }

    private void UpdatePluginParameterConfig(int channelIndex, int instanceId, string parameterName, float value, bool markPresetDirty)
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
        _configManager.Save(_config);
    }

    private void UpdatePluginStateConfig(int channelIndex, int instanceId)
    {
        var graph = GetGraph(channelIndex);
        if (graph is null)
        {
            return;
        }

        graph.SetPluginState(instanceId);
        _configManager.Save(_config);

        var strip = _audioEngine.Channels.ElementAtOrDefault(channelIndex);
        if (strip is not null &&
            strip.PluginChain.TryGetSlotById(instanceId, out var slot, out _) &&
            slot?.Plugin is IRoutingDependencyProvider)
        {
            _audioEngine.RebuildRoutingGraph();
        }
    }

    private void UpdatePluginBypassConfig(int channelIndex, int instanceId, bool bypass)
    {
        var graph = GetGraph(channelIndex);
        if (graph is null)
        {
            return;
        }

        graph.SetPluginBypass(instanceId, bypass);
        MarkPluginPresetCustom(channelIndex, instanceId);
        _configManager.Save(_config);

        var strip = _audioEngine.Channels.ElementAtOrDefault(channelIndex);
        if (strip is not null &&
            strip.PluginChain.TryGetSlotById(instanceId, out var slot, out _) &&
            slot?.Plugin is IChannelOutputPlugin)
        {
            NormalizeOutputSendPlugins();
        }
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

    public void AddChannel()
    {
        int channelIndex = _config.Channels.Count;
        var config = CreateDefaultChannelConfig(channelIndex + 1);
        _config.Channels.Add(config);

        _audioEngine.EnsureChannelCount(_config.Channels.Count);
        InitializePluginGraphs();

        var viewModel = CreateChannelViewModel(channelIndex, config.Name);
        Channels.Add(viewModel);
        int capturedIndex = channelIndex;
        viewModel.PropertyChanged += (_, e) => UpdateChannelConfig(capturedIndex, viewModel, e.PropertyName);

        _suppressChannelConfig = true;
        viewModel.InputGainDb = config.InputGainDb;
        viewModel.OutputGainDb = config.OutputGainDb;
        viewModel.IsMuted = config.IsMuted;
        viewModel.IsSoloed = config.IsSoloed;
        viewModel.InputDeviceId = config.InputDeviceId;
        viewModel.InputChannelMode = config.InputChannel;
        viewModel.InputDeviceLabel = GetInputDeviceLabel(config.InputDeviceId);
        _suppressChannelConfig = false;

        LoadChannelPlugins(channelIndex, config);
        ApplyChannelInput(channelIndex, viewModel, config);
        RefreshPluginViewModels(channelIndex);
        UpdateDynamicWindowWidth();
        _configManager.Save(_config);

        ActiveChannelIndex = channelIndex;
    }

    public void RemoveChannel(int channelIndex)
    {
        if (_config.Channels.Count <= 1)
        {
            return;
        }

        if ((uint)channelIndex >= (uint)_config.Channels.Count)
        {
            return;
        }

        SyncGraphsWithConfig();

        int removedChannelId = channelIndex + 1;
        _config.Channels.RemoveAt(channelIndex);
        ResequenceChannelIds();
        NormalizeChannelReferencesAfterRemoval(removedChannelId, _config.Channels.Count);
        _configManager.Save(_config);

        int nextActive = Math.Clamp(channelIndex, 0, _config.Channels.Count - 1);
        RebuildForChannelTopologyChange(nextActive);
    }

    public void SetChannelInputDevice(int channelIndex, string deviceId)
    {
        if ((uint)channelIndex >= (uint)Channels.Count)
        {
            return;
        }

        var viewModel = Channels[channelIndex];
        var config = GetOrCreateChannelConfig(channelIndex);

        string resolved = _audioEngine.ConfigureChannelInput(channelIndex, deviceId, viewModel.InputChannelMode);
        viewModel.InputDeviceId = resolved;
        viewModel.InputDeviceLabel = GetInputDeviceLabel(resolved);
        config.InputDeviceId = resolved;

        _configManager.Save(_config);
    }

    private void RebuildForChannelTopologyChange(int desiredActiveIndex)
    {
        _isInitializing = true;
        CloseAllContainerWindows();

        _audioEngine.Stop();
        _audioEngine.Dispose();

        _audioEngine = new AudioEngine(_config.AudioSettings, Math.Max(1, _config.Channels.Count));
        _analysisOrchestrator.Initialize(_config.AudioSettings.SampleRate);
        _analysisOrchestrator.Reset();
        _audioEngine.AnalysisTap.Orchestrator = _analysisOrchestrator;
        _analysisOrchestrator.DebugTap = _audioEngine.AnalysisTap;

        BuildChannelViewModels();
        ApplyConfigToViewModels();
        InitializePluginGraphs();
        LoadPluginsFromConfig();
        UpdateDynamicWindowWidth();

        if ((uint)desiredActiveIndex < (uint)Channels.Count)
        {
            ActiveChannelIndex = desiredActiveIndex;
        }

        _isInitializing = false;
        ApplyDeviceSelection();
    }

    private void CloseAllContainerWindows()
    {
        if (_containerWindows.Count == 0)
        {
            return;
        }

        foreach (var window in _containerWindows.Values.ToArray())
        {
            window.Close();
        }

        _containerWindows.Clear();
    }

    private void ResequenceChannelIds()
    {
        for (int i = 0; i < _config.Channels.Count; i++)
        {
            _config.Channels[i].Id = i + 1;
        }
    }

    private void NormalizeChannelReferencesAfterRemoval(int removedChannelId, int newChannelCount)
    {
        if (newChannelCount < 1)
        {
            return;
        }

        for (int i = 0; i < _config.Channels.Count; i++)
        {
            var channel = _config.Channels[i];
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
        var graph = GetGraph(channelIndex);
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
            for (int i = 0; i < _config.Channels.Count; i++)
            {
                var channelName = GetChannelPresetName(i);
                if (string.Equals(channelName, presetName, StringComparison.OrdinalIgnoreCase))
                {
                    var config = GetOrCreateChannelConfig(i);
                    config.PresetName = PluginPresetManager.CustomPresetName;
                    if (i == ActiveChannelIndex)
                    {
                        ActiveChannelPresetName = PluginPresetManager.CustomPresetName;
                    }
                }
            }

            _configManager.Save(_config);
            return true;
        }

        return false;
    }

    private void UpdatePresetNameProperty(int channelIndex)
    {
        var presetName = GetChannelPresetName(channelIndex);
        if (channelIndex == ActiveChannelIndex)
        {
            ActiveChannelPresetName = presetName;
        }
    }

    private float GetPluginParameterValue(int channelIndex, int instanceId, string parameterName, float fallback)
    {
        var graph = GetGraph(channelIndex);
        if (graph is null || !graph.TryGetPluginConfig(instanceId, out var pluginConfig))
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
