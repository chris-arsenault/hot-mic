using System.Runtime.InteropServices;

namespace HotMic.Core.Threading;

internal static class ThreadCpuTimer
{
    public static bool TryGetCurrentThreadCycles(out long cycles)
    {
        if (!QueryThreadCycleTime(GetCurrentThread(), out ulong cycleTime))
        {
            cycles = 0;
            return false;
        }

        cycles = (long)cycleTime;
        return cycles > 0;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryThreadCycleTime(IntPtr threadHandle, out ulong cycleTime);
}
