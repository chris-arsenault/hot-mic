using HotMic.Core.Dsp.Analysis;

namespace HotMic.Core.Dsp.Analysis.Speech;

/// <summary>
/// Type of filler word detected.
/// </summary>
public enum FillerType : byte
{
    None = 0,
    Uh = 1,      // "uh", "ah" - schwa-like
    Um = 2,      // "um" - schwa + nasal closure
    Er = 3,      // "er" - similar to schwa
    Generic = 4  // Unclassified filler
}

/// <summary>
/// Represents a detected filler word event.
/// </summary>
public readonly struct FillerEvent
{
    public FillerType Type { get; }
    public long StartFrame { get; }
    public long EndFrame { get; }
    public float DurationMs { get; }
    public float Confidence { get; }

    public FillerEvent(FillerType type, long startFrame, long endFrame, float durationMs, float confidence)
    {
        Type = type;
        StartFrame = startFrame;
        EndFrame = endFrame;
        DurationMs = durationMs;
        Confidence = confidence;
    }
}

/// <summary>
/// Detects filler words ("uh", "um", etc.) using acoustic features.
/// Based on: low F0 variation, sustained voicing, schwa-like formants, low spectral flux.
/// </summary>
public sealed class FillerDetector
{
    private const int MaxHistoryFrames = 64;
    private const float MinFillerDurationMs = 80f;
    private const float MaxFillerDurationMs = 1200f;

    // Feature history buffers
    private readonly float[] _pitchHistory = new float[MaxHistoryFrames];
    private readonly float[] _fluxHistory = new float[MaxHistoryFrames];
    private readonly float[] _f1History = new float[MaxHistoryFrames];
    private readonly float[] _f2History = new float[MaxHistoryFrames];
    private readonly bool[] _voicedHistory = new bool[MaxHistoryFrames];
    private int _historyIndex;
    private int _historyCount;

    // Filler candidate tracking
    private bool _inCandidate;
    private long _candidateStartFrame;
    private int _candidateFrameCount;

    /// <summary>
    /// Maximum pitch variation (in semitones) during a filler.
    /// </summary>
    public float MaxPitchVariationSemitones { get; set; } = 1.5f;

    /// <summary>
    /// Maximum spectral flux during a filler (normalized).
    /// </summary>
    public float MaxSpectralFlux { get; set; } = 0.02f;

    /// <summary>
    /// Expected F1 range for schwa (Hz).
    /// </summary>
    public (float Min, float Max) SchwaF1Range { get; set; } = (400f, 800f);

    /// <summary>
    /// Expected F2 range for schwa (Hz).
    /// </summary>
    public (float Min, float Max) SchwaF2Range { get; set; } = (1000f, 1600f);

    /// <summary>
    /// Process a frame and detect filler words.
    /// </summary>
    /// <param name="pitchHz">Pitch in Hz (0 if unvoiced).</param>
    /// <param name="spectralFlux">Spectral flux value.</param>
    /// <param name="f1Hz">First formant frequency (0 if not detected).</param>
    /// <param name="f2Hz">Second formant frequency (0 if not detected).</param>
    /// <param name="voicing">Voicing state.</param>
    /// <param name="frameId">Current frame ID.</param>
    /// <param name="hopSize">Hop size in samples.</param>
    /// <param name="sampleRate">Sample rate.</param>
    /// <param name="fillerEvent">Output: filler event if one ended this frame.</param>
    /// <returns>True if a filler event was detected.</returns>
    public bool Process(
        float pitchHz,
        float spectralFlux,
        float f1Hz,
        float f2Hz,
        VoicingState voicing,
        long frameId,
        int hopSize,
        int sampleRate,
        out FillerEvent fillerEvent)
    {
        fillerEvent = default;
        float frameDurationMs = 1000f * hopSize / sampleRate;

        // Convert pitch to semitones
        float pitchSemitones = pitchHz > 0f ? 12f * MathF.Log2(pitchHz / 100f) : 0f;
        bool isVoiced = voicing == VoicingState.Voiced;

        // Store in history
        _pitchHistory[_historyIndex] = pitchSemitones;
        _fluxHistory[_historyIndex] = spectralFlux;
        _f1History[_historyIndex] = f1Hz;
        _f2History[_historyIndex] = f2Hz;
        _voicedHistory[_historyIndex] = isVoiced;
        _historyIndex = (_historyIndex + 1) % MaxHistoryFrames;
        _historyCount = Math.Min(_historyCount + 1, MaxHistoryFrames);

        // Check if current frame could be part of a filler
        bool isPotentialFiller = isVoiced && spectralFlux < MaxSpectralFlux;

        if (isPotentialFiller)
        {
            if (!_inCandidate)
            {
                // Start new candidate
                _inCandidate = true;
                _candidateStartFrame = frameId;
                _candidateFrameCount = 1;
            }
            else
            {
                _candidateFrameCount++;
            }
        }
        else
        {
            if (_inCandidate)
            {
                // End of candidate - check if it qualifies as a filler
                float durationMs = _candidateFrameCount * frameDurationMs;

                if (durationMs >= MinFillerDurationMs && durationMs <= MaxFillerDurationMs)
                {
                    // Analyze the candidate segment
                    var (type, confidence) = AnalyzeCandidate(_candidateFrameCount);

                    if (confidence > 0.3f)
                    {
                        fillerEvent = new FillerEvent(
                            type,
                            _candidateStartFrame,
                            frameId - 1,
                            durationMs,
                            confidence);

                        _inCandidate = false;
                        _candidateFrameCount = 0;
                        return true;
                    }
                }

                _inCandidate = false;
                _candidateFrameCount = 0;
            }
        }

        // Check for max duration timeout
        if (_inCandidate)
        {
            float currentDurationMs = _candidateFrameCount * frameDurationMs;
            if (currentDurationMs > MaxFillerDurationMs)
            {
                _inCandidate = false;
                _candidateFrameCount = 0;
            }
        }

        return false;
    }

