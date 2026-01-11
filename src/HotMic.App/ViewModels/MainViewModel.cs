using System.Collections.ObjectModel;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotMic.Common.Configuration;
using HotMic.Common.Models;
using HotMic.Core.Engine;
using HotMic.Core.Midi;
using HotMic.Core.Plugins;
using HotMic.Core.Plugins.BuiltIn;
using HotMic.App.Models;
using HotMic.App.Views;
using HotMic.Vst3;

namespace HotMic.App.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ConfigManager _configManager = new();
    private readonly DeviceManager _deviceManager = new();
    private AudioEngine _audioEngine;
    private MidiManager? _midiManager;
    private readonly DispatcherTimer _meterTimer;
    private AppConfig _config;
    private long _lastMeterUpdateTicks;
    private long _nextDebugUpdateTicks;
    private static readonly long DebugUpdateIntervalTicks = Math.Max(1, Stopwatch.Frequency / 4);
    private bool _isInitializing = true; // Skip side effects during initial config load

    // 30-second rolling window for drop tracking
    private readonly Queue<(long ticks, long input1, long input2, long underflow1, long underflow2)> _dropHistory = new();
    private static readonly long ThirtySecondsInTicks = Stopwatch.Frequency * 30;

    public MainViewModel()
    {
        _config = _configManager.LoadOrDefault();
        _audioEngine = new AudioEngine(_config.AudioSettings);

        Channel1 = new ChannelStripViewModel(0, "Channel 1", EnqueueParameterChange, slot => HandlePluginAction(0, slot), slot => RemovePlugin(0, slot), (from, to) => ReorderPlugins(0, from, to), (index, value) => UpdatePluginBypassConfig(0, index, value));
        Channel2 = new ChannelStripViewModel(1, "Channel 2", EnqueueParameterChange, slot => HandlePluginAction(1, slot), slot => RemovePlugin(1, slot), (from, to) => ReorderPlugins(1, from, to), (index, value) => UpdatePluginBypassConfig(1, index, value));
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
                if (int.TryParse(parts[2], out int pluginIndex) &&
                    int.TryParse(parts[3], out int paramIndex))
                {
                    ApplyPluginParameter(channelIndex, pluginIndex, paramIndex, "midi", value);
                }
                break;
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

        // Calculate width based on the longest plugin chain (including +1 add placeholder)
        int channel1PluginCount = 0;
        int channel2PluginCount = 0;

        if (_audioEngine.Channels.Count > 0)
        {
            channel1PluginCount = _audioEngine.Channels[0].PluginChain.Count;
        }
        if (_audioEngine.Channels.Count > 1)
        {
            channel2PluginCount = _audioEngine.Channels[1].PluginChain.Count;
        }

        // +1 for the add placeholder slot
        int maxPlugins = Math.Max(channel1PluginCount, channel2PluginCount) + 1;

        double pluginAreaWidth = maxPlugins * PluginSlotWidthWithSpacing;
        double calculatedWidth = FullViewBaseWidth + pluginAreaWidth;

        WindowWidth = Math.Clamp(calculatedWidth, MinFullViewWidth, MaxFullViewWidth);
        WindowHeight = FullViewHeight;
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
        var snapshots = CapturePluginSnapshots();
        _audioEngine.Stop();
        _audioEngine.Dispose();
        _audioEngine = new AudioEngine(_config.AudioSettings);

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

    private IPlugin?[][] CapturePluginSnapshots()
    {
        var snapshots = new IPlugin?[_audioEngine.Channels.Count][];
        for (int i = 0; i < _audioEngine.Channels.Count; i++)
        {
            var slots = _audioEngine.Channels[i].PluginChain.GetSnapshot();
            snapshots[i] = slots.ToArray();
        }

        return snapshots;
    }

    private void RestorePluginsFromSnapshots(IPlugin?[][] snapshots, AudioQualityProfile profile)
    {
        for (int channelIndex = 0; channelIndex < _audioEngine.Channels.Count; channelIndex++)
        {
            var strip = _audioEngine.Channels[channelIndex];
            var snapshot = channelIndex < snapshots.Length ? snapshots[channelIndex] : [];
            var pluginList = new List<IPlugin>();

            foreach (var plugin in snapshot)
            {
                if (plugin is null)
                {
                    continue;
                }

                ReinitializePluginForQuality(plugin, profile);
                pluginList.Add(plugin);
            }

            strip.PluginChain.ReplaceAll(pluginList.ToArray());
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
    // Layout calculation: pluginArea = windowWidth - 294 (from MainRenderer layout)
    // Each slot needs 54px, so: windowWidth = 300 + N * 54 gives comfortable fit
    private const double FullViewBaseWidth = 300;
    private const double PluginSlotWidthWithSpacing = 54; // Narrow slot width + spacing (52 + 2)
    private const double MaxFullViewWidth = 1200;
    private const double MinFullViewWidth = 400;
    private const double FullViewHeight = 290;
    private const double MinimalViewWidth = 400;
    private const double MinimalViewHeight = 140;

    private void ApplyConfigToViewModels()
    {
        IsMinimalView = string.Equals(_config.Ui.ViewMode, "minimal", StringComparison.OrdinalIgnoreCase);
        AlwaysOnTop = _config.Ui.AlwaysOnTop;
        MeterScaleVox = _config.Ui.MeterScaleVox;
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

        var strip = _audioEngine.Channels[channelIndex];
        var pluginList = new List<IPlugin>();
        var profile = GetQualityProfile();

        if (config is not null)
        {
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
                plugin.IsBypassed = pluginConfig.IsBypassed;

                foreach (var parameter in plugin.Parameters)
                {
                    if (pluginConfig.Parameters.TryGetValue(parameter.Name, out var value))
                    {
                        plugin.SetParameter(parameter.Index, value);
                    }
                }

                if (pluginConfig.State is not null && pluginConfig.State.Length > 0)
                {
                    plugin.SetState(pluginConfig.State);
                }

                pluginList.Add(plugin);
            }
        }

        strip.PluginChain.ReplaceAll(pluginList.ToArray());
        RefreshPluginViewModels(channelIndex);
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
        _lastMeterUpdateTicks = nowTicks;
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

    private void HandlePluginAction(int channelIndex, int slotIndex)
    {
        if ((uint)channelIndex >= (uint)_audioEngine.Channels.Count)
        {
            return;
        }

        var strip = _audioEngine.Channels[channelIndex];
        var slots = strip.PluginChain.GetSnapshot();

        // If clicking on the "+1" add placeholder (slotIndex == slots.Length)
        bool isAddPlaceholder = slotIndex == slots.Length;
        var plugin = isAddPlaceholder ? null : (slotIndex < slots.Length ? slots[slotIndex] : null);

        if (plugin is null)
        {
            var choice = ShowPluginBrowser();
            if (choice is null)
            {
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

            if (isAddPlaceholder)
            {
                // Add new slot at the end
                strip.PluginChain.AddSlot(newPlugin);
                UpdatePluginConfig(channelIndex, slots.Length, newPlugin);
            }
            else
            {
                // Replace existing slot
                strip.PluginChain.SetSlot(slotIndex, newPlugin);
                UpdatePluginConfig(channelIndex, slotIndex, newPlugin);
            }
            RefreshPluginViewModels(channelIndex);
            UpdateDynamicWindowWidth();
        }
        else
        {
            if (plugin is Vst3PluginWrapper vst3)
            {
                ShowVst3Editor(vst3);
                return;
            }

            ShowPluginParameters(channelIndex, slotIndex, plugin);
        }
    }

    private void RemovePlugin(int channelIndex, int slotIndex)
    {
        if ((uint)channelIndex >= (uint)_audioEngine.Channels.Count)
        {
            return;
        }

        var strip = _audioEngine.Channels[channelIndex];
        var slots = strip.PluginChain.GetSnapshot();
        if ((uint)slotIndex >= (uint)slots.Length)
        {
            return;
        }

        if (slots[slotIndex] is null)
        {
            return;
        }

        strip.PluginChain.RemoveSlot(slotIndex);
        RemovePluginConfig(channelIndex, slotIndex);
        RefreshPluginViewModels(channelIndex);
        UpdateDynamicWindowWidth();
    }

    private void ReorderPlugins(int channelIndex, int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex || (uint)channelIndex >= (uint)_audioEngine.Channels.Count)
        {
            return;
        }

        var strip = _audioEngine.Channels[channelIndex];
        var slots = strip.PluginChain.GetSnapshot();
        if ((uint)fromIndex >= (uint)slots.Length || (uint)toIndex >= (uint)slots.Length)
        {
            return;
        }

        var newSlots = slots.ToArray();
        var item = newSlots[fromIndex];
        newSlots[fromIndex] = newSlots[toIndex];
        newSlots[toIndex] = item;
        strip.PluginChain.ReplaceAll(newSlots);
        SwapPluginConfig(channelIndex, fromIndex, toIndex);
        RefreshPluginViewModels(channelIndex);
    }

    private PluginChoice? ShowPluginBrowser()
    {
        var choices = new List<PluginChoice>
        {
            // Dynamics
            new() { Id = "builtin:gain", Name = "Gain", IsVst3 = false, Category = PluginCategory.Dynamics, Description = "Simple gain and phase control" },
            new() { Id = "builtin:compressor", Name = "Compressor", IsVst3 = false, Category = PluginCategory.Dynamics, Description = "Dynamic range compression with soft knee" },
            new() { Id = "builtin:noisegate", Name = "Noise Gate", IsVst3 = false, Category = PluginCategory.Dynamics, Description = "Removes audio below threshold" },

            // EQ
            new() { Id = "builtin:eq3", Name = "3-Band EQ", IsVst3 = false, Category = PluginCategory.Eq, Description = "Parametric equalizer with spectrum analyzer" },

            // Noise Reduction
            new() { Id = "builtin:fft-noise", Name = "FFT Noise Removal", IsVst3 = false, Category = PluginCategory.NoiseReduction, Description = "Learns and removes background noise" },

            // AI/ML
            new() { Id = "builtin:rnnoise", Name = "RNNoise", IsVst3 = false, Category = PluginCategory.AiMl, Description = "Neural network noise suppression" },
            new() { Id = "builtin:deepfilternet", Name = "DeepFilterNet", IsVst3 = false, Category = PluginCategory.AiMl, Description = "Deep learning noise reduction" },
            new() { Id = "builtin:voice-gate", Name = "Voice Gate", IsVst3 = false, Category = PluginCategory.AiMl, Description = "AI-powered voice activity detection" },

            // Effects
            new() { Id = "builtin:reverb", Name = "Reverb", IsVst3 = false, Category = PluginCategory.Effects, Description = "Convolution reverb with IR presets" }
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

    private void ShowPluginParameters(int channelIndex, int slotIndex, IPlugin plugin)
    {
        // Use specialized window for noise gate
        if (plugin is NoiseGatePlugin noiseGate)
        {
            ShowNoiseGateWindow(channelIndex, slotIndex, noiseGate);
            return;
        }

        // Use specialized window for compressor
        if (plugin is CompressorPlugin compressor)
        {
            ShowCompressorWindow(channelIndex, slotIndex, compressor);
            return;
        }

        // Use specialized window for gain
        if (plugin is GainPlugin gain)
        {
            ShowGainWindow(channelIndex, slotIndex, gain);
            return;
        }

        // Use specialized window for 3-band EQ
        if (plugin is ThreeBandEqPlugin eq)
        {
            ShowEqWindow(channelIndex, slotIndex, eq);
            return;
        }

        // Use specialized window for FFT noise removal
        if (plugin is FFTNoiseRemovalPlugin fftNoise)
        {
            ShowFFTNoiseWindow(channelIndex, slotIndex, fftNoise);
            return;
        }

        // Use specialized window for Voice Gate (Silero VAD)
        if (plugin is SileroVoiceGatePlugin silero)
        {
            ShowVoiceGateWindow(channelIndex, slotIndex, silero);
            return;
        }

        // Use specialized window for RNNoise
        if (plugin is RNNoisePlugin rnnoise)
        {
            ShowRNNoiseWindow(channelIndex, slotIndex, rnnoise);
            return;
        }

        // Use specialized window for DeepFilterNet
        if (plugin is DeepFilterNetPlugin deepFilter)
        {
            ShowDeepFilterNetWindow(channelIndex, slotIndex, deepFilter);
            return;
        }

        // Use specialized window for Reverb
        if (plugin is ConvolutionReverbPlugin reverb)
        {
            ShowReverbWindow(channelIndex, slotIndex, reverb);
            return;
        }

        var parameterViewModels = plugin.Parameters.Select(parameter =>
        {
            float currentValue = GetPluginParameterValue(channelIndex, slotIndex, parameter.Name, parameter.DefaultValue);
            return new PluginParameterViewModel(parameter.Index, parameter.Name, parameter.MinValue, parameter.MaxValue, currentValue, parameter.Unit,
                value => ApplyPluginParameter(channelIndex, slotIndex, parameter.Index, parameter.Name, value));
        }).ToList();

        Action? learnNoiseAction = plugin is FFTNoiseRemovalPlugin ? () => RequestNoiseLearn(channelIndex, slotIndex) : null;
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
        window.ShowDialog();
    }

    private void ShowNoiseGateWindow(int channelIndex, int slotIndex, NoiseGatePlugin plugin)
    {
        var window = new NoiseGateWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, slotIndex, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, slotIndex, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.ShowDialog();
    }

    private void ShowCompressorWindow(int channelIndex, int slotIndex, CompressorPlugin plugin)
    {
        var window = new CompressorWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, slotIndex, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, slotIndex, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.ShowDialog();
    }

    private void ShowGainWindow(int channelIndex, int slotIndex, GainPlugin plugin)
    {
        var window = new GainWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, slotIndex, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, slotIndex, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.ShowDialog();
    }

    private void ShowEqWindow(int channelIndex, int slotIndex, ThreeBandEqPlugin plugin)
    {
        var window = new EqWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, slotIndex, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, slotIndex, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.ShowDialog();
    }

    private void ShowFFTNoiseWindow(int channelIndex, int slotIndex, FFTNoiseRemovalPlugin plugin)
    {
        var window = new FFTNoiseWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, slotIndex, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, slotIndex, bypassed),
            () => RequestNoiseLearn(channelIndex, slotIndex))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.ShowDialog();
    }

    private void ShowVoiceGateWindow(int channelIndex, int slotIndex, SileroVoiceGatePlugin plugin)
    {
        var window = new VoiceGateWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, slotIndex, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, slotIndex, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.ShowDialog();
    }

    private void ShowRNNoiseWindow(int channelIndex, int slotIndex, RNNoisePlugin plugin)
    {
        var window = new RNNoiseWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, slotIndex, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, slotIndex, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.ShowDialog();
    }

    private void ShowDeepFilterNetWindow(int channelIndex, int slotIndex, DeepFilterNetPlugin plugin)
    {
        var window = new DeepFilterNetWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, slotIndex, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, slotIndex, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.ShowDialog();
    }

    private void ShowReverbWindow(int channelIndex, int slotIndex, ConvolutionReverbPlugin plugin)
    {
        var window = new ReverbWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                ApplyPluginParameter(channelIndex, slotIndex, paramIndex, paramName, value);
            },
            bypassed => SetPluginBypass(channelIndex, slotIndex, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.ShowDialog();
    }

    private void SetPluginBypass(int channelIndex, int slotIndex, bool bypassed)
    {
        var channel = channelIndex == 0 ? Channel1 : Channel2;
        if (slotIndex < channel.PluginSlots.Count)
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

    private void RequestNoiseLearn(int channelIndex, int slotIndex)
    {
        _audioEngine.EnqueueParameterChange(new ParameterChange
        {
            ChannelId = channelIndex,
            Type = ParameterType.PluginCommand,
            PluginIndex = slotIndex,
            Command = PluginCommandType.ToggleNoiseLearn
        });
    }

    private void ApplyPluginParameter(int channelIndex, int slotIndex, int parameterIndex, string parameterName, float value)
    {
        _audioEngine.EnqueueParameterChange(new ParameterChange
        {
            ChannelId = channelIndex,
            Type = ParameterType.PluginParameter,
            PluginIndex = slotIndex,
            ParameterIndex = parameterIndex,
            Value = value
        });

        UpdatePluginParameterConfig(channelIndex, slotIndex, parameterName, value);
        UpdatePluginStateConfig(channelIndex, slotIndex);
    }

    private void RefreshPluginViewModels(int channelIndex)
    {
        var strip = _audioEngine.Channels[channelIndex];
        var slots = strip.PluginChain.GetSnapshot();
        var slotInfos = new List<PluginSlotInfo>(slots.Length);
        foreach (var plugin in slots)
        {
            float latencyMs = 0f;
            if (plugin is not null && _audioEngine.SampleRate > 0)
            {
                latencyMs = plugin.LatencySamples * 1000f / _audioEngine.SampleRate;
            }

            slotInfos.Add(new PluginSlotInfo
            {
                Name = plugin?.Name ?? string.Empty,
                IsBypassed = plugin?.IsBypassed ?? false,
                LatencyMs = latencyMs
            });
        }
        if (channelIndex == 0)
        {
            Channel1.UpdatePlugins(slotInfos);
        }
        else
        {
            Channel2.UpdatePlugins(slotInfos);
        }
    }

    private static int GetChainLatencySamples(ChannelStrip channel)
    {
        var slots = channel.PluginChain.GetSnapshot();
        int latency = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            var plugin = slots[i];
            if (plugin is null || plugin.IsBypassed)
            {
                continue;
            }

            latency += Math.Max(0, plugin.LatencySamples);
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

    private void UpdatePluginConfig(int channelIndex, int slotIndex, IPlugin plugin)
    {
        var config = GetOrCreateChannelConfig(channelIndex);
        EnsurePluginListCapacity(config, slotIndex + 1);

        var pluginConfig = new PluginConfig
        {
            Type = plugin.Id,
            IsBypassed = plugin.IsBypassed,
            Parameters = plugin.Parameters.ToDictionary(p => p.Name, p => p.DefaultValue),
            State = plugin.GetState()
        };

        config.Plugins[slotIndex] = pluginConfig;
        _configManager.Save(_config);
    }

    private void RemovePluginConfig(int channelIndex, int slotIndex)
    {
        var config = GetOrCreateChannelConfig(channelIndex);
        if (slotIndex >= 0 && slotIndex < config.Plugins.Count)
        {
            config.Plugins.RemoveAt(slotIndex);
        }
        _configManager.Save(_config);
    }

    private void SwapPluginConfig(int channelIndex, int fromIndex, int toIndex)
    {
        var config = GetOrCreateChannelConfig(channelIndex);
        int maxIndex = Math.Max(fromIndex, toIndex);
        EnsurePluginListCapacity(config, maxIndex + 1);
        (config.Plugins[fromIndex], config.Plugins[toIndex]) = (config.Plugins[toIndex], config.Plugins[fromIndex]);
        _configManager.Save(_config);
    }

    private void UpdatePluginParameterConfig(int channelIndex, int slotIndex, string parameterName, float value)
    {
        var config = GetOrCreateChannelConfig(channelIndex);
        if (slotIndex < 0 || slotIndex >= config.Plugins.Count)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(config.Plugins[slotIndex].Type))
        {
            return;
        }

        config.Plugins[slotIndex].Parameters[parameterName] = value;
        _configManager.Save(_config);
    }

    private void UpdatePluginStateConfig(int channelIndex, int slotIndex)
    {
        if ((uint)channelIndex >= (uint)_audioEngine.Channels.Count)
        {
            return;
        }

        var strip = _audioEngine.Channels[channelIndex];
        var slots = strip.PluginChain.GetSnapshot();
        if ((uint)slotIndex >= (uint)slots.Length)
        {
            return;
        }

        var plugin = slots[slotIndex];
        if (plugin is null)
        {
            return;
        }

        var config = GetOrCreateChannelConfig(channelIndex);
        if (slotIndex >= config.Plugins.Count)
        {
            return;
        }
        config.Plugins[slotIndex].State = plugin.GetState();
        _configManager.Save(_config);
    }

    private void UpdatePluginBypassConfig(int channelIndex, int slotIndex, bool bypass)
    {
        var config = GetOrCreateChannelConfig(channelIndex);
        if (slotIndex < 0 || slotIndex >= config.Plugins.Count)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(config.Plugins[slotIndex].Type))
        {
            return;
        }

        config.Plugins[slotIndex].IsBypassed = bypass;
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

    public void Dispose()
    {
        _meterTimer.Stop();
        _midiManager?.Dispose();
        _audioEngine.Stop();
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

    private float GetPluginParameterValue(int channelIndex, int slotIndex, string parameterName, float fallback)
    {
        if (channelIndex >= _config.Channels.Count)
        {
            return fallback;
        }

        var channel = _config.Channels[channelIndex];
        if (slotIndex >= channel.Plugins.Count)
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
