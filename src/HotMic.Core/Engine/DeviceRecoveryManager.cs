using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;

namespace HotMic.Core.Engine;

internal sealed class DeviceRecoveryManager : IDisposable
{
    private readonly InputCaptureManager _inputCaptureManager;
    private readonly Action _stopEngine;
    private readonly Action _startEngine;
    private readonly Func<string[]> _getInputDeviceIds;
    private readonly Action<string, string> _deviceDisconnected;
    private readonly Action<string[], string, string> _deviceRecovered;
    private CancellationTokenSource? _recoveryCts;
    private int _isRecovering;

    public DeviceRecoveryManager(
        InputCaptureManager inputCaptureManager,
        Action stopEngine,
        Action startEngine,
        Func<string[]> getInputDeviceIds,
        Action<string, string> deviceDisconnected,
        Action<string[], string, string> deviceRecovered)
    {
        _inputCaptureManager = inputCaptureManager;
        _stopEngine = stopEngine;
        _startEngine = startEngine;
        _getInputDeviceIds = getInputDeviceIds;
        _deviceDisconnected = deviceDisconnected;
        _deviceRecovered = deviceRecovered;
    }

    public string OutputId { get; private set; } = string.Empty;

    public string MonitorOutputId { get; private set; } = string.Empty;

    public bool IsRecovering => Volatile.Read(ref _isRecovering) == 1;

    public void ConfigureOutputDevices(string outputId, string? monitorOutputId)
    {
        OutputId = outputId;
        MonitorOutputId = monitorOutputId ?? string.Empty;
    }

    public void HandleDeviceInvalidated(string deviceId, string message)
    {
        if (Interlocked.CompareExchange(ref _isRecovering, 1, 0) != 0)
        {
            return;
        }

        _stopEngine();
        _deviceDisconnected(deviceId, message);
        StartRecoveryLoop();
    }

    public void Cancel()
    {
        _recoveryCts?.Cancel();
        _recoveryCts?.Dispose();
        _recoveryCts = null;
        Interlocked.Exchange(ref _isRecovering, 0);
    }

    public void Dispose()
    {
        Cancel();
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
                        _startEngine();
                        _deviceRecovered(_getInputDeviceIds(), OutputId, MonitorOutputId);
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

        MonitorOutputId = ResolveMonitorDevice(enumerator, MonitorOutputId);
        _inputCaptureManager.ResolveInputDevices(deviceId => ResolveInputDevice(enumerator, deviceId));
        return true;
    }

    private bool TryResolveOutputDevice(MMDeviceEnumerator enumerator)
    {
        if (IsDeviceActive(enumerator, OutputId))
        {
            return true;
        }

        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        foreach (var device in devices)
        {
            if (device.FriendlyName.Contains("VB-Cable", StringComparison.OrdinalIgnoreCase))
            {
                OutputId = device.ID;
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
}
