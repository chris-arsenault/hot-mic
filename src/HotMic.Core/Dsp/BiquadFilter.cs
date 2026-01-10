using NAudio.Dsp;

namespace HotMic.Core.Dsp;

public sealed class BiquadFilter
{
    private BiQuadFilter? _filter;

    public void SetLowShelf(float sampleRate, float freq, float gainDb, float q)
    {
        _filter = BiQuadFilter.LowShelf(sampleRate, freq, gainDb, q);
    }

    public void SetHighShelf(float sampleRate, float freq, float gainDb, float q)
    {
        _filter = BiQuadFilter.HighShelf(sampleRate, freq, gainDb, q);
    }

    public void SetPeaking(float sampleRate, float freq, float gainDb, float q)
    {
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
        _filter?.Reset();
    }
}
