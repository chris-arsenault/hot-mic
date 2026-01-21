using HotMic.Core.Dsp.Generators;
using HotMic.Core.Threading;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed partial class SignalGeneratorPlugin
{
    private LockFreeRingBuffer _recordBuffer;
    private volatile bool _recordingEnabled;
    private volatile int _targetSlotForCapture;
    private volatile bool _captureRequested;

    /// <summary>
    /// Enable/disable recording of input signal for later capture.
    /// </summary>
    public void SetRecordingEnabled(bool enabled)
    {
        _recordingEnabled = enabled;
        if (enabled)
        {
            _recordBuffer.Clear();
        }
    }

    /// <summary>
    /// Start recording input to a specific slot.
    /// Recording continues until StopRecordingToSlot is called.
    /// </summary>
    public void StartRecordingToSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= SlotCount)
            return;

        _targetSlotForCapture = slotIndex;
        _recordBuffer.Clear();
        _recordingEnabled = true;
    }

    /// <summary>
    /// Stop recording and capture the recorded audio to the target slot.
    /// </summary>
    public void StopRecordingToSlot()
    {
        if (!_recordingEnabled)
            return;

        _recordingEnabled = false;
        _captureRequested = true;
    }

    /// <summary>
    /// Check if currently recording.
    /// </summary>
    public bool IsRecording => _recordingEnabled;

    /// <summary>
    /// Get the slot index currently being recorded to, or -1 if not recording.
    /// </summary>
    public int RecordingTargetSlot => _recordingEnabled ? _targetSlotForCapture : -1;

    /// <summary>
    /// Request capture of recorded audio into the specified slot.
    /// </summary>
    public void CaptureToSlot(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < SlotCount)
        {
            _targetSlotForCapture = slotIndex;
            _captureRequested = true;
        }
    }

    private void ProcessCaptureRequest()
    {
        if (!_captureRequested) return;
        _captureRequested = false;

        int available = _recordBuffer.AvailableRead;
        if (available <= 0) return;

        int slotIndex = _targetSlotForCapture;
        if (slotIndex < 0 || slotIndex >= SlotCount) return;

        // Read from ring buffer into sample buffer
        float[] temp = new float[Math.Min(available, SampleBuffer.MaxSamples)];
        int read = _recordBuffer.Read(temp);

        if (read > 0)
        {
            _slots[slotIndex].LoadSample(temp.AsSpan(0, read), _sampleRate);
            SampleLoaded?.Invoke(slotIndex);
        }
    }
}
