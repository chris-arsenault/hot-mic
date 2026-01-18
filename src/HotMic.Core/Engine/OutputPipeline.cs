using System.Runtime.InteropServices;
using System.Threading;
using HotMic.Core.Analysis;
using HotMic.Core.Metering;
using HotMic.Core.Threading;
using NAudio.Wave;

namespace HotMic.Core.Engine;

internal sealed class OutputPipeline : IWaveProvider
{
    private readonly LockFreeChannel<ParameterChange> _parameterQueue;
    private readonly int _blockSize;
    private readonly Action<int> _reportOutput;
    private readonly Action<int> _reportUnderflow;
    private readonly Func<bool> _isProcessingEnabled;
    private readonly LufsMeterProcessor _masterLufsLeft;
    private readonly LufsMeterProcessor _masterLufsRight;
    private readonly AnalysisTap _analysisTap;
    private RoutingSnapshot _snapshot;
    private LockFreeRingBuffer? _monitorBuffer;
    private int _masterMuted;
    private long _sampleClock;

    public OutputPipeline(
        RoutingSnapshot snapshot,
        LockFreeChannel<ParameterChange> parameterQueue,
        int blockSize,
        Action<int> reportOutput,
        Action<int> reportUnderflow,
        Func<bool> isProcessingEnabled,
        LufsMeterProcessor masterLufsLeft,
        LufsMeterProcessor masterLufsRight,
        AnalysisTap analysisTap,
        WaveFormat waveFormat)
    {
        _snapshot = snapshot;
        _parameterQueue = parameterQueue;
        _blockSize = blockSize;
        _reportOutput = reportOutput;
        _reportUnderflow = reportUnderflow;
        _isProcessingEnabled = isProcessingEnabled;
        _masterLufsLeft = masterLufsLeft;
        _masterLufsRight = masterLufsRight;
        _analysisTap = analysisTap;
        WaveFormat = waveFormat;
    }

    public WaveFormat WaveFormat { get; }

    public void UpdateSnapshot(RoutingSnapshot snapshot)
    {
        Volatile.Write(ref _snapshot, snapshot);
    }

    public void ResetSampleClock()
    {
        Interlocked.Exchange(ref _sampleClock, 0);
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        ApplyParameterChanges();

        var output = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(offset, count));
        int totalFrames = output.Length / 2;
        int processed = 0;
        _reportOutput(totalFrames);

        if (!_isProcessingEnabled())
        {
            output.Clear();
            return count;
        }

        long sampleClock = _sampleClock;
        while (processed < totalFrames)
        {
            int chunk = Math.Min(_blockSize, totalFrames - processed);
            var snapshot = Volatile.Read(ref _snapshot);
            var routing = snapshot.Routing;
            routing.BeginBlock(sampleClock);

            var channels = snapshot.Channels;
            var buffers = snapshot.Buffers;
            bool soloActive = false;
            for (int i = 0; i < channels.Length; i++)
            {
                if (channels[i].IsSoloed)
                {
                    soloActive = true;
                    break;
                }
            }

            for (int i = 0; i < buffers.Length; i++)
            {
                buffers[i].AsSpan(0, chunk).Clear();
            }

            var order = snapshot.ProcessingOrder;
            for (int i = 0; i < order.Length; i++)
            {
                int channelIndex = order[i];
                if ((uint)channelIndex >= (uint)channels.Length)
                {
                    continue;
                }

                var channel = channels[channelIndex];
                var channelSpan = buffers[channelIndex].AsSpan(0, chunk);
                int latencySamples = channel.Process(channelSpan, soloActive && !channel.IsSoloed, sampleClock, routing);
                routing.PublishChannelOutput(channelIndex, buffers[channelIndex], chunk, latencySamples);
            }

            var outputSlice = output.Slice(processed * 2, chunk * 2);
            bool muted = Volatile.Read(ref _masterMuted) != 0;
            var outputBus = routing.OutputBus;

            if (muted || !outputBus.HasData || outputBus.Length < chunk)
            {
                outputSlice.Clear();
                if (!muted)
                {
                    _reportUnderflow(chunk);
                }
            }
            else
            {
                var left = outputBus.Left;
                var right = outputBus.Right;
                for (int i = 0; i < chunk; i++)
                {
                    int baseIndex = i * 2;
                    outputSlice[baseIndex] = left[i];
                    outputSlice[baseIndex + 1] = right[i];
                }
            }

            _masterLufsLeft.ProcessInterleaved(outputSlice, 2, 0);
            _masterLufsRight.ProcessInterleaved(outputSlice, 2, 1);

            if (outputBus.HasData)
            {
                var analysisBuffer = outputBus.Mode == OutputSendMode.Right ? outputBus.Right : outputBus.Left;
                _analysisTap.Capture(analysisBuffer, 0);
            }

            var monitorBuffer = Volatile.Read(ref _monitorBuffer);
            monitorBuffer?.Write(outputSlice);
            processed += chunk;
            sampleClock += chunk;
        }

        _sampleClock = sampleClock;

        return count;
    }

    public void SetMonitorBuffer(LockFreeRingBuffer monitorBuffer)
    {
        Volatile.Write(ref _monitorBuffer, monitorBuffer);
    }

    public void SetMasterMute(bool muted)
    {
        Volatile.Write(ref _masterMuted, muted ? 1 : 0);
    }

    private void ApplyParameterChanges()
    {
        var channels = Volatile.Read(ref _snapshot).Channels;
        while (_parameterQueue.TryDequeue(out var change))
        {
            if ((uint)change.ChannelId >= (uint)channels.Length)
            {
                continue;
            }

            var channel = channels[change.ChannelId];
            switch (change.Type)
            {
                case ParameterType.InputGainDb:
                    channel.SetInputGainDb(change.Value);
                    break;
                case ParameterType.OutputGainDb:
                    channel.SetOutputGainDb(change.Value);
                    break;
                case ParameterType.Mute:
                    channel.SetMuted(change.Value >= 0.5f);
                    break;
                case ParameterType.Solo:
                    channel.SetSoloed(change.Value >= 0.5f);
                    break;
                case ParameterType.PluginBypass:
                    ApplyPluginBypass(channel, change.PluginInstanceId, change.Value >= 0.5f);
                    break;
                case ParameterType.PluginParameter:
                    ApplyPluginParameter(channel, change.PluginInstanceId, change.ParameterIndex, change.Value);
                    break;
                case ParameterType.PluginCommand:
                    channel.PluginChain.TryHandleCommand(change.PluginInstanceId, change.Command);
                    break;
            }
        }
    }

    private static void ApplyPluginBypass(ChannelStrip channel, int pluginInstanceId, bool bypass)
    {
        if (channel.PluginChain.TryGetSlotById(pluginInstanceId, out var slot, out _)
            && slot is not null)
        {
            slot.Plugin.IsBypassed = bypass;
        }
    }

    private static void ApplyPluginParameter(ChannelStrip channel, int pluginInstanceId, int parameterIndex, float value)
    {
        if (channel.PluginChain.TryGetSlotById(pluginInstanceId, out var slot, out _)
            && slot is not null)
        {
            slot.Plugin.SetParameter(parameterIndex, value);
        }
    }
}
