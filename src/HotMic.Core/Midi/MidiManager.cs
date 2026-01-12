using System.Collections.Concurrent;
using NAudio.Midi;
using HotMic.Common.Configuration;

namespace HotMic.Core.Midi;

/// <summary>
/// Manages MIDI input and CC-to-parameter binding.
/// </summary>
public sealed class MidiManager : IDisposable
{
    private MidiIn? _midiIn;
    private int _currentDeviceIndex = -1;
    private MidiConfig _config;
    private readonly ConcurrentDictionary<string, MidiBinding> _bindings = new();
    private readonly ConcurrentDictionary<int, int> _lastCcValues = new();
    private readonly object _lock = new();
    private bool _isDisposed;
    private bool _isLearning;
    private string? _learnTargetPath;
    private Action<int, int>? _learnCallback;

    public event EventHandler<MidiCcEventArgs>? CcReceived;
    public event EventHandler<MidiDeviceEventArgs>? DeviceChanged;
    public event EventHandler<MidiBindingEventArgs>? BindingTriggered;

    public string? CurrentDevice => _midiIn != null ? GetDeviceName(_currentDeviceIndex) : null;
    public bool IsActive => _midiIn != null;
    public bool IsLearning => _isLearning;

    public MidiManager(MidiConfig config)
    {
        _config = config;
        LoadBindings();
    }

    public void Start()
    {
        if (_config.Enabled)
        {
            Connect();
        }
    }

    public void Stop()
    {
        Disconnect();
    }

    public IReadOnlyList<string> GetAvailableDevices()
    {
        var devices = new List<string>();
        for (int i = 0; i < MidiIn.NumberOfDevices; i++)
        {
            devices.Add(MidiIn.DeviceInfo(i).ProductName);
        }
        return devices;
    }

    public bool SelectDevice(string deviceName)
    {
        lock (_lock)
        {
            _config.DeviceName = deviceName;
            Disconnect();
            return Connect();
        }
    }

    public void ApplyConfig(MidiConfig config)
    {
        lock (_lock)
        {
            bool deviceChanged = _config.DeviceName != config.DeviceName;
            bool enabledChanged = _config.Enabled != config.Enabled;
            _config = config;
            LoadBindings();

            if (!_config.Enabled)
            {
                Disconnect();
                return;
            }

            if (deviceChanged || enabledChanged || !IsActive)
            {
                Disconnect();
                Connect();
            }
        }
    }

    public void AddBinding(MidiBinding binding)
    {
        _bindings[binding.TargetPath] = binding;
        _config.Bindings.RemoveAll(b => b.TargetPath == binding.TargetPath);
        _config.Bindings.Add(binding);
    }

    public void RemoveBinding(string targetPath)
    {
        _bindings.TryRemove(targetPath, out _);
        _config.Bindings.RemoveAll(b => b.TargetPath == targetPath);
    }

    public MidiBinding? GetBinding(string targetPath)
    {
        return _bindings.TryGetValue(targetPath, out var binding) ? binding : null;
    }

    public IReadOnlyList<MidiBinding> GetAllBindings()
    {
        return _bindings.Values.ToList();
    }

    public void StartLearn(string targetPath, Action<int, int>? onLearned = null)
    {
        lock (_lock)
        {
            _isLearning = true;
            _learnTargetPath = targetPath;
            _learnCallback = onLearned;
        }
    }

    public void CancelLearn()
    {
        lock (_lock)
        {
            _isLearning = false;
            _learnTargetPath = null;
            _learnCallback = null;
        }
    }

    private void LoadBindings()
    {
        _bindings.Clear();
        foreach (var binding in _config.Bindings)
        {
            _bindings[binding.TargetPath] = binding;
        }
    }

    private bool Connect()
    {
        if (!_config.Enabled) return false;

        try
        {
            int deviceIndex = -1;

            if (!string.IsNullOrEmpty(_config.DeviceName))
            {
                for (int i = 0; i < MidiIn.NumberOfDevices; i++)
                {
                    if (MidiIn.DeviceInfo(i).ProductName == _config.DeviceName)
                    {
                        deviceIndex = i;
                        break;
                    }
                }
            }

            if (deviceIndex < 0 && MidiIn.NumberOfDevices > 0)
            {
                deviceIndex = 0;
            }

            if (deviceIndex < 0)
            {
                DeviceChanged?.Invoke(this, new MidiDeviceEventArgs(null, false, "No MIDI devices found"));
                return false;
            }

            _midiIn = new MidiIn(deviceIndex);
            _currentDeviceIndex = deviceIndex;
            _midiIn.MessageReceived += OnMidiMessageReceived;
            _midiIn.ErrorReceived += OnMidiError;
            _midiIn.Start();

            DeviceChanged?.Invoke(this, new MidiDeviceEventArgs(GetDeviceName(deviceIndex), true));
            return true;
        }
        catch (Exception ex)
        {
            DeviceChanged?.Invoke(this, new MidiDeviceEventArgs(null, false, ex.Message));
            return false;
        }
    }

