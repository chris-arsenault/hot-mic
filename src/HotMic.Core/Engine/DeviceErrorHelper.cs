using System.Runtime.InteropServices;

namespace HotMic.Core.Engine;

internal static class DeviceErrorHelper
{
    private const int AudclntEDeviceInvalidated = unchecked((int)0x88890004);

    public static bool IsDeviceInvalidated(Exception? exception)
    {
        return exception is COMException comException && comException.HResult == AudclntEDeviceInvalidated;
    }
}
