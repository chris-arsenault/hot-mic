using System;

namespace HotMic.Core.Plugins;

public enum SidechainSignalId
{
    SpeechPresence = 0,
    VoicedProbability = 1,
    UnvoicedEnergy = 2,
    SibilanceEnergy = 3,
    Count = 4
}

[Flags]
public enum SidechainSignalMask
{
    None = 0,
    SpeechPresence = 1 << (int)SidechainSignalId.SpeechPresence,
    VoicedProbability = 1 << (int)SidechainSignalId.VoicedProbability,
    UnvoicedEnergy = 1 << (int)SidechainSignalId.UnvoicedEnergy,
    SibilanceEnergy = 1 << (int)SidechainSignalId.SibilanceEnergy,
    All = SpeechPresence | VoicedProbability | UnvoicedEnergy | SibilanceEnergy
}
