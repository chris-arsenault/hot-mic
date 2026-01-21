namespace HotMic.Core.Dsp.Analysis.Speech;

/// <summary>
/// Stress classification for a syllable.
/// </summary>
public enum StressLevel : byte
{
    Unstressed = 0,
    Secondary = 1,
    Primary = 2
}

/// <summary>
/// Detects syllable-level stress based on energy, pitch, and duration features.
/// </summary>
public sealed class StressDetector
{
    private const int FeatureWindowSize = 64;

    // Running statistics for normalization
    private float _meanEnergyDb;
    private float _meanPitchSemitones;
    private int _sampleCount;

    // Feature history for computing relative values
    private readonly float[] _energyHistory = new float[FeatureWindowSize];
    private readonly float[] _pitchHistory = new float[FeatureWindowSize];
    private int _historyIndex;
    private int _historyCount;

    /// <summary>
    /// Energy threshold above mean for primary stress (dB).
    /// </summary>
    public float PrimaryEnergyThresholdDb { get; set; } = 3f;

    /// <summary>
    /// Energy threshold above mean for secondary stress (dB).
    /// </summary>
    public float SecondaryEnergyThresholdDb { get; set; } = 1.5f;

    /// <summary>
    /// Pitch threshold above mean for stress indication (semitones).
    /// </summary>
    public float PitchAccentThresholdSemitones { get; set; } = 2f;

    /// <summary>
    /// Update running statistics with a new frame.
    /// Call this for every voiced frame to maintain accurate baselines.
    /// </summary>
    /// <param name="energyDb">Frame energy in dB.</param>
    /// <param name="pitchHz">Pitch in Hz (0 if unvoiced).</param>
    public void UpdateStatistics(float energyDb, float pitchHz)
    {
        if (pitchHz <= 0f)
        {
            return;
        }

        // Convert pitch to semitones
        float pitchSemitones = 12f * MathF.Log2(pitchHz / 100f);

        // Update history buffer
        _energyHistory[_historyIndex] = energyDb;
        _pitchHistory[_historyIndex] = pitchSemitones;
        _historyIndex = (_historyIndex + 1) % FeatureWindowSize;
        _historyCount = Math.Min(_historyCount + 1, FeatureWindowSize);

        // Update running mean (exponential moving average)
        if (_sampleCount == 0)
        {
            _meanEnergyDb = energyDb;
            _meanPitchSemitones = pitchSemitones;
        }
        else
        {
            float alpha = 0.01f; // Slow adaptation
            _meanEnergyDb = alpha * energyDb + (1f - alpha) * _meanEnergyDb;
            _meanPitchSemitones = alpha * pitchSemitones + (1f - alpha) * _meanPitchSemitones;
        }

        _sampleCount++;
    }

    /// <summary>
    /// Classify stress level for a syllable.
    /// Call this when a syllable is detected.
    /// </summary>
    /// <param name="syllableEnergyDb">Energy at the syllable nucleus.</param>
    /// <param name="syllablePitchHz">Pitch at the syllable nucleus.</param>
    /// <returns>Stress classification.</returns>
    public StressLevel ClassifyStress(float syllableEnergyDb, float syllablePitchHz)
    {
        if (_sampleCount < 10)
        {
            // Not enough data for reliable classification
            return StressLevel.Unstressed;
        }

        // Compute relative energy
        float relativeEnergyDb = syllableEnergyDb - _meanEnergyDb;

        // Compute relative pitch
        float relativePitchSemitones = 0f;
        if (syllablePitchHz > 0f)
        {
            float syllablePitchSemitones = 12f * MathF.Log2(syllablePitchHz / 100f);
            relativePitchSemitones = syllablePitchSemitones - _meanPitchSemitones;
        }

        // Primary stress: high energy AND (pitch accent OR very high energy)
        bool highEnergy = relativeEnergyDb >= PrimaryEnergyThresholdDb;
        bool pitchAccent = relativePitchSemitones >= PitchAccentThresholdSemitones;
        bool veryHighEnergy = relativeEnergyDb >= PrimaryEnergyThresholdDb * 1.5f;

        if (highEnergy && (pitchAccent || veryHighEnergy))
        {
            return StressLevel.Primary;
        }

        // Secondary stress: moderate energy OR pitch accent
        bool moderateEnergy = relativeEnergyDb >= SecondaryEnergyThresholdDb;
        bool lowPitchAccent = relativePitchSemitones >= PitchAccentThresholdSemitones * 0.5f;

        if (moderateEnergy || lowPitchAccent)
        {
            return StressLevel.Secondary;
        }

        return StressLevel.Unstressed;
    }

    /// <summary>
    /// Get current mean energy baseline.
    /// </summary>
    public float MeanEnergyDb => _meanEnergyDb;

    /// <summary>
    /// Get current mean pitch baseline in semitones (relative to 100 Hz).
    /// </summary>
    public float MeanPitchSemitones => _meanPitchSemitones;

    /// <summary>
    /// Reset the detector state.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_energyHistory, 0, _energyHistory.Length);
        Array.Clear(_pitchHistory, 0, _pitchHistory.Length);
        _historyIndex = 0;
        _historyCount = 0;
        _meanEnergyDb = 0f;
        _meanPitchSemitones = 0f;
        _sampleCount = 0;
    }
}
