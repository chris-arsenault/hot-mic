using HotMic.Core.Dsp.Analysis;
using HotMic.Core.Metering;

namespace HotMic.Core.Plugins;

/// <summary>
/// Holds a plugin instance along with its metering and spectral delta state.
/// </summary>
public sealed class PluginSlot
{
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
}
