using System.Collections.Concurrent;
using System.Threading;
using HotMic.Core.Dsp;
using HotMic.Core.Dsp.Filters;
using HotMic.Core.Dsp.Generators;
using HotMic.Core.Threading;

namespace HotMic.Core.Plugins.BuiltIn;

/// <summary>
/// Generator types available for each slot.
/// </summary>
public enum GeneratorType
{
    Sine = 0,
    Square = 1,
    Saw = 2,
    Triangle = 3,
    WhiteNoise = 4,
    PinkNoise = 5,
    BrownNoise = 6,
    BlueNoise = 7,
    Impulse = 8,
    Chirp = 9,
    Sample = 10,
    /// <summary>
    /// Diagnostic: outputs constant 0.5 (DC). If vertical bars appear with DC,
    /// the issue is in capture/processing, not generation.
    /// </summary>
    DcTest = 11
}

/// <summary>
/// Sweep direction for oscillators.
/// </summary>
public enum SweepDirection
{
    Up = 0,
    Down = 1,
    PingPong = 2
}

/// <summary>
/// Sweep curve type.
/// </summary>
public enum SweepCurve
{
    Linear = 0,
    Logarithmic = 1
}

/// <summary>
/// Sample loop mode.
/// </summary>
public enum SampleLoopMode
{
    Loop = 0,
    OneShot = 1,
    PingPong = 2
}

/// <summary>
/// Headroom compensation mode for summing.
/// </summary>
public enum HeadroomMode
{
    None = 0,
    AutoCompensate = 1,
    Normalize = 2
}

/// <summary>
/// Output level presets.
/// </summary>
public enum OutputPreset
{
    Custom = 0,
    VocalConversation = 1, // -18 dBFS
    VocalPerformance = 2,  // -12 dBFS
    Unity = 3              // 0 dBFS
}

/// <summary>
/// Test signal generator plugin with 3 independent generator slots.
/// Designed for testing vocal processing chains with oscillators, noise, impulse, chirp, and sample playback.
/// </summary>
public sealed class SignalGeneratorPlugin : IPlugin
{
    public const int SlotCount = 3;
    private const int ParamsPerSlot = 20;
    private const int MasterParamOffset = 60;

    #region Parameter Indices - Per Slot (multiply by ParamsPerSlot and add slot index * ParamsPerSlot)

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

    #endregion

    #region Slot State

    private struct SlotState
    {
        public GeneratorType Type;
        public float Frequency;
        public float GainDb;
        public float GainLinear;
        public bool Muted;
        public bool Solo;

        // Sweep
        public bool SweepEnabled;
        public float SweepStartHz;
        public float SweepEndHz;
        public float SweepDurationMs;
        public SweepDirection SweepDirection;
        public SweepCurve SweepCurve;

        // Square wave
        public float PulseWidth;

        // Impulse/Chirp
        public float ImpulseIntervalMs;
        public float ChirpDurationMs;

        // Sample
        public SampleLoopMode SampleLoopMode;
        public float SampleSpeed;
        public float SampleTrimStart;
        public float SampleTrimEnd;

        // DSP instances
        public OscillatorCore Oscillator;
        public NoiseGenerator Noise;
        public ImpulseGenerator Impulse;
        public ChirpGenerator Chirp;
        public SamplePlayer SamplePlayer;

        // Smoothing
        public LinearSmoother GainSmoother;
    }

    #endregion

    private SlotState[] _slots;
    private SampleBuffer[] _sampleBuffers;

    // Master section
    private float _masterGainDb;
    private float _masterGainLinear;
    private bool _masterMuted;
    private HeadroomMode _headroomMode;
    private OutputPreset _outputPreset;
    private bool _mixWithInput;
    private LinearSmoother _masterGainSmoother;

    // Recording
    private LockFreeRingBuffer _recordBuffer;
    private volatile bool _recordingEnabled;
    private volatile int _targetSlotForCapture;
    private volatile bool _captureRequested;

    // Sample loading
    private readonly ConcurrentQueue<SampleLoadRequest> _loadQueue;
    private readonly object _sampleSwapLock = new();

