namespace HotMic.Core.Engine;

internal sealed class AudioEngineDiagnosticsCollector
{
    private readonly InputCaptureManager _inputCaptureManager;

    public AudioEngineDiagnosticsCollector(InputCaptureManager inputCaptureManager)
    {
        _inputCaptureManager = inputCaptureManager;
    }

    public AudioEngineDiagnosticsSnapshot Build(
        bool outputActive,
        bool monitorActive,
        bool isRecovering,
        long lastOutputCallbackTicks,
        long outputCallbackCount,
        int lastOutputFrames,
        int monitorBufferedSamples,
        int monitorBufferCapacity,
        long outputUnderflowSamples)
    {
        var inputs = _inputCaptureManager.GetDiagnostics();
        return new AudioEngineDiagnosticsSnapshot(
            outputActive: outputActive,
            monitorActive: monitorActive,
            isRecovering: isRecovering,
            lastOutputCallbackTicks: lastOutputCallbackTicks,
            outputCallbackCount: outputCallbackCount,
            lastOutputFrames: lastOutputFrames,
            monitorBufferedSamples: monitorBufferedSamples,
            monitorBufferCapacity: monitorBufferCapacity,
            outputUnderflowSamples: outputUnderflowSamples,
            inputs: inputs);
    }
}
