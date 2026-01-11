using System.Text.Json;

namespace HotMic.Vst3;

public sealed class Vst3Scanner
{
    private readonly string _cachePath;
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private const int CacheVersion = 2;

    private sealed record VstPluginCache
    {
        public int Version { get; init; } = CacheVersion;
        public List<Vst3PluginInfo> Plugins { get; init; } = new();
    }

    public Vst3Scanner(string? cachePath = null)
    {
        _cachePath = cachePath ?? GetDefaultCachePath();
    }

    public IReadOnlyList<Vst3PluginInfo> Scan(IReadOnlyList<string>? vst2Paths = null, IReadOnlyList<string>? vst3Paths = null, bool useCache = true)
    {
        bool canUseCache = useCache &&
                           (vst2Paths is null || vst2Paths.Count == 0) &&
                           (vst3Paths is null || vst3Paths.Count == 0);

        if (canUseCache && File.Exists(_cachePath))
        {
            try
            {
                var json = File.ReadAllText(_cachePath);
                var cached = JsonSerializer.Deserialize<VstPluginCache>(json, SerializerOptions);
                if (cached is not null && cached.Version == CacheVersion && cached.Plugins.Count > 0)
                {
                    return cached.Plugins;
                }
            }
            catch
            {
            }
        }

        var results = new List<Vst3PluginInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in GetVst3SearchPaths(vst3Paths))
        {
            ScanDirectory(directory, "*.vst3", SearchOption.AllDirectories, VstPluginFormat.Vst3, results, seen, scanDirectories: true);
        }

        foreach (var directory in GetVst2SearchPaths(vst2Paths))
        {
            ScanDirectory(directory, "*.dll", SearchOption.AllDirectories, VstPluginFormat.Vst2, results, seen, scanDirectories: false);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath) ?? string.Empty);
        var cache = new VstPluginCache { Plugins = results };
        File.WriteAllText(_cachePath, JsonSerializer.Serialize(cache, SerializerOptions));
        return results;
    }

    private static IEnumerable<string> GetVst3SearchPaths(IReadOnlyList<string>? additionalPaths)
    {
        var paths = new List<string>();
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        paths.Add(Path.Combine(programFiles, "Common Files", "VST3"));
        paths.Add(Path.Combine(programFilesX86, "Common Files", "VST3"));

        if (additionalPaths is not null)
        {
            paths.AddRange(additionalPaths);
        }

        return NormalizePaths(paths);
    }

    private static IEnumerable<string> GetVst2SearchPaths(IReadOnlyList<string>? additionalPaths)
    {
        var paths = new List<string>();
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        paths.Add(Path.Combine(programFiles, "VstPlugins"));
        paths.Add(Path.Combine(programFiles, "VSTPlugins"));
        paths.Add(Path.Combine(programFiles, "Steinberg", "VstPlugins"));
        paths.Add(Path.Combine(programFiles, "Common Files", "VST2"));
        paths.Add(Path.Combine(programFiles, "Common Files", "Steinberg", "VST2"));

        paths.Add(Path.Combine(programFilesX86, "VstPlugins"));
        paths.Add(Path.Combine(programFilesX86, "VSTPlugins"));
        paths.Add(Path.Combine(programFilesX86, "Steinberg", "VstPlugins"));
        paths.Add(Path.Combine(programFilesX86, "Common Files", "VST2"));
        paths.Add(Path.Combine(programFilesX86, "Common Files", "Steinberg", "VST2"));

        if (additionalPaths is not null)
        {
            paths.AddRange(additionalPaths);
        }

        return NormalizePaths(paths);
    }

    private static IEnumerable<string> NormalizePaths(IEnumerable<string> paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (seen.Add(path))
            {
                yield return path;
            }
        }
    }

    private static void ScanDirectory(
        string directory,
        string searchPattern,
        SearchOption searchOption,
        VstPluginFormat format,
        List<Vst3PluginInfo> results,
        HashSet<string> seen,
        bool scanDirectories)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        try
        {
            var entries = scanDirectories
                ? Directory.EnumerateDirectories(directory, searchPattern, searchOption)
                : Directory.EnumerateFiles(directory, searchPattern, searchOption);

            foreach (var file in entries)
            {
                if (!seen.Add(file))
                {
                    continue;
                }

                results.Add(new Vst3PluginInfo
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    Path = file,
                    Format = format
                });
            }
        }
        catch
        {
        }
    }

    private static string GetDefaultCachePath()
    {
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(basePath, "HotMic", "vst-cache.json");
    }
}
