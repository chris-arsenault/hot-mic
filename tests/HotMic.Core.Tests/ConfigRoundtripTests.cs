using System.Text.Json;
using HotMic.Common.Configuration;
using Xunit;

namespace HotMic.Core.Tests;

/// <summary>
/// Tests that configuration persistence survives serialization roundtrips.
/// These tests exercise the same JSON path as ConfigManager.
/// </summary>
public class ConfigRoundtripTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    private static T Roundtrip<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, SerializerOptions);
        return JsonSerializer.Deserialize<T>(json, SerializerOptions)!;
    }

    [Fact]
    public void AppConfig_FullRoundtrip_PreservesAllFields()
    {
        var original = new AppConfig
        {
            AudioSettings = new AudioSettingsConfig
            {
                OutputDeviceId = "device-123",
                MonitorOutputDeviceId = "monitor-456",
                SampleRate = 44100,
                BufferSize = 512,
                QualityMode = AudioQualityMode.QualityPriority
            },
            EnableVstPlugins = false,
            Ui = new UiConfig { ViewMode = "minimal", AlwaysOnTop = true }
        };
        original.Channels.Add(new ChannelConfig
        {
            Id = 1, Name = "Mic 1", InputDeviceId = "input-1",
            InputChannel = InputChannelMode.Left,
            InputGainDb = -3.5f, OutputGainDb = 2.0f,
            IsMuted = true, IsSoloed = false, PresetName = "Broadcast"
        });
        original.Channels[0].Plugins.Add(new PluginConfig
        {
            InstanceId = 10, Type = "builtin:gain", IsBypassed = true,
            PresetName = "MyPreset", State = new byte[] { 0xDE, 0xAD }
        });
        original.Channels[0].Plugins[0].Parameters["Gain"] = -6.0f;
        original.Channels[0].Plugins[0].Parameters["Phase"] = 1.0f;
        original.Channels[0].Containers.Add(new PluginContainerConfig
        {
            Id = 1, Name = "FX Rack", IsBypassed = true
        });
        original.Channels[0].Containers[0].PluginInstanceIds.Add(10);
        original.Vst2SearchPaths.Add(@"C:\VST2");
        original.Vst3SearchPaths.Add(@"C:\VST3");
        original.Midi = new MidiConfig
        {
            Enabled = true, DeviceName = "nanoKONTROL", FilterChannel = 1
        };
        original.Midi.Bindings.Add(new MidiBinding
        {
            CcNumber = 7, Channel = 1, TargetPath = "ch1/gain",
            MinValue = 0f, MaxValue = 1f
        });

        var restored = Roundtrip(original);

        // Audio settings
        Assert.Equal("device-123", restored.AudioSettings.OutputDeviceId);
        Assert.Equal("monitor-456", restored.AudioSettings.MonitorOutputDeviceId);
        Assert.Equal(44100, restored.AudioSettings.SampleRate);
        Assert.Equal(512, restored.AudioSettings.BufferSize);
        Assert.Equal(AudioQualityMode.QualityPriority, restored.AudioSettings.QualityMode);

        // Top-level
        Assert.False(restored.EnableVstPlugins);
        Assert.Equal("minimal", restored.Ui.ViewMode);
        Assert.True(restored.Ui.AlwaysOnTop);

        // Channels
        Assert.Single(restored.Channels);
        var ch = restored.Channels[0];
        Assert.Equal(1, ch.Id);
        Assert.Equal("Mic 1", ch.Name);
        Assert.Equal("input-1", ch.InputDeviceId);
        Assert.Equal(InputChannelMode.Left, ch.InputChannel);
        Assert.Equal(-3.5f, ch.InputGainDb);
        Assert.Equal(2.0f, ch.OutputGainDb);
        Assert.True(ch.IsMuted);
        Assert.False(ch.IsSoloed);
        Assert.Equal("Broadcast", ch.PresetName);

        // Plugins
        Assert.Single(ch.Plugins);
        var plug = ch.Plugins[0];
        Assert.Equal(10, plug.InstanceId);
        Assert.Equal("builtin:gain", plug.Type);
        Assert.True(plug.IsBypassed);
        Assert.Equal("MyPreset", plug.PresetName);
        Assert.Equal(new byte[] { 0xDE, 0xAD }, plug.State);
        Assert.Equal(-6.0f, plug.Parameters["Gain"]);
        Assert.Equal(1.0f, plug.Parameters["Phase"]);

        // Containers
        Assert.Single(ch.Containers);
        Assert.Equal("FX Rack", ch.Containers[0].Name);
        Assert.True(ch.Containers[0].IsBypassed);
        Assert.Equal(new[] { 10 }, ch.Containers[0].PluginInstanceIds);

        // VST paths
        Assert.Equal(new[] { @"C:\VST2" }, restored.Vst2SearchPaths);
        Assert.Equal(new[] { @"C:\VST3" }, restored.Vst3SearchPaths);

        // MIDI
        Assert.True(restored.Midi.Enabled);
        Assert.Equal("nanoKONTROL", restored.Midi.DeviceName);
        Assert.Equal(1, restored.Midi.FilterChannel);
        Assert.Single(restored.Midi.Bindings);
        Assert.Equal(7, restored.Midi.Bindings[0].CcNumber);
        Assert.Equal("ch1/gain", restored.Midi.Bindings[0].TargetPath);
    }

    [Fact]
    public void EmptyConfig_Roundtrip_PreservesEmptyCollections()
    {
        var original = new AppConfig();
        var restored = Roundtrip(original);

        Assert.Empty(restored.Channels);
        Assert.Empty(restored.Vst2SearchPaths);
        Assert.Empty(restored.Vst3SearchPaths);
        Assert.Empty(restored.Midi.Bindings);
    }

    [Fact]
    public void PluginState_ByteArray_SurvivesBase64Roundtrip()
    {
        var state = new byte[256];
        for (int i = 0; i < 256; i++) state[i] = (byte)i;

        var config = new PluginConfig { InstanceId = 1, Type = "test", State = state };
        var restored = Roundtrip(config);

        Assert.Equal(state, restored.State);
    }

    [Fact]
    public void PluginState_Null_SurvivesRoundtrip()
    {
        var config = new PluginConfig { InstanceId = 1, Type = "test", State = null };
        var restored = Roundtrip(config);

        Assert.Null(restored.State);
    }

    [Fact]
    public void SpecialFloatValues_Roundtrip()
    {
        var config = new PluginConfig { InstanceId = 1, Type = "test" };
        config.Parameters["nan"] = float.NaN;
        config.Parameters["inf"] = float.PositiveInfinity;
        config.Parameters["neginf"] = float.NegativeInfinity;
        config.Parameters["zero"] = 0f;
        config.Parameters["negzero"] = -0f;

        var restored = Roundtrip(config);

        Assert.True(float.IsNaN(restored.Parameters["nan"]));
        Assert.True(float.IsPositiveInfinity(restored.Parameters["inf"]));
        Assert.True(float.IsNegativeInfinity(restored.Parameters["neginf"]));
        Assert.Equal(0f, restored.Parameters["zero"]);
    }

    [Fact]
    public void MultipleChannels_MultiplePlugins_Roundtrip()
    {
        var original = new AppConfig();
        for (int c = 0; c < 3; c++)
        {
            var ch = new ChannelConfig { Id = c + 1, Name = $"Ch {c + 1}" };
            for (int p = 0; p < 4; p++)
            {
                var plug = new PluginConfig
                {
                    InstanceId = c * 10 + p,
                    Type = $"builtin:plugin-{p}"
                };
                plug.Parameters[$"param-{p}"] = p * 0.25f;
                ch.Plugins.Add(plug);
            }
            original.Channels.Add(ch);
        }

        var restored = Roundtrip(original);

        Assert.Equal(3, restored.Channels.Count);
        for (int c = 0; c < 3; c++)
        {
            Assert.Equal(4, restored.Channels[c].Plugins.Count);
            for (int p = 0; p < 4; p++)
            {
                Assert.Equal($"builtin:plugin-{p}", restored.Channels[c].Plugins[p].Type);
                Assert.Equal(p * 0.25f, restored.Channels[c].Plugins[p].Parameters[$"param-{p}"]);
            }
        }
    }

    [Fact]
    public void ConfigManager_SaveAndLoad_Roundtrip()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"hotmic-test-{Guid.NewGuid()}.json");
        try
        {
            var manager = new ConfigManager(tempPath);

            var original = new AppConfig
            {
                AudioSettings = new AudioSettingsConfig { SampleRate = 96000, BufferSize = 128 }
            };
            original.Channels.Add(new ChannelConfig { Id = 1, Name = "Test" });
            original.Channels[0].Nodes.Add(new PluginNodeConfig
            {
                InstanceId = 5, Type = "builtin:compressor",
                State = new byte[] { 1, 2, 3 }
            });

            manager.Save(original);
            var restored = manager.LoadOrDefault();

            Assert.Equal(96000, restored.AudioSettings.SampleRate);
            Assert.Single(restored.Channels);
            Assert.Equal("Test", restored.Channels[0].Name);
            Assert.Single(restored.Channels[0].Nodes);
            var plug = Assert.IsType<PluginNodeConfig>(restored.Channels[0].Nodes[0]);
            Assert.Equal("builtin:compressor", plug.Type);
            Assert.Equal(new byte[] { 1, 2, 3 }, plug.State);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void NodeTree_PluginAndContainer_Roundtrip()
    {
        var config = new ChannelConfig { Id = 1, Name = "Test" };
        config.Nodes.Add(new PluginNodeConfig { InstanceId = 1, Type = "builtin:input" });
        config.Nodes.Add(new ContainerNodeConfig
        {
            ContainerId = 1,
            Name = "Noise Removal",
            IsBypassed = true,
            Plugins =
            {
                new PluginNodeConfig { InstanceId = 2, Type = "builtin:hpf" },
                new PluginNodeConfig { InstanceId = 3, Type = "builtin:speechdenoiser" }
            }
        });
        config.Nodes.Add(new PluginNodeConfig { InstanceId = 4, Type = "builtin:compressor" });
        config.Nodes.Add(new PluginNodeConfig { InstanceId = 5, Type = "builtin:output-send" });

        var restored = Roundtrip(config);

        Assert.Equal(4, restored.Nodes.Count);

        // Node 0: standalone plugin
        var n0 = Assert.IsType<PluginNodeConfig>(restored.Nodes[0]);
        Assert.Equal("builtin:input", n0.Type);
        Assert.Equal(1, n0.InstanceId);

        // Node 1: container with 2 children
        var n1 = Assert.IsType<ContainerNodeConfig>(restored.Nodes[1]);
        Assert.Equal("Noise Removal", n1.Name);
        Assert.True(n1.IsBypassed);
        Assert.Equal(2, n1.Plugins.Count);
        Assert.Equal("builtin:hpf", n1.Plugins[0].Type);
        Assert.Equal("builtin:speechdenoiser", n1.Plugins[1].Type);

        // Node 2: standalone
        var n2 = Assert.IsType<PluginNodeConfig>(restored.Nodes[2]);
        Assert.Equal("builtin:compressor", n2.Type);

        // Node 3: standalone
        var n3 = Assert.IsType<PluginNodeConfig>(restored.Nodes[3]);
        Assert.Equal("builtin:output-send", n3.Type);
    }

    [Fact]
    public void NodeTree_FlattenPlugins_CorrectOrder()
    {
        var nodes = new List<ChainNodeConfig>
        {
            new PluginNodeConfig { InstanceId = 1, Type = "A" },
            new ContainerNodeConfig { ContainerId = 1, Name = "C1", Plugins =
            {
                new PluginNodeConfig { InstanceId = 2, Type = "B" },
                new PluginNodeConfig { InstanceId = 3, Type = "C" }
            }},
            new PluginNodeConfig { InstanceId = 4, Type = "D" }
        };

        var flat = ChainNodeHelpers.FlattenPlugins(nodes);

        Assert.Equal(4, flat.Count);
        Assert.Equal("A", flat[0].Type);
        Assert.Equal("B", flat[1].Type);
        Assert.Equal("C", flat[2].Type);
        Assert.Equal("D", flat[3].Type);
    }

    [Fact]
    public void NodeTree_MigrateFromLegacy_PreservesOrderAndContainers()
    {
#pragma warning disable CS0618
        var plugins = new List<PluginConfig>
        {
            new() { InstanceId = 1, Type = "builtin:input" },
            new() { InstanceId = 2, Type = "builtin:hpf" },
            new() { InstanceId = 3, Type = "builtin:denoiser" },
            new() { InstanceId = 4, Type = "builtin:compressor" },
            new() { InstanceId = 5, Type = "builtin:output-send" }
        };
        var containers = new List<PluginContainerConfig>
        {
            new() { Id = 1, Name = "Noise", PluginInstanceIds = { 2, 3 } }
        };
#pragma warning restore CS0618

        var nodes = ChainNodeHelpers.MigrateFromLegacy(plugins, containers);

        // Should be: Input, Container(HPF, Denoiser), Compressor, OutputSend
        Assert.Equal(4, nodes.Count);

        Assert.IsType<PluginNodeConfig>(nodes[0]);
        Assert.Equal("builtin:input", ((PluginNodeConfig)nodes[0]).Type);

        var container = Assert.IsType<ContainerNodeConfig>(nodes[1]);
        Assert.Equal("Noise", container.Name);
        Assert.Equal(2, container.Plugins.Count);
        Assert.Equal("builtin:hpf", container.Plugins[0].Type);
        Assert.Equal("builtin:denoiser", container.Plugins[1].Type);

        Assert.Equal("builtin:compressor", ((PluginNodeConfig)nodes[2]).Type);
        Assert.Equal("builtin:output-send", ((PluginNodeConfig)nodes[3]).Type);
    }

    [Fact]
    public void NodeTree_RemovePlugin_FromContainer()
    {
        var nodes = new List<ChainNodeConfig>
        {
            new PluginNodeConfig { InstanceId = 1, Type = "A" },
            new ContainerNodeConfig { ContainerId = 1, Name = "C1", Plugins =
            {
                new PluginNodeConfig { InstanceId = 2, Type = "B" },
                new PluginNodeConfig { InstanceId = 3, Type = "C" }
            }},
            new PluginNodeConfig { InstanceId = 4, Type = "D" }
        };

        Assert.True(ChainNodeHelpers.RemovePlugin(nodes, 2, out var removed));
        Assert.Equal("B", removed!.Type);

        // Container should now have 1 child
        var container = Assert.IsType<ContainerNodeConfig>(nodes[1]);
        Assert.Single(container.Plugins);
        Assert.Equal("C", container.Plugins[0].Type);
    }

    [Fact]
    public void NodeTree_FindContainerForPlugin_ReturnsCorrectContainer()
    {
        var nodes = new List<ChainNodeConfig>
        {
            new PluginNodeConfig { InstanceId = 1, Type = "A" },
            new ContainerNodeConfig { ContainerId = 10, Name = "C1", Plugins =
            {
                new PluginNodeConfig { InstanceId = 2, Type = "B" },
            }},
        };

        Assert.Null(ChainNodeHelpers.FindContainerForPlugin(nodes, 1));
        Assert.Equal(10, ChainNodeHelpers.FindContainerForPlugin(nodes, 2)!.ContainerId);
    }
}