    // Metering
    private int _outputLevelBits;
    private int _slot0LevelBits;
    private int _slot1LevelBits;
    private int _slot2LevelBits;

    private int _sampleRate;
    private int _blockSize;

    public SignalGeneratorPlugin()
    {
        _slots = new SlotState[SlotCount];
        _sampleBuffers = new SampleBuffer[SlotCount];
        _loadQueue = new ConcurrentQueue<SampleLoadRequest>();
        _recordBuffer = new LockFreeRingBuffer(SampleBuffer.MaxSamples);

        for (int i = 0; i < SlotCount; i++)
        {
            _sampleBuffers[i] = new SampleBuffer();
            InitializeSlotDefaults(ref _slots[i]);
        }

        _masterGainDb = 0f;
        _masterGainLinear = 1f;
        _masterMuted = false;
        _headroomMode = HeadroomMode.AutoCompensate;
        _outputPreset = OutputPreset.Custom;
        _mixWithInput = false;

        Parameters = BuildParameters();
    }

    private void InitializeSlotDefaults(ref SlotState slot)
    {
        slot.Type = GeneratorType.Sine;
        slot.Frequency = 440f;
        slot.GainDb = -12f;
        slot.GainLinear = DspUtils.DbToLinear(-12f);
        slot.Muted = false;
        slot.Solo = false;
        slot.SweepEnabled = false;
        slot.SweepStartHz = 80f;
        slot.SweepEndHz = 8000f;
        slot.SweepDurationMs = 5000f;
        slot.SweepDirection = SweepDirection.Up;
        slot.SweepCurve = SweepCurve.Logarithmic;
        slot.PulseWidth = 0.5f;
        slot.ImpulseIntervalMs = 100f;
        slot.ChirpDurationMs = 200f;
        slot.SampleLoopMode = SampleLoopMode.Loop;
        slot.SampleSpeed = 1f;
        slot.SampleTrimStart = 0f;
        slot.SampleTrimEnd = 1f;
    }

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
            ref var slot = ref _slots[i];
            slot.Oscillator.Initialize(sampleRate);
            slot.Noise.Initialize((uint)(i + 1) * 12345);
            slot.Impulse.Initialize(sampleRate);
            slot.Chirp.Initialize(sampleRate);
            slot.SamplePlayer.Initialize(sampleRate);
            slot.GainSmoother.Configure(sampleRate, 5f, slot.GainLinear);

