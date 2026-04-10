using System.Text.Json;
using System.Text.Json.Serialization;

namespace HotMic.Common.Configuration;

public sealed class ConfigManager
{
    private readonly string _configPath;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public ConfigManager(string? configPath = null)
    {
        _configPath = configPath ?? GetDefaultConfigPath();
    }

    public AppConfig LoadOrDefault()
    {
        if (!File.Exists(_configPath))
        {
            return CreateDefault();
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions);
            if (config is null) return CreateDefault();
            MigrateChannels(config);
            return config;
        }
        catch (IOException)
        {
            return CreateDefault();
        }
        catch (JsonException)
        {
            return CreateDefault();
        }
    }

    public void Save(AppConfig config)
    {
        var directory = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config, SerializerOptions);
        File.WriteAllText(_configPath, json);
    }

    private static string GetDefaultConfigPath()
    {
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(basePath, "HotMic", "config.json");
    }

    private static AppConfig CreateDefault()
    {
        return new AppConfig
        {
            AudioSettings = new AudioSettingsConfig
            {
                SampleRate = 48000,
                BufferSize = 256,
                QualityMode = AudioQualityMode.LatencyPriority
            },
            Channels =
            {
                CreateDefaultChannel(1, "Mic 1", includeOutputSend: true)
            },
            Ui = new UiConfig
            {
                ViewMode = "full",
                AlwaysOnTop = false
            }
        };
    }

    private static ChannelConfig CreateDefaultChannel(int id, string name, bool includeOutputSend)
    {
        var config = new ChannelConfig
        {
            Id = id,
            Name = name,
            InputChannel = InputChannelMode.Sum
        };

        config.Nodes.Add(new PluginNodeConfig { Type = "builtin:input" });

        if (includeOutputSend)
        {
            config.Nodes.Add(new PluginNodeConfig { Type = "builtin:output-send" });
        }

        return config;
    }

    /// <summary>
    /// Migrate channels from legacy Plugins+Containers format to Nodes format.
    /// </summary>
    private static void MigrateChannels(AppConfig config)
    {
        foreach (var channel in config.Channels)
        {
            if (channel.Nodes.Count > 0)
                continue; // Already in new format

#pragma warning disable CS0618 // Accessing obsolete Plugins/Containers for migration
            if (channel.Plugins.Count == 0)
                continue;

            channel.Nodes = ChainNodeHelpers.MigrateFromLegacy(channel.Plugins, channel.Containers);
            channel.Plugins.Clear();
            channel.Containers.Clear();
#pragma warning restore CS0618
        }
    }
}
