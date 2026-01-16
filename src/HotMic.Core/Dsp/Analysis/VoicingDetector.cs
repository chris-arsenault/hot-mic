using System.Diagnostics;

namespace HotMic.Core.Dsp.Analysis;

/// <summary>
/// Voicing classification for speech analysis.
/// </summary>
public enum VoicingState : byte
{
    Silence = 0,
    Unvoiced = 1,
    Voiced = 2
}

/// <summary>
/// Simple voiced/unvoiced detector based on energy, ZCR, autocorrelation confidence, and spectral flatness.
/// </summary>
public sealed class VoicingDetector
{
    public float ZcrThreshold { get; set; } = 0.1f;
    public float EnergyThresholdDb { get; set; } = -40f;
    public float AutocorrThreshold { get; set; } = 0.3f;
    public float SpectralFlatnessThreshold { get; set; } = 0.5f;

    // Diagnostics
    private static long _lastDiagnosticTicks;
    private static readonly long DiagnosticIntervalTicks = Stopwatch.Frequency; // 1 second

    /// <summary>
    /// Detect voicing state for the given frame and spectrum.
    /// </summary>
    public VoicingState Detect(ReadOnlySpan<float> frame, ReadOnlySpan<float> magnitudes, float pitchConfidence)
    {
        if (frame.IsEmpty)
        {
            return VoicingState.Silence;
        }

        float rms = ComputeRms(frame);
        float energyDb = DspUtils.LinearToDb(rms);

        // Diagnostics
        long now = Stopwatch.GetTimestamp();
        bool shouldLog = now - _lastDiagnosticTicks > DiagnosticIntervalTicks;
        if (shouldLog) _lastDiagnosticTicks = now;

        if (energyDb < EnergyThresholdDb)
        {
            if (shouldLog) Console.WriteLine($"[Voicing] SILENCE: energyDb={energyDb:F1} < threshold={EnergyThresholdDb}");
            return VoicingState.Silence;
        }

        float zcr = ComputeZeroCrossingRate(frame);
        float flatness = ComputeSpectralFlatness(magnitudes);

        bool voiced = pitchConfidence >= AutocorrThreshold
                      && zcr < ZcrThreshold
                      && flatness < SpectralFlatnessThreshold;

        if (shouldLog)
        {
            Console.WriteLine($"[Voicing] energyDb={energyDb:F1}, zcr={zcr:F3}(<{ZcrThreshold}?), flat={flatness:F3}(<{SpectralFlatnessThreshold}?), conf={pitchConfidence:F2}(>={AutocorrThreshold}?)");
            Console.WriteLine($"[Voicing] result={(!voiced ? (zcr > ZcrThreshold || flatness > SpectralFlatnessThreshold ? "Unvoiced" : (pitchConfidence >= AutocorrThreshold ? "Voiced" : "Unvoiced")) : "Voiced")}");
        }

        if (voiced)
        {
            return VoicingState.Voiced;
        }

        if (zcr > ZcrThreshold || flatness > SpectralFlatnessThreshold)
        {
            return VoicingState.Unvoiced;
        }

        return pitchConfidence >= AutocorrThreshold ? VoicingState.Voiced : VoicingState.Unvoiced;
    }

    private static float ComputeRms(ReadOnlySpan<float> frame)
    {
        double sum = 0.0;
        for (int i = 0; i < frame.Length; i++)
        {
            float v = frame[i];
            sum += v * v;
        }

        return (float)Math.Sqrt(sum / Math.Max(1, frame.Length));
    }

    private static float ComputeZeroCrossingRate(ReadOnlySpan<float> frame)
    {
        int crossings = 0;
        float prev = frame[0];
        for (int i = 1; i < frame.Length; i++)
        {
            float current = frame[i];
            if ((prev >= 0f && current < 0f) || (prev < 0f && current >= 0f))
            {
                crossings++;
            }
            prev = current;
        }

        return crossings / MathF.Max(1f, frame.Length - 1);
    }

    private static float ComputeSpectralFlatness(ReadOnlySpan<float> magnitudes)
    {
        if (magnitudes.IsEmpty)
        {
            return 1f;
        }

        float logSum = 0f;
        float linSum = 0f;
        int count = magnitudes.Length;
        for (int i = 0; i < count; i++)
        {
            float mag = MathF.Max(magnitudes[i], 1e-12f);
            logSum += MathF.Log(mag);
            linSum += mag;
        }

        float geometric = MathF.Exp(logSum / count);
        float arithmetic = linSum / count;
        return arithmetic > 1e-12f ? geometric / arithmetic : 1f;
    }
}
