namespace HotMic.Core.Dsp.Analysis.Pitch;

/// <summary>
/// SWIPE-style pitch detector using harmonic summation on the magnitude spectrum.
/// </summary>
public sealed class SwipePitchDetector
{
    private const int CandidatesPerOctave = 48;
    private const int MaxHarmonics = 24;
    private const float ConfidenceThreshold = 0.2f;

    private int _sampleRate;
    private int _fftSize;
    private float _minFrequency;
    private float _maxFrequency;
    private float _binResolution;

    private float[] _candidates = Array.Empty<float>();

    public SwipePitchDetector(int sampleRate, int fftSize, float minFrequency, float maxFrequency)
    {
        Configure(sampleRate, fftSize, minFrequency, maxFrequency);
    }

    public void Configure(int sampleRate, int fftSize, float minFrequency, float maxFrequency)
    {
        _sampleRate = Math.Max(1, sampleRate);
        _fftSize = Math.Max(64, fftSize);
        _minFrequency = Math.Clamp(minFrequency, 40f, _sampleRate * 0.45f);
        _maxFrequency = Math.Clamp(maxFrequency, _minFrequency + 1f, _sampleRate * 0.45f);
        _binResolution = _sampleRate / (float)_fftSize;

        float minLog = MathF.Log2(_minFrequency);
        float maxLog = MathF.Log2(_maxFrequency);
        int count = Math.Max(1, (int)MathF.Ceiling((maxLog - minLog) * CandidatesPerOctave) + 1);

        if (_candidates.Length != count)
        {
            _candidates = new float[count];
        }

        for (int i = 0; i < count; i++)
        {
            float log = minLog + i / (float)CandidatesPerOctave;
            _candidates[i] = MathF.Pow(2f, log);
        }
    }

    /// <summary>
    /// Detect pitch from the magnitude spectrum.
    /// </summary>
    public PitchResult Detect(ReadOnlySpan<float> magnitudes)
    {
        if (magnitudes.IsEmpty || _candidates.Length == 0)
        {
            return new PitchResult(null, 0f, false);
        }

        int half = magnitudes.Length;
        float maxMag = 0f;
        for (int i = 0; i < half; i++)
        {
            float mag = magnitudes[i];
            if (mag > maxMag)
            {
                maxMag = mag;
            }
        }

        if (maxMag <= 1e-12f)
        {
            return new PitchResult(null, 0f, false);
        }

        float bestScore = 0f;
        float secondScore = 0f;
        float bestFreq = 0f;

        for (int i = 0; i < _candidates.Length; i++)
        {
            float f0 = _candidates[i];
            int harmonicLimit = Math.Min(MaxHarmonics, (int)((_sampleRate * 0.5f) / f0));
            if (harmonicLimit <= 0)
            {
                continue;
            }

            float sum = 0f;
            float sumW = 0f;
            for (int h = 1; h <= harmonicLimit; h++)
            {
                float freq = f0 * h;
                int bin = (int)MathF.Round(freq / _binResolution);
                if (bin <= 0 || bin >= half)
                {
                    break;
                }

                float weight = 1f / h;
                sum += magnitudes[bin] * weight;
                sumW += weight;
            }

            float score = sumW > 0f ? sum / sumW : 0f;
            if (score > bestScore)
            {
                secondScore = bestScore;
                bestScore = score;
                bestFreq = f0;
            }
            else if (score > secondScore)
            {
                secondScore = score;
            }
        }

        float confidence = bestScore > 0f ? bestScore / maxMag : 0f;
        confidence = Math.Clamp(confidence, 0f, 1f);
        if (confidence < ConfidenceThreshold)
        {
            return new PitchResult(null, confidence, false);
        }

        float separation = bestScore > 0f ? (bestScore - secondScore) / bestScore : 0f;
        float blendedConfidence = Math.Clamp((confidence + separation) * 0.5f, 0f, 1f);
        return new PitchResult(bestFreq, blendedConfidence, true);
    }
}
