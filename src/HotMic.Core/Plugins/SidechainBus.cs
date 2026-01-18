namespace HotMic.Core.Plugins;

public sealed class SidechainBus
{
    private readonly int _capacity;
    private readonly int _mask;
    private readonly SidechainProducerBuffer[] _producers;

    public SidechainBus(int producerCount, int capacitySamples)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(producerCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacitySamples);

        int size = NextPowerOfTwo(capacitySamples);
        _capacity = size;
        _mask = size - 1;
        _producers = new SidechainProducerBuffer[producerCount];
        for (int i = 0; i < _producers.Length; i++)
        {
            _producers[i] = new SidechainProducerBuffer(_capacity, _mask);
        }
    }

    public int Capacity => _capacity;

    public int ProducerCount => _producers.Length;

    public SidechainSource GetSource(int producerIndex, SidechainSignalId signal)
    {
        if ((uint)producerIndex >= (uint)_producers.Length)
        {
            return SidechainSource.Empty;
        }

        var producer = _producers[producerIndex];
        return new SidechainSource(producer.GetSignalBuffer(signal), _mask);
    }

    public void WriteBlock(int producerIndex, SidechainSignalId signal, long sampleTime, ReadOnlySpan<float> data)
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

public sealed class SidechainProducerBuffer
{
    private readonly float[][] _signals;
    private readonly int _mask;
    private readonly int _capacity;

    public SidechainProducerBuffer(int capacity, int mask)
    {
        _capacity = capacity;
        _mask = mask;
        _signals = new float[(int)SidechainSignalId.Count][];
        for (int i = 0; i < (int)SidechainSignalId.Count; i++)
        {
            _signals[i] = new float[_capacity];
        }
    }

    public float[] GetSignalBuffer(SidechainSignalId signal)
    {
        return _signals[(int)signal];
    }

    public void WriteBlock(SidechainSignalId signal, long sampleTime, ReadOnlySpan<float> data)
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