    private void Disconnect()
    {
        if (_midiIn != null)
        {
            var oldDevice = CurrentDevice;
            try
            {
                _midiIn.MessageReceived -= OnMidiMessageReceived;
                _midiIn.ErrorReceived -= OnMidiError;
                _midiIn.Stop();
                _midiIn.Dispose();
            }
            catch { }
            finally
            {
                _midiIn = null;
                _currentDeviceIndex = -1;
                DeviceChanged?.Invoke(this, new MidiDeviceEventArgs(oldDevice, false));
            }
        }
    }

    private void OnMidiMessageReceived(object? sender, MidiInMessageEventArgs e)
    {
        if (e.MidiEvent is ControlChangeEvent cc)
        {
            HandleControlChange(cc);
        }
    }

    private void OnMidiError(object? sender, MidiInMessageEventArgs e)
    {
        // Log error if needed
    }

    private void HandleControlChange(ControlChangeEvent cc)
    {
        int ccNumber = (int)cc.Controller;
        int ccValue = cc.ControllerValue;
        int channel = cc.Channel;

        // Filter by channel if configured
        if (_config.FilterChannel.HasValue && _config.FilterChannel.Value != channel)
        {
            return;
        }

        // Fire raw CC event
        CcReceived?.Invoke(this, new MidiCcEventArgs(ccNumber, ccValue, channel));

        // Handle learn mode
        if (_isLearning && !string.IsNullOrEmpty(_learnTargetPath))
        {
            // Only learn on significant CC movement
            int key = (channel << 8) | ccNumber;
            _lastCcValues.TryGetValue(key, out int lastValue);
            _lastCcValues[key] = ccValue;

            if (Math.Abs(ccValue - lastValue) >= 10)
            {
                lock (_lock)
                {
                    if (_isLearning && !string.IsNullOrEmpty(_learnTargetPath))
                    {
                        _learnCallback?.Invoke(ccNumber, channel);
                        _isLearning = false;
                        _learnTargetPath = null;
                        _learnCallback = null;
                    }
                }
            }
            return;
        }

        // Process bindings
        foreach (var binding in _bindings.Values)
        {
            if (binding.CcNumber != ccNumber)
            {
                continue;
            }

            if (binding.Channel.HasValue && binding.Channel.Value != channel)
            {
                continue;
            }

            float normalized = ccValue / 127f;
            float value = binding.MinValue + normalized * (binding.MaxValue - binding.MinValue);

            BindingTriggered?.Invoke(this, new MidiBindingEventArgs(binding.TargetPath, value, ccNumber, channel));
        }
    }

    private static string GetDeviceName(int deviceIndex)
    {
        return deviceIndex >= 0 && deviceIndex < MidiIn.NumberOfDevices
            ? MidiIn.DeviceInfo(deviceIndex).ProductName
            : string.Empty;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
    }
}

public sealed class MidiCcEventArgs : EventArgs
{
    public int CcNumber { get; }
    public int Value { get; }
    public int Channel { get; }

    public MidiCcEventArgs(int ccNumber, int value, int channel)
    {
        CcNumber = ccNumber;
        Value = value;
        Channel = channel;
    }
}

public sealed class MidiDeviceEventArgs : EventArgs
{
    public string? DeviceName { get; }
    public bool IsConnected { get; }
    public string? ErrorMessage { get; }

    public MidiDeviceEventArgs(string? deviceName, bool isConnected, string? error = null)
    {
        DeviceName = deviceName;
        IsConnected = isConnected;
        ErrorMessage = error;
    }
}

public sealed class MidiBindingEventArgs : EventArgs
{
    public string TargetPath { get; }
    public float Value { get; }
    public int CcNumber { get; }
    public int Channel { get; }

    public MidiBindingEventArgs(string targetPath, float value, int ccNumber, int channel)
    {
        TargetPath = targetPath;
        Value = value;
        CcNumber = ccNumber;
        Channel = channel;
    }
}
