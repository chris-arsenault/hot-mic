using System;
using System.Threading;
using HotMic.Core.Dsp;
using HotMic.Core.Dsp.Filters;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed partial class SignalGeneratorPlugin : IPlugin
{
    private readonly SignalGeneratorSlot[] _slots;

    // Master section
    private float _masterGainDb;
    private float _masterGainLinear;
    private bool _masterMuted;
    private HeadroomMode _headroomMode;
    private OutputPreset _outputPreset;
    private bool _mixWithInput;
    private LinearSmoother _masterGainSmoother;

    private int _sampleRate;
    private int _blockSize;

    public string Id => "builtin:signal-generator";
    public string Name => "Signal Generator";
    public bool IsBypassed { get; set; }
    public int LatencySamples => 0;
    public IReadOnlyList<PluginParameter> Parameters { get; }

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _blockSize = blockSize;

        _masterGainSmoother.Configure(sampleRate, 5f, _masterGainLinear);

        for (int i = 0; i < SlotCount; i++)
        {
            _slots[i].Initialize(sampleRate, (uint)(i + 1) * 12345);
        }
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        Process(buffer);
    }

    public void Process(Span<float> buffer)
    {
        // Process any pending sample loads
        ProcessLoadQueue();

        // Process capture request
        ProcessCaptureRequest();

        // Record input before overwriting
        if (_recordingEnabled)
        {
            _recordBuffer.Write(buffer);
        }

        if (IsBypassed)
        {
            return;
        }

        // Determine which slots are active (solo logic)
        bool anySolo = false;
        for (int i = 0; i < SlotCount; i++)
        {
            if (_slots[i].Solo)
            {
                anySolo = true;
                break;
            }
        }

        // Calculate headroom compensation
        int activeCount = 0;
        for (int i = 0; i < SlotCount; i++)
        {
            if (_slots[i].IsActive(anySolo))
            {
                activeCount++;
            }
        }

        float headroomCompensation = 1f;
        if (_headroomMode == HeadroomMode.AutoCompensate && activeCount > 1)
        {
            // -3dB per doubling of sources
            headroomCompensation = 1f / MathF.Sqrt(activeCount);
        }

        // Get master gain
        float masterGain = _masterMuted ? 0f : _masterGainSmoother.Current;
        bool masterSmoothing = _masterGainSmoother.IsSmoothing;

        float peak = 0f;
        Span<float> slotPeaks = stackalloc float[SlotCount];

        for (int i = 0; i < buffer.Length; i++)
        {
            if (masterSmoothing)
            {
                masterGain = _masterGainSmoother.Next();
                masterSmoothing = _masterGainSmoother.IsSmoothing;
            }

            float sum = 0f;

            for (int s = 0; s < SlotCount; s++)
            {
                var slot = _slots[s];
                if (!slot.IsActive(anySolo)) continue;

                float slotSample = slot.NextSample();

                // Apply slot gain with smoothing
                float slotGain = slot.NextGain();
                slotSample *= slotGain;

                float absSample = MathF.Abs(slotSample);
                if (absSample > slotPeaks[s]) slotPeaks[s] = absSample;

                sum += slotSample;
            }

            // Apply headroom compensation and master gain
            float output = sum * headroomCompensation * masterGain;

            // Normalize mode: scale to prevent clipping
            if (_headroomMode == HeadroomMode.Normalize)
            {
                output = Math.Clamp(output, -1f, 1f);
            }

            // Mix mode: add to input or replace
            if (_mixWithInput)
            {
                buffer[i] += output;
            }
            else
            {
                buffer[i] = output;
            }

            float absOut = MathF.Abs(buffer[i]);
            if (absOut > peak) peak = absOut;
        }

        // Store metering values
        Interlocked.Exchange(ref _outputLevelBits, BitConverter.SingleToInt32Bits(peak));
        Interlocked.Exchange(ref _slot0LevelBits, BitConverter.SingleToInt32Bits(slotPeaks[0]));
        Interlocked.Exchange(ref _slot1LevelBits, BitConverter.SingleToInt32Bits(slotPeaks[1]));
        Interlocked.Exchange(ref _slot2LevelBits, BitConverter.SingleToInt32Bits(slotPeaks[2]));
    }

    public void SetParameter(int index, float value)
    {
        // Determine if this is a slot parameter or master parameter
        if (index >= MasterParamOffset)
        {
            SetMasterParameter(index, value);
        }
        else
        {
            int slot = index / ParamsPerSlot;
            int paramIndex = index % ParamsPerSlot;
            if (slot < SlotCount)
            {
                _slots[slot].SetParameter(paramIndex, value);
            }
        }
    }

    private void SetMasterParameter(int index, float value)
    {
        switch (index)
        {
            case MasterGainIndex:
                _masterGainDb = Math.Clamp(value, -60f, 12f);
                _masterGainLinear = DspUtils.DbToLinear(_masterGainDb);
                _masterGainSmoother.SetTarget(_masterGainLinear);
                break;
            case MasterMuteIndex:
                _masterMuted = value >= 0.5f;
                break;
            case HeadroomModeIndex:
                _headroomMode = (HeadroomMode)(int)value;
                break;
            case OutputPresetIndex:
                _outputPreset = (OutputPreset)(int)value;
                ApplyOutputPreset();
                break;
            case MixModeIndex:
                _mixWithInput = value >= 0.5f;
                break;
        }
    }

    private void ApplyOutputPreset()
    {
        float targetDb = _outputPreset switch
        {
            OutputPreset.VocalConversation => -18f,
            OutputPreset.VocalPerformance => -12f,
            OutputPreset.Unity => 0f,
            _ => _masterGainDb
        };

        if (_outputPreset != OutputPreset.Custom)
        {
            _masterGainDb = targetDb;
            _masterGainLinear = DspUtils.DbToLinear(targetDb);
            _masterGainSmoother.SetTarget(_masterGainLinear);
        }
    }

    public void Dispose()
    {
        // No unmanaged resources
    }
}
