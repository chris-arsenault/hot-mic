using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace HotMic.Core.Plugins.BuiltIn;

internal sealed class DeepFilterNetConfig
{
    public DeepFilterNetConfig(
        int sampleRate,
        int fftSize,
        int hopSize,
        int nbErb,
        int nbDf,
        int minNbErbFreqs,
        int dfOrder,
        int convLookahead,
        int dfLookahead,
        float normTau,
        float? normAlpha,
        string modelType)
    {
        SampleRate = sampleRate;
        FftSize = fftSize;
        HopSize = hopSize;
        NbErb = nbErb;
        NbDf = nbDf;
        MinNbErbFreqs = minNbErbFreqs;
        DfOrder = dfOrder;
        ConvLookahead = convLookahead;
        DfLookahead = dfLookahead;
        NormTau = normTau;
        NormAlpha = normAlpha;
        ModelType = modelType;
    }

    public int SampleRate { get; }
    public int FftSize { get; }
    public int HopSize { get; }
    public int NbErb { get; }
    public int NbDf { get; }
    public int MinNbErbFreqs { get; }
    public int DfOrder { get; }
    public int ConvLookahead { get; }
    public int DfLookahead { get; }
    public float NormTau { get; }
    public float? NormAlpha { get; }
    public string ModelType { get; }

    public int Lookahead => Math.Max(ConvLookahead, DfLookahead);

    public static DeepFilterNetConfig Load(string path)
    {
        var df = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var net = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var train = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string section = string.Empty;

        foreach (var rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }
            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                section = line[1..^1].Trim();
                continue;
            }

            int idx = line.IndexOf('=');
            if (idx <= 0)
            {
                continue;
            }

            string key = line[..idx].Trim();
            string value = line[(idx + 1)..].Trim();

            switch (section)
            {
                case "df":
                    df[key] = value;
                    break;
                case "deepfilternet":
                    net[key] = value;
                    break;
                case "train":
                    train[key] = value;
                    break;
            }
        }

        int sampleRate = ParseInt(df, "sr");
        int fftSize = ParseInt(df, "fft_size");
        int hopSize = ParseInt(df, "hop_size");
        int nbErb = ParseInt(df, "nb_erb");
        int nbDf = ParseInt(df, "nb_df");
        int minNbErbFreqs = ParseInt(df, "min_nb_erb_freqs");
        int dfOrder = ParseInt(df, "df_order", fallback: ParseInt(net, "df_order", fallback: 5));
        int convLookahead = ParseInt(net, "conv_lookahead", fallback: 0);
        int dfLookahead = ParseInt(df, "df_lookahead", fallback: 0);
        float normTau = ParseFloat(df, "norm_tau", fallback: 1f);
        float? normAlpha = ParseOptionalFloat(df, "norm_alpha");
        string modelType = train.TryGetValue("model", out var model) ? model : "deepfilternet3";

        return new DeepFilterNetConfig(
            sampleRate,
            fftSize,
            hopSize,
            nbErb,
            nbDf,
            minNbErbFreqs,
            dfOrder,
            convLookahead,
            dfLookahead,
            normTau,
            normAlpha,
            modelType);
    }

    private static int ParseInt(Dictionary<string, string> map, string key, int? fallback = null)
    {
        if (map.TryGetValue(key, out var value) &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            return parsed;
        }

        if (fallback.HasValue)
        {
            return fallback.Value;
        }

        throw new InvalidOperationException($"Missing required config key '{key}'.");
    }

    private static float ParseFloat(Dictionary<string, string> map, string key, float? fallback = null)
    {
        if (map.TryGetValue(key, out var value) &&
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
        {
            return parsed;
        }

        if (fallback.HasValue)
        {
            return fallback.Value;
        }

        throw new InvalidOperationException($"Missing required config key '{key}'.");
    }

    private static float? ParseOptionalFloat(Dictionary<string, string> map, string key)
    {
        if (map.TryGetValue(key, out var value) &&
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
        {
            return parsed;
        }

        return null;
    }
}
