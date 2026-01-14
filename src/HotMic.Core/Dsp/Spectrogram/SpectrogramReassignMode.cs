using System;

namespace HotMic.Core.Dsp.Spectrogram;

/// <summary>
/// Controls time and/or frequency reassignment for spectrogram displays.
/// </summary>
[Flags]
public enum SpectrogramReassignMode
{
    Off = 0,
    Frequency = 1,
    Time = 2,
    TimeFrequency = Frequency | Time
}
