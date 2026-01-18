namespace HotMic.Core.Plugins;

public sealed class AnalysisSignalBus
{
    private readonly int _capacity;
    private readonly int _mask;
    private readonly AnalysisSignalProducerBuffer[] _producers;

    public AnalysisSignalBus(int producerCount, int capacitySamples)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(producerCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacitySamples);

        int size = NextPowerOfTwo(capacitySamples);
        _capacity = size;
        _mask = size - 1;
        _producers = new AnalysisSignalProducerBuffer[producerCount];
        for (int i = 0; i < _producers.Length; i++)
        {
            _producers[i] = new AnalysisSignalProducerBuffer(_capacity, _mask);
        }
    }

    public int Capacity => _capacity;

    public int ProducerCount => _producers.Length;

    public AnalysisSignalSource GetSource(int producerIndex, AnalysisSignalId signal)
    {
        if ((uint)producerIndex >= (uint)_producers.Length)
        {
            return AnalysisSignalSource.Empty;
        }

        var producer = _producers[producerIndex];
        return new AnalysisSignalSource(producer.GetSignalBuffer(signal), _mask);
    }

    public void WriteBlock(int producerIndex, AnalysisSignalId signal, long sampleTime, ReadOnlySpan<float> data)
    {
        if ((uint)producerIndex >= (uint)_producers.Length)
        {
            return;
        }

        _producers[producerIndex].WriteBlock(signal, sampleTime, data);
    }

    private static int NextPowerOfTwo(int value)
    {
        int power = 1;
        while (power < value)
        {
            power <<= 1;
        }
        return power;
    }
}

public sealed class AnalysisSignalProducerBuffer
{
    private readonly float[][] _signals;
    private readonly int _mask;
    private readonly int _capacity;

    public AnalysisSignalProducerBuffer(int capacity, int mask)
    {
        _capacity = capacity;
        _mask = mask;
        _signals = new float[(int)AnalysisSignalId.Count][];
        for (int i = 0; i < (int)AnalysisSignalId.Count; i++)
        {
            _signals[i] = new float[_capacity];
        }
    }

    public float[] GetSignalBuffer(AnalysisSignalId signal)
    {
        return _signals[(int)signal];
    }

    public void WriteBlock(AnalysisSignalId signal, long sampleTime, ReadOnlySpan<float> data)
    {
        int count = data.Length;
        if (count == 0)
        {
            return;
        }

        if (sampleTime < 0)
        {
            return;
        }

        int start = (int)(sampleTime & _mask);
        var target = _signals[(int)signal];

        for (int i = 0; i < count; i++)
        {
            int index = (start + i) & _mask;
            target[index] = data[i];
        }
    }
}
