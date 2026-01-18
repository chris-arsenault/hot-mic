using System.Runtime.InteropServices;
using HotMic.Core.Threading;
using NAudio.Wave;

namespace HotMic.Core.Engine;

internal sealed class MonitorWaveProvider : IWaveProvider
{
    private readonly LockFreeRingBuffer _buffer;
    private readonly int _blockSize;
    private readonly float[] _scratch;

    public MonitorWaveProvider(LockFreeRingBuffer buffer, WaveFormat format, int blockSize)
    {
        _buffer = buffer;
        _blockSize = blockSize;
        WaveFormat = format;
        _scratch = new float[blockSize * 2];
    }

    public WaveFormat WaveFormat { get; }

    public int Read(byte[] buffer, int offset, int count)
    {
        var output = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(offset, count));
        int totalSamples = output.Length;
        int processed = 0;

        while (processed < totalSamples)
        {
            int chunk = Math.Min(_scratch.Length, totalSamples - processed);
            var scratchSpan = _scratch.AsSpan(0, chunk);
            int read = _buffer.Read(scratchSpan);
            if (read < chunk)
            {
                scratchSpan.Slice(read).Clear();
            }

            scratchSpan.CopyTo(output.Slice(processed, chunk));
            processed += chunk;
        }

        return count;
    }
}
