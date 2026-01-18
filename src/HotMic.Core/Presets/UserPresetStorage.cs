using System.Text.Json;
using System.Text.Json.Serialization;

namespace HotMic.Core.Presets;

/// <summary>
/// Serializable format for chain presets stored on disk.
/// </summary>
public sealed class StoredChainPreset
{
    public string Name { get; set; } = string.Empty;
    public List<StoredChainEntry> Plugins { get; set; } = new();
    public List<StoredChainContainer> Containers { get; set; } = new();
}

public sealed class StoredChainEntry
{
    public string PluginId { get; set; } = string.Empty;
    public Dictionary<string, float> Parameters { get; set; } = new();
}

public sealed class StoredChainContainer
{
    public string Name { get; set; } = string.Empty;
    public bool IsBypassed { get; set; }
    public List<int> PluginIndices { get; set; } = new();
}

/// <summary>
/// Handles loading and saving user-defined presets to disk.
/// </summary>
public sealed class UserPresetStorage
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    private readonly string _presetsDirectory;

    public UserPresetStorage(string? presetsDirectory = null)
    {
        _presetsDirectory = presetsDirectory ?? GetDefaultPresetsDirectory();
    }

    public string PresetsDirectory => _presetsDirectory;

    /// <summary>
    /// Loads all user chain presets from disk.
    /// </summary>
    public List<StoredChainPreset> LoadChainPresets()
    {
        var presets = new List<StoredChainPreset>();
        var chainDir = Path.Combine(_presetsDirectory, "chains");

        if (!Directory.Exists(chainDir))
        {
            return presets;
        }

        foreach (var file in Directory.GetFiles(chainDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var preset = JsonSerializer.Deserialize<StoredChainPreset>(json, SerializerOptions);
                if (preset != null && !string.IsNullOrWhiteSpace(preset.Name))
                {
                    presets.Add(preset);
                }
            }
            catch
            {
                // Skip invalid preset files
            }
        }

        return presets;
    }

    /// <summary>
    /// Saves a chain preset to disk. Returns true if successful.
    /// </summary>
    public bool SaveChainPreset(StoredChainPreset preset)
    {
        if (string.IsNullOrWhiteSpace(preset.Name))
        {
            return false;
        }

        try
        {
            var chainDir = Path.Combine(_presetsDirectory, "chains");
            Directory.CreateDirectory(chainDir);

            var filename = SanitizeFilename(preset.Name) + ".json";
            var path = Path.Combine(chainDir, filename);

            var json = JsonSerializer.Serialize(preset, SerializerOptions);
            File.WriteAllText(path, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes a user chain preset. Returns true if successful.
    /// </summary>
    public bool DeleteChainPreset(string presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
        {
            return false;
        }

        try
        {
            var chainDir = Path.Combine(_presetsDirectory, "chains");
            var filename = SanitizeFilename(presetName) + ".json";
            var path = Path.Combine(chainDir, filename);

            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }

            // Try to find by name in case filename doesn't match
            foreach (var file in Directory.GetFiles(chainDir, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var preset = JsonSerializer.Deserialize<StoredChainPreset>(json, SerializerOptions);
                    if (preset != null && string.Equals(preset.Name, presetName, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(file);
                        return true;
                    }
                }
                catch
                {
                    // Skip invalid files
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string GetDefaultPresetsDirectory()
    {
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(basePath, "HotMic", "presets");
    }

    private static string SanitizeFilename(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new char[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            sanitized[i] = Array.IndexOf(invalid, c) >= 0 ? '_' : c;
        }
        return new string(sanitized);
    }
}
