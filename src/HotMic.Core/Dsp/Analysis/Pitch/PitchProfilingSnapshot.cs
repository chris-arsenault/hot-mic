namespace HotMic.Core.Dsp.Analysis.Pitch;

/// <summary>
/// Profiling snapshot for pitch detection steps (Stopwatch ticks).
/// </summary>
public readonly struct PitchProfilingSnapshot
{
    /// <summary>
    /// Initializes a new pitch profiling snapshot.
    /// </summary>
    public PitchProfilingSnapshot(
        PitchDetectorType algorithm,
        long lastTotalTicks,
        long maxTotalTicks,
        long lastDiffTicks,
        long maxDiffTicks,
        long lastCmndTicks,
        long maxCmndTicks,
        long lastSearchTicks,
        long maxSearchTicks,
        long lastRefineTicks,
        long maxRefineTicks,
        int frameSize = 0,
        int minPeriod = 0,
        int maxPeriod = 0,
        long lastTotalCpuCycles = 0,
        long maxTotalCpuCycles = 0,
        long lastDiffCpuCycles = 0,
        long maxDiffCpuCycles = 0)
    {
        Algorithm = algorithm;
        LastTotalTicks = lastTotalTicks;
        MaxTotalTicks = maxTotalTicks;
        LastDiffTicks = lastDiffTicks;
        MaxDiffTicks = maxDiffTicks;
        LastCmndTicks = lastCmndTicks;
        MaxCmndTicks = maxCmndTicks;
        LastSearchTicks = lastSearchTicks;
        MaxSearchTicks = maxSearchTicks;
        LastRefineTicks = lastRefineTicks;
        MaxRefineTicks = maxRefineTicks;
        FrameSize = frameSize;
        MinPeriod = minPeriod;
        MaxPeriod = maxPeriod;
        LastTotalCpuCycles = lastTotalCpuCycles;
        MaxTotalCpuCycles = maxTotalCpuCycles;
        LastDiffCpuCycles = lastDiffCpuCycles;
        MaxDiffCpuCycles = maxDiffCpuCycles;
    }

    /// <summary>Gets the algorithm used for pitch detection.</summary>
    public PitchDetectorType Algorithm { get; }

    /// <summary>Gets the last total pitch detection time in stopwatch ticks.</summary>
    public long LastTotalTicks { get; }

    /// <summary>Gets the max total pitch detection time in stopwatch ticks.</summary>
    public long MaxTotalTicks { get; }

    /// <summary>Gets the last difference calculation time in stopwatch ticks.</summary>
    public long LastDiffTicks { get; }

    /// <summary>Gets the max difference calculation time in stopwatch ticks.</summary>
    public long MaxDiffTicks { get; }

    /// <summary>Gets the last CMND calculation time in stopwatch ticks.</summary>
    public long LastCmndTicks { get; }

    /// <summary>Gets the max CMND calculation time in stopwatch ticks.</summary>
    public long MaxCmndTicks { get; }

    /// <summary>Gets the last search/candidate time in stopwatch ticks.</summary>
    public long LastSearchTicks { get; }

    /// <summary>Gets the max search/candidate time in stopwatch ticks.</summary>
    public long MaxSearchTicks { get; }

    /// <summary>Gets the last refine/score time in stopwatch ticks.</summary>
    public long LastRefineTicks { get; }

    /// <summary>Gets the max refine/score time in stopwatch ticks.</summary>
    public long MaxRefineTicks { get; }

    /// <summary>Gets the analysis frame size used by the detector.</summary>
    public int FrameSize { get; }

    /// <summary>Gets the minimum period (lag/tau) used by the detector.</summary>
    public int MinPeriod { get; }

    /// <summary>Gets the maximum period (lag/tau) used by the detector.</summary>
    public int MaxPeriod { get; }

    /// <summary>Gets the last total CPU cycles for pitch detection.</summary>
    public long LastTotalCpuCycles { get; }

    /// <summary>Gets the max total CPU cycles for pitch detection.</summary>
    public long MaxTotalCpuCycles { get; }

    /// <summary>Gets the last diff CPU cycles for pitch detection.</summary>
    public long LastDiffCpuCycles { get; }

    /// <summary>Gets the max diff CPU cycles for pitch detection.</summary>
    public long MaxDiffCpuCycles { get; }

    /// <summary>Gets whether step-level details are available.</summary>
    public bool HasDetail =>
        MaxDiffTicks > 0 || MaxCmndTicks > 0 || MaxSearchTicks > 0 || MaxRefineTicks > 0;

    /// <summary>Gets whether frame sizing data is available.</summary>
    public bool HasFrameInfo => FrameSize > 0;

    /// <summary>Gets whether period range data is available.</summary>
    public bool HasPeriodRange => MinPeriod > 0 && MaxPeriod >= MinPeriod;

    /// <summary>Gets whether CPU cycle data is available.</summary>
    public bool HasCpuCycles => LastTotalCpuCycles > 0 || MaxTotalCpuCycles > 0;

    /// <summary>Gets whether diff CPU cycle data is available.</summary>
    public bool HasDiffCpuCycles => LastDiffCpuCycles > 0 || MaxDiffCpuCycles > 0;
}
