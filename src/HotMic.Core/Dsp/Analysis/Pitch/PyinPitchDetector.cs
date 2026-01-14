namespace HotMic.Core.Dsp.Analysis.Pitch;

/// <summary>
/// Probabilistic YIN pitch detector with candidate selection and continuity penalty.
/// </summary>
public sealed class PyinPitchDetector
{
    private const int MaxCandidates = 6;
    private const float DefaultThreshold = 0.15f;
    private const float JumpPenalty = 0.7f;
    private const float VoicedThreshold = 0.15f;

    private int _sampleRate;
    private int _frameSize;
    private int _minTau;
    private int _maxTau;
    private float _threshold;

    private float[] _difference = Array.Empty<float>();
    private float[] _cmnd = Array.Empty<float>();
    private int[] _candidateTau = new int[MaxCandidates];
    private float[] _candidateProb = new float[MaxCandidates];

    private float _lastPitch;
    private float _lastConfidence;

    public PyinPitchDetector(int sampleRate, int frameSize, float minFrequency, float maxFrequency, float threshold = DefaultThreshold)
    {
        Configure(sampleRate, frameSize, minFrequency, maxFrequency, threshold);
    }

    public void Configure(int sampleRate, int frameSize, float minFrequency, float maxFrequency, float threshold)
    {
        _sampleRate = Math.Max(1, sampleRate);
        _frameSize = Math.Max(64, frameSize);
        _threshold = Math.Clamp(threshold, 0.01f, 0.5f);

        float maxFreq = Math.Clamp(maxFrequency, 40f, _sampleRate * 0.45f);
        float minFreq = Math.Clamp(minFrequency, 20f, maxFreq - 1f);
        _minTau = Math.Max(2, (int)(_sampleRate / maxFreq));
        _maxTau = Math.Min(_frameSize - 2, (int)(_sampleRate / minFreq));

        int required = _maxTau + 1;
        if (_difference.Length < required)
        {
            _difference = new float[required];
            _cmnd = new float[required];
        }
    }

    /// <summary>
    /// Detect pitch using probabilistic candidate scoring (pYIN-style).
    /// </summary>
    public PitchResult Detect(ReadOnlySpan<float> frame)
    {
        if (frame.Length < _frameSize || _maxTau <= _minTau)
        {
            return new PitchResult(null, 0f, false);
        }

        int size = _frameSize;
        int maxTau = _maxTau;

        for (int tau = 0; tau <= maxTau; tau++)
        {
            _difference[tau] = 0f;
        }

        for (int tau = 1; tau <= maxTau; tau++)
        {
            float sum = 0f;
            int limit = size - tau;
            for (int i = 0; i < limit; i++)
            {
                float delta = frame[i] - frame[i + tau];
                sum += delta * delta;
            }
            _difference[tau] = sum;
        }

        float runningSum = 0f;
        _cmnd[0] = 1f;
        for (int tau = 1; tau <= maxTau; tau++)
        {
            runningSum += _difference[tau];
            _cmnd[tau] = runningSum > 1e-12f ? (_difference[tau] * tau / runningSum) : 1f;
        }

        int candidateCount = 0;
        float bestCmnd = float.MaxValue;
        int bestTau = -1;
        for (int tau = _minTau + 1; tau < maxTau; tau++)
        {
            float value = _cmnd[tau];
            if (value < bestCmnd)
            {
                bestCmnd = value;
                bestTau = tau;
            }

            if (value < _threshold && value < _cmnd[tau - 1] && value <= _cmnd[tau + 1])
            {
                if (candidateCount < MaxCandidates)
                {
                    _candidateTau[candidateCount] = tau;
                    _candidateProb[candidateCount] = 1f - value;
                    candidateCount++;
                }
            }
        }

        if (candidateCount == 0 && bestTau > 0)
        {
            _candidateTau[0] = bestTau;
            _candidateProb[0] = Math.Clamp(1f - bestCmnd, 0f, 1f);
            candidateCount = 1;
        }

        if (candidateCount == 0)
        {
            _lastPitch = 0f;
            _lastConfidence = 0f;
            return new PitchResult(null, 0f, false);
        }

        float bestScore = float.MinValue;
        float bestProb = 0f;
        float bestFreq = 0f;

        for (int i = 0; i < candidateCount; i++)
        {
            int tau = _candidateTau[i];
            float prob = Math.Clamp(_candidateProb[i], 0f, 1f);

            float refinedTau = tau;
            if (tau > 1 && tau < maxTau - 1)
            {
                float s0 = _cmnd[tau - 1];
                float s1 = _cmnd[tau];
                float s2 = _cmnd[tau + 1];
                float denom = 2f * (2f * s1 - s2 - s0);
                if (MathF.Abs(denom) > 1e-6f)
                {
                    refinedTau = tau + (s2 - s0) / denom;
                }
            }

            float freq = _sampleRate / refinedTau;
            float score = prob * prob;

            if (_lastPitch > 0f)
            {
                float jump = MathF.Abs(MathF.Log2(freq / _lastPitch));
                score -= jump * JumpPenalty;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestProb = prob;
                bestFreq = freq;
            }
        }

        bool voiced = bestProb >= VoicedThreshold;
        _lastPitch = voiced ? bestFreq : 0f;
        _lastConfidence = voiced ? bestProb : 0f;
        return voiced
            ? new PitchResult(bestFreq, bestProb, true)
            : new PitchResult(null, bestProb, false);
    }
}
