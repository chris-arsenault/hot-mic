using System.Threading;
using HotMic.Core.Dsp.Analysis;
using HotMic.Core.Metering;

namespace HotMic.Core.Plugins;

/// <summary>
/// Holds a plugin instance along with its metering and spectral delta state.
/// </summary>
public sealed class PluginSlot
{
    private long _lastProcessTicks;
    private long _maxProcessTicks;
    private long _lastProcessCpuCycles;
    private long _maxProcessCpuCycles;
    private long _overBudgetCount;

    public PluginSlot(int instanceId, IPlugin plugin, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(instanceId);

        Plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        InstanceId = instanceId;
        Meter = new MeterProcessor(sampleRate);
        Delta = new SpectralDeltaProcessor(sampleRate);
    }

    /// <summary>
    /// Stable instance identifier for this plugin in the chain.
    /// </summary>
    public int InstanceId { get; }

    /// <summary>
    /// The audio plugin implementation.
    /// </summary>
    public IPlugin Plugin { get; }

    /// <summary>
    /// Meter processor for post-plugin levels.
    /// </summary>
    public MeterProcessor Meter { get; }

    /// <summary>
    /// Spectral delta processor for pre/post comparison.
    /// </summary>
    public SpectralDeltaProcessor Delta { get; }

    /// <summary>
    /// Ticks spent in the last plugin Process call.
    /// </summary>
    public long LastProcessTicks => Interlocked.Read(ref _lastProcessTicks);

    /// <summary>
    /// Maximum ticks observed for the plugin Process call.
    /// </summary>
    public long MaxProcessTicks => Interlocked.Read(ref _maxProcessTicks);

    /// <summary>
    /// CPU cycles spent in the last plugin Process call.
    /// </summary>
    public long LastProcessCpuCycles => Interlocked.Read(ref _lastProcessCpuCycles);

    /// <summary>
    /// Maximum CPU cycles observed for the plugin Process call.
    /// </summary>
    public long MaxProcessCpuCycles => Interlocked.Read(ref _maxProcessCpuCycles);

    /// <summary>
    /// Count of times the plugin exceeded the block budget.
    /// </summary>
    public long OverBudgetCount => Interlocked.Read(ref _overBudgetCount);

    internal void RecordProfiling(long elapsedTicks, long budgetTicks, long cpuCycles)
    {
        Interlocked.Exchange(ref _lastProcessTicks, elapsedTicks);
        UpdateMax(ref _maxProcessTicks, elapsedTicks);
        if (elapsedTicks > budgetTicks)
        {
            Interlocked.Increment(ref _overBudgetCount);
        }

        Interlocked.Exchange(ref _lastProcessCpuCycles, cpuCycles);
        if (cpuCycles > 0)
        {
            UpdateMax(ref _maxProcessCpuCycles, cpuCycles);
        }
    }

    internal void ResetProfiling()
    {
        Interlocked.Exchange(ref _lastProcessTicks, 0);
        Interlocked.Exchange(ref _maxProcessTicks, 0);
        Interlocked.Exchange(ref _lastProcessCpuCycles, 0);
        Interlocked.Exchange(ref _maxProcessCpuCycles, 0);
        Interlocked.Exchange(ref _overBudgetCount, 0);
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
