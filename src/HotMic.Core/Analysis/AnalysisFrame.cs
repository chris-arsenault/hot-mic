using HotMic.Core.Dsp.Analysis;

namespace HotMic.Core.Analysis;

/// <summary>
/// Per-frame analysis results. Stored in ring buffers.
/// </summary>
public struct AnalysisFrame
{
    // Pitch
    public float PitchHz;
    public float PitchConfidence;
    public VoicingState VoicingState;

    // Waveform
    public float WaveformMin;
    public float WaveformMax;

    // Spectral features
    public float SpectralCentroid;
    public float SpectralSlope;
    public float SpectralFlux;
    public float Hnr;
    public float Cpp;
}

/// <summary>
/// Formant data for a single frame (up to 5 formants).
/// </summary>
public struct FormantFrame
{
    public float F1, F2, F3, F4, F5;
    public float B1, B2, B3, B4, B5;

    public float GetFrequency(int index) => index switch
    {
        0 => F1,
        1 => F2,
        2 => F3,
        3 => F4,
        4 => F5,
        _ => 0f
    };

    public float GetBandwidth(int index) => index switch
    {
        0 => B1,
        1 => B2,
        2 => B3,
        3 => B4,
        4 => B5,
        _ => 0f
    };

    public void SetFrequency(int index, float value)
    {
        switch (index)
        {
            case 0: F1 = value; break;
            case 1: F2 = value; break;
            case 2: F3 = value; break;
            case 3: F4 = value; break;
            case 4: F5 = value; break;
        }
    }

    public void SetBandwidth(int index, float value)
    {
        switch (index)
        {
            case 0: B1 = value; break;
            case 1: B2 = value; break;
            case 2: B3 = value; break;
            case 3: B4 = value; break;
            case 4: B5 = value; break;
        }
    }
}

/// <summary>
/// Harmonic data for a single frame (up to 24 harmonics).
/// </summary>
public struct HarmonicFrame
{
    public const int MaxHarmonics = 24;

    // Store harmonics as inline fields to avoid array allocation
    public float F0, F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11;
    public float F12, F13, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23;
    public float M0, M1, M2, M3, M4, M5, M6, M7, M8, M9, M10, M11;
    public float M12, M13, M14, M15, M16, M17, M18, M19, M20, M21, M22, M23;

    public int Count;

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
    public float SyllableRate;
    public float ArticulationRate;
    public float PauseRatio;
    public float MonotoneScore;
    public float ClarityScore;
    public float IntelligibilityScore;
    public byte SpeakingState;
    public bool SyllableDetected;
}
