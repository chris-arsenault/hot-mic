using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using HotMic.Common.Configuration;
using HotMic.Core.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace HotMic.Core.Engine;

internal sealed class InputCaptureManager : IDisposable
{
    private readonly List<InputCapture> _captures = new();
    private readonly object _lock = new();
    private readonly int _sampleRate;
    private readonly int _blockSize;
    private readonly Func<bool> _shouldIgnoreStopEvent;
    private readonly Action<string, string> _deviceInvalidated;

    public InputCaptureManager(
        int sampleRate,
        int blockSize,
        Func<bool> shouldIgnoreStopEvent,
        Action<string, string> deviceInvalidated)
    {
        _sampleRate = sampleRate;
        _blockSize = blockSize;
        _shouldIgnoreStopEvent = shouldIgnoreStopEvent;
        _deviceInvalidated = deviceInvalidated;
    }

    public string ConfigureChannelInput(int channelId, string deviceId, InputChannelMode channelMode, bool outputActive)
    {
        if (channelId < 0)
        {
            return string.Empty;
        }

        string resolvedDeviceId = deviceId ?? string.Empty;

        lock (_lock)
        {
            for (int i = 0; i < _captures.Count; i++)
            {
                var capture = _captures[i];
                if (capture.ChannelId != channelId && capture.DeviceId == resolvedDeviceId)
                {
                    resolvedDeviceId = string.Empty;
                    break;
                }
            }

            InputCapture? existing = null;
            for (int i = 0; i < _captures.Count; i++)
            {
                if (_captures[i].ChannelId == channelId)
                {
                    existing = _captures[i];
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(resolvedDeviceId))
            {
                if (existing is not null)
                {
                    existing.Stop();
                    existing.Dispose();
                    _captures.Remove(existing);
                }
            }
            else
            {
                if (existing is null)
                {
                    existing = new InputCapture(this, channelId);
                    _captures.Add(existing);
                }

                existing.Configure(resolvedDeviceId, channelMode);
                if (outputActive)
                {
                    using var enumerator = new MMDeviceEnumerator();
                    existing.Start(enumerator, _sampleRate);
                }
            }
        }

        return resolvedDeviceId;
    }

    public void StartAll(MMDeviceEnumerator enumerator)
    {
        lock (_lock)
        {
            for (int i = 0; i < _captures.Count; i++)
            {
                _captures[i].Start(enumerator, _sampleRate);
            }
        }
    }

    public void StopAll()
    {
        lock (_lock)
        {
            for (int i = 0; i < _captures.Count; i++)
            {
                _captures[i].Stop();
            }
        }
    }

    public void ClearBuffers()
    {
        lock (_lock)
        {
            for (int i = 0; i < _captures.Count; i++)
            {
                _captures[i].Source.Buffer.Clear();
            }
        }
    }

    public void RemoveCapturesAbove(int channelCount)
    {
        lock (_lock)
        {
            for (int i = _captures.Count - 1; i >= 0; i--)
            {
                if (_captures[i].ChannelId >= channelCount)
                {
                    _captures[i].Stop();
                    _captures[i].Dispose();
                    _captures.RemoveAt(i);
                }
            }
        }
    }

    public InputSource?[] GetInputSources(int channelCount)
    {
        var sources = new InputSource?[channelCount];
        lock (_lock)
        {
            for (int i = 0; i < _captures.Count; i++)
            {
                var capture = _captures[i];
                if ((uint)capture.ChannelId < (uint)channelCount)
                {
                    sources[capture.ChannelId] = capture.Source;
                }
            }
        }
        return sources;
    }

    public List<InputDiagnosticsSnapshot> GetDiagnostics()
    {
        var inputs = new List<InputDiagnosticsSnapshot>();
        lock (_lock)
        {
            for (int i = 0; i < _captures.Count; i++)
            {
                var capture = _captures[i];
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

        return inputs;
    }

    public string[] GetInputDeviceIds()
    {
        lock (_lock)
        {
            var ids = new string[_captures.Count];
            for (int i = 0; i < _captures.Count; i++)
            {
                ids[i] = _captures[i].DeviceId;
            }
            return ids;
        }
    }

    public void ResolveInputDevices(Func<string, string> resolveDeviceId)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        lock (_lock)
        {
            for (int i = 0; i < _captures.Count; i++)
            {
                var capture = _captures[i];
                string resolved = resolveDeviceId(capture.DeviceId);
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
    }

    public void Dispose()
    {
        StopAll();
        lock (_lock)
        {
            for (int i = 0; i < _captures.Count; i++)
            {
                _captures[i].Dispose();
            }
            _captures.Clear();
        }
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

    private sealed class InputCapture : IDisposable
    {
        private readonly InputCaptureManager _manager;
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

        public InputCapture(InputCaptureManager manager, int channelId)
        {
            _manager = manager;
            _channelId = channelId;
            _source = new InputSource(manager._blockSize * 32);
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

            _capture = _manager.CreateCapture(enumerator, _deviceId);
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
            int mixBufferSize = Math.Max(sampleRate, _manager._blockSize * 8);
            if (mixBufferSize < 1)
            {
                mixBufferSize = _manager._blockSize * 8;
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
            if (_manager._shouldIgnoreStopEvent())
            {
                return;
            }

            Volatile.Write(ref _active, 0);
            if (DeviceErrorHelper.IsDeviceInvalidated(e.Exception))
            {
                _manager._deviceInvalidated(_deviceId, "Input device disconnected.");
            }
        }
    }
}
