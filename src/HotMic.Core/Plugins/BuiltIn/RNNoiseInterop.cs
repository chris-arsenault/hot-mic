using System.Runtime.InteropServices;

namespace HotMic.Core.Plugins.BuiltIn;

internal static class RNNoiseInterop
{
    private const string DllName = "librnnoise";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr rnnoise_create(IntPtr model);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void rnnoise_destroy(IntPtr state);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float rnnoise_process_frame(IntPtr state, float[] output, float[] input);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int rnnoise_get_size();
}
