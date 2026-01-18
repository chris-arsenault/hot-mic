using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using HotMic.Common.Configuration;
using HotMic.Core.Analysis;
using HotMic.Core.Metering;
using HotMic.Core.Plugins;
using HotMic.Core.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace HotMic.Core.Engine;

public sealed class AudioEngine : IDisposable
{
    private readonly DeviceManager _deviceManager = new();
    private readonly LockFreeChannel<ParameterChange> _parameterQueue = new();
    private readonly PluginDisposalQueue _pluginDisposalQueue = new();
    private readonly InputCaptureManager _inputCaptureManager;
    private readonly RoutingScheduler _routingScheduler;
    private readonly AudioEngineDiagnosticsCollector _diagnosticsCollector;
    private readonly DeviceRecoveryManager _recoveryManager;
    private LockFreeRingBuffer? _monitorBuffer;
    private ChannelStrip[] _channels = Array.Empty<ChannelStrip>();
    private RoutingSnapshot? _routingSnapshot;
    private readonly LufsMeterProcessor _masterLufsLeft;
    private readonly LufsMeterProcessor _masterLufsRight;
    private readonly int _sampleRate;
    private readonly int _blockSize;
    private readonly int _latencyMs;

    private long _lastOutputCallbackTicks;
    private long _outputCallbackCount;
    private int _lastOutputFrames;
    private int _outputActive;
    private int _processingEnabled = 1;
    private int _monitorActive;
    private long _outputUnderflowSamples;

    private WasapiOut? _output;
    private WasapiOut? _monitorOutput;
    private OutputPipeline? _outputPipeline;

    private int _isStopping;
    private int _presetLoadDepth;
    private int _processingEnabledBeforePresetLoad;

    private readonly AnalysisTap _analysisTap = new();

    public event EventHandler<DeviceDisconnectedEventArgs>? DeviceDisconnected;
    public event EventHandler<DeviceRecoveredEventArgs>? DeviceRecovered;

    public AudioEngine(AudioSettingsConfig settings, int channelCount)
    {
        _sampleRate = settings.SampleRate;
        _blockSize = settings.BufferSize;
        _latencyMs = Math.Max(20, (int)(1000.0 * _blockSize / _sampleRate));
        _masterLufsLeft = new LufsMeterProcessor(_sampleRate);
        _masterLufsRight = new LufsMeterProcessor(_sampleRate);

        _inputCaptureManager = new InputCaptureManager(
            _sampleRate,
            _blockSize,
            ShouldIgnoreStopEvent,
            HandleDeviceInvalidated);
        _routingScheduler = new RoutingScheduler(_sampleRate, _blockSize);
        _diagnosticsCollector = new AudioEngineDiagnosticsCollector(_inputCaptureManager);
        _recoveryManager = new DeviceRecoveryManager(
            _inputCaptureManager,
            Stop,
            Start,
            _inputCaptureManager.GetInputDeviceIds,
            RaiseDeviceDisconnected,
            RaiseDeviceRecovered);

        ConfigureOutputDevices(settings.OutputDeviceId, settings.MonitorOutputDeviceId);
        EnsureChannelCount(Math.Max(1, channelCount));
    }

    public IReadOnlyList<ChannelStrip> Channels => _channels;

    public int SampleRate => _sampleRate;

    public int BlockSize => _blockSize;

    public int[] GetRoutingProcessingOrder()
    {
        var snapshot = Volatile.Read(ref _routingSnapshot);
        if (snapshot is null)
        {
            return Array.Empty<int>();
        }

        var order = snapshot.ProcessingOrder;
        var copy = new int[order.Length];
        Array.Copy(order, copy, order.Length);
        return copy;
    }

    /// <summary>
    /// Gets the analysis tap for attaching visualizers.
    /// </summary>
    public AnalysisTap AnalysisTap => _analysisTap;

    /// <summary>
    /// K-weighted LUFS meter for the master left output channel.
    /// </summary>
    public LufsMeterProcessor MasterLufsLeft => _masterLufsLeft;

    /// <summary>
    /// K-weighted LUFS meter for the master right output channel.
    /// </summary>
    public LufsMeterProcessor MasterLufsRight => _masterLufsRight;

    public void ConfigureOutputDevices(string outputId, string? monitorOutputId)
    {
        _recoveryManager.ConfigureOutputDevices(outputId, monitorOutputId);
    }

    public string ConfigureChannelInput(int channelId, string deviceId, InputChannelMode channelMode)
    {
        if ((uint)channelId >= (uint)_channels.Length)
        {
            return string.Empty;
        }

        string resolved = _inputCaptureManager.ConfigureChannelInput(
            channelId,
            deviceId,
            channelMode,
            Volatile.Read(ref _outputActive) == 1);

        RebuildRoutingSnapshot(Volatile.Read(ref _channels));
        return resolved;
    }

