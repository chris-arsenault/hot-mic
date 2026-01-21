using System.Diagnostics;
using System.Threading;
using HotMic.Core.Threading;

namespace HotMic.Core.Dsp.Analysis.Pitch;

/// <summary>
/// Result of pitch detection.
/// </summary>
public readonly record struct PitchResult(float? FrequencyHz, float Confidence, bool IsVoiced);

/// <summary>
/// YIN pitch detector (de Cheveigné & Kawahara, 2002).
/// Uses pre-allocated buffers and is safe for real-time analysis threads.
/// </summary>
public sealed class YinPitchDetector
{
    private int _sampleRate;
    private int _frameSize;
    private int _minTau;
    private int _maxTau;
    private float _threshold;

    private float[] _difference = Array.Empty<float>();
    private float[] _cmnd = Array.Empty<float>();
    private int _profilingEnabled;
    private long _lastTotalTicks;
    private long _maxTotalTicks;
    private long _lastDiffTicks;
    private long _maxDiffTicks;
    private long _lastCmndTicks;
    private long _maxCmndTicks;
    private long _lastSearchTicks;
    private long _maxSearchTicks;
    private long _lastRefineTicks;
    private long _maxRefineTicks;
    private long _lastTotalCpuCycles;
    private long _maxTotalCpuCycles;
    private long _lastDiffCpuCycles;
    private long _maxDiffCpuCycles;

