using System;
using HotMic.Core.Plugins;

namespace HotMic.Core.Engine;

/// <summary>
/// Captures audio and analysis signal data from a channel at a plugin point.
/// </summary>
public sealed class CopyBus
{
    private readonly float[] _audio;
    private readonly float[][] _signalBuffers;
    private AnalysisSignalMask _signals;
    private int _latencySamples;
    private long _sampleClock;
    private long _sampleTime;
    private int _length;

    public CopyBus(int blockSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);

        _audio = new float[blockSize];
        _signalBuffers = new float[(int)AnalysisSignalId.Count][];
        for (int i = 0; i < _signalBuffers.Length; i++)
        {
            _signalBuffers[i] = new float[blockSize];
        }
    }

    /// <summary>
    /// Gets the set of analysis signals captured in the current block.
    /// </summary>
    public AnalysisSignalMask Signals => _signals;

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
    /// Gets the captured analysis signal for the current block.
    /// </summary>
    public ReadOnlySpan<float> GetAnalysisSignal(AnalysisSignalId signal)
    {
        var mask = (AnalysisSignalMask)(1 << (int)signal);
        if ((_signals & mask) == 0)
        {
            return ReadOnlySpan<float>.Empty;
        }

        return _signalBuffers[(int)signal].AsSpan(0, _length);
    }

    /// <summary>
    /// Captures audio and analysis signal data from a processing context.
    /// </summary>
    public void Write(ReadOnlySpan<float> audio, in PluginProcessContext context)
    {
        _length = Math.Min(audio.Length, _audio.Length);
        audio.Slice(0, _length).CopyTo(_audio);
        _latencySamples = Math.Max(0, context.CumulativeLatencySamples);
        _sampleClock = context.SampleClock;
        _sampleTime = context.SampleTime;
        _signals = AnalysisSignalMask.None;

        for (int i = 0; i < _signalBuffers.Length; i++)
        {
            CopyAnalysisSignal(context, (AnalysisSignalId)i, _signalBuffers[i], ref _signals);
        }
    }

    private void CopyAnalysisSignal(in PluginProcessContext context, AnalysisSignalId signal, float[] destination, ref AnalysisSignalMask mask)
    {
        if (!context.TryGetAnalysisSignalSource(signal, out var source))
        {
            return;
        }

        var signalMask = (AnalysisSignalMask)(1 << (int)signal);
        mask |= signalMask;

        long sampleTime = _sampleTime;
        for (int i = 0; i < _length; i++)
        {
            destination[i] = source.ReadSample(sampleTime + i);
        }
    }
}
