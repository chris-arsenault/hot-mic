using System;
using System.Runtime.InteropServices;
using NAudio;
using NAudio.Midi;

namespace HotMic.Core.Midi;

internal sealed class WinMmMidiIn : IDisposable
{
    private IntPtr _handle = IntPtr.Zero;
    private bool _disposed;
    private readonly WinMmMidiInterop.MidiInCallback _callback;

    public event EventHandler<MidiInMessageEventArgs>? MessageReceived;
    public event EventHandler<MidiInMessageEventArgs>? ErrorReceived;

    public static int NumberOfDevices => WinMmMidiInterop.midiInGetNumDevs();

    public static MidiInCapabilities DeviceInfo(int deviceIndex)
    {
        var structSize = Marshal.SizeOf<MidiInCapabilities>();
        MmException.Try(WinMmMidiInterop.midiInGetDevCaps((IntPtr)deviceIndex, out var caps, structSize), "midiInGetDevCaps");
        return caps;
    }

    public WinMmMidiIn(int deviceIndex)
    {
        _callback = Callback;
        MmException.Try(
            WinMmMidiInterop.midiInOpen(out _handle, (IntPtr)deviceIndex, _callback, IntPtr.Zero, WinMmMidiInterop.CALLBACK_FUNCTION),
            "midiInOpen");
    }

    public void Start()
    {
        ThrowIfDisposed();
        MmException.Try(WinMmMidiInterop.midiInStart(_handle), "midiInStart");
    }

    public void Stop()
    {
        if (_disposed || _handle == IntPtr.Zero)
        {
            return;
        }

        MmException.Try(WinMmMidiInterop.midiInStop(_handle), "midiInStop");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_handle != IntPtr.Zero)
        {
            try
            {
                WinMmMidiInterop.midiInReset(_handle);
            }
            catch
            {
                // Swallow cleanup failures to avoid teardown crashes.
            }

            try
            {
                WinMmMidiInterop.midiInClose(_handle);
            }
            catch
            {
                // Swallow cleanup failures to avoid teardown crashes.
            }

            _handle = IntPtr.Zero;
        }

        GC.KeepAlive(_callback);
        GC.SuppressFinalize(this);
    }

    private void Callback(
        IntPtr midiInHandle,
        WinMmMidiInterop.MidiInMessage message,
        IntPtr userData,
        IntPtr messageParameter1,
        IntPtr messageParameter2)
    {
        try
        {
            switch (message)
            {
                case WinMmMidiInterop.MidiInMessage.Data:
                case WinMmMidiInterop.MidiInMessage.MoreData:
                    RaiseMessage(MessageReceived, messageParameter1, messageParameter2);
                    break;
                case WinMmMidiInterop.MidiInMessage.Error:
                    RaiseMessage(ErrorReceived, messageParameter1, messageParameter2);
                    break;
            }
        }
        catch
        {
            // Avoid crashing the process from native callback exceptions.
        }
    }

    private void RaiseMessage(
        EventHandler<MidiInMessageEventArgs>? handler,
        IntPtr messageParameter1,
        IntPtr messageParameter2)
    {
        if (handler == null)
        {
            return;
        }

        // WinMM uses a DWORD_PTR for packed messages; mask to 32 bits without overflow.
        int rawMessage = unchecked((int)messageParameter1.ToInt64());
        int timestamp = unchecked((int)messageParameter2.ToInt64());
        handler(this, new MidiInMessageEventArgs(rawMessage, timestamp));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static class WinMmMidiInterop
    {
        internal enum MidiInMessage
        {
            Open = 0x3C1,
            Close = 0x3C2,
            Data = 0x3C3,
            LongData = 0x3C4,
            Error = 0x3C5,
            LongError = 0x3C6,
            MoreData = 0x3CC,
        }

        public delegate void MidiInCallback(
            IntPtr midiInHandle,
            MidiInMessage message,
            IntPtr userData,
            IntPtr messageParameter1,
            IntPtr messageParameter2);

        [DllImport("winmm.dll", EntryPoint = "midiInOpen")]
        public static extern MmResult midiInOpen(
            out IntPtr hMidiIn,
            IntPtr uDeviceID,
            MidiInCallback callback,
            IntPtr dwInstance,
            int dwFlags);

        [DllImport("winmm.dll")]
        public static extern MmResult midiInStart(IntPtr hMidiIn);

        [DllImport("winmm.dll")]
        public static extern MmResult midiInStop(IntPtr hMidiIn);

        [DllImport("winmm.dll")]
        public static extern MmResult midiInReset(IntPtr hMidiIn);

        [DllImport("winmm.dll")]
        public static extern MmResult midiInClose(IntPtr hMidiIn);

        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        public static extern MmResult midiInGetDevCaps(IntPtr deviceId, out MidiInCapabilities capabilities, int size);

        [DllImport("winmm.dll")]
        public static extern int midiInGetNumDevs();

        public const int CALLBACK_FUNCTION = 0x30000;
    }
}
