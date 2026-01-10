using System.Collections.Concurrent;

namespace HotMic.Core.Threading;

public sealed class LockFreeQueue<T>
{
    private readonly ConcurrentQueue<T> _queue = new();

    public void Enqueue(T item)
    {
        _queue.Enqueue(item);
    }

    public bool TryDequeue(out T item)
    {
        return _queue.TryDequeue(out item);
    }
}
