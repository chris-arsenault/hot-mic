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
            return config ?? CreateDefault();
        }
        catch
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

        config.Plugins.Add(new PluginConfig
        {
            Type = "builtin:input"
        });

        if (includeOutputSend)
        {
            config.Plugins.Add(new PluginConfig
            {
                Type = "builtin:output-send"
            });
        }

        return config;
    }
}
