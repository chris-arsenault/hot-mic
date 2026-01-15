using System;
using System.Threading;
using JetBrains.Profiler.Api;

namespace HotMic.App.Diagnostics;

internal static class SpectrographProfiler
{
    private const string SessionName = "HotMic Spectrograph Hotkey";
    private const string SnapshotPrefix = "hotmic-spectrograph";
    private static int _collecting;

    public static bool IsCollecting =>
        Volatile.Read(ref _collecting) == 1;

    public static void Toggle()
    {
        if (_collecting == 0)
        {
            MeasureProfiler.StartCollectingData(SessionName);
            Interlocked.Exchange(ref _collecting, 1);
            return;
        }

        MeasureProfiler.SaveData($"{SnapshotPrefix}-{DateTime.UtcNow:yyyyMMdd-HHmmss}");
        Interlocked.Exchange(ref _collecting, 0);
    }
}
