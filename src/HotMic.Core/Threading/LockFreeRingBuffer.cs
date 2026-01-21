using System.Threading;

namespace HotMic.Core.Threading;

public sealed class LockFreeRingBuffer
{
    private readonly float[] _buffer;
    private readonly int _mask;
    private long _writeIndex;
    private long _readIndex;
    private long _droppedSamples;

    public LockFreeRingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        int size = NextPowerOfTwo(capacity);
        _buffer = new float[size];
        _mask = size - 1;
    }

    public int Capacity => _buffer.Length;

    /// <summary>
    /// Total samples dropped because the buffer was full.
    /// </summary>
    public long DroppedSamples => Volatile.Read(ref _droppedSamples);

    public int AvailableRead
    {
        get
        {
            long write = Volatile.Read(ref _writeIndex);
            long read = Volatile.Read(ref _readIndex);
            long available = write - read;
            if (available < 0)
            {
                return 0;
            }

            return (int)Math.Min(_buffer.Length, available);
        }
    }

    public int Write(ReadOnlySpan<float> data)
    {
        long write = Volatile.Read(ref _writeIndex);
        long read = Volatile.Read(ref _readIndex);
        long available = Math.Max(0, write - read);
        int free = _buffer.Length - (int)Math.Min(_buffer.Length, available);
        int toWrite = Math.Min(data.Length, free);
        int dropped = data.Length - toWrite;

        for (int i = 0; i < toWrite; i++)
        {
            _buffer[(int)((write + i) & _mask)] = data[i];
        }

        Volatile.Write(ref _writeIndex, write + toWrite);
        if (dropped > 0)
        {
            Interlocked.Add(ref _droppedSamples, dropped);
        }
        return toWrite;
    }

    public int Read(Span<float> destination)
    {
        long write = Volatile.Read(ref _writeIndex);
        long read = Volatile.Read(ref _readIndex);
        long available = Math.Max(0, write - read);
        int toRead = Math.Min(destination.Length, (int)Math.Min(_buffer.Length, available));

        for (int i = 0; i < toRead; i++)
        {
            destination[i] = _buffer[(int)((read + i) & _mask)];
        }

        Volatile.Write(ref _readIndex, read + toRead);
        return toRead;
    }

    public int Skip(int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        long write = Volatile.Read(ref _writeIndex);
        long read = Volatile.Read(ref _readIndex);
        long available = Math.Max(0, write - read);
        int toSkip = Math.Min(count, (int)Math.Min(_buffer.Length, available));
        if (toSkip <= 0)
        {
            return 0;
        }

        Volatile.Write(ref _readIndex, read + toSkip);
        return toSkip;
    }

    public void Clear()
    {
        Volatile.Write(ref _writeIndex, 0);
        Volatile.Write(ref _readIndex, 0);
        Volatile.Write(ref _droppedSamples, 0);
    }

    private static int NextPowerOfTwo(int value)
    {
        int power = 1;
        while (power < value)
        {
            power <<= 1;
        }

        return power;
    }
}
