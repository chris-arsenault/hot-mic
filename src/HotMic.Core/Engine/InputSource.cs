using HotMic.Core.Threading;
using System.Threading;

namespace HotMic.Core.Engine;

internal sealed class InputSource
{
    private readonly LockFreeRingBuffer _buffer;
    private long _underflowSamples;

    public InputSource(int capacity)
    {
        _buffer = new LockFreeRingBuffer(capacity);
    }

    public LockFreeRingBuffer Buffer => _buffer;

    public int BufferedSamples => _buffer.AvailableRead;

    public int Capacity => _buffer.Capacity;

    public long UnderflowSamples => Interlocked.Read(ref _underflowSamples);

    public int Read(Span<float> destination)
    {
        return _buffer.Read(destination);
    }

    public void RecordUnderflow(int missingSamples)
    {
        if (missingSamples <= 0)
        {
            return;
        }

        Interlocked.Add(ref _underflowSamples, missingSamples);
    }
}
