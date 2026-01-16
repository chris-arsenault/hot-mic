using HotMic.Core.Dsp.Analysis;

namespace HotMic.Core.Dsp.Analysis.Speech;

/// <summary>
/// Analyzes speech clarity based on spectral and voicing characteristics.
/// Provides metrics for articulation quality.
/// </summary>
public sealed class ClarityAnalyzer
{
    private const int HistorySize = 32;

    // History buffers for computing metrics
    private readonly float[] _hnrHistory = new float[HistorySize];
    private readonly float[] _spectralSlopeHistory = new float[HistorySize];
    private readonly VoicingState[] _voicingHistory = new VoicingState[HistorySize];
    private int _historyIndex;
    private int _historyCount;

    // Transition tracking
    private VoicingState _lastVoicing = VoicingState.Silence;
    private int _transitionCount;
    private int _frameSinceReset;

    /// <summary>
    /// Process a frame and update clarity metrics.
    /// </summary>
    /// <param name="hnr">Harmonic-to-noise ratio (linear scale).</param>
    /// <param name="spectralSlope">Spectral slope (dB/kHz).</param>
    /// <param name="voicing">Voicing state.</param>
    public void Process(float hnr, float spectralSlope, VoicingState voicing)
    {
        // Track voicing transitions
        if (voicing != _lastVoicing && _frameSinceReset > 0)
        {
            _transitionCount++;
        }
        _lastVoicing = voicing;
        _frameSinceReset++;

        // Store in history
        _hnrHistory[_historyIndex] = hnr;
        _spectralSlopeHistory[_historyIndex] = spectralSlope;
        _voicingHistory[_historyIndex] = voicing;
        _historyIndex = (_historyIndex + 1) % HistorySize;
        _historyCount = Math.Min(_historyCount + 1, HistorySize);
    }

    /// <summary>
    /// Compute clarity score and component metrics.
    /// </summary>
    /// <param name="vowelClarity">Output: vowel clarity based on HNR (0-100).</param>
    /// <param name="consonantClarity">Output: consonant clarity based on spectral slope (0-100).</param>
    /// <param name="transitionSharpness">Output: voicing transition rate (0-100).</param>
    /// <param name="overallClarity">Output: combined clarity score (0-100).</param>
    public void Compute(
        out float vowelClarity,
        out float consonantClarity,
        out float transitionSharpness,
        out float overallClarity)
    {
        vowelClarity = 0f;
        consonantClarity = 0f;
        transitionSharpness = 50f;
        overallClarity = 0f;

        if (_historyCount < 4)
        {
            return;
        }

        // Vowel clarity: based on HNR during voiced segments
        float hnrSum = 0f;
        int voicedCount = 0;

        // Consonant clarity: based on spectral slope during unvoiced segments
        float slopeSum = 0f;
        int unvoicedCount = 0;

        for (int i = 0; i < _historyCount; i++)
        {
            int idx = (_historyIndex - 1 - i + HistorySize) % HistorySize;

            if (_voicingHistory[idx] == VoicingState.Voiced)
            {
                hnrSum += _hnrHistory[idx];
                voicedCount++;
            }
            else if (_voicingHistory[idx] == VoicingState.Unvoiced)
            {
                // More negative slope = more energy in high frequencies = crisper consonants
                slopeSum += _spectralSlopeHistory[idx];
                unvoicedCount++;
            }
        }

        // Vowel clarity: map HNR to 0-100
        // Good HNR for speech is typically 10-25 dB (linear: 3-18)
        if (voicedCount > 0)
        {
            float avgHnr = hnrSum / voicedCount;
            // Map HNR 0-20 to 0-100
            vowelClarity = MathF.Min(avgHnr * 5f, 100f);
        }

        // Consonant clarity: map spectral slope to 0-100
        // More negative slope during unvoiced = crisper consonants
        if (unvoicedCount > 0)
        {
            float avgSlope = slopeSum / unvoicedCount;
            // Typical unvoiced consonants have slope around -5 to -15 dB/kHz
            // Map -20 to 0 => 100 to 0
            consonantClarity = MathF.Min(MathF.Max(-avgSlope * 5f, 0f), 100f);
        }
        else
        {
            // No unvoiced segments - assume neutral
            consonantClarity = 50f;
        }

        // Transition sharpness: based on transition rate
        // Clearer speech has more distinct V/UV transitions
        if (_frameSinceReset > 10)
        {
            float transitionsPerFrame = (float)_transitionCount / _frameSinceReset;
            // Expect roughly 0.05-0.15 transitions per frame for normal speech
            // Map to 0-100 with optimal around 0.1
            float normalizedRate = transitionsPerFrame / 0.1f;
            transitionSharpness = MathF.Min(normalizedRate * 50f, 100f);
        }

        // Overall clarity: weighted combination
        // Vowel clarity is most important (50%), then consonant (30%), then transitions (20%)
        overallClarity = vowelClarity * 0.5f + consonantClarity * 0.3f + transitionSharpness * 0.2f;
    }

    /// <summary>
    /// Get instantaneous clarity estimate (for per-frame display).
    /// </summary>
    /// <param name="hnr">Current HNR value.</param>
    /// <param name="voicing">Current voicing state.</param>
    /// <returns>Instantaneous clarity score (0-100).</returns>
    public float GetInstantClarity(float hnr, VoicingState voicing)
    {
        if (voicing == VoicingState.Voiced)
        {
            // During voiced: HNR-based clarity
            return MathF.Min(hnr * 5f, 100f);
        }
        else if (voicing == VoicingState.Unvoiced)
        {
            // During unvoiced: return moderate (actual clarity depends on spectral features)
            return 50f;
        }

        // Silence
        return 0f;
    }

    /// <summary>
    /// Reset the analyzer state.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_hnrHistory, 0, _hnrHistory.Length);
        Array.Clear(_spectralSlopeHistory, 0, _spectralSlopeHistory.Length);
        Array.Clear(_voicingHistory, 0, _voicingHistory.Length);
        _historyIndex = 0;
        _historyCount = 0;
        _lastVoicing = VoicingState.Silence;
        _transitionCount = 0;
        _frameSinceReset = 0;
    }
}
