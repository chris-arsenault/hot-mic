using System;
using HotMic.Core.Plugins;

namespace HotMic.Core.Engine;

/// <summary>
/// Holds per-block routing state for channel inputs, copies, and output send.
/// </summary>
public sealed class RoutingContext
{
    private readonly ChannelRoutingState[] _channels;
    private readonly OutputBus _outputBus;
    private long _sampleClock;

    public RoutingContext(int channelCount, int sampleRate, int blockSize)
    {
        if (channelCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channelCount));
        }

        ChannelCount = channelCount;
        SampleRate = sampleRate;
        BlockSize = blockSize;
        _channels = new ChannelRoutingState[channelCount];
        for (int i = 0; i < channelCount; i++)
        {
            _channels[i] = new ChannelRoutingState(blockSize);
        }

        _outputBus = new OutputBus(blockSize);
    }

    /// <summary>
    /// Gets the number of channels in the routing graph.
    /// </summary>
    public int ChannelCount { get; }

    /// <summary>
    /// Gets the sample rate for the current graph.
    /// </summary>
    public int SampleRate { get; }

    /// <summary>
    /// Gets the block size for the current graph.
    /// </summary>
    public int BlockSize { get; }

    /// <summary>
    /// Gets the current sample clock for the active block.
    /// </summary>
    public long SampleClock => _sampleClock;

    /// <summary>
    /// Gets the main output bus for the current block.
    /// </summary>
    public OutputBus OutputBus => _outputBus;

    /// <summary>
    /// Starts a new processing block and resets per-channel routing state.
    /// </summary>
    public void BeginBlock(long sampleClock)
    {
        _sampleClock = sampleClock;
        _outputBus.BeginBlock(sampleClock);
        for (int i = 0; i < _channels.Length; i++)
        {
            _channels[i].ResetOutput(sampleClock);
        }
    }

    /// <summary>
    /// Assigns the live input source for a channel.
    /// </summary>
    internal void SetInputSource(int channelId, InputSource? source)
    {
        if ((uint)channelId >= (uint)_channels.Length)
        {
            return;
        }

        _channels[channelId].InputSource = source;
    }

    /// <summary>
    /// Reads the input stream for a channel into a buffer.
    /// </summary>
    public int ReadInput(int channelId, Span<float> buffer)
    {
        if ((uint)channelId >= (uint)_channels.Length)
        {
            buffer.Clear();
            return 0;
        }

        var source = _channels[channelId].InputSource;
        if (source is null)
        {
            buffer.Clear();
            return 0;
        }

        int read = source.Read(buffer);
        if (read < buffer.Length)
        {
            buffer.Slice(read).Clear();
            source.RecordUnderflow(buffer.Length - read);
        }

        return read;
    }

    /// <summary>
    /// Gets the copy bus for a channel.
    /// </summary>
    public CopyBus GetCopyBus(int channelId)
    {
        if ((uint)channelId >= (uint)_channels.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(channelId));
        }

        return _channels[channelId].CopyBus;
    }

    /// <summary>
    /// Publishes the processed output for a channel.
    /// </summary>
    public void PublishChannelOutput(int channelId, float[] buffer, int length, int latencySamples)
    {
        if ((uint)channelId >= (uint)_channels.Length)
        {
            return;
        }

        _channels[channelId].SetOutput(buffer, length, latencySamples, _sampleClock);
    }

    /// <summary>
    /// Tries to read the current output for a channel.
    /// </summary>
    public bool TryGetChannelOutput(int channelId, out ReadOnlySpan<float> buffer, out int length, out int latencySamples)
    {
        if ((uint)channelId >= (uint)_channels.Length)
        {
            buffer = ReadOnlySpan<float>.Empty;
            length = 0;
            latencySamples = 0;
            return false;
        }

        return _channels[channelId].TryGetOutput(_sampleClock, out buffer, out length, out latencySamples);
    }

    private sealed class ChannelRoutingState
    {
        private readonly CopyBus _copyBus;
        private float[]? _outputBuffer;
        private int _outputLength;
        private int _outputLatency;
        private long _outputSampleClock;

        public ChannelRoutingState(int blockSize)
        {
            _copyBus = new CopyBus(blockSize);
            _outputSampleClock = -1;
        }

        public InputSource? InputSource { get; set; }

        public CopyBus CopyBus => _copyBus;

        public void ResetOutput(long sampleClock)
        {
            _outputBuffer = null;
            _outputLength = 0;
            _outputLatency = 0;
            _outputSampleClock = sampleClock - 1;
        }

        public void SetOutput(float[] buffer, int length, int latencySamples, long sampleClock)
        {
            _outputBuffer = buffer;
            _outputLength = length;
            _outputLatency = Math.Max(0, latencySamples);
            _outputSampleClock = sampleClock;
        }

        public bool TryGetOutput(long sampleClock, out ReadOnlySpan<float> buffer, out int length, out int latencySamples)
        {
            if (_outputBuffer is null || _outputSampleClock != sampleClock)
            {
                buffer = ReadOnlySpan<float>.Empty;
                length = 0;
                latencySamples = 0;
                return false;
            }

            length = _outputLength;
            latencySamples = _outputLatency;
            buffer = _outputBuffer.AsSpan(0, length);
            return true;
        }
    }
}
