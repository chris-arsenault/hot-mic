using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using HotMic.Common.Configuration;
using HotMic.Core.Analysis;
using HotMic.Core.Metering;
using HotMic.Core.Plugins;
using HotMic.Core.Plugins.BuiltIn;
using HotMic.Core.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace HotMic.Core.Engine;

public sealed class AudioEngine : IDisposable
{
    private const int AudclntEDeviceInvalidated = unchecked((int)0x88890004);

    private readonly DeviceManager _deviceManager = new();
    private readonly LockFreeQueue<ParameterChange> _parameterQueue = new();
    private readonly List<PendingPluginDisposal> _pendingPluginDisposals = new();
    private readonly List<IPlugin> _disposeBuffer = new();
    private readonly object _pendingPluginDisposalsLock = new();
    private readonly List<InputCapture> _inputCaptures = new();
    private readonly object _inputCaptureLock = new();
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
    private OutputMixProvider? _outputProvider;

    private string _outputId = string.Empty;
    private string _monitorOutputId = string.Empty;
    private CancellationTokenSource? _recoveryCts;
    private int _isRecovering;
    private int _isStopping;

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

        ConfigureOutputDevices(settings.OutputDeviceId, settings.MonitorOutputDeviceId);
        EnsureChannelCount(Math.Max(1, channelCount));
    }

    public IReadOnlyList<ChannelStrip> Channels => _channels;

    public int SampleRate => _sampleRate;

    public int BlockSize => _blockSize;

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
        _outputId = outputId;
        _monitorOutputId = monitorOutputId ?? string.Empty;
    }

    public string ConfigureChannelInput(int channelId, string deviceId, InputChannelMode channelMode)
    {
        if ((uint)channelId >= (uint)_channels.Length)
        {
            return string.Empty;
        }

        string resolvedDeviceId = deviceId ?? string.Empty;

        lock (_inputCaptureLock)
        {
            for (int i = 0; i < _inputCaptures.Count; i++)
            {
                var capture = _inputCaptures[i];
                if (capture.ChannelId != channelId && capture.DeviceId == resolvedDeviceId)
                {
                    resolvedDeviceId = string.Empty;
                    break;
                }
            }

            InputCapture? existing = null;
            for (int i = 0; i < _inputCaptures.Count; i++)
            {
                if (_inputCaptures[i].ChannelId == channelId)
                {
                    existing = _inputCaptures[i];
                    break;
                }
            }
            if (string.IsNullOrWhiteSpace(resolvedDeviceId))
            {
                if (existing is not null)
                {
                    existing.Stop();
                    existing.Dispose();
                    _inputCaptures.Remove(existing);
                }
            }
            else
            {
                if (existing is null)
                {
                    existing = new InputCapture(this, channelId, _blockSize);
                    _inputCaptures.Add(existing);
                }

                existing.Configure(resolvedDeviceId, channelMode);
                if (Volatile.Read(ref _outputActive) == 1)
                {
                    using var enumerator = new MMDeviceEnumerator();
                    existing.Start(enumerator, _sampleRate);
                }
            }

            UpdateRoutingInputSources();
        }

        return resolvedDeviceId;
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

        lock (_inputCaptureLock)
        {
            for (int i = _inputCaptures.Count - 1; i >= 0; i--)
            {
                if (_inputCaptures[i].ChannelId >= channelCount)
                {
                    _inputCaptures[i].Stop();
                    _inputCaptures[i].Dispose();
                    _inputCaptures.RemoveAt(i);
                }
            }
        }

        RebuildRoutingSnapshot(next, BuildProcessingOrder(next));
    }

    public void RebuildRoutingGraph()
    {
        var channels = Volatile.Read(ref _channels);
        RebuildRoutingSnapshot(channels, BuildProcessingOrder(channels));
    }

    private void RebuildRoutingSnapshot(ChannelStrip[] channels, int[] processingOrder)
    {
        if (processingOrder.Length != channels.Length)
        {
            processingOrder = BuildDefaultOrder(channels.Length);
        }

        var snapshot = new RoutingSnapshot(channels, processingOrder, _sampleRate, _blockSize);
        lock (_inputCaptureLock)
        {
            for (int i = 0; i < channels.Length; i++)
            {
                snapshot.Routing.SetInputSource(i, null);
            }

            for (int i = 0; i < _inputCaptures.Count; i++)
            {
                var capture = _inputCaptures[i];
                if ((uint)capture.ChannelId < (uint)channels.Length)
                {
                    snapshot.Routing.SetInputSource(capture.ChannelId, capture.Source);
                }
            }
        }

        _routingSnapshot = snapshot;
        _outputProvider?.UpdateSnapshot(snapshot);
    }

    private void UpdateRoutingInputSources()
    {
        var snapshot = _routingSnapshot;
        if (snapshot is null)
        {
            return;
        }

        for (int i = 0; i < snapshot.Channels.Length; i++)
        {
            snapshot.Routing.SetInputSource(i, null);
        }

        for (int i = 0; i < _inputCaptures.Count; i++)
        {
            var capture = _inputCaptures[i];
            if ((uint)capture.ChannelId < (uint)snapshot.Channels.Length)
            {
                snapshot.Routing.SetInputSource(capture.ChannelId, capture.Source);
            }
        }
    }

    private int[] BuildProcessingOrder(ChannelStrip[] channels)
    {
        int count = channels.Length;
        if (count == 0)
        {
            return Array.Empty<int>();
        }

        var edges = new bool[count, count];
        var indegree = new int[count];

        int maxDependencies = 0;
        for (int i = 0; i < count; i++)
        {
            var slots = channels[i].PluginChain.GetSnapshot();
            for (int s = 0; s < slots.Length; s++)
            {
                var slot = slots[s];
                if (slot?.Plugin is IRoutingDependencyProvider provider)
                {
                    if (provider.MaxRoutingDependencies > maxDependencies)
                    {
                        maxDependencies = provider.MaxRoutingDependencies;
                    }
                }
            }
        }

        var dependencyBuffer = maxDependencies > 0
            ? new RoutingDependency[maxDependencies]
            : Array.Empty<RoutingDependency>();

        for (int i = 0; i < count; i++)
        {
            var slots = channels[i].PluginChain.GetSnapshot();
            for (int s = 0; s < slots.Length; s++)
            {
                var slot = slots[s];
                if (slot is null)
                {
                    continue;
                }

                if (slot.Plugin is IRoutingDependencyProvider provider)
                {
                    int maxDeps = provider.MaxRoutingDependencies;
                    if (maxDeps <= 0)
                    {
                        continue;
                    }

                    if (maxDeps > dependencyBuffer.Length)
                    {
                        maxDeps = dependencyBuffer.Length;
                    }
                    if (maxDeps == 0)
                    {
                        continue;
                    }

                    int channelId = i + 1;
                    int depCount = provider.GetRoutingDependencies(channelId, dependencyBuffer.AsSpan(0, maxDeps));
                    if (depCount > maxDeps)
                    {
                        depCount = maxDeps;
                    }

                    for (int d = 0; d < depCount; d++)
                    {
                        var dependency = dependencyBuffer[d];
                        AddEdge(edges, indegree, dependency.SourceChannelId - 1, dependency.TargetChannelId - 1);
                    }
                }
            }
        }

        var queue = new Queue<int>(count);
        for (int i = 0; i < count; i++)
        {
            if (indegree[i] == 0)
            {
                queue.Enqueue(i);
            }
        }

        var order = new int[count];
        int index = 0;
        while (queue.Count > 0)
        {
            int node = queue.Dequeue();
            order[index++] = node;
            for (int j = 0; j < count; j++)
            {
                if (!edges[node, j])
                {
                    continue;
                }

                indegree[j]--;
                if (indegree[j] == 0)
                {
                    queue.Enqueue(j);
                }
            }
        }

        if (index != count)
        {
            return BuildDefaultOrder(count);
        }

        return order;
    }

    private static int[] BuildDefaultOrder(int channelCount)
    {
        var order = new int[channelCount];
        for (int i = 0; i < channelCount; i++)
        {
            order[i] = i;
        }
        return order;
    }

    private static void AddEdge(bool[,] edges, int[] indegree, int sourceIndex, int targetIndex)
    {
        int count = indegree.Length;
        if ((uint)sourceIndex >= (uint)count || (uint)targetIndex >= (uint)count || sourceIndex == targetIndex)
        {
            return;
        }

        if (!edges[sourceIndex, targetIndex])
        {
            edges[sourceIndex, targetIndex] = true;
            indegree[targetIndex]++;
        }
    }

    private void StartInputCaptures(MMDeviceEnumerator enumerator)
    {
        lock (_inputCaptureLock)
        {
            for (int i = 0; i < _inputCaptures.Count; i++)
            {
                _inputCaptures[i].Start(enumerator, _sampleRate);
            }
        }
    }

    private void StopInputCaptures()
    {
        lock (_inputCaptureLock)
        {
            for (int i = 0; i < _inputCaptures.Count; i++)
            {
                _inputCaptures[i].Stop();
            }
        }
    }

    private void ClearInputBuffers()
    {
        lock (_inputCaptureLock)
        {
            for (int i = 0; i < _inputCaptures.Count; i++)
            {
                _inputCaptures[i].Source.Buffer.Clear();
            }
        }
    }

    public void SetMasterMute(bool muted)
    {
        _outputProvider?.SetMasterMute(muted);
    }

    public void SetProcessingEnabled(bool enabled)
    {
        Volatile.Write(ref _processingEnabled, enabled ? 1 : 0);
    }

    public void Start()
    {
        if (string.IsNullOrWhiteSpace(_outputId))
        {
            throw new InvalidOperationException("Output device is not configured.");
        }

        using var enumerator = new MMDeviceEnumerator();

        var outputDevice = enumerator.GetDevice(_outputId);
        var outputFormat = WaveFormat.CreateIeeeFloatWaveFormat(_sampleRate, 2);

        var channels = Volatile.Read(ref _channels);
        if (channels.Length == 0)
        {
            EnsureChannelCount(1);
            channels = Volatile.Read(ref _channels);
        }

        if (_routingSnapshot is null)
        {
            RebuildRoutingSnapshot(channels, BuildProcessingOrder(channels));
        }

        var snapshot = _routingSnapshot ?? new RoutingSnapshot(channels, BuildDefaultOrder(channels.Length), _sampleRate, _blockSize);
        _outputProvider = new OutputMixProvider(
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
        _output.Init(_outputProvider);
        _output.PlaybackStopped += OnOutputStopped;
        _output.Play();
        Interlocked.Exchange(ref _outputActive, 1);

        if (!string.IsNullOrWhiteSpace(_monitorOutputId))
        {
            _monitorBuffer = new LockFreeRingBuffer(_blockSize * 32 * 2);
            var monitorDevice = enumerator.GetDevice(_monitorOutputId);
            _monitorOutput = new WasapiOut(monitorDevice, AudioClientShareMode.Shared, true, _latencyMs);
            _monitorOutput.Init(new MonitorWaveProvider(_monitorBuffer, outputFormat, _blockSize));
            _outputProvider.SetMonitorBuffer(_monitorBuffer);
            _monitorOutput.PlaybackStopped += OnMonitorOutputStopped;
            _monitorOutput.Play();
            Interlocked.Exchange(ref _monitorActive, 1);
        }
        StartInputCaptures(enumerator);
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _isStopping, 1) == 1)
        {
            return;
        }

        StopInputCaptures();

        if (_output is not null)
        {
            _output.PlaybackStopped -= OnOutputStopped;
        }
        _output?.Stop();
        _output?.Dispose();
        _output = null;
        Interlocked.Exchange(ref _outputActive, 0);

        if (_monitorOutput is not null)
        {
            _monitorOutput.PlaybackStopped -= OnMonitorOutputStopped;
        }
        _monitorOutput?.Stop();
        _monitorOutput?.Dispose();
        _monitorOutput = null;
        Interlocked.Exchange(ref _monitorActive, 0);

        ClearInputBuffers();
        _monitorBuffer?.Clear();

        Interlocked.Exchange(ref _isStopping, 0);
        DrainPendingPluginDisposals(force: true);
    }

    public void EnqueueParameterChange(ParameterChange change)
    {
        _parameterQueue.Enqueue(change);
    }

    public void QueuePluginDisposal(IPlugin plugin)
    {
        if (Volatile.Read(ref _outputActive) == 0)
        {
            plugin.Dispose();
            return;
        }

        long targetCallback = Interlocked.Read(ref _outputCallbackCount) + 1;
        lock (_pendingPluginDisposalsLock)
        {
            _pendingPluginDisposals.Add(new PendingPluginDisposal(plugin, targetCallback));
        }
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
        var inputs = new List<InputDiagnosticsSnapshot>();
        lock (_inputCaptureLock)
        {
            for (int i = 0; i < _inputCaptures.Count; i++)
            {
                var capture = _inputCaptures[i];
                inputs.Add(new InputDiagnosticsSnapshot(
                    channelId: capture.ChannelId,
                    deviceId: capture.DeviceId,
                    isActive: capture.IsActive,
                    lastCallbackTicks: capture.LastCallbackTicks,
                    callbackCount: capture.CallbackCount,
                    lastFrames: capture.LastFrames,
                    bufferedSamples: capture.Source.BufferedSamples,
                    bufferCapacity: capture.Source.Capacity,
                    channels: capture.Channels,
                    sampleRate: capture.SampleRate,
                    droppedSamples: capture.DroppedSamples,
                    underflowSamples: capture.Source.UnderflowSamples));
            }
        }

        return new AudioEngineDiagnosticsSnapshot(
            outputActive: Volatile.Read(ref _outputActive) == 1,
            monitorActive: Volatile.Read(ref _monitorActive) == 1,
            isRecovering: Volatile.Read(ref _isRecovering) == 1,
            lastOutputCallbackTicks: Volatile.Read(ref _lastOutputCallbackTicks),
            outputCallbackCount: Interlocked.Read(ref _outputCallbackCount),
            lastOutputFrames: Volatile.Read(ref _lastOutputFrames),
            monitorBufferedSamples: _monitorBuffer?.AvailableRead ?? 0,
            monitorBufferCapacity: _monitorBuffer?.Capacity ?? 0,
            outputUnderflowSamples: Interlocked.Read(ref _outputUnderflowSamples),
            inputs: inputs.ToArray());
    }

    public void Dispose()
    {
        CancelRecovery();
        Stop();
        DrainPendingPluginDisposals(force: true);
        DisposeAllPlugins();
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
        lock (_pendingPluginDisposalsLock)
        {
            if (_pendingPluginDisposals.Count == 0)
            {
                return;
            }

            long callbackCount = force ? long.MaxValue : Interlocked.Read(ref _outputCallbackCount);
            for (int i = _pendingPluginDisposals.Count - 1; i >= 0; i--)
            {
                var pending = _pendingPluginDisposals[i];
                if (callbackCount >= pending.TargetCallbackCount)
                {
                    _disposeBuffer.Add(pending.Plugin);
                    _pendingPluginDisposals.RemoveAt(i);
                }
            }
        }

        for (int i = 0; i < _disposeBuffer.Count; i++)
        {
            _disposeBuffer[i].Dispose();
        }
        _disposeBuffer.Clear();
    }

    private WasapiCapture? CreateCapture(MMDeviceEnumerator enumerator, string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return null;
        }

        try
        {
            var device = enumerator.GetDevice(deviceId);
            int channels = GetPreferredInputChannels(device);
            var capture = new WasapiCapture(device)
            {
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_sampleRate, channels)
            };

            return capture;
        }
        catch
        {
            return null;
        }
    }

    private static int GetPreferredInputChannels(MMDevice device)
    {
        try
        {
            int channels = device.AudioClient.MixFormat.Channels;
            return channels >= 2 ? 2 : 1;
        }
        catch
        {
            return 1;
        }
    }


    private void OnOutputStopped(object? sender, StoppedEventArgs e)
    {
        if (ShouldIgnoreStopEvent())
        {
            return;
        }

        Interlocked.Exchange(ref _outputActive, 0);
        if (IsDeviceInvalidated(e.Exception))
        {
            HandleDeviceInvalidated(_outputId, "Output device disconnected.");
        }
    }

    private void OnMonitorOutputStopped(object? sender, StoppedEventArgs e)
    {
        if (ShouldIgnoreStopEvent())
        {
            return;
        }

        Interlocked.Exchange(ref _monitorActive, 0);
        if (IsDeviceInvalidated(e.Exception))
        {
            HandleDeviceInvalidated(_monitorOutputId, "Monitor device disconnected.");
        }
    }

    private static void WriteInputBuffer(
        LockFreeRingBuffer buffer,
        ReadOnlySpan<byte> data,
        int channels,
        int blockAlign,
        float[] mixBuffer,
        int channelMode,
        ref long droppedSamples)
    {
        if (channels <= 1)
        {
            var floatSpan = MemoryMarshal.Cast<byte, float>(data);
            int written = buffer.Write(floatSpan);
            int dropped = floatSpan.Length - written;
            if (dropped > 0)
            {
                Interlocked.Add(ref droppedSamples, dropped);
            }
            return;
        }

        if (blockAlign <= 0)
        {
            return;
        }

        int frames = data.Length / blockAlign;
        if (frames <= 0)
        {
            return;
        }

        var inputSamples = MemoryMarshal.Cast<byte, float>(data);
        int remainingFrames = frames;
        int sourceIndex = 0;

        while (remainingFrames > 0)
        {
            int chunk = Math.Min(remainingFrames, mixBuffer.Length);
            if (channelMode == (int)InputChannelMode.Left || channelMode == (int)InputChannelMode.Right)
            {
                int channelIndex = channelMode == (int)InputChannelMode.Left ? 0 : Math.Min(1, channels - 1);
                for (int i = 0; i < chunk; i++)
                {
                    int baseIndex = (sourceIndex + i) * channels;
                    mixBuffer[i] = inputSamples[baseIndex + channelIndex];
                }
            }
            else
            {
                for (int i = 0; i < chunk; i++)
                {
                    float sum = 0f;
                    int baseIndex = (sourceIndex + i) * channels;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        sum += inputSamples[baseIndex + ch];
                    }

                    mixBuffer[i] = sum / channels;
                }
            }

            int written = buffer.Write(mixBuffer.AsSpan(0, chunk));
            int dropped = chunk - written;
            if (dropped > 0)
            {
                Interlocked.Add(ref droppedSamples, dropped);
            }

            sourceIndex += chunk;
            remainingFrames -= chunk;
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

    private static bool IsDeviceInvalidated(Exception? exception)
    {
        return exception is COMException comException && comException.HResult == AudclntEDeviceInvalidated;
    }

    private void HandleDeviceInvalidated(string deviceId, string message)
    {
        if (Interlocked.CompareExchange(ref _isRecovering, 1, 0) != 0)
        {
            return;
        }

        Stop();
        DeviceDisconnected?.Invoke(this, new DeviceDisconnectedEventArgs(deviceId, message));
        StartRecoveryLoop();
    }

    private void StartRecoveryLoop()
    {
        _recoveryCts?.Cancel();
        _recoveryCts?.Dispose();
        _recoveryCts = new CancellationTokenSource();
        var token = _recoveryCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                if (TryRecoverDevices())
                {
                    try
                    {
                        Start();
                        DeviceRecovered?.Invoke(this, new DeviceRecoveredEventArgs(GetInputDeviceIds(), _outputId, _monitorOutputId));
                        Interlocked.Exchange(ref _isRecovering, 0);
                        return;
                    }
                    catch
                    {
                    }
                }

                await Task.Delay(1000, token).ConfigureAwait(false);
            }

            Interlocked.Exchange(ref _isRecovering, 0);
        }, token);
    }

    private string[] GetInputDeviceIds()
    {
        lock (_inputCaptureLock)
        {
            var ids = new string[_inputCaptures.Count];
            for (int i = 0; i < _inputCaptures.Count; i++)
            {
                ids[i] = _inputCaptures[i].DeviceId;
            }
            return ids;
        }
    }

    private bool TryRecoverDevices()
    {
        using var enumerator = new MMDeviceEnumerator();

        if (!TryResolveOutputDevice(enumerator))
        {
            return false;
        }

        _monitorOutputId = ResolveMonitorDevice(enumerator, _monitorOutputId);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        lock (_inputCaptureLock)
        {
            for (int i = 0; i < _inputCaptures.Count; i++)
            {
                var capture = _inputCaptures[i];
                string resolved = ResolveInputDevice(enumerator, capture.DeviceId);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    if (!used.Add(resolved))
                    {
                        resolved = string.Empty;
                    }
                }

                capture.Configure(resolved, capture.ChannelMode);
            }
        }
        return true;
    }

    private bool TryResolveOutputDevice(MMDeviceEnumerator enumerator)
    {
        if (IsDeviceActive(enumerator, _outputId))
        {
            return true;
        }

        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        foreach (var device in devices)
        {
            if (device.FriendlyName.Contains("VB-Cable", StringComparison.OrdinalIgnoreCase))
            {
                _outputId = device.ID;
                return true;
            }
        }

        return false;
    }

    private static string ResolveInputDevice(MMDeviceEnumerator enumerator, string currentId)
    {
        if (IsDeviceActive(enumerator, currentId))
        {
            return currentId;
        }

        try
        {
            var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            return defaultDevice.ID;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ResolveMonitorDevice(MMDeviceEnumerator enumerator, string currentId)
    {
        if (IsDeviceActive(enumerator, currentId))
        {
            return currentId;
        }

        try
        {
            var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            return defaultDevice.ID;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsDeviceActive(MMDeviceEnumerator enumerator, string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return false;
        }

        try
        {
            var device = enumerator.GetDevice(deviceId);
            return device.State == DeviceState.Active;
        }
        catch
        {
            return false;
        }
    }

    private void CancelRecovery()
    {
        _recoveryCts?.Cancel();
        _recoveryCts?.Dispose();
        _recoveryCts = null;
        Interlocked.Exchange(ref _isRecovering, 0);
    }

    private sealed class PendingPluginDisposal
    {
        public PendingPluginDisposal(IPlugin plugin, long targetCallbackCount)
        {
            Plugin = plugin;
            TargetCallbackCount = targetCallbackCount;
        }

        public IPlugin Plugin { get; }
        public long TargetCallbackCount { get; }
    }

    private sealed class InputCapture : IDisposable
    {
        private readonly AudioEngine _engine;
        private readonly int _channelId;
        private readonly InputSource _source;
        private WasapiCapture? _capture;
        private string _deviceId = string.Empty;
        private int _channels = 1;
        private int _sampleRate;
        private int _blockAlign;
        private float[] _mixBuffer = Array.Empty<float>();
        private int _channelMode;
        private long _droppedSamples;
        private long _lastCallbackTicks;
        private long _callbackCount;
        private int _lastFrames;
        private int _active;

        public InputCapture(AudioEngine engine, int channelId, int blockSize)
        {
            _engine = engine;
            _channelId = channelId;
            _source = new InputSource(blockSize * 32);
        }

        public int ChannelId => _channelId;

        public InputSource Source => _source;

        public string DeviceId => _deviceId;

        public bool IsActive => Volatile.Read(ref _active) == 1;

        public long LastCallbackTicks => Volatile.Read(ref _lastCallbackTicks);

        public long CallbackCount => Interlocked.Read(ref _callbackCount);

        public int LastFrames => Volatile.Read(ref _lastFrames);

        public int Channels => Volatile.Read(ref _channels);

        public int SampleRate => Volatile.Read(ref _sampleRate);

        public long DroppedSamples => Interlocked.Read(ref _droppedSamples);

        public InputChannelMode ChannelMode => (InputChannelMode)Volatile.Read(ref _channelMode);

        public void Configure(string deviceId, InputChannelMode channelMode)
        {
            _deviceId = deviceId ?? string.Empty;
            Volatile.Write(ref _channelMode, (int)channelMode);
        }

        public void Start(MMDeviceEnumerator enumerator, int sampleRate)
        {
            Stop();

            if (string.IsNullOrWhiteSpace(_deviceId))
            {
                return;
            }

            _capture = _engine.CreateCapture(enumerator, _deviceId);
            if (_capture is null)
            {
                return;
            }

            CacheInputFormat(_capture, sampleRate);
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnStopped;

            try
            {
                _capture.StartRecording();
                CacheInputFormat(_capture, sampleRate);
                Volatile.Write(ref _active, 1);
            }
            catch
            {
                CleanupCapture();
                Volatile.Write(ref _active, 0);
            }
        }

        public void Stop()
        {
            if (_capture is null)
            {
                Volatile.Write(ref _active, 0);
                return;
            }

            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnStopped;
            _capture.StopRecording();
            CleanupCapture();
            Volatile.Write(ref _active, 0);
        }

        public void Dispose()
        {
            Stop();
        }

        private void CleanupCapture()
        {
            _capture?.Dispose();
            _capture = null;
        }

        private void CacheInputFormat(WasapiCapture capture, int sampleRate)
        {
            int channels = capture.WaveFormat.Channels;
            int blockAlign = capture.WaveFormat.BlockAlign;
            int mixBufferSize = Math.Max(sampleRate, _engine._blockSize * 8);
            if (mixBufferSize < 1)
            {
                mixBufferSize = _engine._blockSize * 8;
            }

            Volatile.Write(ref _channels, channels);
            Volatile.Write(ref _sampleRate, capture.WaveFormat.SampleRate);
            _blockAlign = blockAlign;
            if (_mixBuffer.Length != mixBufferSize)
            {
                _mixBuffer = new float[mixBufferSize];
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            int blockAlign = _blockAlign;
            int frames = blockAlign > 0 ? e.BytesRecorded / blockAlign : e.BytesRecorded / sizeof(float);
            Volatile.Write(ref _lastFrames, frames);
            Volatile.Write(ref _lastCallbackTicks, Stopwatch.GetTimestamp());
            Interlocked.Increment(ref _callbackCount);

            WriteInputBuffer(
                _source.Buffer,
                e.Buffer.AsSpan(0, e.BytesRecorded),
                Volatile.Read(ref _channels),
                blockAlign,
                _mixBuffer,
                Volatile.Read(ref _channelMode),
                ref _droppedSamples);
        }

        private void OnStopped(object? sender, StoppedEventArgs e)
        {
            if (_engine.ShouldIgnoreStopEvent())
            {
                return;
            }

            Volatile.Write(ref _active, 0);
            if (AudioEngine.IsDeviceInvalidated(e.Exception))
            {
                _engine.HandleDeviceInvalidated(_deviceId, "Input device disconnected.");
            }
        }
    }

    private sealed class RoutingSnapshot
    {
        public RoutingSnapshot(ChannelStrip[] channels, int[] processingOrder, int sampleRate, int blockSize)
        {
            Channels = channels;
            ProcessingOrder = processingOrder;
            Routing = new RoutingContext(Math.Max(1, channels.Length), sampleRate, blockSize);
            Buffers = new float[channels.Length][];
            for (int i = 0; i < channels.Length; i++)
            {
                Buffers[i] = new float[blockSize];
            }
        }

        public ChannelStrip[] Channels { get; }

        public float[][] Buffers { get; }

        public int[] ProcessingOrder { get; }

        public RoutingContext Routing { get; }
    }

    private sealed class OutputMixProvider : IWaveProvider
    {
        private readonly LockFreeQueue<ParameterChange> _parameterQueue;
        private readonly int _blockSize;
        private readonly Action<int> _reportOutput;
        private readonly Action<int> _reportUnderflow;
        private readonly Func<bool> _isProcessingEnabled;
        private readonly LufsMeterProcessor _masterLufsLeft;
        private readonly LufsMeterProcessor _masterLufsRight;
        private readonly AnalysisTap _analysisTap;
        private RoutingSnapshot _snapshot;
        private LockFreeRingBuffer? _monitorBuffer;
        private int _masterMuted;
        private long _sampleClock;

        public OutputMixProvider(
            RoutingSnapshot snapshot,
            LockFreeQueue<ParameterChange> parameterQueue,
            int blockSize,
            Action<int> reportOutput,
            Action<int> reportUnderflow,
            Func<bool> isProcessingEnabled,
            LufsMeterProcessor masterLufsLeft,
            LufsMeterProcessor masterLufsRight,
            AnalysisTap analysisTap,
            WaveFormat waveFormat)
        {
            _snapshot = snapshot;
            _parameterQueue = parameterQueue;
            _blockSize = blockSize;
            _reportOutput = reportOutput;
            _reportUnderflow = reportUnderflow;
            _isProcessingEnabled = isProcessingEnabled;
            _masterLufsLeft = masterLufsLeft;
            _masterLufsRight = masterLufsRight;
            _analysisTap = analysisTap;
            WaveFormat = waveFormat;
        }

        public WaveFormat WaveFormat { get; }

        public void UpdateSnapshot(RoutingSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            ApplyParameterChanges();

            var output = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(offset, count));
            int totalFrames = output.Length / 2;
            int processed = 0;
            _reportOutput(totalFrames);

            if (!_isProcessingEnabled())
            {
                output.Clear();
                return count;
            }

            long sampleClock = _sampleClock;
            while (processed < totalFrames)
            {
                int chunk = Math.Min(_blockSize, totalFrames - processed);
                var snapshot = _snapshot;
                var routing = snapshot.Routing;
                routing.BeginBlock(sampleClock);

                var channels = snapshot.Channels;
                var buffers = snapshot.Buffers;
                bool soloActive = false;
                for (int i = 0; i < channels.Length; i++)
                {
                    if (channels[i].IsSoloed)
                    {
                        soloActive = true;
                        break;
                    }
                }

                for (int i = 0; i < buffers.Length; i++)
                {
                    buffers[i].AsSpan(0, chunk).Clear();
                }

                var order = snapshot.ProcessingOrder;
                for (int i = 0; i < order.Length; i++)
                {
                    int channelIndex = order[i];
                    if ((uint)channelIndex >= (uint)channels.Length)
                    {
                        continue;
                    }

                    var channel = channels[channelIndex];
                    var channelSpan = buffers[channelIndex].AsSpan(0, chunk);
                    int latencySamples = channel.Process(channelSpan, soloActive && !channel.IsSoloed, sampleClock, routing);
                    routing.PublishChannelOutput(channelIndex, buffers[channelIndex], chunk, latencySamples);
                }

                var outputSlice = output.Slice(processed * 2, chunk * 2);
                bool muted = Volatile.Read(ref _masterMuted) != 0;
                var outputBus = routing.OutputBus;

                if (muted || !outputBus.HasData || outputBus.Length < chunk)
                {
                    outputSlice.Clear();
                    if (!muted)
                    {
                        _reportUnderflow(chunk);
                    }
                }
                else
                {
                    var left = outputBus.Left;
                    var right = outputBus.Right;
                    for (int i = 0; i < chunk; i++)
                    {
                        int baseIndex = i * 2;
                        outputSlice[baseIndex] = left[i];
                        outputSlice[baseIndex + 1] = right[i];
                    }
                }

                _masterLufsLeft.ProcessInterleaved(outputSlice, 2, 0);
                _masterLufsRight.ProcessInterleaved(outputSlice, 2, 1);

                if (outputBus.HasData)
                {
                    var analysisBuffer = outputBus.Mode == OutputSendMode.Right ? outputBus.Right : outputBus.Left;
                    _analysisTap.Capture(analysisBuffer, 0);
                }

                _monitorBuffer?.Write(outputSlice);
                processed += chunk;
                sampleClock += chunk;
            }

            _sampleClock = sampleClock;

            return count;
        }

        public void SetMonitorBuffer(LockFreeRingBuffer monitorBuffer)
        {
            _monitorBuffer = monitorBuffer;
        }

        public void SetMasterMute(bool muted)
        {
            Volatile.Write(ref _masterMuted, muted ? 1 : 0);
        }

        private void ApplyParameterChanges()
        {
            var channels = _snapshot.Channels;
            while (_parameterQueue.TryDequeue(out var change))
            {
                if ((uint)change.ChannelId >= (uint)channels.Length)
                {
                    continue;
                }

                var channel = channels[change.ChannelId];
                switch (change.Type)
                {
                    case ParameterType.InputGainDb:
                        channel.SetInputGainDb(change.Value);
                        break;
                    case ParameterType.OutputGainDb:
                        channel.SetOutputGainDb(change.Value);
                        break;
                    case ParameterType.Mute:
                        channel.SetMuted(change.Value >= 0.5f);
                        break;
                    case ParameterType.Solo:
                        channel.SetSoloed(change.Value >= 0.5f);
                        break;
                    case ParameterType.PluginBypass:
                        ApplyPluginBypass(channel, change.PluginInstanceId, change.Value >= 0.5f);
                        break;
                    case ParameterType.PluginParameter:
                        ApplyPluginParameter(channel, change.PluginInstanceId, change.ParameterIndex, change.Value);
                        break;
                    case ParameterType.PluginCommand:
                        ApplyPluginCommand(channel, change.PluginInstanceId, change.Command);
                        break;
                }
            }
        }

        private static void ApplyPluginBypass(ChannelStrip channel, int pluginInstanceId, bool bypass)
        {
            if (channel.PluginChain.TryGetSlotById(pluginInstanceId, out var slot, out _)
                && slot is not null)
            {
                slot.Plugin.IsBypassed = bypass;
            }
        }

        private static void ApplyPluginParameter(ChannelStrip channel, int pluginInstanceId, int parameterIndex, float value)
        {
            if (channel.PluginChain.TryGetSlotById(pluginInstanceId, out var slot, out _)
                && slot is not null)
            {
                slot.Plugin.SetParameter(parameterIndex, value);
            }
        }

        private static void ApplyPluginCommand(ChannelStrip channel, int pluginInstanceId, PluginCommandType command)
        {
            if (!channel.PluginChain.TryGetSlotById(pluginInstanceId, out var slot, out _)
                || slot is null)
            {
                return;
            }

            var plugin = slot.Plugin;
            if (plugin is FFTNoiseRemovalPlugin noise && command == PluginCommandType.LearnNoiseProfile)
            {
                noise.LearnNoiseProfile();
            }
            else if (plugin is FFTNoiseRemovalPlugin noiseToggle && command == PluginCommandType.ToggleNoiseLearn)
            {
                noiseToggle.ToggleLearning();
            }
        }
    }

    private sealed class MonitorWaveProvider : IWaveProvider
    {
        private readonly LockFreeRingBuffer _buffer;
        private readonly int _blockSize;
        private readonly float[] _scratch;

        public MonitorWaveProvider(LockFreeRingBuffer buffer, WaveFormat format, int blockSize)
        {
            _buffer = buffer;
            _blockSize = blockSize;
            WaveFormat = format;
            _scratch = new float[blockSize * 2];
        }

        public WaveFormat WaveFormat { get; }

        public int Read(byte[] buffer, int offset, int count)
        {
            var output = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(offset, count));
            int totalSamples = output.Length;
            int processed = 0;

            while (processed < totalSamples)
            {
                int chunk = Math.Min(_scratch.Length, totalSamples - processed);
                var scratchSpan = _scratch.AsSpan(0, chunk);
                int read = _buffer.Read(scratchSpan);
                if (read < chunk)
                {
                    scratchSpan.Slice(read).Clear();
                }

                scratchSpan.CopyTo(output.Slice(processed, chunk));
                processed += chunk;
            }

            return count;
        }
    }
}