            ApplySlotParameters(i);
        }
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
            bool active = !_slots[i].Muted && (!anySolo || _slots[i].Solo);
            if (active) activeCount++;
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
        Span<float> slotPeaks = stackalloc float[3];

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
                ref var slot = ref _slots[s];

                bool active = !slot.Muted && (!anySolo || slot.Solo);
                if (!active) continue;

                float slotSample = GenerateSlotSample(ref slot, s);

                // Apply slot gain with smoothing
                float slotGain = slot.GainSmoother.IsSmoothing
                    ? slot.GainSmoother.Next()
                    : slot.GainSmoother.Current;

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

    private float GenerateSlotSample(ref SlotState slot, int slotIndex)
    {
        return slot.Type switch
        {
            GeneratorType.Sine => slot.Oscillator.NextSine(),
            GeneratorType.Square => slot.Oscillator.NextSquare(),
            GeneratorType.Saw => slot.Oscillator.NextSaw(),
            GeneratorType.Triangle => slot.Oscillator.NextTriangle(),
            GeneratorType.WhiteNoise => slot.Noise.NextWhite(),
            GeneratorType.PinkNoise => slot.Noise.NextPink(),
            GeneratorType.BrownNoise => slot.Noise.NextBrown(),
            GeneratorType.BlueNoise => slot.Noise.NextBlue(),
            GeneratorType.Impulse => slot.Impulse.Next(),
            GeneratorType.Chirp => slot.Chirp.Next(),
            GeneratorType.Sample => slot.SamplePlayer.Next(_sampleBuffers[slotIndex]),
            GeneratorType.DcTest => 0.5f, // Constant DC for diagnostics
            _ => 0f
        };
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
                SetSlotParameter(slot, paramIndex, value);
            }
        }
    }

    private void SetSlotParameter(int slot, int paramIndex, float value)
    {
        ref var s = ref _slots[slot];

        switch (paramIndex)
        {
            case TypeIndex:
                s.Type = (GeneratorType)(int)value;
                break;
            case FrequencyIndex:
                s.Frequency = Math.Clamp(value, 20f, 20000f);
                s.Oscillator.SetFrequency(s.Frequency);
                break;
            case GainIndex:
                s.GainDb = Math.Clamp(value, -60f, 12f);
                s.GainLinear = DspUtils.DbToLinear(s.GainDb);
                s.GainSmoother.SetTarget(s.GainLinear);
                break;
            case MuteIndex:
                s.Muted = value >= 0.5f;
                break;
            case SoloIndex:
                s.Solo = value >= 0.5f;
                break;
            case SweepEnabledIndex:
                s.SweepEnabled = value >= 0.5f;
                ApplySlotSweep(slot);
                break;
            case SweepStartHzIndex:
                s.SweepStartHz = Math.Clamp(value, 20f, 20000f);
                ApplySlotSweep(slot);
                break;
            case SweepEndHzIndex:
                s.SweepEndHz = Math.Clamp(value, 20f, 20000f);
                ApplySlotSweep(slot);
                break;
            case SweepDurationMsIndex:
                s.SweepDurationMs = Math.Clamp(value, 100f, 30000f);
                ApplySlotSweep(slot);
                break;
            case SweepDirectionIndex:
                s.SweepDirection = (SweepDirection)(int)value;
                ApplySlotSweep(slot);
                break;
            case SweepCurveIndex:
                s.SweepCurve = (SweepCurve)(int)value;
                ApplySlotSweep(slot);
                break;
            case PulseWidthIndex:
                s.PulseWidth = Math.Clamp(value, 0.1f, 0.9f);
                s.Oscillator.SetPulseWidth(s.PulseWidth);
                break;
            case ImpulseIntervalMsIndex:
                s.ImpulseIntervalMs = Math.Clamp(value, 10f, 5000f);
                s.Impulse.SetInterval(s.ImpulseIntervalMs);
                break;
            case ChirpDurationMsIndex:
                s.ChirpDurationMs = Math.Clamp(value, 50f, 500f);
                s.Chirp.SetDuration(s.ChirpDurationMs);
                break;
            case SampleLoopModeIndex:
                s.SampleLoopMode = (SampleLoopMode)(int)value;
                s.SamplePlayer.SetLoopMode((int)s.SampleLoopMode);
                break;
            case SampleSpeedIndex:
                s.SampleSpeed = Math.Clamp(value, 0.5f, 2f);
                s.SamplePlayer.SetSpeed(s.SampleSpeed);
                break;
            case SampleTrimStartIndex:
                s.SampleTrimStart = Math.Clamp(value, 0f, 0.99f);
                s.SamplePlayer.SetTrimStart(s.SampleTrimStart);
                break;
            case SampleTrimEndIndex:
                s.SampleTrimEnd = Math.Clamp(value, 0.01f, 1f);
                s.SamplePlayer.SetTrimEnd(s.SampleTrimEnd);
                break;
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

    private void ApplySlotSweep(int slot)
    {
        ref var s = ref _slots[slot];
        s.Oscillator.ConfigureSweep(
            s.SweepEnabled,
            s.SweepStartHz,
            s.SweepEndHz,
            s.SweepDurationMs,
            (int)s.SweepDirection,
            (int)s.SweepCurve);
    }

    private void ApplySlotParameters(int slot)
    {
        ref var s = ref _slots[slot];
        s.Oscillator.SetFrequency(s.Frequency);
        s.Oscillator.SetPulseWidth(s.PulseWidth);
        ApplySlotSweep(slot);
        s.Impulse.SetInterval(s.ImpulseIntervalMs);
        s.Chirp.SetDuration(s.ChirpDurationMs);
        s.SamplePlayer.SetSpeed(s.SampleSpeed);
        s.SamplePlayer.SetLoopMode((int)s.SampleLoopMode);
        s.SamplePlayer.SetTrimStart(s.SampleTrimStart);
        s.SamplePlayer.SetTrimEnd(s.SampleTrimEnd);
    }

    #region Sample Loading

    private record SampleLoadRequest(int SlotIndex, float[] Samples, int SampleRate);

    /// <summary>
    /// Queue a sample file for loading into a slot (call from UI thread).
    /// </summary>
    public void LoadSampleAsync(int slotIndex, float[] samples, int sampleRate)
    {
        if (slotIndex < 0 || slotIndex >= SlotCount) return;
        _loadQueue.Enqueue(new SampleLoadRequest(slotIndex, samples, sampleRate));
    }

    private void ProcessLoadQueue()
    {
        while (_loadQueue.TryDequeue(out var request))
        {
            if (request.SlotIndex >= 0 && request.SlotIndex < SlotCount)
            {
                _sampleBuffers[request.SlotIndex].Load(request.Samples, request.SampleRate);
                _slots[request.SlotIndex].SamplePlayer.Reset();
            }
        }
    }

    #endregion

    #region Recording

    /// <summary>
    /// Enable/disable recording of input signal for later capture.
    /// </summary>
    public void SetRecordingEnabled(bool enabled)
    {
        _recordingEnabled = enabled;
        if (enabled)
        {
            _recordBuffer.Clear();
        }
    }

    /// <summary>
    /// Start recording input to a specific slot.
    /// Recording continues until StopRecordingToSlot is called.
    /// </summary>
    public void StartRecordingToSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= SlotCount)
            return;

        _targetSlotForCapture = slotIndex;
        _recordBuffer.Clear();
        _recordingEnabled = true;
    }

    /// <summary>
    /// Stop recording and capture the recorded audio to the target slot.
    /// </summary>
    public void StopRecordingToSlot()
    {
        if (!_recordingEnabled)
            return;

        _recordingEnabled = false;
        _captureRequested = true;
    }

    /// <summary>
    /// Check if currently recording.
    /// </summary>
    public bool IsRecording => _recordingEnabled;

    /// <summary>
    /// Get the slot index currently being recorded to, or -1 if not recording.
    /// </summary>
    public int RecordingTargetSlot => _recordingEnabled ? _targetSlotForCapture : -1;

    /// <summary>
    /// Request capture of recorded audio into the specified slot.
    /// </summary>
    public void CaptureToSlot(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < SlotCount)
        {
            _targetSlotForCapture = slotIndex;
            _captureRequested = true;
        }
    }

    private void ProcessCaptureRequest()
    {
        if (!_captureRequested) return;
        _captureRequested = false;

        int available = _recordBuffer.AvailableRead;
        if (available <= 0) return;

        int slotIndex = _targetSlotForCapture;
        if (slotIndex < 0 || slotIndex >= SlotCount) return;

        // Read from ring buffer into sample buffer
        float[] temp = new float[Math.Min(available, SampleBuffer.MaxSamples)];
        int read = _recordBuffer.Read(temp);

        if (read > 0)
        {
            _sampleBuffers[slotIndex].Load(temp.AsSpan(0, read), _sampleRate);
            _slots[slotIndex].SamplePlayer.Reset();
        }
    }

    #endregion

    #region Metering

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
        public readonly GeneratorType Type;
        public readonly float Frequency;
        public readonly float GainDb;
        public readonly bool Muted;
        public readonly bool Solo;
        public readonly bool SweepEnabled;
        public readonly float SweepStartHz;
        public readonly float SweepEndHz;
        public readonly float SweepDurationMs;
        public readonly SweepDirection SweepDirection;
        public readonly SweepCurve SweepCurve;
        public readonly float PulseWidth;
        public readonly float ImpulseIntervalMs;
        public readonly float ChirpDurationMs;
        public readonly SampleLoopMode LoopMode;
        public readonly float SampleSpeed;
        public readonly float TrimStart;
        public readonly float TrimEnd;

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

        ref var s = ref _slots[slotIndex];
        return new SlotUIState(
            s.Type, s.Frequency, s.GainDb, s.Muted, s.Solo,
            s.SweepEnabled, s.SweepStartHz, s.SweepEndHz, s.SweepDurationMs,
            s.SweepDirection, s.SweepCurve, s.PulseWidth,
            s.ImpulseIntervalMs, s.ChirpDurationMs, s.SampleLoopMode,
            s.SampleSpeed, s.SampleTrimStart, s.SampleTrimEnd);
    }

    /// <summary>
    /// Gets master section state for UI rendering.
    /// </summary>
    public (float GainDb, bool Muted, HeadroomMode Headroom) GetMasterState()
    {
        return (_masterGainDb, _masterMuted, _headroomMode);
    }

    #endregion

    #region State Serialization

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
            ref var s = ref _slots[i];
            WriteFloat(bytes, ref offset, (float)s.Type);
            WriteFloat(bytes, ref offset, s.Frequency);
            WriteFloat(bytes, ref offset, s.GainDb);
            WriteFloat(bytes, ref offset, s.Muted ? 1f : 0f);
            WriteFloat(bytes, ref offset, s.Solo ? 1f : 0f);
            WriteFloat(bytes, ref offset, s.SweepEnabled ? 1f : 0f);
            WriteFloat(bytes, ref offset, s.SweepStartHz);
            WriteFloat(bytes, ref offset, s.SweepEndHz);
            WriteFloat(bytes, ref offset, s.SweepDurationMs);
            WriteFloat(bytes, ref offset, (float)s.SweepDirection);
            WriteFloat(bytes, ref offset, (float)s.SweepCurve);
            WriteFloat(bytes, ref offset, s.PulseWidth);
            WriteFloat(bytes, ref offset, s.ImpulseIntervalMs);
            WriteFloat(bytes, ref offset, s.ChirpDurationMs);
            WriteFloat(bytes, ref offset, (float)s.SampleLoopMode);
            WriteFloat(bytes, ref offset, s.SampleSpeed);
            WriteFloat(bytes, ref offset, s.SampleTrimStart);
            WriteFloat(bytes, ref offset, s.SampleTrimEnd);
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
            ref var s = ref _slots[i];
            s.Type = (GeneratorType)(int)ReadFloat(state, ref offset);
            s.Frequency = ReadFloat(state, ref offset);
            s.GainDb = ReadFloat(state, ref offset);
            s.GainLinear = DspUtils.DbToLinear(s.GainDb);
            s.Muted = ReadFloat(state, ref offset) >= 0.5f;
            s.Solo = ReadFloat(state, ref offset) >= 0.5f;
            s.SweepEnabled = ReadFloat(state, ref offset) >= 0.5f;
            s.SweepStartHz = ReadFloat(state, ref offset);
            s.SweepEndHz = ReadFloat(state, ref offset);
            s.SweepDurationMs = ReadFloat(state, ref offset);
            s.SweepDirection = (SweepDirection)(int)ReadFloat(state, ref offset);
            s.SweepCurve = (SweepCurve)(int)ReadFloat(state, ref offset);
            s.PulseWidth = ReadFloat(state, ref offset);
            s.ImpulseIntervalMs = ReadFloat(state, ref offset);
            s.ChirpDurationMs = ReadFloat(state, ref offset);
            s.SampleLoopMode = (SampleLoopMode)(int)ReadFloat(state, ref offset);
            s.SampleSpeed = ReadFloat(state, ref offset);
            s.SampleTrimStart = ReadFloat(state, ref offset);
            s.SampleTrimEnd = ReadFloat(state, ref offset);

            if (_sampleRate > 0)
            {
                s.GainSmoother.SetTarget(s.GainLinear);
                ApplySlotParameters(i);
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

    #endregion

    public void Dispose()
    {
        // No unmanaged resources
    }

    #region Parameter Building

    private static IReadOnlyList<PluginParameter> BuildParameters()
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

    #endregion
}
