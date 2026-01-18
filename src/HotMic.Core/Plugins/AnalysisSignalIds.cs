using System;

namespace HotMic.Core.Plugins;

public enum AnalysisSignalId
{
    SpeechPresence = 0,
    VoicingScore = 1,
    VoicingState = 2,
    FricativeActivity = 3,
    SibilanceEnergy = 4,
    OnsetFluxHigh = 5,
    PitchHz = 6,
    PitchConfidence = 7,
    FormantF1Hz = 8,
    FormantF2Hz = 9,
    FormantF3Hz = 10,
    FormantConfidence = 11,
    SpectralFlux = 12,
    HnrDb = 13,
    Count = 14
}

[Flags]
public enum AnalysisSignalMask
{
    None = 0,
    SpeechPresence = 1 << (int)AnalysisSignalId.SpeechPresence,
    VoicingScore = 1 << (int)AnalysisSignalId.VoicingScore,
    VoicingState = 1 << (int)AnalysisSignalId.VoicingState,
    FricativeActivity = 1 << (int)AnalysisSignalId.FricativeActivity,
    SibilanceEnergy = 1 << (int)AnalysisSignalId.SibilanceEnergy,
    OnsetFluxHigh = 1 << (int)AnalysisSignalId.OnsetFluxHigh,
    PitchHz = 1 << (int)AnalysisSignalId.PitchHz,
    PitchConfidence = 1 << (int)AnalysisSignalId.PitchConfidence,
    FormantF1Hz = 1 << (int)AnalysisSignalId.FormantF1Hz,
    FormantF2Hz = 1 << (int)AnalysisSignalId.FormantF2Hz,
    FormantF3Hz = 1 << (int)AnalysisSignalId.FormantF3Hz,
    FormantConfidence = 1 << (int)AnalysisSignalId.FormantConfidence,
    SpectralFlux = 1 << (int)AnalysisSignalId.SpectralFlux,
    HnrDb = 1 << (int)AnalysisSignalId.HnrDb,
    All = (1 << (int)AnalysisSignalId.Count) - 1
}
