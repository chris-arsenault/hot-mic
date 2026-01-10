using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using HotMic.Common.Configuration;
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
    private readonly LockFreeRingBuffer _inputBuffer1;
    private readonly LockFreeRingBuffer _inputBuffer2;
    private LockFreeRingBuffer? _monitorBuffer;
    private readonly ChannelStrip[] _channels;
    private readonly int _sampleRate;
    private readonly int _blockSize;
    private readonly int _latencyMs;

    private long _lastOutputCallbackTicks;
    private long _lastInput1CallbackTicks;
    private long _lastInput2CallbackTicks;
    private long _outputCallbackCount;
    private long _input1CallbackCount;
    private long _input2CallbackCount;
    private int _lastOutputFrames;
    private int _lastInput1Frames;
    private int _lastInput2Frames;
    private int _outputActive;
    private int _input1Active;
    private int _input2Active;
    private int _monitorActive;

    private WasapiCapture? _capture1;
    private WasapiCapture? _capture2;
    private WasapiOut? _output;
    private WasapiOut? _monitorOutput;
    private OutputMixProvider? _outputProvider;

    private string _input1Id = string.Empty;
    private string _input2Id = string.Empty;
    private string _outputId = string.Empty;
    private string _monitorOutputId = string.Empty;
    private CancellationTokenSource? _recoveryCts;
    private int _isRecovering;
    private int _isStopping;

    public event EventHandler<DeviceDisconnectedEventArgs>? DeviceDisconnected;
    public event EventHandler<DeviceRecoveredEventArgs>? DeviceRecovered;

    public AudioEngine(AudioSettingsConfig settings)
    {
        _sampleRate = settings.SampleRate;
        _blockSize = settings.BufferSize;
        _latencyMs = Math.Max(1, (int)(1000.0 * _blockSize / _sampleRate));

        _inputBuffer1 = new LockFreeRingBuffer(_blockSize * 8);
        _inputBuffer2 = new LockFreeRingBuffer(_blockSize * 8);
        _channels =
        [
            new ChannelStrip(_sampleRate, _blockSize),
            new ChannelStrip(_sampleRate, _blockSize)
        ];

        ConfigureDevices(settings.InputDevice1Id, settings.InputDevice2Id, settings.OutputDeviceId, settings.MonitorOutputDeviceId);
    }

    public IReadOnlyList<ChannelStrip> Channels => _channels;

    public int SampleRate => _sampleRate;

    public int BlockSize => _blockSize;

    public void ConfigureDevices(string input1Id, string input2Id, string outputId, string? monitorOutputId)
    {
        _input1Id = input1Id;
        _input2Id = input2Id;
        _outputId = outputId;
        _monitorOutputId = monitorOutputId ?? string.Empty;
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
        _outputProvider = new OutputMixProvider(_channels, _parameterQueue, _inputBuffer1, _inputBuffer2, _blockSize, ReportOutputCallback, outputFormat);

        _output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, true, _latencyMs);
        _output.Init(_outputProvider);
        _output.PlaybackStopped += OnOutputStopped;
        _output.Play();
        Interlocked.Exchange(ref _outputActive, 1);

        if (!string.IsNullOrWhiteSpace(_monitorOutputId))
        {
            _monitorBuffer = new LockFreeRingBuffer(_blockSize * 8 * 2);
            var monitorDevice = enumerator.GetDevice(_monitorOutputId);
            _monitorOutput = new WasapiOut(monitorDevice, AudioClientShareMode.Shared, true, _latencyMs);
            _monitorOutput.Init(new MonitorWaveProvider(_monitorBuffer, outputFormat, _blockSize));
            _outputProvider.SetMonitorBuffer(_monitorBuffer);
            _monitorOutput.PlaybackStopped += OnMonitorOutputStopped;
            _monitorOutput.Play();
            Interlocked.Exchange(ref _monitorActive, 1);
        }

        _capture1 = CreateCapture(enumerator, _input1Id);
        _capture2 = CreateCapture(enumerator, _input2Id);

        if (_capture1 is not null)
        {
            _capture1.DataAvailable += OnInput1DataAvailable;
            _capture1.RecordingStopped += OnInput1Stopped;
            try
            {
                _capture1.StartRecording();
                Interlocked.Exchange(ref _input1Active, 1);
            }
            catch
            {
                _capture1.DataAvailable -= OnInput1DataAvailable;
                _capture1.Dispose();
                _capture1 = null;
                Interlocked.Exchange(ref _input1Active, 0);
            }
        }

        if (_capture2 is not null)
        {
            _capture2.DataAvailable += OnInput2DataAvailable;
            _capture2.RecordingStopped += OnInput2Stopped;
            try
            {
                _capture2.StartRecording();
                Interlocked.Exchange(ref _input2Active, 1);
            }
            catch
            {
                _capture2.DataAvailable -= OnInput2DataAvailable;
                _capture2.Dispose();
                _capture2 = null;
                Interlocked.Exchange(ref _input2Active, 0);
            }
        }
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _isStopping, 1) == 1)
        {
            return;
        }

        if (_capture1 is not null)
        {
            _capture1.DataAvailable -= OnInput1DataAvailable;
            _capture1.RecordingStopped -= OnInput1Stopped;
            _capture1.StopRecording();
            _capture1.Dispose();
            _capture1 = null;
        }

        if (_capture2 is not null)
        {
            _capture2.DataAvailable -= OnInput2DataAvailable;
            _capture2.RecordingStopped -= OnInput2Stopped;
            _capture2.StopRecording();
            _capture2.Dispose();
            _capture2 = null;
        }

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

        _inputBuffer1.Clear();
        _inputBuffer2.Clear();
        _monitorBuffer?.Clear();
        Interlocked.Exchange(ref _input1Active, 0);
        Interlocked.Exchange(ref _input2Active, 0);

        Interlocked.Exchange(ref _isStopping, 0);
    }

    public void EnqueueParameterChange(ParameterChange change)
    {
        _parameterQueue.Enqueue(change);
    }

    public bool IsVbCableInstalled()
    {
        return _deviceManager.FindVBCableOutput() is not null;
    }

    public AudioEngineDiagnosticsSnapshot GetDiagnosticsSnapshot()
    {
        return new AudioEngineDiagnosticsSnapshot(
            outputActive: Volatile.Read(ref _outputActive) == 1,
            input1Active: Volatile.Read(ref _input1Active) == 1,
            input2Active: Volatile.Read(ref _input2Active) == 1,
            monitorActive: Volatile.Read(ref _monitorActive) == 1,
            isRecovering: Volatile.Read(ref _isRecovering) == 1,
            lastOutputCallbackTicks: Volatile.Read(ref _lastOutputCallbackTicks),
            lastInput1CallbackTicks: Volatile.Read(ref _lastInput1CallbackTicks),
            lastInput2CallbackTicks: Volatile.Read(ref _lastInput2CallbackTicks),
            outputCallbackCount: Interlocked.Read(ref _outputCallbackCount),
            input1CallbackCount: Interlocked.Read(ref _input1CallbackCount),
            input2CallbackCount: Interlocked.Read(ref _input2CallbackCount),
            lastOutputFrames: Volatile.Read(ref _lastOutputFrames),
            lastInput1Frames: Volatile.Read(ref _lastInput1Frames),
            lastInput2Frames: Volatile.Read(ref _lastInput2Frames),
            input1BufferedSamples: _inputBuffer1.AvailableRead,
            input2BufferedSamples: _inputBuffer2.AvailableRead,
            monitorBufferedSamples: _monitorBuffer?.AvailableRead ?? 0,
            input1BufferCapacity: _inputBuffer1.Capacity,
            input2BufferCapacity: _inputBuffer2.Capacity,
            monitorBufferCapacity: _monitorBuffer?.Capacity ?? 0);
    }

    public void Dispose()
    {
        CancelRecovery();
        Stop();
    }

    private WasapiCapture? CreateCapture(MMDeviceEnumerator enumerator, string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return null;
        }

        var device = enumerator.GetDevice(deviceId);
        var capture = new WasapiCapture(device)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_sampleRate, 1)
        };

        return capture;
    }

    private void OnInput1DataAvailable(object? sender, WaveInEventArgs e)
    {
        ReportInput1Callback(e.BytesRecorded / sizeof(float));
        WriteInputBuffer(_inputBuffer1, e.Buffer.AsSpan(0, e.BytesRecorded));
    }

    private void OnInput2DataAvailable(object? sender, WaveInEventArgs e)
    {
        ReportInput2Callback(e.BytesRecorded / sizeof(float));
        WriteInputBuffer(_inputBuffer2, e.Buffer.AsSpan(0, e.BytesRecorded));
    }

    private void OnInput1Stopped(object? sender, StoppedEventArgs e)
    {
        if (ShouldIgnoreStopEvent())
        {
            return;
        }

        Interlocked.Exchange(ref _input1Active, 0);
        if (IsDeviceInvalidated(e.Exception))
        {
            HandleDeviceInvalidated(_input1Id, "Input device disconnected.");
        }
    }

    private void OnInput2Stopped(object? sender, StoppedEventArgs e)
    {
        if (ShouldIgnoreStopEvent())
        {
            return;
        }

        Interlocked.Exchange(ref _input2Active, 0);
        if (IsDeviceInvalidated(e.Exception))
        {
            HandleDeviceInvalidated(_input2Id, "Input device disconnected.");
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

    private static void WriteInputBuffer(LockFreeRingBuffer buffer, ReadOnlySpan<byte> data)
    {
        var floatSpan = MemoryMarshal.Cast<byte, float>(data);
        buffer.Write(floatSpan);
    }

    private void ReportOutputCallback(int frames)
    {
        Interlocked.Exchange(ref _lastOutputFrames, frames);
        Interlocked.Exchange(ref _lastOutputCallbackTicks, Stopwatch.GetTimestamp());
        Interlocked.Increment(ref _outputCallbackCount);
    }

    private void ReportInput1Callback(int frames)
    {
        Interlocked.Exchange(ref _lastInput1Frames, frames);
        Interlocked.Exchange(ref _lastInput1CallbackTicks, Stopwatch.GetTimestamp());
        Interlocked.Increment(ref _input1CallbackCount);
    }

    private void ReportInput2Callback(int frames)
    {
        Interlocked.Exchange(ref _lastInput2Frames, frames);
        Interlocked.Exchange(ref _lastInput2CallbackTicks, Stopwatch.GetTimestamp());
        Interlocked.Increment(ref _input2CallbackCount);
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
                        DeviceRecovered?.Invoke(this, new DeviceRecoveredEventArgs(_input1Id, _input2Id, _outputId, _monitorOutputId));
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

    private bool TryRecoverDevices()
    {
        using var enumerator = new MMDeviceEnumerator();

        if (!TryResolveOutputDevice(enumerator))
        {
            return false;
        }

        _input1Id = ResolveInputDevice(enumerator, _input1Id);
        _input2Id = ResolveInputDevice(enumerator, _input2Id);
        _monitorOutputId = ResolveMonitorDevice(enumerator, _monitorOutputId);
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

    private sealed class OutputMixProvider : IWaveProvider
    {
        private readonly ChannelStrip[] _channels;
        private readonly LockFreeQueue<ParameterChange> _parameterQueue;
        private readonly LockFreeRingBuffer _input1;
        private readonly LockFreeRingBuffer _input2;
        private readonly int _blockSize;
        private readonly float[] _channel1Buffer;
        private readonly float[] _channel2Buffer;
        private LockFreeRingBuffer? _monitorBuffer;

        private readonly Action<int> _reportOutput;

        public OutputMixProvider(ChannelStrip[] channels, LockFreeQueue<ParameterChange> parameterQueue, LockFreeRingBuffer input1, LockFreeRingBuffer input2, int blockSize, Action<int> reportOutput, WaveFormat waveFormat)
        {
            _channels = channels;
            _parameterQueue = parameterQueue;
            _input1 = input1;
            _input2 = input2;
            _blockSize = blockSize;
            _reportOutput = reportOutput;
            WaveFormat = waveFormat;
            _channel1Buffer = new float[blockSize];
            _channel2Buffer = new float[blockSize];
        }

        public WaveFormat WaveFormat { get; }

        public int Read(byte[] buffer, int offset, int count)
        {
            ApplyParameterChanges();

            var output = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(offset, count));
            int totalFrames = output.Length / 2;
            int processed = 0;
            _reportOutput(totalFrames);

            while (processed < totalFrames)
            {
                int chunk = Math.Min(_blockSize, totalFrames - processed);
                var ch1Span = _channel1Buffer.AsSpan(0, chunk);
                var ch2Span = _channel2Buffer.AsSpan(0, chunk);

                int read1 = _input1.Read(ch1Span);
                if (read1 < chunk)
                {
                    ch1Span.Slice(read1).Clear();
                }

                int read2 = _input2.Read(ch2Span);
                if (read2 < chunk)
                {
                    ch2Span.Slice(read2).Clear();
                }

                bool soloActive = _channels[0].IsSoloed || _channels[1].IsSoloed;
                _channels[0].Process(ch1Span, soloActive && !_channels[0].IsSoloed);
                _channels[1].Process(ch2Span, soloActive && !_channels[1].IsSoloed);

                var outputSlice = output.Slice(processed * 2, chunk * 2);
                for (int i = 0; i < chunk; i++)
                {
                    int baseIndex = i * 2;
                    outputSlice[baseIndex] = ch1Span[i];
                    outputSlice[baseIndex + 1] = ch2Span[i];
                }

                _monitorBuffer?.Write(outputSlice);
                processed += chunk;
            }

            return count;
        }

        public void SetMonitorBuffer(LockFreeRingBuffer monitorBuffer)
        {
            _monitorBuffer = monitorBuffer;
        }

        private void ApplyParameterChanges()
        {
            while (_parameterQueue.TryDequeue(out var change))
            {
                if ((uint)change.ChannelId >= (uint)_channels.Length)
                {
                    continue;
                }

                var channel = _channels[change.ChannelId];
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
                        ApplyPluginBypass(channel, change.PluginIndex, change.Value >= 0.5f);
                        break;
                    case ParameterType.PluginParameter:
                        ApplyPluginParameter(channel, change.PluginIndex, change.ParameterIndex, change.Value);
                        break;
                    case ParameterType.PluginCommand:
                        ApplyPluginCommand(channel, change.PluginIndex, change.Command);
                        break;
                }
            }
        }

        private static void ApplyPluginBypass(ChannelStrip channel, int pluginIndex, bool bypass)
        {
            var slots = channel.PluginChain.GetSnapshot();
            if ((uint)pluginIndex >= (uint)slots.Length)
            {
                return;
            }

            var plugin = slots[pluginIndex];
            if (plugin is not null)
            {
                plugin.IsBypassed = bypass;
            }
        }

        private static void ApplyPluginParameter(ChannelStrip channel, int pluginIndex, int parameterIndex, float value)
        {
            var slots = channel.PluginChain.GetSnapshot();
            if ((uint)pluginIndex >= (uint)slots.Length)
            {
                return;
            }

            var plugin = slots[pluginIndex];
            plugin?.SetParameter(parameterIndex, value);
        }

        private static void ApplyPluginCommand(ChannelStrip channel, int pluginIndex, PluginCommandType command)
        {
            var slots = channel.PluginChain.GetSnapshot();
            if ((uint)pluginIndex >= (uint)slots.Length)
            {
                return;
            }

            var plugin = slots[pluginIndex];
            if (plugin is FFTNoiseRemovalPlugin noise && command == PluginCommandType.LearnNoiseProfile)
            {
                noise.LearnNoiseProfile();
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
