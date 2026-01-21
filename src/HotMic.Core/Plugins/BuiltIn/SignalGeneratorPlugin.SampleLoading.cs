using System.Collections.Concurrent;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed partial class SignalGeneratorPlugin
{
    private readonly ConcurrentQueue<SampleLoadRequest> _loadQueue;

    private record SampleLoadRequest(int SlotIndex, float[] Samples, int SampleRate);

    /// <summary>
    /// Event raised when a sample is loaded or captured, for UI to trigger auto-save.
    /// </summary>
    public event Action<int>? SampleLoaded;

    /// <summary>
    /// Queue a sample file for loading into a slot (call from UI thread).
    /// </summary>
    public void LoadSampleAsync(int slotIndex, float[] samples, int sampleRate)
    {
        if (slotIndex < 0 || slotIndex >= SlotCount) return;
        _loadQueue.Enqueue(new SampleLoadRequest(slotIndex, samples, sampleRate));
    }

    /// <summary>
    /// Get sample data for persistence (call from UI thread).
    /// Returns null if no sample loaded.
    /// </summary>
    public (float[] Samples, int SampleRate)? GetSampleData(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= SlotCount) return null;
        return _slots[slotIndex].GetSampleData();
    }

    /// <summary>
    /// Check if a sample is loaded in the specified slot.
    /// </summary>
    public bool HasSample(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= SlotCount) return false;
        return _slots[slotIndex].HasSample();
    }

    /// <summary>
    /// Reset sample playback position to start.
    /// </summary>
    public void ResetSamplePlayback(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= SlotCount) return;
        _slots[slotIndex].ResetSamplePlayback();
    }

    private void ProcessLoadQueue()
    {
        while (_loadQueue.TryDequeue(out var request))
        {
            if (request.SlotIndex >= 0 && request.SlotIndex < SlotCount)
            {
                _slots[request.SlotIndex].LoadSample(request.Samples, request.SampleRate);
                SampleLoaded?.Invoke(request.SlotIndex);
            }
        }
    }
}
