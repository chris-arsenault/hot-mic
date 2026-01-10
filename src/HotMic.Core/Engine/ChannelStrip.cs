using System.Runtime.CompilerServices;
using System.Threading;
using HotMic.Core.Metering;
using HotMic.Core.Plugins;

namespace HotMic.Core.Engine;

public sealed class ChannelStrip
{
    private readonly PluginChain _pluginChain;
    private readonly MeterProcessor _inputMeter;
    private readonly MeterProcessor _outputMeter;
    private float _inputGain = 1f;
    private float _outputGain = 1f;
    private int _isMuted;
    private int _isSoloed;

    public ChannelStrip(int sampleRate, int blockSize)
    {
        _pluginChain = new PluginChain(5);
        _inputMeter = new MeterProcessor(sampleRate);
        _outputMeter = new MeterProcessor(sampleRate);
    }

    public MeterProcessor InputMeter => _inputMeter;

    public MeterProcessor OutputMeter => _outputMeter;

    public PluginChain PluginChain => _pluginChain;

    public void SetInputGainDb(float gainDb)
    {
        _inputGain = DbToLinear(gainDb);
    }

    public void SetOutputGainDb(float gainDb)
    {
        _outputGain = DbToLinear(gainDb);
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

    public void Process(Span<float> buffer, bool globalMute)
    {
        if (globalMute || Volatile.Read(ref _isMuted) == 1)
        {
            buffer.Clear();
            _inputMeter.Process(buffer);
            _outputMeter.Process(buffer);
            return;
        }

        ApplyGain(buffer, _inputGain);
        _inputMeter.Process(buffer);
        _pluginChain.Process(buffer);
        ApplyGain(buffer, _outputGain);
        _outputMeter.Process(buffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyGain(Span<float> buffer, float gain)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] *= gain;
        }
    }

    private static float DbToLinear(float db)
    {
        return MathF.Pow(10f, db / 20f);
    }
}
