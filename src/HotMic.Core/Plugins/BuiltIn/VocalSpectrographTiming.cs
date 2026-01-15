using System;
using System.Diagnostics;
using System.Threading;

namespace HotMic.Core.Plugins.BuiltIn;

/// <summary>
/// Snapshot of spectrograph analysis timings (microseconds).
/// </summary>
public readonly struct SpectrographTimingSnapshot
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpectrographTimingSnapshot"/> struct.
    /// </summary>
    public SpectrographTimingSnapshot(
        int frameUs,
        int preprocessUs,
        int transformUs,
        int normalizationUs,
        int pitchUs,
        int clarityUs,
        int reassignUs,
        int featuresUs,
        int writebackUs)
    {
        FrameUs = frameUs;
        PreprocessUs = preprocessUs;
        TransformUs = transformUs;
        NormalizationUs = normalizationUs;
        PitchUs = pitchUs;
        ClarityUs = clarityUs;
        ReassignUs = reassignUs;
        FeaturesUs = featuresUs;
        WritebackUs = writebackUs;
    }

    /// <summary>Average total frame time.</summary>
    public int FrameUs { get; }
    /// <summary>Preprocess stage (buffer shift + filters + pre-emphasis).</summary>
    public int PreprocessUs { get; }
    /// <summary>Transform stage (FFT/CQT/ZoomFFT).</summary>
    public int TransformUs { get; }
    /// <summary>Normalization stage (A-weight/peak/RMS normalization).</summary>
    public int NormalizationUs { get; }
    /// <summary>Pitch/CPP/voicing/formants/harmonics stage.</summary>
    public int PitchUs { get; }
    /// <summary>Clarity stage (noise, HPSS, smoothing, HNR).</summary>
    public int ClarityUs { get; }
    /// <summary>Reassignment stage.</summary>
    public int ReassignUs { get; }
    /// <summary>Spectral feature extraction stage.</summary>
    public int FeaturesUs { get; }
    /// <summary>Frame writeback stage (buffer updates).</summary>
    public int WritebackUs { get; }
}

internal sealed class SpectrographTimingCollector
{
    private const int PublishFrames = 20;
    private static readonly double TicksToMicroseconds = 1_000_000.0 / Stopwatch.Frequency;

    private int _frames;
    private long _sumFrameTicks;
    private long _sumPreprocessTicks;
    private long _sumTransformTicks;
    private long _sumNormalizationTicks;
    private long _sumPitchTicks;
    private long _sumClarityTicks;
    private long _sumReassignTicks;
    private long _sumFeaturesTicks;
    private long _sumWritebackTicks;

    private int _frameUs;
    private int _preprocessUs;
    private int _transformUs;
    private int _normalizationUs;
    private int _pitchUs;
    private int _clarityUs;
    private int _reassignUs;
    private int _featuresUs;
    private int _writebackUs;

    public void RecordFrame(
        long frameTicks,
        long preprocessTicks,
        long transformTicks,
        long normalizationTicks,
        long pitchTicks,
        long clarityTicks,
        long reassignTicks,
        long featuresTicks,
        long writebackTicks)
    {
        _sumFrameTicks += frameTicks;
        _sumPreprocessTicks += preprocessTicks;
        _sumTransformTicks += transformTicks;
        _sumNormalizationTicks += normalizationTicks;
        _sumPitchTicks += pitchTicks;
        _sumClarityTicks += clarityTicks;
        _sumReassignTicks += reassignTicks;
        _sumFeaturesTicks += featuresTicks;
        _sumWritebackTicks += writebackTicks;

        _frames++;
        if (_frames < PublishFrames)
        {
            return;
        }

        Publish();
    }

    public SpectrographTimingSnapshot GetSnapshot()
    {
        return new SpectrographTimingSnapshot(
            Volatile.Read(ref _frameUs),
            Volatile.Read(ref _preprocessUs),
            Volatile.Read(ref _transformUs),
            Volatile.Read(ref _normalizationUs),
            Volatile.Read(ref _pitchUs),
            Volatile.Read(ref _clarityUs),
            Volatile.Read(ref _reassignUs),
            Volatile.Read(ref _featuresUs),
            Volatile.Read(ref _writebackUs));
    }

    private void Publish()
    {
        int frames = _frames;
        if (frames <= 0)
        {
            return;
        }

        Interlocked.Exchange(ref _frameUs, ToMicroseconds(_sumFrameTicks, frames));
        Interlocked.Exchange(ref _preprocessUs, ToMicroseconds(_sumPreprocessTicks, frames));
        Interlocked.Exchange(ref _transformUs, ToMicroseconds(_sumTransformTicks, frames));
        Interlocked.Exchange(ref _normalizationUs, ToMicroseconds(_sumNormalizationTicks, frames));
        Interlocked.Exchange(ref _pitchUs, ToMicroseconds(_sumPitchTicks, frames));
        Interlocked.Exchange(ref _clarityUs, ToMicroseconds(_sumClarityTicks, frames));
        Interlocked.Exchange(ref _reassignUs, ToMicroseconds(_sumReassignTicks, frames));
        Interlocked.Exchange(ref _featuresUs, ToMicroseconds(_sumFeaturesTicks, frames));
        Interlocked.Exchange(ref _writebackUs, ToMicroseconds(_sumWritebackTicks, frames));

        _frames = 0;
        _sumFrameTicks = 0;
        _sumPreprocessTicks = 0;
        _sumTransformTicks = 0;
        _sumNormalizationTicks = 0;
        _sumPitchTicks = 0;
        _sumClarityTicks = 0;
        _sumReassignTicks = 0;
        _sumFeaturesTicks = 0;
        _sumWritebackTicks = 0;
    }

    private static int ToMicroseconds(long ticks, int frames)
    {
        double us = ticks * TicksToMicroseconds / frames;
        if (us <= 0)
        {
            return 0;
        }

        if (us >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return (int)Math.Round(us);
    }
}

public sealed partial class VocalSpectrographPlugin
{
    /// <summary>
    /// Gets the latest averaged analysis timing snapshot.
    /// </summary>
    public SpectrographTimingSnapshot TimingSnapshot => _timing.GetSnapshot();
}
