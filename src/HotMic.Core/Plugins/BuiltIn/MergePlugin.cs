using System.Threading;
using HotMic.Core.Dsp;

namespace HotMic.Core.Plugins.BuiltIn;

/// <summary>
/// Merges audio from multiple source channels into the current buffer with optional alignment.
/// </summary>
public sealed class MergePlugin : IContextualPlugin
{
    private const int SumModeIndex = 0;
    private const int PhaseModeIndex = 1;
    private const int LatencyModeIndex = 2;
    private const int SourceCountIndex = 3;
    private const int SourceStartIndex = 4;
    private const int MaxSources = 16;
    private const int DefaultSourceCount = 2;
    private const int MaxSourceChannelId = 32;
    private const byte StateVersion = 1;

    private static readonly PluginParameter[] ParametersDefinition = BuildParameters();

    private readonly int[] _sourceChannels = new int[MaxSources];
    private DelayLine? _targetDelayLine;
    private DelayLine?[] _sourceDelayLines = new DelayLine?[MaxSources];
    private float[]? _targetScratch;
    private float[]?[] _sourceScratch = new float[]?[MaxSources];
    private int _maxDelaySamples;
    private int _latencySamples;
    private MergeSumStrategy _sumStrategy = MergeSumStrategy.Sum;
    private MergePhaseMode _phaseMode = MergePhaseMode.None;
    private MergeLatencyMode _latencyMode = MergeLatencyMode.Align;
    private int _sourceCount = DefaultSourceCount;

    public string Id => "builtin:merge";

    public string Name => "Merge";

    public bool IsBypassed { get; set; }

    public int LatencySamples => Volatile.Read(ref _latencySamples);

    public IReadOnlyList<PluginParameter> Parameters => ParametersDefinition;

    public MergePlugin()
    {
        for (int i = 0; i < MaxSources; i++)
        {
            _sourceChannels[i] = i + 1;
        }
    }

    public int SourceCount => _sourceCount;

    public int GetSourceChannelId(int index)
    {
        if ((uint)index >= (uint)_sourceCount)
        {
            return 0;
        }

        return _sourceChannels[index];
    }

    public void Initialize(int sampleRate, int blockSize)
    {
        _maxDelaySamples = Math.Max(sampleRate, blockSize);
        _targetDelayLine ??= new DelayLine(_maxDelaySamples);
        _targetDelayLine.EnsureCapacity(_maxDelaySamples);
        _targetScratch = new float[blockSize];

        for (int i = 0; i < MaxSources; i++)
        {
            _sourceDelayLines[i] ??= new DelayLine(_maxDelaySamples);
            _sourceDelayLines[i]!.EnsureCapacity(_maxDelaySamples);
            _sourceScratch[i] = new float[blockSize];
        }
    }

    public void Process(Span<float> buffer)
    {
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            Volatile.Write(ref _latencySamples, 0);
            return;
        }

        var targetDelayLine = _targetDelayLine;
        var targetScratch = _targetScratch;
        if (targetDelayLine is null || targetScratch is null)
        {
            return;
        }

        int targetLatency = context.CumulativeLatencySamples;
        int alignLatency = targetLatency;

        if (_latencyMode == MergeLatencyMode.Align)
        {
            for (int i = 0; i < _sourceCount; i++)
            {
                int channelId = _sourceChannels[i];
                int channelIndex = channelId - 1;
                if ((uint)channelIndex >= (uint)context.Routing.ChannelCount || channelIndex == context.ChannelId)
                {
                    continue;
                }

                if (!context.Routing.TryGetChannelOutput(channelIndex, out _, out _, out int latencySamples))
                {
                    continue;
                }

                if (latencySamples > alignLatency)
                {
                    alignLatency = latencySamples;
                }
            }
        }

        int targetDelay = _latencyMode == MergeLatencyMode.Align ? alignLatency - targetLatency : 0;
        int maxTargetDelay = targetDelayLine.Capacity - 1;
        if (targetDelay < 0)
        {
            targetDelay = 0;
        }
        if (targetDelay > maxTargetDelay)
        {
            targetDelay = maxTargetDelay;
        }

