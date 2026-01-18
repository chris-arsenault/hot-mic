using System.Runtime.CompilerServices;
using System.Threading;
using HotMic.Core.Dsp;
using HotMic.Core.Metering;
using HotMic.Core.Plugins;

namespace HotMic.Core.Engine;

public sealed class ChannelStrip
{
    private readonly PluginChain _pluginChain;
    private readonly MeterProcessor _inputMeter;
    private readonly MeterProcessor _outputMeter;
    private readonly Action<Span<float>> _inputSplitAction;
    private LinearSmoother _inputGainSmoother;
    private LinearSmoother _outputGainSmoother;
    private LinearSmoother _muteSmoother;
    private int _isMuted;
    private int _isSoloed;
    private float _muteTarget = 1f;

    public ChannelStrip(int channelId, int sampleRate, int blockSize)
    {
        ChannelId = channelId;
        _pluginChain = new PluginChain(sampleRate, blockSize);
        _inputMeter = new MeterProcessor(sampleRate);
        _outputMeter = new MeterProcessor(sampleRate);
        _inputSplitAction = ProcessInputSplit;
        _inputGainSmoother.Configure(sampleRate, 5f, 1f);
        _outputGainSmoother.Configure(sampleRate, 5f, 1f);
        _muteSmoother.Configure(sampleRate, 5f, 1f);
    }

    public MeterProcessor InputMeter => _inputMeter;

    public MeterProcessor OutputMeter => _outputMeter;

    public PluginChain PluginChain => _pluginChain;

    public int ChannelId { get; }

    public void SetInputGainDb(float gainDb)
    {
        _inputGainSmoother.SetTarget(DbToLinear(gainDb));
    }

    public void SetOutputGainDb(float gainDb)
    {
        _outputGainSmoother.SetTarget(DbToLinear(gainDb));
    }

    public void SetMuted(bool muted)
    {
        Volatile.Write(ref _isMuted, muted ? 1 : 0);
    }

    public void SetSoloed(bool soloed)
    {
        Volatile.Write(ref _isSoloed, soloed ? 1 : 0);
    }

    public bool IsSoloed => Volatile.Read(ref _isSoloed) == 1;

    public int Process(Span<float> buffer, bool globalMute, long sampleClock, RoutingContext routingContext)
    {
        bool localMute = Volatile.Read(ref _isMuted) == 1;
        float targetMute = (globalMute || localMute) ? 0f : 1f;
        if (MathF.Abs(targetMute - _muteTarget) > 1e-6f)
        {
            _muteTarget = targetMute;
            _muteSmoother.SetTarget(_muteTarget);
        }

        if (_muteTarget <= 0f && !_muteSmoother.IsSmoothing)
        {
            buffer.Clear();
            _inputMeter.Process(buffer);
            _pluginChain.ProcessMeters(buffer);
            _outputMeter.Process(buffer);
            return 0;
        }

        int inputSplitIndex = _pluginChain.InputStageIndex;
        int latencySamples;
        if (inputSplitIndex >= 0)
        {
            latencySamples = _pluginChain.ProcessWithSplit(
                buffer,
                sampleClock,
                ChannelId,
                routingContext,
                inputSplitIndex,
                _inputSplitAction);
        }
        else
        {
            ApplyGain(buffer, ref _inputGainSmoother);
            _inputMeter.Process(buffer);
            latencySamples = _pluginChain.Process(buffer, sampleClock, ChannelId, routingContext);
        }

        ApplyGain(buffer, ref _outputGainSmoother);
        ApplyGain(buffer, ref _muteSmoother);

        if (TryGetOutputSendMode(out var sendMode))
        {
            routingContext.OutputBus.TryWrite(buffer, sendMode, latencySamples, sampleClock);
        }

        _outputMeter.Process(buffer);
        return latencySamples;
    }

    private void ProcessInputSplit(Span<float> buffer)
    {
        ApplyGain(buffer, ref _inputGainSmoother);
        _inputMeter.Process(buffer);
    }

    private bool TryGetOutputSendMode(out OutputSendMode mode)
    {
        var slots = _pluginChain.GetSnapshot();
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot is null)
            {
                continue;
            }

            if (slot.Plugin is IChannelOutputPlugin send && !slot.Plugin.IsBypassed)
            {
                mode = send.OutputMode;
                return true;
            }
        }

        mode = OutputSendMode.Both;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyGain(Span<float> buffer, ref LinearSmoother smoother)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        if (!smoother.IsSmoothing)
        {
            float gain = smoother.Current;
            if (MathF.Abs(gain - 1f) <= 1e-6f)
            {
                return;
            }

            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] *= gain;
            }
            return;
        }

        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] *= smoother.Next();
        }
    }

    private static float DbToLinear(float db)
    {
        return DspUtils.DbToLinear(db);
    }
}
