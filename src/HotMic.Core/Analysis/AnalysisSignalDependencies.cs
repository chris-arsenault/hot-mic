using HotMic.Core.Plugins;

namespace HotMic.Core.Analysis;

public static class AnalysisSignalDependencies
{
    private static readonly AnalysisSignalMask[] SignalDependencies =
    {
        AnalysisSignalMask.None,                                             // SpeechPresence
        AnalysisSignalMask.None,                                             // VoicingScore
        AnalysisSignalMask.None,                                             // VoicingState
        AnalysisSignalMask.None,                                             // FricativeActivity
        AnalysisSignalMask.None,                                             // SibilanceEnergy
        AnalysisSignalMask.None,                                             // OnsetFluxHigh
        AnalysisSignalMask.PitchConfidence,                                  // PitchHz
        AnalysisSignalMask.PitchHz,                                          // PitchConfidence
        AnalysisSignalMask.None,                                             // SpectralFlux
        AnalysisSignalMask.None                                              // HnrDb
    };

    public static AnalysisSignalMask Expand(AnalysisSignalMask requested)
    {
        AnalysisSignalMask expanded = requested;
        while (true)
        {
            AnalysisSignalMask added = AnalysisSignalMask.None;
            int mask = (int)expanded;
            for (int i = 0; i < SignalDependencies.Length; i++)
            {
                if ((mask & (1 << i)) == 0)
                {
                    continue;
                }

                added |= SignalDependencies[i];
            }

            AnalysisSignalMask next = expanded | added;
            if (next == expanded)
            {
                return expanded;
            }

            expanded = next;
        }
    }
}
