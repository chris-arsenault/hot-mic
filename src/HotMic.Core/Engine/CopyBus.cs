using System;
using HotMic.Core.Plugins;

namespace HotMic.Core.Engine;

/// <summary>
/// Captures audio and sidechain data from a channel at a plugin point.
/// </summary>
public sealed class CopyBus
{
    private readonly float[] _audio;
    private readonly float[] _speech;
    private readonly float[] _voiced;
    private readonly float[] _unvoiced;
    private readonly float[] _sibilance;
    private SidechainSignalMask _signals;
    private int _latencySamples;
    private long _sampleClock;
    private long _sampleTime;
    private int _length;

    public CopyBus(int blockSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);

        _audio = new float[blockSize];
        _speech = new float[blockSize];
        _voiced = new float[blockSize];
        _unvoiced = new float[blockSize];
        _sibilance = new float[blockSize];
    }

    /// <summary>
    /// Gets the set of sidechain signals captured in the current block.
    /// </summary>
    public SidechainSignalMask Signals => _signals;

    /// <summary>
    /// Gets the cumulative latency (in samples) for the captured block.
    /// </summary>
    public int LatencySamples => _latencySamples;

    /// <summary>
    /// Gets the current sample clock for the captured block.
    /// </summary>
    public long SampleClock => _sampleClock;

    /// <summary>
    /// Gets the sample time (monotonic) for the captured block.
    /// </summary>
    public long SampleTime => _sampleTime;

    /// <summary>
    /// Gets the number of valid samples in the current block.
    /// </summary>
    public int Length => _length;

    /// <summary>
    /// Gets the captured audio for the current block.
    /// </summary>
    public ReadOnlySpan<float> Audio => _audio.AsSpan(0, _length);

    /// <summary>
    /// Gets the captured sidechain signal for the current block.
    /// </summary>
    public ReadOnlySpan<float> GetSidechain(SidechainSignalId signal)
    {
        return signal switch
        {
            SidechainSignalId.SpeechPresence => _speech.AsSpan(0, _length),
            SidechainSignalId.VoicedProbability => _voiced.AsSpan(0, _length),
            SidechainSignalId.UnvoicedEnergy => _unvoiced.AsSpan(0, _length),
            SidechainSignalId.SibilanceEnergy => _sibilance.AsSpan(0, _length),
            _ => ReadOnlySpan<float>.Empty
        };
    }

    /// <summary>
    /// Captures audio and sidechain data from a processing context.
    /// </summary>
    public void Write(ReadOnlySpan<float> audio, in PluginProcessContext context)
    {
        _length = Math.Min(audio.Length, _audio.Length);
        audio.Slice(0, _length).CopyTo(_audio);
        _latencySamples = Math.Max(0, context.CumulativeLatencySamples);
        _sampleClock = context.SampleClock;
        _sampleTime = context.SampleTime;
        _signals = SidechainSignalMask.None;

        CopySidechainSignal(context, SidechainSignalId.SpeechPresence, _speech, ref _signals);
        CopySidechainSignal(context, SidechainSignalId.VoicedProbability, _voiced, ref _signals);
        CopySidechainSignal(context, SidechainSignalId.UnvoicedEnergy, _unvoiced, ref _signals);
        CopySidechainSignal(context, SidechainSignalId.SibilanceEnergy, _sibilance, ref _signals);
    }

    private void CopySidechainSignal(in PluginProcessContext context, SidechainSignalId signal, float[] destination, ref SidechainSignalMask mask)
    {
        if (!context.TryGetSidechainSource(signal, out var source))
        {
            return;
        }

        var signalMask = (SidechainSignalMask)(1 << (int)signal);
        mask |= signalMask;

        long sampleTime = _sampleTime;
        for (int i = 0; i < _length; i++)
        {
            destination[i] = source.ReadSample(sampleTime + i);
        }
    }
}