    public void EnsureChannelCount(int channelCount)
    {
        if (channelCount < 1)
        {
            channelCount = 1;
        }

        var current = Volatile.Read(ref _channels);
        if (current.Length == channelCount)
        {
            return;
        }

        var next = new ChannelStrip[channelCount];
        int copyCount = Math.Min(current.Length, channelCount);
        Array.Copy(current, next, copyCount);
        for (int i = copyCount; i < channelCount; i++)
        {
            next[i] = new ChannelStrip(i, _sampleRate, _blockSize);
        }

        if (channelCount < current.Length)
        {
            for (int i = channelCount; i < current.Length; i++)
            {
                var slots = current[i].PluginChain.DetachAll();
                for (int j = 0; j < slots.Length; j++)
                {
                    if (slots[j] is { } slot)
                    {
                        QueuePluginDisposal(slot.Plugin);
                    }
                }
            }
        }

        Interlocked.Exchange(ref _channels, next);
        _inputCaptureManager.RemoveCapturesAbove(channelCount);
        RebuildRoutingSnapshot(next);
    }

    public void RebuildRoutingGraph()
    {
        var channels = Volatile.Read(ref _channels);
        RebuildRoutingSnapshot(channels);
    }

    /// <summary>
    /// Suspends input capture and processing while a preset load is applied.
    /// </summary>
    public void BeginPresetLoad()
    {
        if (Interlocked.Increment(ref _presetLoadDepth) != 1)
        {
            return;
        }

        _processingEnabledBeforePresetLoad = Volatile.Read(ref _processingEnabled);
        SetProcessingEnabled(false);
        _inputCaptureManager.StopAll();
        _inputCaptureManager.ClearBuffers();
        _monitorBuffer?.Clear();
        _outputPipeline?.ResetSampleClock();
    }

    /// <summary>
    /// Resumes input capture and processing after a preset load completes.
    /// </summary>
    public void EndPresetLoad()
    {
        if (Interlocked.Decrement(ref _presetLoadDepth) != 0)
        {
            return;
        }

        _inputCaptureManager.ClearBuffers();
        if (_processingEnabledBeforePresetLoad != 0)
        {
            SetProcessingEnabled(true);
        }

        if (Volatile.Read(ref _outputActive) == 1)
        {
            using var enumerator = new MMDeviceEnumerator();
            _inputCaptureManager.StartAll(enumerator);
        }
    }

    public void SetMasterMute(bool muted)
    {
        _outputPipeline?.SetMasterMute(muted);
    }

    public void SetProcessingEnabled(bool enabled)
    {
        Volatile.Write(ref _processingEnabled, enabled ? 1 : 0);
    }

    public void Start()
    {
        if (string.IsNullOrWhiteSpace(_recoveryManager.OutputId))
        {
            throw new InvalidOperationException("Output device is not configured.");
        }

        using var enumerator = new MMDeviceEnumerator();

        var outputDevice = enumerator.GetDevice(_recoveryManager.OutputId);
        var outputFormat = WaveFormat.CreateIeeeFloatWaveFormat(_sampleRate, 2);

        var channels = Volatile.Read(ref _channels);
        if (channels.Length == 0)
        {
            EnsureChannelCount(1);
            channels = Volatile.Read(ref _channels);
        }

        if (_routingSnapshot is null)
        {
            RebuildRoutingSnapshot(channels);
        }

        var snapshot = _routingSnapshot ?? _routingScheduler.BuildSnapshot(channels, _inputCaptureManager.GetInputSources(channels.Length));
        _outputPipeline = new OutputPipeline(
            snapshot,
            _parameterQueue,
            _blockSize,
            ReportOutputCallback,
            ReportOutputUnderflow,
            () => Volatile.Read(ref _processingEnabled) != 0,
            _masterLufsLeft,
            _masterLufsRight,
            _analysisTap,
            outputFormat);

        _output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, true, _latencyMs);
        _output.Init(_outputPipeline);
        _output.PlaybackStopped += OnOutputStopped;
        _output.Play();
        Interlocked.Exchange(ref _outputActive, 1);

        if (!string.IsNullOrWhiteSpace(_recoveryManager.MonitorOutputId))
        {
            _monitorBuffer = new LockFreeRingBuffer(_blockSize * 32 * 2);
            var monitorDevice = enumerator.GetDevice(_recoveryManager.MonitorOutputId);
            _monitorOutput = new WasapiOut(monitorDevice, AudioClientShareMode.Shared, true, _latencyMs);
            _monitorOutput.Init(new MonitorWaveProvider(_monitorBuffer, outputFormat, _blockSize));
            _outputPipeline.SetMonitorBuffer(_monitorBuffer);
            _monitorOutput.PlaybackStopped += OnMonitorOutputStopped;
            _monitorOutput.Play();
            Interlocked.Exchange(ref _monitorActive, 1);
        }

