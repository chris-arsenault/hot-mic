using HotMic.Core.Plugins;
using HotMic.Core.Plugins.BuiltIn;
using Xunit;

namespace HotMic.Core.Tests;

/// <summary>
/// Tests parameter clamping and state serialization roundtrips for all built-in plugins.
/// </summary>
public class PluginParameterTests
{
    private static readonly string[] AllBuiltinIds =
    [
        "builtin:gain", "builtin:noisegate", "builtin:compressor", "builtin:deesser",
        "builtin:limiter", "builtin:hpf", "builtin:eq3", "builtin:fft-noise",
        "builtin:deverb", "builtin:saturation", "builtin:upward-expander",
        "builtin:spectral-contrast", "builtin:dynamic-eq", "builtin:air-exciter",
        "builtin:bass-enhancer", "builtin:consonant-transient", "builtin:room-tone",
        "builtin:vitalizer-mk2t"
    ];

    // Plugins requiring native/ONNX dependencies — skip in CI
    private static readonly HashSet<string> SkipIds = new()
    {
        "builtin:rnnoise",           // native lib
        "builtin:speechdenoiser",    // ONNX model
        "builtin:voice-gate",        // ONNX model
        "builtin:reverb"             // IR file
    };

    public static IEnumerable<object[]> BuiltinPluginIds()
    {
        foreach (var id in AllBuiltinIds)
        {
            if (!SkipIds.Contains(id))
                yield return new object[] { id };
        }
    }

    /// <summary>
    /// Verifies that parameter min/max declarations are valid (min &lt; max).
    /// </summary>
    [Theory]
    [MemberData(nameof(BuiltinPluginIds))]
    public void ParameterRanges_MinLessThanMax(string pluginId)
    {
        using var plugin = PluginFactory.Create(pluginId)!;

        foreach (var param in plugin.Parameters)
        {
            Assert.True(param.MinValue <= param.MaxValue,
                $"{pluginId} param '{param.Name}': min ({param.MinValue}) > max ({param.MaxValue})");
            Assert.True(param.DefaultValue >= param.MinValue && param.DefaultValue <= param.MaxValue,
                $"{pluginId} param '{param.Name}': default ({param.DefaultValue}) outside [{param.MinValue}, {param.MaxValue}]");
        }
    }

    [Theory]
    [MemberData(nameof(BuiltinPluginIds))]
    public void DefaultValues_MatchDeclaredDefaults(string pluginId)
    {
        using var plugin = PluginFactory.Create(pluginId)!;
        plugin.Initialize(48000, 256);

        // Get state of default-initialized plugin
        var defaultState = plugin.GetState();

        // Set all parameters to their declared defaults explicitly
        foreach (var param in plugin.Parameters)
        {
            plugin.SetParameter(param.Index, param.DefaultValue);
        }

        var explicitDefaultState = plugin.GetState();

        // States should match — declared defaults should equal initial state
        Assert.Equal(defaultState, explicitDefaultState);
    }

    [Theory]
    [MemberData(nameof(BuiltinPluginIds))]
    public void StateRoundtrip_NonDefaultValues_Preserved(string pluginId)
    {
        using var plugin = PluginFactory.Create(pluginId)!;
        plugin.Initialize(48000, 256);

        // Set each parameter to midpoint of its range
        foreach (var param in plugin.Parameters)
        {
            float mid = (param.MinValue + param.MaxValue) / 2f;
            plugin.SetParameter(param.Index, mid);
        }

        var state = plugin.GetState();

        // Restore state to a fresh instance
        using var plugin2 = PluginFactory.Create(pluginId)!;
        plugin2.Initialize(48000, 256);
        plugin2.SetState(state);

        var restoredState = plugin2.GetState();
        Assert.Equal(state, restoredState);
    }
}
