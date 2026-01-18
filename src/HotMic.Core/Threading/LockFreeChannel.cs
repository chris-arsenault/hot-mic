using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace HotMic.Core.Threading;

public sealed class LockFreeChannel<T>
{
    private readonly ConcurrentQueue<T> _queue = new();

    public void Enqueue(T item)
    {
        _queue.Enqueue(item);
    }

    public bool TryDequeue([MaybeNullWhen(false)] out T item)
    {
        return _queue.TryDequeue(out item);
    }
}
