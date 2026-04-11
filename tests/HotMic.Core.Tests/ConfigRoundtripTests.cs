using System.Text.Json;
using HotMic.Common.Configuration;
using Xunit;

namespace HotMic.Core.Tests;

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

        var ch = new ChannelConfig
        {
            Id = 1, Name = "Mic 1", InputDeviceId = "input-1",
            InputChannel = InputChannelMode.Left,
            InputGainDb = -3.5f, OutputGainDb = 2.0f,
            IsMuted = true, IsSoloed = false, PresetName = "Broadcast"
        };

        var gainPlugin = new PluginNodeConfig
        {
            InstanceId = 10, Type = "builtin:gain", IsBypassed = true,
            PresetName = "MyPreset", State = new byte[] { 0xDE, 0xAD }
        };
        gainPlugin.Parameters["Gain"] = -6.0f;
        gainPlugin.Parameters["Phase"] = 1.0f;

        ch.Nodes.Add(gainPlugin);
        ch.Nodes.Add(new ContainerNodeConfig
        {
            ContainerId = 1, Name = "FX Rack", IsBypassed = true,
            Plugins = { new PluginNodeConfig { InstanceId = 20, Type = "builtin:compressor" } }
        });

        original.Channels.Add(ch);
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

        Assert.Equal(44100, restored.AudioSettings.SampleRate);
        Assert.False(restored.EnableVstPlugins);
        Assert.Equal("minimal", restored.Ui.ViewMode);
        Assert.True(restored.Ui.AlwaysOnTop);

        Assert.Single(restored.Channels);
        var rch = restored.Channels[0];
        Assert.Equal("Mic 1", rch.Name);
        Assert.Equal(InputChannelMode.Left, rch.InputChannel);
        Assert.Equal(-3.5f, rch.InputGainDb);
        Assert.True(rch.IsMuted);
        Assert.Equal("Broadcast", rch.PresetName);

        // Nodes
        Assert.Equal(2, rch.Nodes.Count);
        var rGain = Assert.IsType<PluginNodeConfig>(rch.Nodes[0]);
        Assert.Equal("builtin:gain", rGain.Type);
        Assert.True(rGain.IsBypassed);
        Assert.Equal(new byte[] { 0xDE, 0xAD }, rGain.State);
        Assert.Equal(-6.0f, rGain.Parameters["Gain"]);

        var rContainer = Assert.IsType<ContainerNodeConfig>(rch.Nodes[1]);
        Assert.Equal("FX Rack", rContainer.Name);
        Assert.True(rContainer.IsBypassed);
        Assert.Single(rContainer.Plugins);
        Assert.Equal("builtin:compressor", rContainer.Plugins[0].Type);

        // VST paths
        Assert.Equal(new[] { @"C:\VST2" }, restored.Vst2SearchPaths);
        Assert.Equal(new[] { @"C:\VST3" }, restored.Vst3SearchPaths);

        // MIDI
        Assert.True(restored.Midi.Enabled);
        Assert.Single(restored.Midi.Bindings);
        Assert.Equal(7, restored.Midi.Bindings[0].CcNumber);
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

        var node = new PluginNodeConfig { InstanceId = 1, Type = "test", State = state };
        var restored = Roundtrip(node);

        Assert.Equal(state, restored.State);
    }

    [Fact]
    public void PluginState_Null_SurvivesRoundtrip()
    {
        var node = new PluginNodeConfig { InstanceId = 1, Type = "test", State = null };
        var restored = Roundtrip(node);

        Assert.Null(restored.State);
    }

    [Fact]
    public void SpecialFloatValues_Roundtrip()
    {
        var node = new PluginNodeConfig { InstanceId = 1, Type = "test" };
        node.Parameters["nan"] = float.NaN;
        node.Parameters["inf"] = float.PositiveInfinity;
        node.Parameters["neginf"] = float.NegativeInfinity;
        node.Parameters["zero"] = 0f;

        var restored = Roundtrip(node);

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
                var node = new PluginNodeConfig
                {
                    InstanceId = c * 10 + p,
                    Type = $"builtin:plugin-{p}"
                };
                node.Parameters[$"param-{p}"] = p * 0.25f;
                ch.Nodes.Add(node);
            }
            original.Channels.Add(ch);
        }

        var restored = Roundtrip(original);

        Assert.Equal(3, restored.Channels.Count);
        for (int c = 0; c < 3; c++)
        {
            Assert.Equal(4, restored.Channels[c].Nodes.Count);
            for (int p = 0; p < 4; p++)
            {
                var node = Assert.IsType<PluginNodeConfig>(restored.Channels[c].Nodes[p]);
                Assert.Equal($"builtin:plugin-{p}", node.Type);
                Assert.Equal(p * 0.25f, node.Parameters[$"param-{p}"]);
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
            ContainerId = 1, Name = "Noise Removal", IsBypassed = true,
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
        Assert.IsType<PluginNodeConfig>(restored.Nodes[0]);
        var container = Assert.IsType<ContainerNodeConfig>(restored.Nodes[1]);
        Assert.Equal("Noise Removal", container.Name);
        Assert.True(container.IsBypassed);
        Assert.Equal(2, container.Plugins.Count);
        Assert.IsType<PluginNodeConfig>(restored.Nodes[2]);
        Assert.IsType<PluginNodeConfig>(restored.Nodes[3]);
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
