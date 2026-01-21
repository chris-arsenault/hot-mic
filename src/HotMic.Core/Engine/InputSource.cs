using HotMic.Core.Threading;
using System.Threading;

namespace HotMic.Core.Engine;

internal sealed class InputSource
{
    private readonly LockFreeRingBuffer _buffer;
    private long _underflowSamples;
    private long _overflowSamples;

    public InputSource(int capacity)
    {
        _buffer = new LockFreeRingBuffer(capacity);
    }

    public LockFreeRingBuffer Buffer => _buffer;

    public int BufferedSamples => _buffer.AvailableRead;

    public int Capacity => _buffer.Capacity;

    public long UnderflowSamples => Interlocked.Read(ref _underflowSamples);

    public long OverflowSamples => Interlocked.Read(ref _overflowSamples);

    public int Read(Span<float> destination)
    {
        TrimOverflow(destination.Length);
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

    private void TrimOverflow(int readSize)
    {
        if (readSize <= 0)
        {
            return;
        }

        // Keep latency bounded by discarding oldest samples when the buffer backs up.
        int capacity = _buffer.Capacity;
        int available = _buffer.AvailableRead;
        int highWater = capacity * 3 / 4;
        if (available <= highWater)
        {
            return;
        }

        int target = Math.Max(readSize, capacity / 2);
        int excess = available - target;
        if (excess <= 0)
        {
            return;
        }

        int skipped = _buffer.Skip(excess);
        if (skipped > 0)
        {
            Interlocked.Add(ref _overflowSamples, skipped);
        }
    }
}