    private (FillerType type, float confidence) AnalyzeCandidate(int frameCount)
    {
        if (frameCount < 3 || _historyCount < frameCount)
        {
            return (FillerType.None, 0f);
        }

        // Analyze recent frames in the candidate
        int framesToAnalyze = Math.Min(frameCount, MaxHistoryFrames);
        int startIdx = (_historyIndex - framesToAnalyze + MaxHistoryFrames) % MaxHistoryFrames;

        float pitchSum = 0f;
        float pitchSumSq = 0f;
        float fluxSum = 0f;
        float f1Sum = 0f;
        float f2Sum = 0f;
        int voicedCount = 0;
        int formantCount = 0;

        for (int i = 0; i < framesToAnalyze; i++)
        {
            int idx = (startIdx + i) % MaxHistoryFrames;

            if (_voicedHistory[idx])
            {
                voicedCount++;
                float pitch = _pitchHistory[idx];
                pitchSum += pitch;
                pitchSumSq += pitch * pitch;
            }

            fluxSum += _fluxHistory[idx];

            if (_f1History[idx] > 0f && _f2History[idx] > 0f)
            {
                f1Sum += _f1History[idx];
                f2Sum += _f2History[idx];
                formantCount++;
            }
        }

        // Check voicing consistency
        float voicingRatio = (float)voicedCount / framesToAnalyze;
        if (voicingRatio < 0.8f)
        {
            return (FillerType.None, 0f);
        }

        // Check pitch stability
        float pitchVariance = 0f;
        if (voicedCount > 1)
        {
            float pitchMean = pitchSum / voicedCount;
            pitchVariance = (pitchSumSq / voicedCount) - (pitchMean * pitchMean);
        }
        float pitchStdDev = pitchVariance > 0f ? MathF.Sqrt(pitchVariance) : 0f;

        if (pitchStdDev > MaxPitchVariationSemitones)
        {
            return (FillerType.None, 0f);
        }

        // Check spectral flux
        float avgFlux = fluxSum / framesToAnalyze;
        if (avgFlux > MaxSpectralFlux)
        {
            return (FillerType.None, 0f);
        }

        // Compute confidence based on feature quality
        float pitchStabilityScore = 1f - (pitchStdDev / MaxPitchVariationSemitones);
        float fluxStabilityScore = 1f - (avgFlux / MaxSpectralFlux);
        float confidence = (pitchStabilityScore + fluxStabilityScore + voicingRatio) / 3f;

        // Classify filler type based on formants
        FillerType type = FillerType.Generic;
        if (formantCount > framesToAnalyze / 2)
        {
            float avgF1 = f1Sum / formantCount;
            float avgF2 = f2Sum / formantCount;

            bool f1InSchwaRange = avgF1 >= SchwaF1Range.Min && avgF1 <= SchwaF1Range.Max;
            bool f2InSchwaRange = avgF2 >= SchwaF2Range.Min && avgF2 <= SchwaF2Range.Max;

            if (f1InSchwaRange && f2InSchwaRange)
            {
                // Check for nasal closure (F2 drop) at end for "um"
                int lastIdx = (_historyIndex - 1 + MaxHistoryFrames) % MaxHistoryFrames;
                int midIdx = (_historyIndex - framesToAnalyze / 2 + MaxHistoryFrames) % MaxHistoryFrames;

                if (_f2History[lastIdx] > 0f && _f2History[midIdx] > 0f)
                {
                    float f2Drop = _f2History[midIdx] - _f2History[lastIdx];
                    if (f2Drop > 200f)
                    {
                        type = FillerType.Um;
                    }
                    else
                    {
                        type = FillerType.Uh;
                    }
                }
                else
                {
                    type = FillerType.Uh;
                }

                confidence += 0.1f; // Bonus for matching schwa formants
            }
        }

        return (type, Math.Clamp(confidence, 0f, 1f));
    }

    /// <summary>
    /// Check if currently tracking a potential filler.
    /// </summary>
    public bool InPotentialFiller => _inCandidate;

    /// <summary>
    /// Duration of current potential filler in frames.
    /// </summary>
    public int CurrentCandidateFrames => _candidateFrameCount;

    /// <summary>
    /// Reset the detector state.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_pitchHistory, 0, _pitchHistory.Length);
        Array.Clear(_fluxHistory, 0, _fluxHistory.Length);
        Array.Clear(_f1History, 0, _f1History.Length);
        Array.Clear(_f2History, 0, _f2History.Length);
        Array.Clear(_voicedHistory, 0, _voicedHistory.Length);
        _historyIndex = 0;
        _historyCount = 0;
        _inCandidate = false;
        _candidateStartFrame = 0;
        _candidateFrameCount = 0;
    }
}
