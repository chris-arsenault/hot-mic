using NAudio.Dsp;

namespace HotMic.Core.Dsp;

public sealed class BiquadFilter
{
    private FilterMode _mode;
    private float _sampleRate;
    private float _freq;
    private float _gainDb;
    private float _q;
    private BiQuadFilter? _filter;

    public void SetLowShelf(float sampleRate, float freq, float gainDb, float q)
    {
        _mode = FilterMode.LowShelf;
        _sampleRate = sampleRate;
        _freq = freq;
        _gainDb = gainDb;
        _q = q;
        // NAudio's LowShelf signature: (sampleRate, cutoffFrequency, shelfSlope, dbGain)
        _filter = BiQuadFilter.LowShelf(sampleRate, freq, q, gainDb);
    }

    public void SetHighShelf(float sampleRate, float freq, float gainDb, float q)
    {
        _mode = FilterMode.HighShelf;
        _sampleRate = sampleRate;
        _freq = freq;
        _gainDb = gainDb;
        _q = q;
        // NAudio's HighShelf signature: (sampleRate, cutoffFrequency, shelfSlope, dbGain)
        _filter = BiQuadFilter.HighShelf(sampleRate, freq, q, gainDb);
    }

    public void SetPeaking(float sampleRate, float freq, float gainDb, float q)
    {
        _mode = FilterMode.Peaking;
        _sampleRate = sampleRate;
        _freq = freq;
        _gainDb = gainDb;
        _q = q;
        _filter = BiQuadFilter.PeakingEQ(sampleRate, freq, q, gainDb);
    }

    public float Process(float input)
    {
        if (_filter is null)
        {
            return input;
        }

        return _filter.Transform(input);
    }

    public void Reset()
    {
        _filter = _mode switch
        {
            FilterMode.LowShelf => BiQuadFilter.LowShelf(_sampleRate, _freq, _q, _gainDb),
            FilterMode.HighShelf => BiQuadFilter.HighShelf(_sampleRate, _freq, _q, _gainDb),
            FilterMode.Peaking => BiQuadFilter.PeakingEQ(_sampleRate, _freq, _q, _gainDb),
            _ => null
        };
    }

    private enum FilterMode
    {
        None,
        LowShelf,
        HighShelf,
        Peaking
    }
}
