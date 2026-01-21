using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace HotMic.App.Diagnostics;

internal static class EnhanceDebug
{
    private const string EnvVar = "DEBUG_ENHANCE";
    private static readonly bool Enabled = string.Equals(Environment.GetEnvironmentVariable(EnvVar), "1", StringComparison.OrdinalIgnoreCase);
    private static readonly Dictionary<string, TagStats> CollectionStats = new(StringComparer.OrdinalIgnoreCase);
    private static bool _collectionActive;
    private static string _collectionName = string.Empty;
    private static DateTimeOffset _collectionStart;

    public static bool IsEnabled => Enabled;
    public static bool IsCollecting => _collectionActive;

    public static bool ShouldLog(ref long lastTicks, int intervalMs = 500)
    {
        if (!Enabled)
        {
            return false;
        }

        long now = Stopwatch.GetTimestamp();
        long intervalTicks = intervalMs * Stopwatch.Frequency / 1000;
        if (now - lastTicks < intervalTicks)
        {
            return false;
        }

        lastTicks = now;
        return true;
    }

    public static float ScaleFactor(int index)
    {
        return index switch
        {
            1 => 2f,
            2 => 5f,
            3 => 10f,
            _ => 1f
        };
    }

    public static void Log(string tag, string message)
    {
        if (!Enabled)
        {
            return;
        }

        Trace.WriteLine($"[EnhanceDebug] {tag}: {message}");

        if (_collectionActive && !string.Equals(tag, "SignalGenerator", StringComparison.OrdinalIgnoreCase))
        {
            Collect(tag, message);
        }
    }

    public static void BeginCollection(string name)
    {
        if (!Enabled)
        {
            return;
        }

        if (_collectionActive)
        {
            EndCollection("restart");
        }

        _collectionActive = true;
        _collectionName = string.IsNullOrWhiteSpace(name) ? "sample" : name;
        _collectionStart = DateTimeOffset.Now;
        CollectionStats.Clear();

        Trace.WriteLine($"[EnhanceDebug] Collection started: {_collectionName}");
    }

    public static void EndCollection(string reason)
    {
        if (!Enabled || !_collectionActive)
        {
            return;
        }

        _collectionActive = false;
        var duration = DateTimeOffset.Now - _collectionStart;

        Trace.WriteLine($"[EnhanceDebugSummary] {_collectionName} duration={duration.TotalSeconds:0.00}s reason={reason} tags={CollectionStats.Count}");

        foreach (var (tag, stats) in CollectionStats)
        {
            var line = new StringBuilder();
            line.Append(CultureInfo.InvariantCulture, $"[EnhanceDebugSummary] {tag} lines={stats.LineCount}");

            foreach (var metric in stats.Metrics)
            {
                var m = metric.Value;
                if (m.Count == 0) continue;
                float avg = (float)(m.Sum / m.Count);
                line.Append(CultureInfo.InvariantCulture, $" {metric.Key}=min:{m.Min:0.###} max:{m.Max:0.###} avg:{avg:0.###}");
            }

            Trace.WriteLine(line.ToString());
        }

        CollectionStats.Clear();
    }

    private static void Collect(string tag, string message)
    {
        if (!_collectionActive)
        {
            return;
        }

        if (!CollectionStats.TryGetValue(tag, out var stats))
        {
            stats = new TagStats();
            CollectionStats[tag] = stats;
        }

        stats.LineCount++;

        var tokens = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            if (!TryParseMetric(token, out string key, out float value))
            {
                continue;
            }

            if (!stats.Metrics.TryGetValue(key, out var metric))
            {
                metric = MetricStats.Create(value);
                stats.Metrics[key] = metric;
            }
            else
            {
                metric.Add(value);
                stats.Metrics[key] = metric;
            }
        }
    }

    private static bool TryParseMetric(string token, out string key, out float value)
    {
        key = string.Empty;
        value = 0f;

        int eq = token.IndexOf('=');
        if (eq <= 0 || eq >= token.Length - 1)
        {
            return false;
        }

        key = token.Substring(0, eq);
        string raw = token.Substring(eq + 1);

        if (string.IsNullOrEmpty(raw))
        {
            return false;
        }

        if (raw[0] == '"' || raw[0] == '\'')
        {
            return false;
        }

        if (raw[0] == 'x' || raw[0] == 'X')
        {
            raw = raw.Substring(1);
        }

        raw = raw.TrimEnd(',', ';');

        int end = raw.Length;
        while (end > 0)
        {
            char c = raw[end - 1];
            if ((c >= '0' && c <= '9') || c == '.' || c == '-' || c == '+')
            {
                break;
            }
            end--;
        }

        if (end <= 0)
        {
            return false;
        }

        if (end != raw.Length)
        {
            raw = raw.Substring(0, end);
        }

        return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private sealed class TagStats
    {
        public int LineCount;
        public readonly Dictionary<string, MetricStats> Metrics = new(StringComparer.OrdinalIgnoreCase);
    }

    private struct MetricStats
    {
        public int Count;
        public float Min;
        public float Max;
        public double Sum;

        public static MetricStats Create(float value)
        {
            return new MetricStats
            {
                Count = 1,
                Min = value,
                Max = value,
                Sum = value
            };
        }

        public void Add(float value)
        {
            Count++;
            Sum += value;
            if (value < Min) Min = value;
            if (value > Max) Max = value;
        }
    }
}
