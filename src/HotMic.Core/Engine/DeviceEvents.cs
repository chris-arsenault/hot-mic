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
    public DeviceRecoveredEventArgs(string inputDevice1Id, string inputDevice2Id, string outputDeviceId, string monitorDeviceId)
    {
        InputDevice1Id = inputDevice1Id;
        InputDevice2Id = inputDevice2Id;
        OutputDeviceId = outputDeviceId;
        MonitorDeviceId = monitorDeviceId;
    }

    public string InputDevice1Id { get; }

    public string InputDevice2Id { get; }

    public string OutputDeviceId { get; }

    public string MonitorDeviceId { get; }
}
