namespace HotMic.Core.Engine;

public readonly struct AudioEngineDiagnosticsSnapshot
{
    public AudioEngineDiagnosticsSnapshot(
        bool outputActive,
        bool monitorActive,
        bool isRecovering,
        long lastOutputCallbackTicks,
        long outputCallbackCount,
        int lastOutputFrames,
        int monitorBufferedSamples,
        int monitorBufferCapacity,
        long outputUnderflowSamples,
        long blockBudgetTicks,
        long lastBlockTicks,
        long maxBlockTicks,
        long blockOverrunCount,
        long lastBlockCpuTicks,
        long maxBlockCpuTicks,
        IReadOnlyList<InputDiagnosticsSnapshot> inputs)
    {
        OutputActive = outputActive;
        MonitorActive = monitorActive;
        IsRecovering = isRecovering;
        LastOutputCallbackTicks = lastOutputCallbackTicks;
        OutputCallbackCount = outputCallbackCount;
        LastOutputFrames = lastOutputFrames;
        MonitorBufferedSamples = monitorBufferedSamples;
        MonitorBufferCapacity = monitorBufferCapacity;
        OutputUnderflowSamples = outputUnderflowSamples;
        BlockBudgetTicks = blockBudgetTicks;
        LastBlockTicks = lastBlockTicks;
        MaxBlockTicks = maxBlockTicks;
        BlockOverrunCount = blockOverrunCount;
        LastBlockCpuTicks = lastBlockCpuTicks;
        MaxBlockCpuTicks = maxBlockCpuTicks;
        Inputs = inputs;
    }

    public bool OutputActive { get; }
    public bool MonitorActive { get; }
    public bool IsRecovering { get; }
    public long LastOutputCallbackTicks { get; }
    public long OutputCallbackCount { get; }
    public int LastOutputFrames { get; }
    public int MonitorBufferedSamples { get; }
    public int MonitorBufferCapacity { get; }
    public long OutputUnderflowSamples { get; }
    public long BlockBudgetTicks { get; }
    public long LastBlockTicks { get; }
    public long MaxBlockTicks { get; }
    public long BlockOverrunCount { get; }
    public long LastBlockCpuTicks { get; }
    public long MaxBlockCpuTicks { get; }
    public IReadOnlyList<InputDiagnosticsSnapshot> Inputs { get; }
}

public readonly struct InputDiagnosticsSnapshot
{
    public InputDiagnosticsSnapshot(
        int channelId,
        string deviceId,
        bool isActive,
        long lastCallbackTicks,
        long callbackCount,
        int lastFrames,
        int bufferedSamples,
        int bufferCapacity,
        int channels,
        int sampleRate,
        long droppedSamples,
        long overflowSamples,
        long underflowSamples)
    {
        ChannelId = channelId;
        DeviceId = deviceId;
        IsActive = isActive;
        LastCallbackTicks = lastCallbackTicks;
        CallbackCount = callbackCount;
        LastFrames = lastFrames;
        BufferedSamples = bufferedSamples;
        BufferCapacity = bufferCapacity;
        Channels = channels;
        SampleRate = sampleRate;
        DroppedSamples = droppedSamples;
        OverflowSamples = overflowSamples;
        UnderflowSamples = underflowSamples;
    }

    public int ChannelId { get; }
    public string DeviceId { get; }
    public bool IsActive { get; }
    public long LastCallbackTicks { get; }
    public long CallbackCount { get; }
    public int LastFrames { get; }
    public int BufferedSamples { get; }
    public int BufferCapacity { get; }
    public int Channels { get; }
    public int SampleRate { get; }
    public long DroppedSamples { get; }
    public long OverflowSamples { get; }
    public long UnderflowSamples { get; }
}