    public YinPitchDetector(int sampleRate, int frameSize, float minFrequency, float maxFrequency, float threshold = 0.15f)
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
        return new PitchProfilingSnapshot(
            PitchDetectorType.Yin,
            Interlocked.Read(ref _lastTotalTicks),
            Interlocked.Read(ref _maxTotalTicks),
            Interlocked.Read(ref _lastDiffTicks),
            Interlocked.Read(ref _maxDiffTicks),
            Interlocked.Read(ref _lastCmndTicks),
            Interlocked.Read(ref _maxCmndTicks),
            Interlocked.Read(ref _lastSearchTicks),
            Interlocked.Read(ref _maxSearchTicks),
            Interlocked.Read(ref _lastRefineTicks),
            Interlocked.Read(ref _maxRefineTicks),
            _frameSize,
            _minTau,
            _maxTau,
            Interlocked.Read(ref _lastTotalCpuCycles),
            Interlocked.Read(ref _maxTotalCpuCycles),
            Interlocked.Read(ref _lastDiffCpuCycles),
            Interlocked.Read(ref _maxDiffCpuCycles));
    }

    /// <summary>
    /// Detect pitch for the provided frame (length must be >= configured frame size).
    /// </summary>
    public PitchResult Detect(ReadOnlySpan<float> frame)
    {
        if (frame.Length < _frameSize || _maxTau <= _minTau)
        {
            return new PitchResult(null, 0f, false);
        }

        bool profilingEnabled = Volatile.Read(ref _profilingEnabled) != 0;
        long totalStart = 0;
        long totalCpuStart = 0;
        bool totalCpuTiming = false;
        if (profilingEnabled)
        {
            totalStart = Stopwatch.GetTimestamp();
            totalCpuTiming = ThreadCpuTimer.TryGetCurrentThreadCycles(out totalCpuStart);
        }

        int size = _frameSize;
        int maxTau = _maxTau;

        // Difference function
        long diffStart = 0;
        long diffCpuStart = 0;
        bool diffCpuTiming = false;
        if (profilingEnabled)
        {
            diffStart = Stopwatch.GetTimestamp();
            diffCpuTiming = totalCpuTiming && ThreadCpuTimer.TryGetCurrentThreadCycles(out diffCpuStart);
        }

        for (int tau = 0; tau <= maxTau; tau++)
        {
            _difference[tau] = 0f;
        }

        for (int tau = 1; tau <= maxTau; tau++)
        {
            double sum = 0.0;
            int limit = size - tau;
            for (int i = 0; i < limit; i++)
            {
                // Use double math to avoid subnormal float multiply slowdowns in silence.
                double delta = (double)frame[i] - frame[i + tau];
                sum += delta * delta;
            }
            _difference[tau] = (float)sum;
        }

        if (profilingEnabled)
        {
            long diffTicks = Stopwatch.GetTimestamp() - diffStart;
            RecordProfiling(ref _lastDiffTicks, ref _maxDiffTicks, diffTicks);
            if (diffCpuTiming && ThreadCpuTimer.TryGetCurrentThreadCycles(out long diffCpuEnd))
            {
                RecordProfiling(ref _lastDiffCpuCycles, ref _maxDiffCpuCycles, diffCpuEnd - diffCpuStart);
            }
        }

        // Cumulative mean normalized difference (CMND)
        long cmndStart = 0;
        if (profilingEnabled)
        {
            cmndStart = Stopwatch.GetTimestamp();
        }

        double runningSum = 0.0;
        _cmnd[0] = 1f;
        for (int tau = 1; tau <= maxTau; tau++)
        {
            runningSum += _difference[tau];
            _cmnd[tau] = runningSum > 1e-12 ? (float)(_difference[tau] * tau / runningSum) : 1f;
        }

        if (profilingEnabled)
        {
            long cmndTicks = Stopwatch.GetTimestamp() - cmndStart;
            RecordProfiling(ref _lastCmndTicks, ref _maxCmndTicks, cmndTicks);
        }

        // Absolute threshold
        long searchStart = 0;
        if (profilingEnabled)
        {
            searchStart = Stopwatch.GetTimestamp();
        }

        int tauEstimate = -1;
        for (int tau = _minTau; tau <= maxTau; tau++)
        {
            if (_cmnd[tau] < _threshold && _cmnd[tau] < _cmnd[tau - 1])
            {
                while (tau + 1 <= maxTau && _cmnd[tau + 1] < _cmnd[tau])
                {
                    tau++;
                }
                tauEstimate = tau;
                break;
            }
        }

        if (profilingEnabled)
        {
            long searchTicks = Stopwatch.GetTimestamp() - searchStart;
            RecordProfiling(ref _lastSearchTicks, ref _maxSearchTicks, searchTicks);
        }

        if (tauEstimate < 0)
        {
            if (profilingEnabled)
            {
                RecordProfiling(ref _lastRefineTicks, ref _maxRefineTicks, 0);
                RecordProfiling(ref _lastTotalTicks, ref _maxTotalTicks, Stopwatch.GetTimestamp() - totalStart);
                if (totalCpuTiming && ThreadCpuTimer.TryGetCurrentThreadCycles(out long totalCpuEnd))
                {
                    RecordProfiling(ref _lastTotalCpuCycles, ref _maxTotalCpuCycles, totalCpuEnd - totalCpuStart);
                }
            }
            return new PitchResult(null, 0f, false);
        }

        long refineStart = 0;
        if (profilingEnabled)
        {
            refineStart = Stopwatch.GetTimestamp();
        }

        float refinedTau = tauEstimate;
        if (tauEstimate > 1 && tauEstimate < maxTau - 1)
        {
            float s0 = _cmnd[tauEstimate - 1];
            float s1 = _cmnd[tauEstimate];
            float s2 = _cmnd[tauEstimate + 1];
            float denom = 2f * (2f * s1 - s2 - s0);
            if (MathF.Abs(denom) > 1e-6f)
            {
                refinedTau = tauEstimate + (s2 - s0) / denom;
            }
        }

        if (profilingEnabled)
        {
            long refineTicks = Stopwatch.GetTimestamp() - refineStart;
            RecordProfiling(ref _lastRefineTicks, ref _maxRefineTicks, refineTicks);
            RecordProfiling(ref _lastTotalTicks, ref _maxTotalTicks, Stopwatch.GetTimestamp() - totalStart);
            if (totalCpuTiming && ThreadCpuTimer.TryGetCurrentThreadCycles(out long totalCpuEnd))
            {
                RecordProfiling(ref _lastTotalCpuCycles, ref _maxTotalCpuCycles, totalCpuEnd - totalCpuStart);
            }
        }

        float frequency = _sampleRate / refinedTau;
        float confidence = Math.Clamp(1f - _cmnd[tauEstimate], 0f, 1f);
        return new PitchResult(frequency, confidence, true);
    }

    private void ResetProfiling()
    {
        Interlocked.Exchange(ref _lastTotalTicks, 0);
        Interlocked.Exchange(ref _maxTotalTicks, 0);
        Interlocked.Exchange(ref _lastDiffTicks, 0);
        Interlocked.Exchange(ref _maxDiffTicks, 0);
        Interlocked.Exchange(ref _lastCmndTicks, 0);
        Interlocked.Exchange(ref _maxCmndTicks, 0);
        Interlocked.Exchange(ref _lastSearchTicks, 0);
        Interlocked.Exchange(ref _maxSearchTicks, 0);
        Interlocked.Exchange(ref _lastRefineTicks, 0);
        Interlocked.Exchange(ref _maxRefineTicks, 0);
        Interlocked.Exchange(ref _lastTotalCpuCycles, 0);
        Interlocked.Exchange(ref _maxTotalCpuCycles, 0);
        Interlocked.Exchange(ref _lastDiffCpuCycles, 0);
        Interlocked.Exchange(ref _maxDiffCpuCycles, 0);
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
