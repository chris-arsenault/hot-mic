namespace HotMic.Core.Plugins;

public readonly struct SidechainSource
{
    private static readonly float[] EmptyBuffer = new float[1];
    private readonly float[] _buffer;
    private readonly int _mask;

    public static SidechainSource Empty => new SidechainSource(EmptyBuffer, 0);

    public SidechainSource(float[] buffer, int mask)
    {
        _buffer = buffer;
        _mask = mask;
    }

    public float ReadSample(long sampleTime)
    {
        if (sampleTime < 0)
        {
            return 0f;
        }

        int index = (int)(sampleTime & _mask);
        return _buffer[index];
    }
}

public readonly struct SidechainWriter
{
    private readonly SidechainBus? _bus;
    private readonly int _producerIndex;
    private readonly SidechainSignalMask _allowedSignals;

    public SidechainWriter(SidechainBus? bus, int producerIndex, SidechainSignalMask allowedSignals)
    {
        _bus = bus;
        _producerIndex = producerIndex;
        _allowedSignals = allowedSignals;
    }

    public bool IsEnabled => _bus is not null && _allowedSignals != SidechainSignalMask.None;

    public void WriteBlock(SidechainSignalId signal, long sampleTime, ReadOnlySpan<float> data)
    {
        if (_bus is null || data.IsEmpty)
        {
            return;
        }

        var mask = (SidechainSignalMask)(1 << (int)signal);
        if ((_allowedSignals & mask) == 0)
        {
            return;
        }

        _bus.WriteBlock(_producerIndex, signal, sampleTime, data);
    }
}
