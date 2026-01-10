namespace HotMic.Core.Engine;

public readonly struct AudioEngineDiagnosticsSnapshot
{
    public AudioEngineDiagnosticsSnapshot(
        bool outputActive,
        bool input1Active,
        bool input2Active,
        bool monitorActive,
        bool isRecovering,
        long lastOutputCallbackTicks,
        long lastInput1CallbackTicks,
        long lastInput2CallbackTicks,
        long outputCallbackCount,
        long input1CallbackCount,
        long input2CallbackCount,
        int lastOutputFrames,
        int lastInput1Frames,
        int lastInput2Frames,
        int input1BufferedSamples,
        int input2BufferedSamples,
        int monitorBufferedSamples,
        int input1BufferCapacity,
        int input2BufferCapacity,
        int monitorBufferCapacity)
    {
        OutputActive = outputActive;
        Input1Active = input1Active;
        Input2Active = input2Active;
        MonitorActive = monitorActive;
        IsRecovering = isRecovering;
        LastOutputCallbackTicks = lastOutputCallbackTicks;
        LastInput1CallbackTicks = lastInput1CallbackTicks;
        LastInput2CallbackTicks = lastInput2CallbackTicks;
        OutputCallbackCount = outputCallbackCount;
        Input1CallbackCount = input1CallbackCount;
        Input2CallbackCount = input2CallbackCount;
        LastOutputFrames = lastOutputFrames;
        LastInput1Frames = lastInput1Frames;
        LastInput2Frames = lastInput2Frames;
        Input1BufferedSamples = input1BufferedSamples;
        Input2BufferedSamples = input2BufferedSamples;
        MonitorBufferedSamples = monitorBufferedSamples;
        Input1BufferCapacity = input1BufferCapacity;
        Input2BufferCapacity = input2BufferCapacity;
        MonitorBufferCapacity = monitorBufferCapacity;
    }

    public bool OutputActive { get; }
    public bool Input1Active { get; }
    public bool Input2Active { get; }
    public bool MonitorActive { get; }
    public bool IsRecovering { get; }
    public long LastOutputCallbackTicks { get; }
    public long LastInput1CallbackTicks { get; }
    public long LastInput2CallbackTicks { get; }
    public long OutputCallbackCount { get; }
    public long Input1CallbackCount { get; }
    public long Input2CallbackCount { get; }
    public int LastOutputFrames { get; }
    public int LastInput1Frames { get; }
    public int LastInput2Frames { get; }
    public int Input1BufferedSamples { get; }
    public int Input2BufferedSamples { get; }
    public int MonitorBufferedSamples { get; }
    public int Input1BufferCapacity { get; }
    public int Input2BufferCapacity { get; }
    public int MonitorBufferCapacity { get; }
}
