namespace HotMic.Core.Dsp.Spectrogram;

/// <summary>
/// Tracks an adaptive noise floor estimate for spectrogram dynamic range.
/// </summary>
public sealed class SpectrogramDynamicRangeTracker
{
    private const float Percentile = 0.1f;

    private float[] _scratch = Array.Empty<float>();
    private float _floorDb = -80f;

    /// <summary>
    /// Ensure internal buffers are sized for the provided bin count.
    /// </summary>
    public void EnsureCapacity(int bins)
    {
        if (bins <= 0)
        {
            return;
        }

        if (_scratch.Length != bins)
        {
            _scratch = new float[bins];
        }
    }

    /// <summary>
    /// Reset the tracked floor to a specified value.
    /// </summary>
    public void Reset(float floorDb)
    {
        _floorDb = floorDb;
    }

    /// <summary>
    /// Update the floor estimate from the provided magnitudes and return the smoothed value.
    /// </summary>
    public float Update(ReadOnlySpan<float> magnitudes, float adaptRate)
    {
        int bins = Math.Min(magnitudes.Length, _scratch.Length);
        if (bins == 0)
        {
            return _floorDb;
        }

        for (int i = 0; i < bins; i++)
        {
            _scratch[i] = DspUtils.LinearToDb(magnitudes[i]);
        }

        return UpdateFromScratch(bins, adaptRate);
    }

    /// <summary>
    /// Update the floor estimate from pre-computed dB values.
    /// </summary>
    public float UpdateDb(ReadOnlySpan<float> magnitudesDb, float adaptRate)
    {
        int bins = Math.Min(magnitudesDb.Length, _scratch.Length);
        if (bins == 0)
        {
            return _floorDb;
        }

        magnitudesDb.Slice(0, bins).CopyTo(_scratch);
        return UpdateFromScratch(bins, adaptRate);
    }

    private float UpdateFromScratch(int bins, float adaptRate)
    {
        Array.Sort(_scratch, 0, bins);
        int index = Math.Clamp((int)MathF.Floor((bins - 1) * Percentile), 0, bins - 1);
        float target = _scratch[index];
        _floorDb += (target - _floorDb) * Math.Clamp(adaptRate, 0f, 1f);
        return _floorDb;
    }
}
