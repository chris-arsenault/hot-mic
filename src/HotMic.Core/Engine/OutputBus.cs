using System;

namespace HotMic.Core.Engine;

/// <summary>
/// Stores the mono output send for a block and exposes it as stereo.
/// </summary>
public sealed class OutputBus
{
    private readonly float[] _left;
    private readonly float[] _right;
    private long _sampleClock;
    private int _hasWriter;
    private int _latencySamples;
    private int _length;
    private OutputSendMode _mode;

    public OutputBus(int blockSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);

        _left = new float[blockSize];
        _right = new float[blockSize];
    }

    /// <summary>
    /// Gets a value indicating whether any channel wrote output this block.
    /// </summary>
    public bool HasData => _hasWriter != 0;

    /// <summary>
    /// Gets the cumulative latency (in samples) for the current output.
    /// </summary>
    public int LatencySamples => _latencySamples;

    /// <summary>
    /// Gets the number of valid samples in the current block.
    /// </summary>
    public int Length => _length;

    /// <summary>
    /// Gets the output routing mode for the current block.
    /// </summary>
    public OutputSendMode Mode => _mode;

    /// <summary>
    /// Gets the left channel samples for the current block.
    /// </summary>
    public ReadOnlySpan<float> Left => _left.AsSpan(0, _length);

    /// <summary>
    /// Gets the right channel samples for the current block.
    /// </summary>
    public ReadOnlySpan<float> Right => _right.AsSpan(0, _length);

    /// <summary>
    /// Resets the bus for a new audio block.
    /// </summary>
    public void BeginBlock(long sampleClock)
    {
        _sampleClock = sampleClock;
        _hasWriter = 0;
        _length = 0;
        _latencySamples = 0;
        _mode = OutputSendMode.Both;
    }

    /// <summary>
    /// Writes the mono output into the bus if no other sender wrote this block.
    /// </summary>
    public bool TryWrite(ReadOnlySpan<float> buffer, OutputSendMode mode, int latencySamples, long sampleClock)
    {
        if (buffer.IsEmpty || sampleClock != _sampleClock)
        {
            return false;
        }

        if (_hasWriter != 0)
        {
            return false;
        }

        _hasWriter = 1;
        _length = buffer.Length;
        _latencySamples = Math.Max(0, latencySamples);
        _mode = mode;

        switch (mode)
        {
            case OutputSendMode.Left:
                for (int i = 0; i < buffer.Length; i++)
                {
                    _left[i] = buffer[i];
                    _right[i] = 0f;
                }
                break;
            case OutputSendMode.Right:
                for (int i = 0; i < buffer.Length; i++)
                {
                    _left[i] = 0f;
                    _right[i] = buffer[i];
                }
                break;
            case OutputSendMode.Both:
            default:
                for (int i = 0; i < buffer.Length; i++)
                {
                    float sample = buffer[i];
                    _left[i] = sample;
                    _right[i] = sample;
                }
                break;
        }

        return true;
    }
}
