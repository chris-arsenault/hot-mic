using System.Text.Json;

namespace HotMic.Vst3;

public sealed class Vst3Scanner
{
    private readonly string _cachePath;
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public Vst3Scanner(string? cachePath = null)
    {
        _cachePath = cachePath ?? GetDefaultCachePath();
    }

    public IReadOnlyList<Vst3PluginInfo> Scan(bool useCache = true)
    {
        if (useCache && File.Exists(_cachePath))
        {
            try
            {
                var json = File.ReadAllText(_cachePath);
                var cached = JsonSerializer.Deserialize<List<Vst3PluginInfo>>(json, SerializerOptions);
                if (cached is not null && cached.Count > 0)
                {
                    return cached;
                }
            }
            catch
            {
            }
        }

        var results = new List<Vst3PluginInfo>();
        foreach (var directory in GetDefaultSearchPaths())
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.vst3", SearchOption.AllDirectories))
            {
                results.Add(new Vst3PluginInfo
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    Path = file
                });
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath) ?? string.Empty);
        File.WriteAllText(_cachePath, JsonSerializer.Serialize(results, SerializerOptions));
        return results;
    }

    private static IEnumerable<string> GetDefaultSearchPaths()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        yield return Path.Combine(programFiles, "Common Files", "VST3");
        yield return Path.Combine(programFilesX86, "Common Files", "VST3");
    }

    private static string GetDefaultCachePath()
    {
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(basePath, "HotMic", "vst3-cache.json");
    }
}
