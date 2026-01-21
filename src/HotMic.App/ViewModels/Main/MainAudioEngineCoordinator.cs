using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using HotMic.Common.Configuration;
using HotMic.Core.Analysis;
using HotMic.Core.Engine;
using HotMic.Core.Plugins;
using HotMic.Core.Plugins.BuiltIn;
using HotMic.Vst3;

namespace HotMic.App.ViewModels;

internal sealed class MainAudioEngineCoordinator : IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly ConfigManager _configManager;
    private readonly MainPluginCoordinator _pluginCoordinator;
    private readonly MainChannelCoordinator _channelCoordinator;
    private readonly PluginContainerWindowManager _containerWindows;
    private readonly Func<AudioEngine> _getAudioEngine;
    private readonly Action<AudioEngine> _setAudioEngine;
    private readonly Func<AppConfig> _getConfig;
    private readonly AnalysisOrchestrator _analysisOrchestrator = new();
    private readonly Queue<(long ticks, long inputDrops, long inputUnderflow, long outputUnderflow)> _dropHistory = new();
    private bool[] _inputUsageScratch = Array.Empty<bool>();
    private bool[] _lastInputUsage = Array.Empty<bool>();
    private bool _lastInputUsageValid;
    private long _lastInputDrops;
    private long _lastInputUnderflow;
    private long _lastOutputUnderflow;
    private AnalysisSignalMask[] _analysisTapRequestedSignals = Array.Empty<AnalysisSignalMask>();
    private AnalysisSignalMask _analysisVisualRequestedSignals;
    private bool _analysisRequestedSubscribed;
    private bool _disposed;
    private int _isReinitializing;
    private readonly Dictionary<(int ChannelIndex, int InstanceId), ClipLogState> _clipLogStates = new();
    private long _lastMeterUpdateTicks;
    private long _nextDebugUpdateTicks;
    private bool _lastProfilingEnabled;
    private static readonly long DebugUpdateIntervalTicks = Math.Max(1, Stopwatch.Frequency / 4);
    private static readonly long ThirtySecondsInTicks = Stopwatch.Frequency * 30;
    private static readonly long ClipLogIntervalTicks = Math.Max(1, Stopwatch.Frequency * 2);
    private static readonly long NonFiniteLogIntervalTicks = Math.Max(1, Stopwatch.Frequency * 2);

    public MainAudioEngineCoordinator(
        MainViewModel viewModel,
        ConfigManager configManager,
        MainPluginCoordinator pluginCoordinator,
        MainChannelCoordinator channelCoordinator,
        PluginContainerWindowManager containerWindows,
        Func<AudioEngine> getAudioEngine,
        Action<AudioEngine> setAudioEngine,
        Func<AppConfig> getConfig)
    {
        _viewModel = viewModel;
        _configManager = configManager;
        _pluginCoordinator = pluginCoordinator;
        _channelCoordinator = channelCoordinator;
        _containerWindows = containerWindows;
        _getAudioEngine = getAudioEngine;
        _setAudioEngine = setAudioEngine;
        _getConfig = getConfig;

        AttachAnalysisOrchestrator(AudioEngine, _getConfig().AudioSettings.SampleRate);
    }

    public AnalysisOrchestrator AnalysisOrchestrator => _analysisOrchestrator;

    private AudioEngine AudioEngine => _getAudioEngine();

    public void SetAnalysisTapRequestedSignals(int channelIndex, AnalysisSignalMask requestedSignals)
    {
        if ((uint)channelIndex >= (uint)AudioEngine.Channels.Count)
        {
            return;
        }

        EnsureAnalysisTapRequestCapacity(AudioEngine.Channels.Count);
        _analysisTapRequestedSignals[channelIndex] = requestedSignals;
        UpdateVisualRequestedSignals();
    }

    public void EnsureEngineMatchesConfig()
    {
        var config = _getConfig();
        if (AudioEngine.SampleRate == config.AudioSettings.SampleRate &&
            AudioEngine.BlockSize == config.AudioSettings.BufferSize)
        {
            return;
        }

        AudioEngine.Dispose();
        var engine = new AudioEngine(config.AudioSettings, config.Channels.Count);
        _setAudioEngine(engine);
        AttachAnalysisOrchestrator(engine, config.AudioSettings.SampleRate);
    }

    public void StartEngine()
    {
        if (!AudioEngine.IsVbCableInstalled())
        {
            _viewModel.StatusMessage = "VB-Cable not detected. Please install VB-Cable to enable output.";
            return;
        }

        _viewModel.StatusMessage = string.Empty;
        ApplyDeviceSelection();
    }

    public void ApplyDeviceSelection()
    {
        AudioEngine.Stop();

        if (_viewModel.SelectedOutputDevice is null)
        {
            _viewModel.StatusMessage = "Select an output device.";
            return;
        }

        if (!DeviceManager.IsVbCableDeviceName(_viewModel.SelectedOutputDevice.Name))
        {
            _viewModel.StatusMessage = "Output must be set to VB-Cable.";
            return;
        }

        var config = _getConfig();
        config.AudioSettings.OutputDeviceId = _viewModel.SelectedOutputDevice?.Id ?? string.Empty;
        config.AudioSettings.MonitorOutputDeviceId = _viewModel.SelectedMonitorDevice?.Id ?? string.Empty;
        config.AudioSettings.SampleRate = _viewModel.SelectedSampleRate;
        config.AudioSettings.BufferSize = _viewModel.SelectedBufferSize;
        config.AudioSettings.QualityMode = _viewModel.QualityMode;
        _configManager.Save(config);

        _viewModel.StatusMessage = string.Empty;
        AudioEngine.ConfigureOutputDevices(config.AudioSettings.OutputDeviceId, config.AudioSettings.MonitorOutputDeviceId);
        AudioEngine.EnsureChannelCount(Math.Max(1, _viewModel.Channels.Count));
        if (_pluginCoordinator.GraphCount != AudioEngine.Channels.Count)
        {
            _pluginCoordinator.InitializePluginGraphs();
        }

        _pluginCoordinator.ApplyChannelInputsToEngine();
        AudioEngine.RebuildRoutingGraph();
        ApplyChannelStateToEngine();
        try
        {
            AudioEngine.Start();
            AudioEngine.SetMasterMute(_viewModel.MasterMuted);
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = $"Audio start failed: {ex.Message}";
        }

        if (_viewModel.StatusMessage.Length == 0 &&
            (AudioEngine.SampleRate != _viewModel.SelectedSampleRate || AudioEngine.BlockSize != _viewModel.SelectedBufferSize))
        {
            _viewModel.StatusMessage = "Sample rate/buffer changes apply on restart.";
        }
    }

    public void ApplyQualityMode(AudioQualityMode mode)
    {
        var profile = AudioQualityProfiles.ForMode(mode, _viewModel.SelectedSampleRate);
        _viewModel.SelectedBufferSize = profile.BufferSize;
        var config = _getConfig();
        config.AudioSettings.QualityMode = mode;
        config.AudioSettings.BufferSize = _viewModel.SelectedBufferSize;
        _configManager.Save(config);
        RestartAudioEngineForQuality(profile);
    }

    public void RestartAudioEngineForQuality(AudioQualityProfile profile)
    {
        AudioEngine.Stop();
        var snapshots = CapturePluginSnapshots();
        AudioEngine.Dispose();

        var config = _getConfig();
        var engine = new AudioEngine(config.AudioSettings, Math.Max(1, config.Channels.Count));
        _setAudioEngine(engine);

        AttachAnalysisOrchestrator(engine, config.AudioSettings.SampleRate);

        if (_viewModel.SelectedOutputDevice is null)
        {
            _viewModel.StatusMessage = "Select an output device.";
            return;
        }

        if (!DeviceManager.IsVbCableDeviceName(_viewModel.SelectedOutputDevice.Name))
        {
            _viewModel.StatusMessage = "Output must be set to VB-Cable.";
            return;
        }

        engine.ConfigureOutputDevices(
            _viewModel.SelectedOutputDevice?.Id ?? string.Empty,
            _viewModel.SelectedMonitorDevice?.Id ?? string.Empty);
        engine.EnsureChannelCount(Math.Max(1, _viewModel.Channels.Count));
        engine.RebuildRoutingGraph();
        ApplyChannelStateToEngine();

        RestorePluginsFromSnapshots(snapshots, profile);
        _pluginCoordinator.InitializePluginGraphs();
        _pluginCoordinator.SyncGraphsWithConfig();
        _pluginCoordinator.NormalizeOutputSendPlugins();
        _pluginCoordinator.ApplyChannelInputsToEngine();

        try
        {
            engine.Start();
            _viewModel.StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = $"Audio start failed: {ex.Message}";
        }

        for (int i = 0; i < engine.Channels.Count; i++)
        {
            _pluginCoordinator.RefreshPluginViewModels(i);
        }
    }

    public void ReinitializeAudioEngine()
    {
        if (Interlocked.Exchange(ref _isReinitializing, 1) == 1)
        {
            return;
        }

        try
        {
            _viewModel.StatusMessage = "Reinitializing audio engine...";
            _clipLogStates.Clear();
            _dropHistory.Clear();
            _lastInputDrops = 0;
            _lastInputUnderflow = 0;
            _lastOutputUnderflow = 0;
            _lastInputUsageValid = false;

            var profile = AudioQualityProfiles.ForMode(_viewModel.QualityMode, _viewModel.SelectedSampleRate);
            RestartAudioEngineForQuality(profile);
        }
        finally
        {
            Interlocked.Exchange(ref _isReinitializing, 0);
        }
    }

    public void RebuildForChannelTopologyChange(int desiredActiveIndex, Action applyConfigToViewModels, Action updateDynamicWindowWidth)
    {
        _viewModel.IsInitializing = true;
        _containerWindows.CloseAll();

        AudioEngine.Stop();
        AudioEngine.Dispose();

        var config = _getConfig();
        var engine = new AudioEngine(config.AudioSettings, Math.Max(1, config.Channels.Count));
        _setAudioEngine(engine);
        AttachAnalysisOrchestrator(engine, config.AudioSettings.SampleRate);

        _channelCoordinator.BuildChannelViewModels();
        applyConfigToViewModels();
        _pluginCoordinator.InitializePluginGraphs();
        _pluginCoordinator.LoadPluginsFromConfig();
        updateDynamicWindowWidth();

        if ((uint)desiredActiveIndex < (uint)_viewModel.Channels.Count)
        {
            _viewModel.ActiveChannelIndex = desiredActiveIndex;
        }

        _viewModel.IsInitializing = false;
        ApplyDeviceSelection();
    }

    public void SetMasterMute(bool muted)
    {
        AudioEngine.SetMasterMute(muted);
        var config = _getConfig();
        config.Ui.MasterMuted = muted;
        _configManager.Save(config);
    }

    public void UpdateMeters()
    {
        if (Volatile.Read(ref _isReinitializing) == 1)
        {
            return;
        }

        int channelCount = Math.Min(AudioEngine.Channels.Count, _viewModel.Channels.Count);
        if (channelCount == 0)
        {
            return;
        }

        long nowTicks = Stopwatch.GetTimestamp();
        bool profilingEnabled = _viewModel.ShowDebugOverlay;
        if (profilingEnabled != _lastProfilingEnabled)
        {
            AudioEngine.SetProfilingEnabled(profilingEnabled);
            _lastProfilingEnabled = profilingEnabled;
        }

        for (int i = 0; i < channelCount; i++)
        {
            var channel = AudioEngine.Channels[i];
            var viewModel = _viewModel.Channels[i];
            viewModel.InputPeakLevel = channel.InputMeter.GetPeakLevel();
            viewModel.InputRmsLevel = channel.InputMeter.GetRmsLevel();
            viewModel.OutputPeakLevel = channel.OutputMeter.GetPeakLevel();
            viewModel.OutputRmsLevel = channel.OutputMeter.GetRmsLevel();

            UpdatePluginMeters(i, viewModel, channel);
            UpdateContainerMeters(viewModel, channel);
            UpdateContainerWindowMeters(i, channel);
        }

        _viewModel.MasterLufsMomentaryLeft = AudioEngine.MasterLufsLeft.GetMomentaryLufs();
        _viewModel.MasterLufsShortTermLeft = AudioEngine.MasterLufsLeft.GetShortTermLufs();
        _viewModel.MasterLufsMomentaryRight = AudioEngine.MasterLufsRight.GetMomentaryLufs();
        _viewModel.MasterLufsShortTermRight = AudioEngine.MasterLufsRight.GetShortTermLufs();

        UpdateMasterMeterLevels();

        _viewModel.Diagnostics = AudioEngine.GetDiagnosticsSnapshot();
        int sampleRate = Math.Max(1, AudioEngine.SampleRate);
        float baseLatencyMs = _viewModel.SelectedBufferSize * 1000f / sampleRate;
        int chainLatencySamples = GetOutputChainLatencySamples();
        float chainLatencyMs = chainLatencySamples * 1000f / sampleRate;
        _viewModel.LatencyMs = baseLatencyMs + chainLatencyMs;

        var inputUsage = BuildInputUsageMap();
        bool hadUsage = _lastInputUsageValid;
        long inputDrops = 0;
        long inputUnderflows = 0;
        var inputs = _viewModel.Diagnostics.Inputs;
        for (int i = 0; i < inputs.Count; i++)
        {
            int channelId = inputs[i].ChannelId;
            if ((uint)channelId < (uint)inputUsage.Length && inputUsage[channelId])
            {
                inputDrops += inputs[i].DroppedSamples + inputs[i].OverflowSamples;
                inputUnderflows += inputs[i].UnderflowSamples;
            }
        }

        bool usageChanged = UpdateLastInputUsage(inputUsage);
        bool countersReset = false;
        if (hadUsage)
        {
            countersReset = inputDrops < _lastInputDrops ||
                            inputUnderflows < _lastInputUnderflow ||
                            _viewModel.Diagnostics.OutputUnderflowSamples < _lastOutputUnderflow;
        }

        _lastInputDrops = inputDrops;
        _lastInputUnderflow = inputUnderflows;
        _lastOutputUnderflow = _viewModel.Diagnostics.OutputUnderflowSamples;

        if (usageChanged || countersReset)
        {
            _dropHistory.Clear();
            _viewModel.InputDrops30Sec = 0;
            _viewModel.InputUnderflowDrops30Sec = 0;
            _viewModel.OutputUnderflowDrops30Sec = 0;
            _viewModel.Drops30Sec = 0;
        }

        _viewModel.TotalDrops = inputDrops + inputUnderflows + _viewModel.Diagnostics.OutputUnderflowSamples;

        _dropHistory.Enqueue((nowTicks, inputDrops, inputUnderflows, _viewModel.Diagnostics.OutputUnderflowSamples));

        long cutoffTicks = nowTicks - ThirtySecondsInTicks;
        while (_dropHistory.Count > 0 && _dropHistory.Peek().ticks < cutoffTicks)
        {
            _dropHistory.Dequeue();
        }

        if (_dropHistory.Count > 0)
        {
            var oldest = _dropHistory.Peek();
            _viewModel.InputDrops30Sec = inputDrops - oldest.inputDrops;
            _viewModel.InputUnderflowDrops30Sec = inputUnderflows - oldest.inputUnderflow;
            _viewModel.OutputUnderflowDrops30Sec = _viewModel.Diagnostics.OutputUnderflowSamples - oldest.outputUnderflow;
            _viewModel.Drops30Sec = _viewModel.InputDrops30Sec + _viewModel.InputUnderflowDrops30Sec + _viewModel.OutputUnderflowDrops30Sec;
        }

        UpdateDebugInfo(nowTicks, inputUsage);
        AudioEngine.DrainPendingPluginDisposals();
        _lastMeterUpdateTicks = nowTicks;
    }

    private void ApplyChannelStateToEngine()
    {
        int count = Math.Min(AudioEngine.Channels.Count, _viewModel.Channels.Count);
        for (int i = 0; i < count; i++)
        {
            var channel = AudioEngine.Channels[i];
            var viewModel = _viewModel.Channels[i];
            channel.SetInputGainDb(viewModel.InputGainDb);
            channel.SetOutputGainDb(viewModel.OutputGainDb);
            channel.SetMuted(viewModel.IsMuted);
            channel.SetSoloed(viewModel.IsSoloed);
        }

        AudioEngine.SetMasterMute(_viewModel.MasterMuted);
    }

    private PluginSlot?[][] CapturePluginSnapshots()
    {
        var snapshots = new PluginSlot?[AudioEngine.Channels.Count][];
        for (int i = 0; i < AudioEngine.Channels.Count; i++)
        {
            snapshots[i] = AudioEngine.Channels[i].PluginChain.DetachAll();
        }

        return snapshots;
    }

    private void RestorePluginsFromSnapshots(PluginSlot?[][] snapshots, AudioQualityProfile profile)
    {
        for (int channelIndex = 0; channelIndex < AudioEngine.Channels.Count; channelIndex++)
        {
            var strip = AudioEngine.Channels[channelIndex];
            var snapshot = channelIndex < snapshots.Length ? snapshots[channelIndex] : [];
            var pluginList = new List<PluginSlot>();

            foreach (var slot in snapshot)
            {
                if (slot is null)
                {
                    continue;
                }

                ReinitializePluginForQuality(slot.Plugin, profile);
                slot.Meter.Reset();
                slot.Delta.Reset();
                pluginList.Add(slot);
            }

            var oldSlots = strip.PluginChain.GetSnapshot();
            var newSlots = pluginList.ToArray();
            strip.PluginChain.ReplaceAll(newSlots);
            _pluginCoordinator.QueueRemovedPlugins(oldSlots, newSlots);
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
            vst3.Initialize(AudioEngine.SampleRate, AudioEngine.BlockSize);
            if (state.Length > 0)
            {
                vst3.SetState(state);
            }
        }
        else
        {
            plugin.Initialize(AudioEngine.SampleRate, AudioEngine.BlockSize);
        }

        plugin.IsBypassed = bypassed;
    }

    private void UpdateMasterMeterLevels()
    {
        float peak = 0f;
        float rms = 0f;
        OutputSendMode mode = OutputSendMode.Both;

        if (TryGetOutputSendSource(out int channelIndex, out mode))
        {
            var channel = AudioEngine.Channels[channelIndex];
            peak = channel.OutputMeter.GetPeakLevel();
            rms = channel.OutputMeter.GetRmsLevel();
        }

        if (_viewModel.MasterMuted)
        {
            peak = 0f;
            rms = 0f;
        }

        switch (mode)
        {
            case OutputSendMode.Left:
                _viewModel.MasterPeakLeft = peak;
                _viewModel.MasterPeakRight = 0f;
                _viewModel.MasterRmsLeft = rms;
                _viewModel.MasterRmsRight = 0f;
                break;
            case OutputSendMode.Right:
                _viewModel.MasterPeakLeft = 0f;
                _viewModel.MasterPeakRight = peak;
                _viewModel.MasterRmsLeft = 0f;
                _viewModel.MasterRmsRight = rms;
                break;
            case OutputSendMode.Both:
            default:
                _viewModel.MasterPeakLeft = peak;
                _viewModel.MasterPeakRight = peak;
                _viewModel.MasterRmsLeft = rms;
                _viewModel.MasterRmsRight = rms;
                break;
        }
    }

    private bool TryGetOutputSendSource(out int channelIndex, out OutputSendMode mode)
    {
        for (int i = 0; i < AudioEngine.Channels.Count; i++)
        {
            var slots = AudioEngine.Channels[i].PluginChain.GetSnapshot();
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
            return GetChainLatencySamples(AudioEngine.Channels[channelIndex]);
        }

        int max = 0;
        for (int i = 0; i < AudioEngine.Channels.Count; i++)
        {
            max = Math.Max(max, GetChainLatencySamples(AudioEngine.Channels[i]));
        }

        return max;
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

    private void UpdateContainerWindowMeters(int channelIndex, ChannelStrip channel)
    {
        var windows = _containerWindows.GetWindowSnapshot();
        if (windows.Count == 0)
        {
            return;
        }

        for (int w = 0; w < windows.Count; w++)
        {
            var entry = windows[w];
            if (entry.ChannelIndex != channelIndex)
            {
                continue;
            }

            var viewModel = entry.ViewModel;
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
                    slotVm.IsClipping = slot.Meter.GetClipHoldActive();
                    slotVm.SetBypassSilent(slot.Plugin.IsBypassed);

                    var delta = slot.Delta;
                    if (delta.DisplayMode != slotVm.DeltaDisplayMode)
                    {
                        delta.DisplayMode = slotVm.DeltaDisplayMode;
                    }

                    if (slotVm.SpectralDelta is null)
                    {
                        slotVm.SpectralDelta = delta.BandDeltas;
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
                    slotVm.IsClipping = false;
                    slotVm.SpectralDelta = null;
                }
            }
        }
    }

    private void UpdatePluginMeters(int channelIndex, ChannelStripViewModel viewModel, ChannelStrip channel)
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
                bool isClipping = chainSlot.Meter.GetClipHoldActive();
                int nonFiniteSamples = chainSlot.Meter.ConsumeNonFiniteSamples();
                slot.IsClipping = isClipping;
                UpdateClipLogging(channelIndex, i, chainSlot, slots, isClipping, nonFiniteSamples);
                slot.SetBypassSilent(chainSlot.Plugin.IsBypassed);
            }
            else
            {
                slot.OutputPeakLevel = 0f;
                slot.OutputRmsLevel = 0f;
                slot.IsClipping = false;
            }
        }

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

            if (delta.DisplayMode != slot.DeltaDisplayMode)
            {
                delta.DisplayMode = slot.DeltaDisplayMode;
            }

            if (slot.SpectralDelta is null)
            {
                slot.SpectralDelta = delta.BandDeltas;
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
            slot.IsClipping = false;
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

    private void UpdateClipLogging(int channelIndex, int slotIndex, PluginSlot chainSlot, PluginSlot?[] slots, bool isClipping, int nonFiniteSamples)
    {
        var key = (channelIndex, chainSlot.InstanceId);
        _clipLogStates.TryGetValue(key, out var state);
        long now = Stopwatch.GetTimestamp();

        if (isClipping && !state.WasClipping && now - state.LastClipLogTicks >= ClipLogIntervalTicks)
        {
            string upstream = GetUpstreamPluginLabel(slots, slotIndex);
            string pluginLabel = $"{chainSlot.Plugin.Name} ({chainSlot.Plugin.Id})";
            string nonFiniteLabel = nonFiniteSamples > 0 ? $" nonFinite={nonFiniteSamples}" : string.Empty;
            string message = $"[Clip] ch{channelIndex + 1} slot{slotIndex + 1} id={chainSlot.InstanceId} plugin=\"{pluginLabel}\" upstream=\"{upstream}\" peak={chainSlot.Meter.GetPeakLevel():0.###} rms={chainSlot.Meter.GetRmsLevel():0.###}{nonFiniteLabel}";
            Console.WriteLine(message);
            state.LastClipLogTicks = now;
        }

        if (nonFiniteSamples > 0 && now - state.LastNonFiniteLogTicks >= NonFiniteLogIntervalTicks)
        {
            string upstream = GetUpstreamPluginLabel(slots, slotIndex);
            string pluginLabel = $"{chainSlot.Plugin.Name} ({chainSlot.Plugin.Id})";
            string message = $"[Clip] non-finite ch{channelIndex + 1} slot{slotIndex + 1} id={chainSlot.InstanceId} plugin=\"{pluginLabel}\" upstream=\"{upstream}\" samples={nonFiniteSamples}";
            Console.WriteLine(message);
            state.LastNonFiniteLogTicks = now;
        }

        state.WasClipping = isClipping;
        _clipLogStates[key] = state;
    }

    private static string GetUpstreamPluginLabel(PluginSlot?[] slots, int slotIndex)
    {
        for (int i = slotIndex - 1; i >= 0; i--)
        {
            var slot = slots[i];
            if (slot is null || slot.Plugin.IsBypassed)
            {
                continue;
            }

            return $"{slot.Plugin.Name} ({slot.Plugin.Id})";
        }

        return "input";
    }

    private void UpdateDebugInfo(long nowTicks, bool[] inputUsage)
    {
        if (nowTicks < _nextDebugUpdateTicks)
        {
            return;
        }

        _nextDebugUpdateTicks = nowTicks + DebugUpdateIntervalTicks;
        var diagnostics = AudioEngine.GetDiagnosticsSnapshot();

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
        int[] processingOrder = AudioEngine.GetRoutingProcessingOrder();
        string graphOrder = processingOrder.Length == 0
            ? "n/a"
            : string.Join(" > ", processingOrder.Select(index => $"ch{index + 1}"));

        _viewModel.RoutingGraphOrder = graphOrder;

        long inputDrops = 0;
        long inputUnderflows = 0;
        for (int i = 0; i < inputs.Count; i++)
        {
            int channelId = inputs[i].ChannelId;
            if ((uint)channelId < (uint)inputUsage.Length && inputUsage[channelId])
            {
                inputDrops += inputs[i].DroppedSamples + inputs[i].OverflowSamples;
                inputUnderflows += inputs[i].UnderflowSamples;
            }
        }

        _viewModel.ProfilingLine = BuildProfilingLine(diagnostics);
        _viewModel.WorstPluginLine = BuildWorstPluginLine(diagnostics.BlockBudgetTicks);
        _viewModel.AnalysisTapProfilingLine = BuildAnalysisTapProfilingLine();
        _viewModel.AnalysisTapPitchProfilingLine = BuildAnalysisTapPitchProfilingLine();
        _viewModel.AnalysisTapGateLine = BuildAnalysisTapGateLine();
        _viewModel.VitalizerLine = BuildVitalizerLine();

        _viewModel.DebugLines =
        [
            $"Audio: out={FormatFlag(diagnostics.OutputActive)} mon={FormatFlag(diagnostics.MonitorActive)} inputs={inputStatus} recov={(diagnostics.IsRecovering ? "yes" : "no")}",
            $"Callbacks(ms): out={outputAge} in={inputAges}",
            $"Buffers: {bufferSummary} mon {diagnostics.MonitorBufferedSamples}/{diagnostics.MonitorBufferCapacity}",
            $"Graph: {graphOrder}",
            $"Drops: in {inputDrops} under {inputUnderflows} out {diagnostics.OutputUnderflowSamples}",
            $"Formats: out {_viewModel.SelectedSampleRate}Hz/2ch",
            $"UI {uiAge}ms",
            _viewModel.VitalizerLine
        ];
    }

    private string BuildProfilingLine(AudioEngineDiagnosticsSnapshot diagnostics)
    {
        if (diagnostics.BlockBudgetTicks <= 0 || (diagnostics.LastBlockTicks == 0 && diagnostics.MaxBlockTicks == 0))
        {
            return "Proc: n/a";
        }

        double budgetMs = diagnostics.BlockBudgetTicks * 1000.0 / Stopwatch.Frequency;
        double lastMs = diagnostics.LastBlockTicks * 1000.0 / Stopwatch.Frequency;
        double maxMs = diagnostics.MaxBlockTicks * 1000.0 / Stopwatch.Frequency;
        string line = $"Proc: last {lastMs:0.00}ms max {maxMs:0.00}ms budget {budgetMs:0.00}ms over {diagnostics.BlockOverrunCount}";

        if (diagnostics.LastBlockCpuTicks > 0 || diagnostics.MaxBlockCpuTicks > 0)
        {
            double lastCpuMs = diagnostics.LastBlockCpuTicks / 10_000.0;
            double maxCpuMs = diagnostics.MaxBlockCpuTicks / 10_000.0;
            line += $" cpu {lastCpuMs:0.00}/{maxCpuMs:0.00}ms";
        }

        return line;
    }

    private string BuildWorstPluginLine(long budgetTicks)
    {
        var channels = AudioEngine.Channels;
        long worstMax = 0;
        long worstLast = 0;
        long worstCpuLast = 0;
        long worstCpuMax = 0;
        long worstOver = 0;
        string worstName = string.Empty;
        int worstChannel = -1;

        for (int i = 0; i < channels.Count; i++)
        {
            var slots = channels[i].PluginChain.GetSnapshot();
            for (int s = 0; s < slots.Length; s++)
            {
                var slot = slots[s];
                if (slot is null)
                {
                    continue;
                }

                var plugin = slot.Plugin;
                if (plugin.IsBypassed)
                {
                    continue;
                }

                long maxTicks = slot.MaxProcessTicks;
                if (maxTicks <= 0)
                {
                    continue;
                }

                if (maxTicks > worstMax)
                {
                    worstMax = maxTicks;
                    worstLast = slot.LastProcessTicks;
                    worstCpuLast = slot.LastProcessCpuCycles;
                    worstCpuMax = slot.MaxProcessCpuCycles;
                    worstOver = slot.OverBudgetCount;
                    worstName = plugin.Name;
                    worstChannel = i;
                }
            }
        }

        if (worstChannel < 0)
        {
            return "Worst: n/a";
        }

        double lastMs = worstLast * 1000.0 / Stopwatch.Frequency;
        double maxMs = worstMax * 1000.0 / Stopwatch.Frequency;
        double pct = budgetTicks > 0 ? worstMax * 100.0 / budgetTicks : 0.0;

        string line = $"Worst: ch{worstChannel + 1} {worstName} last {lastMs:0.00}ms max {maxMs:0.00}ms ({pct:0}%)";
        if (worstOver > 0)
        {
            line += $" over {worstOver}";
        }
        if (worstCpuLast > 0 || worstCpuMax > 0)
        {
            line += $" cpu {FormatCycles(worstCpuLast)}/{FormatCycles(worstCpuMax)}";
        }

        return line;
    }

    private string BuildAnalysisTapProfilingLine()
    {
        if (!TryGetWorstAnalysisTapSnapshot(out var worstSnapshot, out int worstChannel))
        {
            return "Tap: n/a";
        }

        double lastTotalMs = TicksToMs(worstSnapshot.ProcessorLastTicks);
        double maxTotalMs = TicksToMs(worstSnapshot.ProcessorMaxTicks);
        double preLastMs = TicksToMs(worstSnapshot.PreprocessLastTicks);
        double preMaxMs = TicksToMs(worstSnapshot.PreprocessMaxTicks);
        double fftLastMs = TicksToMs(worstSnapshot.FftLastTicks);
        double fftMaxMs = TicksToMs(worstSnapshot.FftMaxTicks);
        double pitchLastMs = TicksToMs(worstSnapshot.PitchLastTicks);
        double pitchMaxMs = TicksToMs(worstSnapshot.PitchMaxTicks);
        double voiceLastMs = TicksToMs(worstSnapshot.VoicingLastTicks);
        double voiceMaxMs = TicksToMs(worstSnapshot.VoicingMaxTicks);
        double featLastMs = TicksToMs(worstSnapshot.FeatureLastTicks);
        double featMaxMs = TicksToMs(worstSnapshot.FeatureMaxTicks);
        double writeLastMs = TicksToMs(worstSnapshot.WriteLastTicks);
        double writeMaxMs = TicksToMs(worstSnapshot.WriteMaxTicks);
        double resolveLastMs = TicksToMs(worstSnapshot.ResolveLastTicks);
        double resolveMaxMs = TicksToMs(worstSnapshot.ResolveMaxTicks);
        double captureLastMs = TicksToMs(worstSnapshot.CaptureLastTicks);
        double captureMaxMs = TicksToMs(worstSnapshot.CaptureMaxTicks);
        string channelLabel = worstChannel >= 0 ? $"ch{worstChannel + 1} " : string.Empty;

        return $"Tap: {channelLabel}total {lastTotalMs:0.00}/{maxTotalMs:0.00}ms pre {preLastMs:0.00}/{preMaxMs:0.00} fft {fftLastMs:0.00}/{fftMaxMs:0.00} pitch {pitchLastMs:0.00}/{pitchMaxMs:0.00} voice {voiceLastMs:0.00}/{voiceMaxMs:0.00} feat {featLastMs:0.00}/{featMaxMs:0.00} write {writeLastMs:0.00}/{writeMaxMs:0.00} res {resolveLastMs:0.00}/{resolveMaxMs:0.00} cap {captureLastMs:0.00}/{captureMaxMs:0.00}";
    }

    private string BuildAnalysisTapPitchProfilingLine()
    {
        if (!TryGetWorstAnalysisTapSnapshot(out var worstSnapshot, out int worstChannel))
        {
            return "Pitch: n/a";
        }

        var pitchProfile = worstSnapshot.PitchProfile;
        double lastTotalMs = TicksToMs(pitchProfile.LastTotalTicks);
        double maxTotalMs = TicksToMs(pitchProfile.MaxTotalTicks);
        string channelLabel = worstChannel >= 0 ? $"ch{worstChannel + 1} " : string.Empty;
        string algorithmLabel = pitchProfile.Algorithm.ToString();
        string rangeSuffix = string.Empty;
        if (pitchProfile.HasFrameInfo)
        {
            rangeSuffix = $" n {pitchProfile.FrameSize}";
            if (pitchProfile.HasPeriodRange)
            {
                rangeSuffix += $" lag {pitchProfile.MinPeriod}-{pitchProfile.MaxPeriod}";
            }
        }
        string cpuSuffix = string.Empty;
        if (pitchProfile.HasCpuCycles)
        {
            cpuSuffix = $" cpu {FormatCycles(pitchProfile.LastTotalCpuCycles)}/{FormatCycles(pitchProfile.MaxTotalCpuCycles)}";
            if (pitchProfile.HasDiffCpuCycles)
            {
                cpuSuffix += $" dcpu {FormatCycles(pitchProfile.LastDiffCpuCycles)}/{FormatCycles(pitchProfile.MaxDiffCpuCycles)}";
            }
        }

        if (pitchProfile.HasDetail)
        {
            double diffLastMs = TicksToMs(pitchProfile.LastDiffTicks);
            double diffMaxMs = TicksToMs(pitchProfile.MaxDiffTicks);
            double cmndLastMs = TicksToMs(pitchProfile.LastCmndTicks);
            double cmndMaxMs = TicksToMs(pitchProfile.MaxCmndTicks);
            double searchLastMs = TicksToMs(pitchProfile.LastSearchTicks);
            double searchMaxMs = TicksToMs(pitchProfile.MaxSearchTicks);
            double refineLastMs = TicksToMs(pitchProfile.LastRefineTicks);
            double refineMaxMs = TicksToMs(pitchProfile.MaxRefineTicks);

            return $"Pitch: {channelLabel}{algorithmLabel} total {lastTotalMs:0.00}/{maxTotalMs:0.00}ms diff {diffLastMs:0.00}/{diffMaxMs:0.00} cmnd {cmndLastMs:0.00}/{cmndMaxMs:0.00} search {searchLastMs:0.00}/{searchMaxMs:0.00} ref {refineLastMs:0.00}/{refineMaxMs:0.00}{rangeSuffix}{cpuSuffix}";
        }

        if (lastTotalMs <= 0.0 && maxTotalMs <= 0.0)
        {
            return $"Pitch: {channelLabel}{algorithmLabel} n/a{rangeSuffix}{cpuSuffix}";
        }

        return $"Pitch: {channelLabel}{algorithmLabel} total {lastTotalMs:0.00}/{maxTotalMs:0.00}ms{rangeSuffix}{cpuSuffix}";
    }

    private string BuildAnalysisTapGateLine()
    {
        if (!TryGetWorstAnalysisTapSnapshot(out var worstSnapshot, out int worstChannel))
        {
            return "Gate: n/a";
        }

        string channelLabel = worstChannel >= 0 ? $"ch{worstChannel + 1} " : string.Empty;
        string modeLabel = worstSnapshot.SpeechPresenceMode switch
        {
            AnalysisTapMode.UseExisting => "use",
            AnalysisTapMode.Generate => "gen",
            AnalysisTapMode.Disabled => "off",
            _ => "?"
        };

        string sourceLabel = worstSnapshot.SpeechPresenceHasSource ? "yes" : "no";
        string gateLabel = worstSnapshot.SpeechPresenceGateEnabled ? "on" : "off";
        float gateValue = worstSnapshot.SpeechPresenceGateValue;
        string valueLabel = float.IsFinite(gateValue) ? $"{gateValue:0.###}" : "nan";
        string stateLabel = worstSnapshot.SpeechPresenceGateEnabled
            ? (gateValue > AnalysisSignalProcessor.SpeechPresenceGateThreshold ? "open" : "closed")
            : "n/a";

        return $"Gate: {channelLabel}speech {modeLabel} src {sourceLabel} gate {gateLabel} val {valueLabel} {stateLabel}";
    }

    private string BuildVitalizerLine()
    {
        var channels = AudioEngine.Channels;
        for (int i = 0; i < channels.Count; i++)
        {
            var slots = channels[i].PluginChain.GetSnapshot();
            for (int s = 0; s < slots.Length; s++)
            {
                var slot = slots[s];
                if (slot?.Plugin is not VitalizerMk2TPlugin vitalizer)
                {
                    continue;
                }

                string channelLabel = $"ch{i + 1}";
                string bypassLabel = vitalizer.IsBypassed ? "bypass" : "active";
                string tubeLabel = vitalizer.TubeEnabled ? "tube on" : "tube off";
                string limitLabel = vitalizer.LimitEnabled ? "limit on" : "limit off";

                if (!vitalizer.TubeEnabled)
                {
                    return $"Vitalizer: {channelLabel} {bypassLabel} {tubeLabel} {limitLabel}";
                }

                float pre = vitalizer.TubePreRms;
                float post = vitalizer.TubePostRms;
                float target = vitalizer.TubeMakeupTarget;
                float current = vitalizer.TubeMakeupCurrent;
                string deltaLabel = "n/a";
                if (pre > 1e-6f && post > 1e-6f)
                {
                    float deltaDb = DspUtils.LinearToDb(post / pre);
                    deltaLabel = $"{deltaDb:0.0}dB";
                }

                return $"Vitalizer: {channelLabel} {bypassLabel} {tubeLabel} {limitLabel} pre {pre:0.000} post {post:0.000} Δ{deltaLabel} makeup {current:0.00}->{target:0.00}";
            }
        }

        return "Vitalizer: n/a";
    }

    private bool TryGetWorstAnalysisTapSnapshot(out AnalysisTapProfilingSnapshot worstSnapshot, out int worstChannel)
    {
        worstSnapshot = default;
        worstChannel = -1;
        if (!_viewModel.ShowDebugOverlay)
        {
            return false;
        }

        var channels = AudioEngine.Channels;
        long worstTicks = 0;

        for (int i = 0; i < channels.Count; i++)
        {
            var slots = channels[i].PluginChain.GetSnapshot();
            for (int s = 0; s < slots.Length; s++)
            {
                var slot = slots[s];
                if (slot?.Plugin is not AnalysisTapPlugin tap || tap.IsBypassed)
                {
                    continue;
                }

                var snapshot = tap.ProfilingSnapshot;
                long candidate = snapshot.ProcessorMaxTicks > 0 ? snapshot.ProcessorMaxTicks : snapshot.ProcessorLastTicks;
                if (candidate <= 0)
                {
                    continue;
                }

                if (candidate > worstTicks)
                {
                    worstTicks = candidate;
                    worstSnapshot = snapshot;
                    worstChannel = i;
                }
            }
        }

        return worstTicks > 0;
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

    private static double TicksToMs(long ticks)
    {
        if (ticks <= 0)
        {
            return 0.0;
        }

        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    private static string FormatCycles(long cycles)
    {
        if (cycles <= 0)
        {
            return "0cy";
        }

        double value = cycles;
        if (value >= 1_000_000_000)
        {
            return $"{value / 1_000_000_000:0.00}Gcy";
        }
        if (value >= 1_000_000)
        {
            return $"{value / 1_000_000:0.00}Mcy";
        }
        if (value >= 1_000)
        {
            return $"{value / 1_000:0.0}kcy";
        }

        return $"{cycles}cy";
    }

    private static string FormatFlag(bool value) => value ? "on" : "off";

    private bool[] BuildInputUsageMap()
    {
        int channelCount = AudioEngine.Channels.Count;
        if (_inputUsageScratch.Length < channelCount)
        {
            _inputUsageScratch = new bool[channelCount];
        }

        Array.Clear(_inputUsageScratch, 0, channelCount);

        for (int i = 0; i < channelCount; i++)
        {
            var slots = AudioEngine.Channels[i].PluginChain.GetSnapshot();
            int selectedPriority = int.MinValue;
            bool deviceSelected = false;
            for (int s = 0; s < slots.Length; s++)
            {
                var slot = slots[s];
                if (slot?.Plugin is not IChannelInputPlugin input || slot.Plugin.IsBypassed)
                {
                    continue;
                }

                int priority = (int)input.InputKind;
                if (selectedPriority < 0 || priority > selectedPriority)
                {
                    selectedPriority = priority;
                    deviceSelected = slot.Plugin is InputPlugin;
                }
            }

            _inputUsageScratch[i] = selectedPriority >= 0 && deviceSelected;
        }

        return _inputUsageScratch;
    }

    private bool UpdateLastInputUsage(bool[] inputUsage)
    {
        int length = inputUsage.Length;
        if (_lastInputUsage.Length != length)
        {
            _lastInputUsage = new bool[length];
            _lastInputUsageValid = false;
        }

        bool changed = !_lastInputUsageValid;
        if (!changed)
        {
            for (int i = 0; i < length; i++)
            {
                if (_lastInputUsage[i] != inputUsage[i])
                {
                    changed = true;
                    break;
                }
            }
        }

        if (changed)
        {
            Array.Copy(inputUsage, _lastInputUsage, length);
            _lastInputUsageValid = true;
        }

        return changed;
    }

    private void EnsureAnalysisTapRequestCapacity(int channelCount)
    {
        if (_analysisTapRequestedSignals.Length == channelCount)
        {
            return;
        }

        Array.Resize(ref _analysisTapRequestedSignals, channelCount);
    }

    private void UpdateVisualRequestedSignals()
    {
        var channels = AudioEngine.Channels;
        EnsureAnalysisTapRequestCapacity(channels.Count);

        for (int i = 0; i < channels.Count; i++)
        {
            AnalysisSignalMask visualMask = i == 0 ? _analysisVisualRequestedSignals : AnalysisSignalMask.None;
            AnalysisSignalMask combined = _analysisTapRequestedSignals[i] | visualMask;
            channels[i].PluginChain.SetVisualRequestedSignals(combined);
        }
    }

    private void HandleRequestedSignalsChanged(AnalysisSignalMask requestedSignals)
    {
        _analysisVisualRequestedSignals = requestedSignals;
        UpdateVisualRequestedSignals();
    }

    private void AttachAnalysisOrchestrator(AudioEngine engine, int sampleRate)
    {
        _analysisOrchestrator.Initialize(sampleRate);
        _analysisOrchestrator.Reset();
        engine.AnalysisCaptureLink.Orchestrator = _analysisOrchestrator;
        _analysisOrchestrator.CaptureLink = engine.AnalysisCaptureLink;

        if (!_analysisRequestedSubscribed)
        {
            _analysisOrchestrator.RequestedSignalsChanged += HandleRequestedSignalsChanged;
            _analysisRequestedSubscribed = true;
        }

        _analysisVisualRequestedSignals = _analysisOrchestrator.RequestedSignals;
        UpdateVisualRequestedSignals();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_analysisRequestedSubscribed)
        {
            _analysisOrchestrator.RequestedSignalsChanged -= HandleRequestedSignalsChanged;
            _analysisRequestedSubscribed = false;
        }

        _analysisOrchestrator.Dispose();
        GC.SuppressFinalize(this);
    }

    private struct ClipLogState
    {
        public bool WasClipping;
        public long LastClipLogTicks;
        public long LastNonFiniteLogTicks;
    }
}