        if (targetDelay > 0)
        {
            targetDelayLine.Process(buffer, buffer, targetDelay);
        }

        if (_phaseMode == MergePhaseMode.InvertTarget)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = -buffer[i];
            }
        }

        int activeSources = 0;
        bool invertSources = _phaseMode == MergePhaseMode.InvertSources;

        for (int i = 0; i < _sourceCount; i++)
        {
            int channelId = _sourceChannels[i];
            int channelIndex = channelId - 1;
            if ((uint)channelIndex >= (uint)context.Routing.ChannelCount || channelIndex == context.ChannelId)
            {
                continue;
            }

            if (!context.Routing.TryGetChannelOutput(channelIndex, out var sourceBuffer, out int length, out int latencySamples))
            {
                continue;
            }

            if (length <= 0)
            {
                continue;
            }

            if ((uint)activeSources >= (uint)MaxSources)
            {
                break;
            }

            int delay = _latencyMode == MergeLatencyMode.Align ? alignLatency - latencySamples : 0;
            if (delay < 0)
            {
                delay = 0;
            }

            var delayLine = _sourceDelayLines[activeSources];
            var scratch = _sourceScratch[activeSources];
            if (delayLine is null || scratch is null)
            {
                continue;
            }

            int maxDelay = delayLine.Capacity - 1;
            if (delay > maxDelay)
            {
                delay = maxDelay;
            }

            int count = Math.Min(buffer.Length, length);
            delayLine.Process(sourceBuffer.Slice(0, count), scratch.AsSpan(0, count), delay);

            for (int s = 0; s < count; s++)
            {
                float sample = scratch[s];
                if (invertSources)
                {
                    sample = -sample;
                }
                buffer[s] += sample;
            }

            activeSources++;
        }

        int total = 1 + activeSources;
        if (total > 1)
        {
            float scale = _sumStrategy switch
            {
                MergeSumStrategy.Average => 1f / total,
                MergeSumStrategy.EqualPower => 1f / MathF.Sqrt(total),
                _ => 1f
            };

            if (MathF.Abs(scale - 1f) > 1e-6f)
            {
                for (int i = 0; i < buffer.Length; i++)
                {
                    buffer[i] *= scale;
                }
            }
        }

        Volatile.Write(ref _latencySamples, targetDelay);
    }

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case SumModeIndex:
                _sumStrategy = (MergeSumStrategy)ClampToInt(value, 0, (int)MergeSumStrategy.EqualPower);
                break;
            case PhaseModeIndex:
                _phaseMode = (MergePhaseMode)ClampToInt(value, 0, (int)MergePhaseMode.InvertTarget);
                break;
            case LatencyModeIndex:
                _latencyMode = (MergeLatencyMode)ClampToInt(value, 0, (int)MergeLatencyMode.Off);
                break;
            case SourceCountIndex:
                _sourceCount = ClampToInt(value, 2, MaxSources);
                break;
            default:
                if (index >= SourceStartIndex)
                {
                    int sourceIndex = index - SourceStartIndex;
                    if ((uint)sourceIndex < (uint)MaxSources)
                    {
                        _sourceChannels[sourceIndex] = ClampToInt(value, 1, MaxSourceChannelId);
                    }
                }
                break;
        }
    }

    public byte[] GetState()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(StateVersion);
        writer.Write((int)_sumStrategy);
        writer.Write((int)_phaseMode);
        writer.Write((int)_latencyMode);
        writer.Write(_sourceCount);
        for (int i = 0; i < _sourceCount && i < MaxSources; i++)
        {
            writer.Write(_sourceChannels[i]);
        }
        return stream.ToArray();
    }

    public void SetState(byte[] state)
    {
        if (state is null || state.Length == 0)
        {
            return;
        }

        try
        {
            using var stream = new MemoryStream(state);
            using var reader = new BinaryReader(stream);
            byte version = reader.ReadByte();
            if (version != StateVersion)
            {
                return;
            }

            _sumStrategy = (MergeSumStrategy)ClampToInt(reader.ReadInt32(), 0, (int)MergeSumStrategy.EqualPower);
            _phaseMode = (MergePhaseMode)ClampToInt(reader.ReadInt32(), 0, (int)MergePhaseMode.InvertTarget);
            _latencyMode = (MergeLatencyMode)ClampToInt(reader.ReadInt32(), 0, (int)MergeLatencyMode.Off);
            _sourceCount = ClampToInt(reader.ReadInt32(), 2, MaxSources);
            for (int i = 0; i < _sourceCount; i++)
            {
                _sourceChannels[i] = ClampToInt(reader.ReadInt32(), 1, MaxSourceChannelId);
            }
        }
        catch
        {
        }
    }

    public void Dispose()
    {
    }

    private static int ClampToInt(float value, int min, int max)
    {
        int result = (int)MathF.Round(value);
        if (result < min)
        {
            return min;
        }
        if (result > max)
        {
            return max;
        }
        return result;
    }

    private static PluginParameter[] BuildParameters()
    {
        var parameters = new PluginParameter[SourceStartIndex + MaxSources];
        parameters[SumModeIndex] = new PluginParameter
        {
            Index = SumModeIndex,
            Name = "Sum Mode",
            MinValue = 0f,
            MaxValue = (int)MergeSumStrategy.EqualPower,
            DefaultValue = (int)MergeSumStrategy.Sum,
            Unit = string.Empty,
            FormatValue = value => ((MergeSumStrategy)ClampToInt(value, 0, (int)MergeSumStrategy.EqualPower)) switch
            {
                MergeSumStrategy.Sum => "Sum",
                MergeSumStrategy.Average => "Average",
                _ => "Equal Power"
            }
        };
        parameters[PhaseModeIndex] = new PluginParameter
        {
            Index = PhaseModeIndex,
            Name = "Phase Mode",
            MinValue = 0f,
            MaxValue = (int)MergePhaseMode.InvertTarget,
            DefaultValue = (int)MergePhaseMode.None,
            Unit = string.Empty,
            FormatValue = value => ((MergePhaseMode)ClampToInt(value, 0, (int)MergePhaseMode.InvertTarget)) switch
            {
                MergePhaseMode.None => "None",
                MergePhaseMode.InvertSources => "Invert Sources",
                _ => "Invert Target"
            }
        };
        parameters[LatencyModeIndex] = new PluginParameter
        {
            Index = LatencyModeIndex,
            Name = "Latency Mode",
            MinValue = 0f,
            MaxValue = (int)MergeLatencyMode.Off,
            DefaultValue = (int)MergeLatencyMode.Align,
            Unit = string.Empty,
            FormatValue = value => ((MergeLatencyMode)ClampToInt(value, 0, (int)MergeLatencyMode.Off)) switch
            {
                MergeLatencyMode.Align => "Align",
                _ => "Off"
            }
        };
        parameters[SourceCountIndex] = new PluginParameter
        {
            Index = SourceCountIndex,
            Name = "Source Count",
            MinValue = 2f,
            MaxValue = MaxSources,
            DefaultValue = DefaultSourceCount,
            Unit = string.Empty,
            FormatValue = value => $"{ClampToInt(value, 2, MaxSources)}"
        };

        for (int i = 0; i < MaxSources; i++)
        {
            parameters[SourceStartIndex + i] = new PluginParameter
            {
                Index = SourceStartIndex + i,
                Name = $"Source {i + 1}",
                MinValue = 1f,
                MaxValue = MaxSourceChannelId,
                DefaultValue = i + 1,
                Unit = string.Empty,
                FormatValue = value => $"Ch {ClampToInt(value, 1, MaxSourceChannelId)}"
            };
        }

        return parameters;
    }

    private enum MergeSumStrategy
    {
        Sum = 0,
        Average = 1,
        EqualPower = 2
    }

    private enum MergePhaseMode
    {
        None = 0,
        InvertSources = 1,
        InvertTarget = 2
    }

    private enum MergeLatencyMode
    {
        Align = 0,
        Off = 1
    }
}
