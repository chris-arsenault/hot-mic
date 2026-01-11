namespace HotMic.Common.Configuration;

public enum AudioQualityMode
{
    LatencyPriority = 0,
    QualityPriority = 1
}

public sealed record AudioQualityProfile(
    AudioQualityMode Mode,
    int TargetLatencyMs,
    int MaxLatencyMs,
    int BufferSize,
    int NoiseFftSize,
    int NoiseHopSize,
    int NoiseLearnFrames,
    int EqAnalysisSize,
    float EqSmoothingMs,
    int EqCoefficientUpdateStride,
    float GainSmoothingMs,
    float GateRatio,
    float GateMaxRangeDb,
    float GateSidechainHpfHz,
    float CompressorKneeDb,
    float CompressorRmsBlend,
    float CompressorReleaseShape,
    float CompressorSidechainHpfHz);

public static class AudioQualityProfiles
{
    public static AudioQualityProfile ForMode(AudioQualityMode mode, int sampleRate)
    {
        _ = sampleRate; // Reserved for future sample-rate dependent profiles.
        return mode == AudioQualityMode.QualityPriority
            ? new AudioQualityProfile(
                mode,
                TargetLatencyMs: 60,
                MaxLatencyMs: 120,
                BufferSize: 1024,
                NoiseFftSize: 2048,
                NoiseHopSize: 512,
                NoiseLearnFrames: 80,
                EqAnalysisSize: 2048,
                EqSmoothingMs: 20f,
                EqCoefficientUpdateStride: 8,
                GainSmoothingMs: 8f,
                GateRatio: 2.5f,
                GateMaxRangeDb: 30f,
                GateSidechainHpfHz: 80f,
                CompressorKneeDb: 9f,
                CompressorRmsBlend: 0.8f,
                CompressorReleaseShape: 0.1f,
                CompressorSidechainHpfHz: 80f)
            : new AudioQualityProfile(
                mode,
                TargetLatencyMs: 30,
                MaxLatencyMs: 60,
                BufferSize: 256,
                NoiseFftSize: 1024,
                NoiseHopSize: 256,
                NoiseLearnFrames: 50,
                EqAnalysisSize: 1024,
                EqSmoothingMs: 12f,
                EqCoefficientUpdateStride: 16,
                GainSmoothingMs: 4f,
                GateRatio: 3f,
                GateMaxRangeDb: 24f,
                GateSidechainHpfHz: 80f,
                CompressorKneeDb: 6f,
                CompressorRmsBlend: 0.7f,
                CompressorReleaseShape: 0.15f,
                CompressorSidechainHpfHz: 80f);
    }
}
