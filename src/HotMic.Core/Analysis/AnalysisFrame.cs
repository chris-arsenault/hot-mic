using HotMic.Core.Dsp.Analysis;

namespace HotMic.Core.Analysis;

/// <summary>
/// Per-frame analysis results. Stored in ring buffers.
/// </summary>
public struct AnalysisFrame
{
    // Pitch
    public float PitchHz { get; set; }
    public float PitchConfidence { get; set; }
    public VoicingState VoicingState { get; set; }

    // Waveform
    public float WaveformMin { get; set; }
    public float WaveformMax { get; set; }

    // Spectral features
    public float SpectralCentroid { get; set; }
    public float SpectralSlope { get; set; }
    public float SpectralFlux { get; set; }
    public float Hnr { get; set; }
    public float Cpp { get; set; }
}

/// <summary>
/// Harmonic data for a single frame (up to 24 harmonics).
/// </summary>
public struct HarmonicFrame
{
    public const int MaxHarmonics = 24;

    // Store harmonics as inline fields to avoid array allocation
    public float F0 { get; set; }
    public float F1 { get; set; }
    public float F2 { get; set; }
    public float F3 { get; set; }
    public float F4 { get; set; }
    public float F5 { get; set; }
    public float F6 { get; set; }
    public float F7 { get; set; }
    public float F8 { get; set; }
    public float F9 { get; set; }
    public float F10 { get; set; }
    public float F11 { get; set; }
    public float F12 { get; set; }
    public float F13 { get; set; }
    public float F14 { get; set; }
    public float F15 { get; set; }
    public float F16 { get; set; }
    public float F17 { get; set; }
    public float F18 { get; set; }
    public float F19 { get; set; }
    public float F20 { get; set; }
    public float F21 { get; set; }
    public float F22 { get; set; }
    public float F23 { get; set; }
    public float M0 { get; set; }
    public float M1 { get; set; }
    public float M2 { get; set; }
    public float M3 { get; set; }
    public float M4 { get; set; }
    public float M5 { get; set; }
    public float M6 { get; set; }
    public float M7 { get; set; }
    public float M8 { get; set; }
    public float M9 { get; set; }
    public float M10 { get; set; }
    public float M11 { get; set; }
    public float M12 { get; set; }
    public float M13 { get; set; }
    public float M14 { get; set; }
    public float M15 { get; set; }
    public float M16 { get; set; }
    public float M17 { get; set; }
    public float M18 { get; set; }
    public float M19 { get; set; }
    public float M20 { get; set; }
    public float M21 { get; set; }
    public float M22 { get; set; }
    public float M23 { get; set; }

    public int Count { get; set; }

    public float GetFrequency(int index) => index switch
    {
        0 => F0, 1 => F1, 2 => F2, 3 => F3, 4 => F4, 5 => F5,
        6 => F6, 7 => F7, 8 => F8, 9 => F9, 10 => F10, 11 => F11,
        12 => F12, 13 => F13, 14 => F14, 15 => F15, 16 => F16, 17 => F17,
        18 => F18, 19 => F19, 20 => F20, 21 => F21, 22 => F22, 23 => F23,
        _ => 0f
    };

    public float GetMagnitude(int index) => index switch
    {
        0 => M0, 1 => M1, 2 => M2, 3 => M3, 4 => M4, 5 => M5,
        6 => M6, 7 => M7, 8 => M8, 9 => M9, 10 => M10, 11 => M11,
        12 => M12, 13 => M13, 14 => M14, 15 => M15, 16 => M16, 17 => M17,
        18 => M18, 19 => M19, 20 => M20, 21 => M21, 22 => M22, 23 => M23,
        _ => 0f
    };

    public void SetFrequency(int index, float value)
    {
        switch (index)
        {
            case 0: F0 = value; break; case 1: F1 = value; break;
            case 2: F2 = value; break; case 3: F3 = value; break;
            case 4: F4 = value; break; case 5: F5 = value; break;
            case 6: F6 = value; break; case 7: F7 = value; break;
            case 8: F8 = value; break; case 9: F9 = value; break;
            case 10: F10 = value; break; case 11: F11 = value; break;
            case 12: F12 = value; break; case 13: F13 = value; break;
            case 14: F14 = value; break; case 15: F15 = value; break;
            case 16: F16 = value; break; case 17: F17 = value; break;
            case 18: F18 = value; break; case 19: F19 = value; break;
            case 20: F20 = value; break; case 21: F21 = value; break;
            case 22: F22 = value; break; case 23: F23 = value; break;
        }
    }

    public void SetMagnitude(int index, float value)
    {
        switch (index)
        {
            case 0: M0 = value; break; case 1: M1 = value; break;
            case 2: M2 = value; break; case 3: M3 = value; break;
            case 4: M4 = value; break; case 5: M5 = value; break;
            case 6: M6 = value; break; case 7: M7 = value; break;
            case 8: M8 = value; break; case 9: M9 = value; break;
            case 10: M10 = value; break; case 11: M11 = value; break;
            case 12: M12 = value; break; case 13: M13 = value; break;
            case 14: M14 = value; break; case 15: M15 = value; break;
            case 16: M16 = value; break; case 17: M17 = value; break;
            case 18: M18 = value; break; case 19: M19 = value; break;
            case 20: M20 = value; break; case 21: M21 = value; break;
            case 22: M22 = value; break; case 23: M23 = value; break;
        }
    }
}

/// <summary>
/// Speech metrics for a single frame.
/// </summary>
public struct SpeechMetricsFrame
{
    public float SyllableRate { get; set; }
    public float ArticulationRate { get; set; }
    public float PauseRatio { get; set; }
    public float MonotoneScore { get; set; }
    public float ClarityScore { get; set; }
    public float IntelligibilityScore { get; set; }
    public byte SpeakingState { get; set; }
    public bool SyllableDetected { get; set; }
}
