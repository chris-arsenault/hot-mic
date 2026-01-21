using HotMic.Core.Dsp;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed partial class SignalGeneratorPlugin
{
    public byte[] GetState()
    {
        // Calculate state size
        // Per slot: 18 floats = 72 bytes
        // Master: 5 floats = 20 bytes
        int slotBytes = SlotCount * 18 * sizeof(float);
        int masterBytes = 5 * sizeof(float);
        var bytes = new byte[slotBytes + masterBytes];

        int offset = 0;
        for (int i = 0; i < SlotCount; i++)
        {
            var slot = _slots[i];
            WriteFloat(bytes, ref offset, (float)slot.Type);
            WriteFloat(bytes, ref offset, slot.Frequency);
            WriteFloat(bytes, ref offset, slot.GainDb);
            WriteFloat(bytes, ref offset, slot.Muted ? 1f : 0f);
            WriteFloat(bytes, ref offset, slot.Solo ? 1f : 0f);
            WriteFloat(bytes, ref offset, slot.SweepEnabled ? 1f : 0f);
            WriteFloat(bytes, ref offset, slot.SweepStartHz);
            WriteFloat(bytes, ref offset, slot.SweepEndHz);
            WriteFloat(bytes, ref offset, slot.SweepDurationMs);
            WriteFloat(bytes, ref offset, (float)slot.SweepDirection);
            WriteFloat(bytes, ref offset, (float)slot.SweepCurve);
            WriteFloat(bytes, ref offset, slot.PulseWidth);
            WriteFloat(bytes, ref offset, slot.ImpulseIntervalMs);
            WriteFloat(bytes, ref offset, slot.ChirpDurationMs);
            WriteFloat(bytes, ref offset, (float)slot.SampleLoopMode);
            WriteFloat(bytes, ref offset, slot.SampleSpeed);
            WriteFloat(bytes, ref offset, slot.SampleTrimStart);
            WriteFloat(bytes, ref offset, slot.SampleTrimEnd);
        }

        WriteFloat(bytes, ref offset, _masterGainDb);
        WriteFloat(bytes, ref offset, _masterMuted ? 1f : 0f);
        WriteFloat(bytes, ref offset, (float)_headroomMode);
        WriteFloat(bytes, ref offset, (float)_outputPreset);
        WriteFloat(bytes, ref offset, _mixWithInput ? 1f : 0f);

        return bytes;
    }

    public void SetState(byte[] state)
    {
        int slotFloats = 18;
        int slotBytes = SlotCount * slotFloats * sizeof(float);

        if (state.Length < slotBytes) return;

        int offset = 0;
        for (int i = 0; i < SlotCount; i++)
        {
            var slot = _slots[i];
            slot.Type = (GeneratorType)(int)ReadFloat(state, ref offset);
            slot.Frequency = ReadFloat(state, ref offset);
            slot.GainDb = ReadFloat(state, ref offset);
            slot.GainLinear = DspUtils.DbToLinear(slot.GainDb);
            slot.Muted = ReadFloat(state, ref offset) >= 0.5f;
            slot.Solo = ReadFloat(state, ref offset) >= 0.5f;
            slot.SweepEnabled = ReadFloat(state, ref offset) >= 0.5f;
            slot.SweepStartHz = ReadFloat(state, ref offset);
            slot.SweepEndHz = ReadFloat(state, ref offset);
            slot.SweepDurationMs = ReadFloat(state, ref offset);
            slot.SweepDirection = (SweepDirection)(int)ReadFloat(state, ref offset);
            slot.SweepCurve = (SweepCurve)(int)ReadFloat(state, ref offset);
            slot.PulseWidth = ReadFloat(state, ref offset);
            slot.ImpulseIntervalMs = ReadFloat(state, ref offset);
            slot.ChirpDurationMs = ReadFloat(state, ref offset);
            slot.SampleLoopMode = (SampleLoopMode)(int)ReadFloat(state, ref offset);
            slot.SampleSpeed = ReadFloat(state, ref offset);
            slot.SampleTrimStart = ReadFloat(state, ref offset);
            slot.SampleTrimEnd = ReadFloat(state, ref offset);

            if (_sampleRate > 0)
            {
                slot.SetGainSmootherTarget(slot.GainLinear);
                slot.ApplyParameters();
            }
        }

        if (state.Length >= slotBytes + 5 * sizeof(float))
        {
            _masterGainDb = ReadFloat(state, ref offset);
            _masterGainLinear = DspUtils.DbToLinear(_masterGainDb);
            _masterMuted = ReadFloat(state, ref offset) >= 0.5f;
            _headroomMode = (HeadroomMode)(int)ReadFloat(state, ref offset);
            _outputPreset = (OutputPreset)(int)ReadFloat(state, ref offset);
            _mixWithInput = ReadFloat(state, ref offset) >= 0.5f;

            if (_sampleRate > 0)
            {
                _masterGainSmoother.SetTarget(_masterGainLinear);
            }
        }
    }

    private static void WriteFloat(byte[] bytes, ref int offset, float value)
    {
        Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, offset, 4);
        offset += 4;
    }

    private static float ReadFloat(byte[] bytes, ref int offset)
    {
        float value = BitConverter.ToSingle(bytes, offset);
        offset += 4;
        return value;
    }
}
