using System.Diagnostics;
using System.Threading;

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
    private int _profilingEnabled;
    private long _lastTotalTicks;
    private long _maxTotalTicks;

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

    internal void SetProfilingEnabled(bool enabled)
    {
        int value = enabled ? 1 : 0;
        int prior = Interlocked.Exchange(ref _profilingEnabled, value);
        if (prior != value)
        {
            ResetProfiling();
        }
    }

    internal PitchProfilingSnapshot GetProfilingSnapshot()
    {
        int minPeriod = _maxFrequency > 0f ? Math.Max(2, (int)(_sampleRate / _maxFrequency)) : 0;
        int maxPeriod = _minFrequency > 0f ? Math.Min(_fftSize - 2, (int)(_sampleRate / _minFrequency)) : 0;
        return new PitchProfilingSnapshot(
            PitchDetectorType.Swipe,
            Interlocked.Read(ref _lastTotalTicks),
            Interlocked.Read(ref _maxTotalTicks),
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            _fftSize,
            minPeriod,
            maxPeriod);
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

        bool profilingEnabled = Volatile.Read(ref _profilingEnabled) != 0;
        long totalStart = 0;
        if (profilingEnabled)
        {
            totalStart = Stopwatch.GetTimestamp();
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
            if (profilingEnabled)
            {
                RecordProfiling(ref _lastTotalTicks, ref _maxTotalTicks, Stopwatch.GetTimestamp() - totalStart);
            }
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

            double sum = 0.0;
            double sumW = 0.0;
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

            float score = sumW > 0.0 ? (float)(sum / sumW) : 0f;
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
            if (profilingEnabled)
            {
                RecordProfiling(ref _lastTotalTicks, ref _maxTotalTicks, Stopwatch.GetTimestamp() - totalStart);
            }
            return new PitchResult(null, confidence, false);
        }

        float separation = bestScore > 0f ? (bestScore - secondScore) / bestScore : 0f;
        float blendedConfidence = Math.Clamp((confidence + separation) * 0.5f, 0f, 1f);
        if (profilingEnabled)
        {
            RecordProfiling(ref _lastTotalTicks, ref _maxTotalTicks, Stopwatch.GetTimestamp() - totalStart);
        }
        return new PitchResult(bestFreq, blendedConfidence, true);
    }

    private void ResetProfiling()
    {
        Interlocked.Exchange(ref _lastTotalTicks, 0);
        Interlocked.Exchange(ref _maxTotalTicks, 0);
    }

    private static void RecordProfiling(ref long lastTicks, ref long maxTicks, long elapsedTicks)
    {
        Interlocked.Exchange(ref lastTicks, elapsedTicks);
        if (elapsedTicks <= 0)
        {
            return;
        }

        UpdateMax(ref maxTicks, elapsedTicks);
    }

    private static void UpdateMax(ref long location, long value)
    {
        long current = Interlocked.Read(ref location);
        while (value > current)
        {
            long prior = Interlocked.CompareExchange(ref location, value, current);
            if (prior == current)
            {
                break;
            }

            current = prior;
        }
    }
}
