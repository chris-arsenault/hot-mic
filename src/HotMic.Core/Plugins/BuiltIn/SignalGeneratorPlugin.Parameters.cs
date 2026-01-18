using System.Collections.Concurrent;
using HotMic.Core.Dsp.Generators;
using HotMic.Core.Threading;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed partial class SignalGeneratorPlugin
{
    public const int SlotCount = 3;
    private const int ParamsPerSlot = 20;
    private const int MasterParamOffset = 60;

    // Slot parameters (offset by slot * ParamsPerSlot)
    public const int TypeIndex = 0;
    public const int FrequencyIndex = 1;
    public const int GainIndex = 2;
    public const int MuteIndex = 3;
    public const int SoloIndex = 4;
    public const int SweepEnabledIndex = 5;
    public const int SweepStartHzIndex = 6;
    public const int SweepEndHzIndex = 7;
    public const int SweepDurationMsIndex = 8;
    public const int SweepDirectionIndex = 9;
    public const int SweepCurveIndex = 10;
    public const int PulseWidthIndex = 11;
    public const int ImpulseIntervalMsIndex = 12;
    public const int ChirpDurationMsIndex = 13;
    public const int SampleLoopModeIndex = 14;
    public const int SampleSpeedIndex = 15;
    public const int SampleTrimStartIndex = 16;
    public const int SampleTrimEndIndex = 17;
    // 18-19 reserved

    // Master parameters (offset 60)
    public const int MasterGainIndex = MasterParamOffset + 0;
    public const int MasterMuteIndex = MasterParamOffset + 1;
    public const int HeadroomModeIndex = MasterParamOffset + 2;
    public const int OutputPresetIndex = MasterParamOffset + 3;
    public const int MixModeIndex = MasterParamOffset + 4;

    public SignalGeneratorPlugin()
    {
        _slots = new SignalGeneratorSlot[SlotCount];

        for (int i = 0; i < SlotCount; i++)
        {
            _slots[i] = new SignalGeneratorSlot();
            _slots[i].InitializeDefaults();
        }

        _loadQueue = new ConcurrentQueue<SampleLoadRequest>();
        _recordBuffer = new LockFreeRingBuffer(SampleBuffer.MaxSamples);

        _masterGainDb = 0f;
        _masterGainLinear = 1f;
        _masterMuted = false;
        _headroomMode = HeadroomMode.AutoCompensate;
        _outputPreset = OutputPreset.Custom;
        _mixWithInput = false;

        Parameters = BuildParameters();
    }

    private static List<PluginParameter> BuildParameters()
    {
        var parameters = new List<PluginParameter>();

        for (int slot = 0; slot < SlotCount; slot++)
        {
            int baseIndex = slot * ParamsPerSlot;
            string prefix = $"Slot {slot + 1} ";

            parameters.Add(new PluginParameter
            {
                Index = baseIndex + TypeIndex,
                Name = prefix + "Type",
                MinValue = 0,
                MaxValue = 10,
                DefaultValue = 0,
                Unit = "",
                FormatValue = v => ((GeneratorType)(int)v).ToString()
            });
            parameters.Add(new PluginParameter
            {
                Index = baseIndex + FrequencyIndex,
                Name = prefix + "Frequency",
                MinValue = 20,
                MaxValue = 20000,
                DefaultValue = 440,
                Unit = "Hz"
            });
            parameters.Add(new PluginParameter
            {
                Index = baseIndex + GainIndex,
                Name = prefix + "Gain",
                MinValue = -60,
                MaxValue = 12,
                DefaultValue = -12,
                Unit = "dB"
            });
            parameters.Add(new PluginParameter
            {
                Index = baseIndex + MuteIndex,
                Name = prefix + "Mute",
                MinValue = 0,
                MaxValue = 1,
                DefaultValue = 0,
                Unit = ""
            });
            parameters.Add(new PluginParameter
            {
                Index = baseIndex + SoloIndex,
                Name = prefix + "Solo",
                MinValue = 0,
                MaxValue = 1,
                DefaultValue = 0,
                Unit = ""
            });
            parameters.Add(new PluginParameter
            {
                Index = baseIndex + SweepEnabledIndex,
                Name = prefix + "Sweep",
                MinValue = 0,
                MaxValue = 1,
                DefaultValue = 0,
                Unit = ""
            });
            parameters.Add(new PluginParameter
            {
                Index = baseIndex + SweepStartHzIndex,
                Name = prefix + "Sweep Start",
                MinValue = 20,
                MaxValue = 20000,
                DefaultValue = 80,
                Unit = "Hz"
            });
            parameters.Add(new PluginParameter
            {
                Index = baseIndex + SweepEndHzIndex,
                Name = prefix + "Sweep End",
                MinValue = 20,
                MaxValue = 20000,
                DefaultValue = 8000,
                Unit = "Hz"
            });
            parameters.Add(new PluginParameter
            {
                Index = baseIndex + SweepDurationMsIndex,
                Name = prefix + "Sweep Duration",
                MinValue = 100,
                MaxValue = 30000,
                DefaultValue = 5000,
                Unit = "ms"
            });
            parameters.Add(new PluginParameter
            {
                Index = baseIndex + SweepDirectionIndex,
                Name = prefix + "Sweep Dir",
                MinValue = 0,
                MaxValue = 2,
                DefaultValue = 0,
                Unit = "",
                FormatValue = v => ((SweepDirection)(int)v).ToString()
            });
            parameters.Add(new PluginParameter
            {
                Index = baseIndex + SweepCurveIndex,
                Name = prefix + "Sweep Curve",
                MinValue = 0,
                MaxValue = 1,
                DefaultValue = 1,
                Unit = "",
                FormatValue = v => ((SweepCurve)(int)v).ToString()
            });
            parameters.Add(new PluginParameter
            {
                Index = baseIndex + PulseWidthIndex,
                Name = prefix + "Pulse Width",
                MinValue = 0.1f,
                MaxValue = 0.9f,
                DefaultValue = 0.5f,
                Unit = ""
            });
            parameters.Add(new PluginParameter
            {
                Index = baseIndex + ImpulseIntervalMsIndex,
                Name = prefix + "Impulse Interval",
                MinValue = 10,
                MaxValue = 5000,
                DefaultValue = 100,
                Unit = "ms"
            });
            parameters.Add(new PluginParameter
            {
                Index = baseIndex + ChirpDurationMsIndex,
                Name = prefix + "Chirp Duration",
                MinValue = 50,
                MaxValue = 500,
                DefaultValue = 200,
                Unit = "ms"
            });
            parameters.Add(new PluginParameter
            {
                Index = baseIndex + SampleLoopModeIndex,
                Name = prefix + "Loop Mode",
                MinValue = 0,
                MaxValue = 2,
                DefaultValue = 0,
                Unit = "",
                FormatValue = v => ((SampleLoopMode)(int)v).ToString()
            });
            parameters.Add(new PluginParameter
            {
                Index = baseIndex + SampleSpeedIndex,
                Name = prefix + "Speed",
                MinValue = 0.5f,
                MaxValue = 2f,
                DefaultValue = 1f,
                Unit = "x"
            });
            parameters.Add(new PluginParameter
            {
                Index = baseIndex + SampleTrimStartIndex,
                Name = prefix + "Trim Start",
                MinValue = 0,
                MaxValue = 0.99f,
                DefaultValue = 0,
                Unit = ""
            });
            parameters.Add(new PluginParameter
            {
                Index = baseIndex + SampleTrimEndIndex,
                Name = prefix + "Trim End",
                MinValue = 0.01f,
                MaxValue = 1,
                DefaultValue = 1,
                Unit = ""
            });
        }

        // Master parameters
        parameters.Add(new PluginParameter
        {
            Index = MasterGainIndex,
            Name = "Master Gain",
            MinValue = -60,
            MaxValue = 12,
            DefaultValue = 0,
            Unit = "dB"
        });
        parameters.Add(new PluginParameter
        {
            Index = MasterMuteIndex,
            Name = "Master Mute",
            MinValue = 0,
            MaxValue = 1,
            DefaultValue = 0,
            Unit = ""
        });
        parameters.Add(new PluginParameter
        {
            Index = HeadroomModeIndex,
            Name = "Headroom",
            MinValue = 0,
            MaxValue = 2,
            DefaultValue = 1,
            Unit = "",
            FormatValue = v => ((HeadroomMode)(int)v).ToString()
        });
        parameters.Add(new PluginParameter
        {
            Index = OutputPresetIndex,
            Name = "Output Preset",
            MinValue = 0,
            MaxValue = 3,
            DefaultValue = 0,
            Unit = "",
            FormatValue = v => ((OutputPreset)(int)v).ToString()
        });
        parameters.Add(new PluginParameter
        {
            Index = MixModeIndex,
            Name = "Mix Mode",
            MinValue = 0,
            MaxValue = 1,
            DefaultValue = 0,
            Unit = "",
            FormatValue = v => v >= 0.5f ? "Add" : "Replace"
        });

        return parameters;
    }
}
