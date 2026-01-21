using System.Threading;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed partial class SignalGeneratorPlugin
{
    private int _outputLevelBits;
    private int _slot0LevelBits;
    private int _slot1LevelBits;
    private int _slot2LevelBits;

    public float GetOutputLevel()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.CompareExchange(ref _outputLevelBits, 0, 0));
    }

    public float GetSlotLevel(int slot)
    {
        return slot switch
        {
            0 => BitConverter.Int32BitsToSingle(Interlocked.CompareExchange(ref _slot0LevelBits, 0, 0)),
            1 => BitConverter.Int32BitsToSingle(Interlocked.CompareExchange(ref _slot1LevelBits, 0, 0)),
            2 => BitConverter.Int32BitsToSingle(Interlocked.CompareExchange(ref _slot2LevelBits, 0, 0)),
            _ => 0f
        };
    }

    // Exposed for channel strip UI
    public float Slot0GainDb => _slots[0].GainDb;
    public float Slot1GainDb => _slots[1].GainDb;
    public float Slot2GainDb => _slots[2].GainDb;
    public float MasterGainDb => _masterGainDb;

    /// <summary>
    /// Full slot state for UI rendering.
    /// </summary>
    public readonly struct SlotUIState
    {
        public GeneratorType Type { get; }
        public float Frequency { get; }
        public float GainDb { get; }
        public bool Muted { get; }
        public bool Solo { get; }
        public bool SweepEnabled { get; }
        public float SweepStartHz { get; }
        public float SweepEndHz { get; }
        public float SweepDurationMs { get; }
        public SweepDirection SweepDirection { get; }
        public SweepCurve SweepCurve { get; }
        public float PulseWidth { get; }
        public float ImpulseIntervalMs { get; }
        public float ChirpDurationMs { get; }
        public SampleLoopMode LoopMode { get; }
        public float SampleSpeed { get; }
        public float TrimStart { get; }
        public float TrimEnd { get; }

        public SlotUIState(
            GeneratorType type, float frequency, float gainDb, bool muted, bool solo,
            bool sweepEnabled, float sweepStartHz, float sweepEndHz, float sweepDurationMs,
            SweepDirection sweepDirection, SweepCurve sweepCurve, float pulseWidth,
            float impulseIntervalMs, float chirpDurationMs, SampleLoopMode loopMode,
            float sampleSpeed, float trimStart, float trimEnd)
        {
            Type = type;
            Frequency = frequency;
            GainDb = gainDb;
            Muted = muted;
            Solo = solo;
            SweepEnabled = sweepEnabled;
            SweepStartHz = sweepStartHz;
            SweepEndHz = sweepEndHz;
            SweepDurationMs = sweepDurationMs;
            SweepDirection = sweepDirection;
            SweepCurve = sweepCurve;
            PulseWidth = pulseWidth;
            ImpulseIntervalMs = impulseIntervalMs;
            ChirpDurationMs = chirpDurationMs;
            LoopMode = loopMode;
            SampleSpeed = sampleSpeed;
            TrimStart = trimStart;
            TrimEnd = trimEnd;
        }
    }

    /// <summary>
    /// Gets the current state of a slot for UI rendering.
    /// </summary>
    public SlotUIState GetSlotState(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= SlotCount)
            return new SlotUIState(GeneratorType.Sine, 440f, -12f, false, false,
                false, 80f, 8000f, 5000f, SweepDirection.Up, SweepCurve.Logarithmic, 0.5f,
                100f, 200f, SampleLoopMode.Loop, 1f, 0f, 1f);

        var slot = _slots[slotIndex];
        return new SlotUIState(
            slot.Type, slot.Frequency, slot.GainDb, slot.Muted, slot.Solo,
            slot.SweepEnabled, slot.SweepStartHz, slot.SweepEndHz, slot.SweepDurationMs,
            slot.SweepDirection, slot.SweepCurve, slot.PulseWidth,
            slot.ImpulseIntervalMs, slot.ChirpDurationMs, slot.SampleLoopMode,
            slot.SampleSpeed, slot.SampleTrimStart, slot.SampleTrimEnd);
    }

    /// <summary>
    /// Gets master section state for UI rendering.
    /// </summary>
    public (float GainDb, bool Muted, HeadroomMode Headroom) GetMasterState()
    {
        return (_masterGainDb, _masterMuted, _headroomMode);
    }

    public int ConsumeSampleStartCount(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= SlotCount) return 0;
        return _slots[slotIndex].ConsumeSampleStartCount();
    }

    public int ConsumeSampleLoopCount(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= SlotCount) return 0;
        return _slots[slotIndex].ConsumeSampleLoopCount();
    }

    public int ConsumeSampleStopCount(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= SlotCount) return 0;
        return _slots[slotIndex].ConsumeSampleStopCount();
    }
}
