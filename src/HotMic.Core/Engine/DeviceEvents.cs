namespace HotMic.Core.Engine;

public sealed class DeviceDisconnectedEventArgs : EventArgs
{
    public DeviceDisconnectedEventArgs(string deviceId, string message)
    {
        DeviceId = deviceId;
        Message = message;
    }

    public string DeviceId { get; }

    public string Message { get; }
}

public sealed class DeviceRecoveredEventArgs : EventArgs
{
    public DeviceRecoveredEventArgs(IReadOnlyList<string> inputDeviceIds, string outputDeviceId, string monitorDeviceId)
    {
        InputDeviceIds = inputDeviceIds;
        OutputDeviceId = outputDeviceId;
        MonitorDeviceId = monitorDeviceId;
    }

    public IReadOnlyList<string> InputDeviceIds { get; }

    public string OutputDeviceId { get; }

    public string MonitorDeviceId { get; }
}
