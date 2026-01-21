namespace HotMic.Core.Plugins;

public readonly struct AnalysisSignalSource
{
    private static readonly float[] EmptyBuffer = new float[1];
    private readonly float[] _buffer;
    private readonly int _mask;

    public static AnalysisSignalSource Empty => new AnalysisSignalSource(EmptyBuffer, 0);

    public AnalysisSignalSource(float[] buffer, int mask)
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

public readonly struct AnalysisSignalWriter
{
    private readonly AnalysisSignalBus? _bus;
    private readonly int _producerIndex;
    private readonly AnalysisSignalMask _allowedSignals;

    public AnalysisSignalWriter(AnalysisSignalBus? bus, int producerIndex, AnalysisSignalMask allowedSignals)
    {
        _bus = bus;
        _producerIndex = producerIndex;
        _allowedSignals = allowedSignals;
    }

    public bool IsEnabled => _bus is not null && _allowedSignals != AnalysisSignalMask.None;

    public AnalysisSignalMask AllowedSignals => _allowedSignals;

    public void WriteBlock(AnalysisSignalId signal, long sampleTime, ReadOnlySpan<float> data)
    {
        if (_bus is null || data.IsEmpty)
        {
            return;
        }

        var mask = (AnalysisSignalMask)(1 << (int)signal);
        if ((_allowedSignals & mask) == 0)
        {
            return;
        }

        _bus.WriteBlock(_producerIndex, signal, sampleTime, data);
    }
}