        _inputCaptureManager.StartAll(enumerator);
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _isStopping, 1) == 1)
        {
            return;
        }

        _inputCaptureManager.StopAll();

        if (_output is not null)
        {
            _output.PlaybackStopped -= OnOutputStopped;
        }
        _output?.Stop();
        _output?.Dispose();
        _output = null;
        _outputPipeline = null;
        Interlocked.Exchange(ref _outputActive, 0);

        if (_monitorOutput is not null)
        {
            _monitorOutput.PlaybackStopped -= OnMonitorOutputStopped;
        }
        _monitorOutput?.Stop();
        _monitorOutput?.Dispose();
        _monitorOutput = null;
        Interlocked.Exchange(ref _monitorActive, 0);

        _inputCaptureManager.ClearBuffers();
        _monitorBuffer?.Clear();
        _monitorBuffer = null;

        Interlocked.Exchange(ref _isStopping, 0);
        DrainPendingPluginDisposals(force: true);
    }

    public void EnqueueParameterChange(ParameterChange change)
    {
        _parameterQueue.Enqueue(change);
    }

    public void QueuePluginDisposal(IPlugin plugin)
    {
        long targetCallback = Interlocked.Read(ref _outputCallbackCount) + 1;
        _pluginDisposalQueue.Queue(plugin, targetCallback, Volatile.Read(ref _outputActive) == 1);
    }

    public void DrainPendingPluginDisposals()
    {
        DrainPendingPluginDisposals(force: false);
    }

    public bool IsVbCableInstalled()
    {
        return _deviceManager.FindVBCableOutput() is not null;
    }

    public AudioEngineDiagnosticsSnapshot GetDiagnosticsSnapshot()
    {
        return _diagnosticsCollector.Build(
            outputActive: Volatile.Read(ref _outputActive) == 1,
            monitorActive: Volatile.Read(ref _monitorActive) == 1,
            isRecovering: _recoveryManager.IsRecovering,
            lastOutputCallbackTicks: Volatile.Read(ref _lastOutputCallbackTicks),
            outputCallbackCount: Interlocked.Read(ref _outputCallbackCount),
            lastOutputFrames: Volatile.Read(ref _lastOutputFrames),
            monitorBufferedSamples: _monitorBuffer?.AvailableRead ?? 0,
            monitorBufferCapacity: _monitorBuffer?.Capacity ?? 0,
            outputUnderflowSamples: Interlocked.Read(ref _outputUnderflowSamples));
    }

    public void Dispose()
    {
        _recoveryManager.Cancel();
        Stop();
        DrainPendingPluginDisposals(force: true);
        DisposeAllPlugins();
        _inputCaptureManager.Dispose();
    }

    private void DisposeAllPlugins()
    {
        var channels = Volatile.Read(ref _channels);
        for (int i = 0; i < channels.Length; i++)
        {
            var slots = channels[i].PluginChain.DetachAll();
            for (int j = 0; j < slots.Length; j++)
            {
                slots[j]?.Plugin.Dispose();
            }
        }
    }

    private void DrainPendingPluginDisposals(bool force)
    {
        long callbackCount = Interlocked.Read(ref _outputCallbackCount);
        _pluginDisposalQueue.Drain(callbackCount, force);
    }

    private void RebuildRoutingSnapshot(ChannelStrip[] channels)
    {
        var inputSources = _inputCaptureManager.GetInputSources(channels.Length);
        var snapshot = _routingScheduler.BuildSnapshot(channels, inputSources);
        Volatile.Write(ref _routingSnapshot, snapshot);
        _outputPipeline?.UpdateSnapshot(snapshot);
    }

    private void OnOutputStopped(object? sender, StoppedEventArgs e)
    {
        if (ShouldIgnoreStopEvent())
        {
            return;
        }

        Interlocked.Exchange(ref _outputActive, 0);
        if (DeviceErrorHelper.IsDeviceInvalidated(e.Exception))
        {
            HandleDeviceInvalidated(_recoveryManager.OutputId, "Output device disconnected.");
        }
    }

    private void OnMonitorOutputStopped(object? sender, StoppedEventArgs e)
    {
        if (ShouldIgnoreStopEvent())
        {
            return;
        }

        Interlocked.Exchange(ref _monitorActive, 0);
        if (DeviceErrorHelper.IsDeviceInvalidated(e.Exception))
        {
            HandleDeviceInvalidated(_recoveryManager.MonitorOutputId, "Monitor device disconnected.");
        }
    }

    private void ReportOutputCallback(int frames)
    {
        Interlocked.Exchange(ref _lastOutputFrames, frames);
        Interlocked.Exchange(ref _lastOutputCallbackTicks, Stopwatch.GetTimestamp());
        Interlocked.Increment(ref _outputCallbackCount);
    }

    private void ReportOutputUnderflow(int missingFrames)
    {
        if (missingFrames <= 0)
        {
            return;
        }

        Interlocked.Add(ref _outputUnderflowSamples, missingFrames);
    }

    private bool ShouldIgnoreStopEvent()
    {
        return Volatile.Read(ref _isStopping) == 1;
    }

    private void HandleDeviceInvalidated(string deviceId, string message)
    {
        _recoveryManager.HandleDeviceInvalidated(deviceId, message);
    }

    private void RaiseDeviceDisconnected(string deviceId, string message)
    {
        DeviceDisconnected?.Invoke(this, new DeviceDisconnectedEventArgs(deviceId, message));
    }

    private void RaiseDeviceRecovered(string[] inputDeviceIds, string outputId, string monitorOutputId)
    {
        DeviceRecovered?.Invoke(this, new DeviceRecoveredEventArgs(inputDeviceIds, outputId, monitorOutputId));
    }
}
