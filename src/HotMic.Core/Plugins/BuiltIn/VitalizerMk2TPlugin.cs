using System.Threading;
using HotMic.Core.Dsp;
using HotMic.Core.Dsp.Filters;

namespace HotMic.Core.Plugins.BuiltIn;

/// <summary>
/// SPL Tube Vitalizer Mk2-T inspired processor (mono approximation, no stereo expander).
/// </summary>
public sealed class VitalizerMk2TPlugin : IPlugin
{
    public const int DriveIndex = 0;
    public const int BassIndex = 1;
    public const int BassCompIndex = 2;
    public const int BassLcIndex = 3;
    public const int MidHiTuneIndex = 4;
    public const int ProcessIndex = 5;
    public const int HighFreqIndex = 6;
    public const int IntensityIndex = 7;
    public const int HighCompIndex = 8;
    public const int HighLcIndex = 9;
    public const int TubeIndex = 10;
    public const int OutputIndex = 11;
    public const int LimitIndex = 12;

    private const float DriveMinDb = -20f;
    private const float DriveMaxDb = 6f;
    private const float OutputMinDb = -20f;
    private const float OutputMaxDb = 6f;

    private const float BassMaxDb = 9f;
    private const float MidTiltMaxDb = 8f;
    private const float HighMaxDb = 9f;

    private const float ProcessGateThreshold = 0.25f; // ~9 o'clock
    private const float MidDampMax = 0.35f;
    private const float PhaseDepthMax = 0.85f;

    private const float BassCompThresholdDb = -24f;
    private const float HighCompThresholdDb = -20f;

    private const float BassCompAttackMs = 25f;
    private const float BassCompReleaseMs = 140f;
    private const float HighCompAttackMs = 6f;
    private const float HighCompReleaseMs = 80f;
    private const float MidEnvAttackMs = 12f;
    private const float MidEnvReleaseMs = 120f;

    private const float TubeSmoothingMs = 10f;
    private const float TubeLowpassHz = 18000f;
    private const float TubeDriveBase = 1.4f;

    private const float LimitThresholdLinear = 4f; // +12 dB

    private float _driveDb = 0f;
    private float _bass = 0f;
    private float _bassCompRatio = 1f;
    private int _bassLcEnabled;
    private float _midHiTuneHz = 6000f;
    private float _process = 0.35f;
    private float _highFreqHz = 9000f;
    private float _intensity = 0.3f;
    private float _highCompRatio = 1f;
    private int _highLcEnabled;
    private int _tubeEnabled;
    private float _outputDb = 0f;
    private int _limitEnabled;

    private int _sampleRate;
    private int _blockSize;
    private int _latencySamples;

    private LinearSmoother _driveSmoother;
    private LinearSmoother _processSmoother;
    private LinearSmoother _intensitySmoother;
    private LinearSmoother _outputSmoother;
    private LinearSmoother _tubeMakeupSmoother;

    private readonly BiquadFilter _bassShelf = new();
    private readonly BiquadFilter _bassLcPeak = new();
    private readonly BiquadFilter _bassEnvLowPass = new();
    private readonly EnvelopeFollower _bassEnv = new();

    private readonly BiquadFilter _midLowShelf = new();
    private readonly BiquadFilter _midHighShelf = new();
    private readonly BiquadFilter _midEnvBand = new();
    private readonly EnvelopeFollower _midEnv = new();
    private readonly AllPassFilter _midPhaseLowA = new();
    private readonly AllPassFilter _midPhaseHighA = new();
    private readonly AllPassFilter _midPhaseLowB = new();
    private readonly AllPassFilter _midPhaseHighB = new();

    private readonly BiquadFilter _highShelfA = new();
    private readonly BiquadFilter _highShelfB = new();
    private readonly BiquadFilter _highLcPeak = new();
    private readonly BiquadFilter _highEnvBand = new();
    private readonly EnvelopeFollower _highEnv = new();

    private readonly HalfbandResampler _tubeUpsampler = new();
    private readonly HalfbandResampler _tubeDownsampler = new();
    private readonly BiquadFilter _tubeLowpass = new();
    private float[] _tubeBuffer = Array.Empty<float>();

    // Tube stage metering (atomic for UI/debug overlay)
    private float _meterTubePreRms;
    private float _meterTubePostRms;
    private float _meterTubeMakeupTarget;
    private float _meterTubeMakeupCurrent;

    public VitalizerMk2TPlugin()
    {
        Parameters =
        [
            new PluginParameter { Index = DriveIndex, Name = "Drive", MinValue = DriveMinDb, MaxValue = DriveMaxDb, DefaultValue = 0f, Unit = "dB" },
            new PluginParameter { Index = BassIndex, Name = "Bass", MinValue = -1f, MaxValue = 1f, DefaultValue = 0f, Unit = string.Empty },
            new PluginParameter { Index = BassCompIndex, Name = "Bass Comp", MinValue = 1f, MaxValue = 10f, DefaultValue = 1f, Unit = ":1" },
            new PluginParameter { Index = BassLcIndex, Name = "Bass LC", MinValue = 0f, MaxValue = 1f, DefaultValue = 0f, Unit = string.Empty, FormatValue = FormatToggle },
            new PluginParameter { Index = MidHiTuneIndex, Name = "Mid-Hi Tune", MinValue = 1100f, MaxValue = 22000f, DefaultValue = 6000f, Unit = "Hz" },
            new PluginParameter { Index = ProcessIndex, Name = "Process", MinValue = 0f, MaxValue = 1f, DefaultValue = 0.35f, Unit = string.Empty },
            new PluginParameter { Index = HighFreqIndex, Name = "High Freq", MinValue = 2000f, MaxValue = 20000f, DefaultValue = 9000f, Unit = "Hz" },
            new PluginParameter { Index = IntensityIndex, Name = "Intensity", MinValue = 0f, MaxValue = 1f, DefaultValue = 0.3f, Unit = string.Empty },
            new PluginParameter { Index = HighCompIndex, Name = "High Comp", MinValue = 1f, MaxValue = 10f, DefaultValue = 1f, Unit = ":1" },
            new PluginParameter { Index = HighLcIndex, Name = "High LC", MinValue = 0f, MaxValue = 1f, DefaultValue = 0f, Unit = string.Empty, FormatValue = FormatToggle },
            new PluginParameter { Index = TubeIndex, Name = "Tube", MinValue = 0f, MaxValue = 1f, DefaultValue = 0f, Unit = string.Empty, FormatValue = FormatToggle },
            new PluginParameter { Index = OutputIndex, Name = "Output", MinValue = OutputMinDb, MaxValue = OutputMaxDb, DefaultValue = 0f, Unit = "dB" },
            new PluginParameter { Index = LimitIndex, Name = "Limit", MinValue = 0f, MaxValue = 1f, DefaultValue = 0f, Unit = string.Empty, FormatValue = FormatToggle }
        ];
    }

    public string Id => "builtin:vitalizer-mk2t";

    public string Name => "Vitalizer Mk2-T";

    public bool IsBypassed { get; set; }

    public int LatencySamples => Volatile.Read(ref _latencySamples);

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public float DriveDb => _driveDb;
    public float Bass => _bass;
    public float BassCompRatio => _bassCompRatio;
    public bool BassLcEnabled => _bassLcEnabled != 0;
    public float MidHiTuneHz => _midHiTuneHz;
    public float ProcessAmount => _process;
    public float HighFreqHz => _highFreqHz;
    public float IntensityAmount => _intensity;
    public float HighCompRatio => _highCompRatio;
    public bool HighLcEnabled => _highLcEnabled != 0;
    public bool TubeEnabled => _tubeEnabled != 0;
    public float OutputDb => _outputDb;
    public bool LimitEnabled => _limitEnabled != 0;
    public int SampleRate => _sampleRate;
    public float TubePreRms => Volatile.Read(ref _meterTubePreRms);
    public float TubePostRms => Volatile.Read(ref _meterTubePostRms);
    public float TubeMakeupTarget => Volatile.Read(ref _meterTubeMakeupTarget);
    public float TubeMakeupCurrent => Volatile.Read(ref _meterTubeMakeupCurrent);

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _blockSize = blockSize;
        _tubeBuffer = new float[blockSize * 2];

        _driveSmoother.Configure(sampleRate, TubeSmoothingMs, DspUtils.DbToLinear(_driveDb));
        _processSmoother.Configure(sampleRate, TubeSmoothingMs, _process);
        _intensitySmoother.Configure(sampleRate, TubeSmoothingMs, _intensity);
        _outputSmoother.Configure(sampleRate, TubeSmoothingMs, DspUtils.DbToLinear(_outputDb));
        _tubeMakeupSmoother.Configure(sampleRate, 20f, 1f);

        _bassEnv.Configure(BassCompAttackMs, BassCompReleaseMs, sampleRate);
        _highEnv.Configure(HighCompAttackMs, HighCompReleaseMs, sampleRate);
        _midEnv.Configure(MidEnvAttackMs, MidEnvReleaseMs, sampleRate);

        UpdateBassFilters();
        UpdateMidFilters();
        UpdateHighFilters();
        UpdateTubeFilters();
        UpdateLatency();
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        Process(buffer);
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return;
        }

        bool driveSmooth = _driveSmoother.IsSmoothing;
        bool processSmooth = _processSmoother.IsSmoothing;
        bool intensitySmooth = _intensitySmoother.IsSmoothing;
        bool outputSmooth = _outputSmoother.IsSmoothing;

        float driveGain = _driveSmoother.Current;
        float processValue = _processSmoother.Current;
        float intensityValue = _intensitySmoother.Current;
        float outputGain = _outputSmoother.Current;

        float processMix = MapProcessMix(processValue);
        float intensityMix = Math.Clamp(intensityValue, 0f, 1f);

        float bassCompScale = 1f - MapCompNullifier(_bassCompRatio);
        float highCompScale = 1f - MapCompNullifier(_highCompRatio);

        bool bassLc = _bassLcEnabled != 0;
        bool highLc = _highLcEnabled != 0;
        bool tubeEnabled = _tubeEnabled != 0;
        float preTubeEnergy = 0f;

        for (int i = 0; i < buffer.Length; i++)
        {
            if (driveSmooth)
            {
                driveGain = _driveSmoother.Next();
                driveSmooth = _driveSmoother.IsSmoothing;
            }

            if (processSmooth)
            {
                processValue = _processSmoother.Next();
                processMix = MapProcessMix(processValue);
                processSmooth = _processSmoother.IsSmoothing;
            }

            if (intensitySmooth)
            {
                intensityValue = _intensitySmoother.Next();
                intensityMix = Math.Clamp(intensityValue, 0f, 1f);
                intensitySmooth = _intensitySmoother.IsSmoothing;
            }

            float x = buffer[i] * driveGain;

            // Bass path
            float bassOut = _bassShelf.Process(x);
            if (bassLc)
            {
                bassOut = _bassLcPeak.Process(bassOut);
                bassOut = ApplyBassLcSaturation(bassOut);
            }

            float bassEnv = _bassEnv.Process(_bassEnvLowPass.Process(bassOut));
            float bassCompGain = ComputeCompGain(bassEnv, BassCompThresholdDb, _bassCompRatio);
            float bassDelta = (bassOut - x) * bassCompGain * bassCompScale;

            // Mid-Hi Tune path (broad tilt)
            float midOut = _midLowShelf.Process(x);
            midOut = _midHighShelf.Process(midOut);

            float midEnv = _midEnv.Process(_midEnvBand.Process(x));
            float phaseDepth = Math.Clamp(midEnv * processMix * PhaseDepthMax, 0f, 1f);

            // Amplitude-correlated phase shifting (manual describes phase-damping of dominant mids)
            float midPhaseA = Lerp(
                _midPhaseLowA.Process(midOut),
                _midPhaseHighA.Process(midOut),
                phaseDepth);
            float midPhaseB = Lerp(
                _midPhaseLowB.Process(midPhaseA),
                _midPhaseHighB.Process(midPhaseA),
                phaseDepth);

            float midDamp = 1f - MathF.Min(MidDampMax * midEnv * processMix, 0.6f);
            midOut = midPhaseB * midDamp;

            float midDelta = (midOut - x);

            float processed = x + processMix * (bassDelta + midDelta);

            // High frequency stage (LC-EQ)
            float highOut = _highShelfA.Process(processed);
            highOut = _highShelfB.Process(highOut);
            if (highLc)
            {
                highOut = _highLcPeak.Process(highOut);
            }

            float highEnv = _highEnv.Process(_highEnvBand.Process(processed));
            float highCompGain = ComputeCompGain(highEnv, HighCompThresholdDb, _highCompRatio);
            float highDelta = (highOut - processed) * highCompGain * highCompScale;

            float outSample = processed + intensityMix * highDelta;

            buffer[i] = outSample;

            if (tubeEnabled)
            {
                preTubeEnergy += outSample * outSample;
            }
        }

        if (tubeEnabled)
        {
            ProcessTubeStage(buffer);
            ApplyTubeMakeup(buffer, preTubeEnergy);
        }

        if (_limitEnabled != 0 && tubeEnabled)
        {
            ApplySoftLimit(buffer);
        }

        if (!outputSmooth)
        {
            if (MathF.Abs(outputGain - 1f) > 1e-6f)
            {
                for (int i = 0; i < buffer.Length; i++)
                {
                    buffer[i] *= outputGain;
                }
            }
        }
        else
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] *= _outputSmoother.Next();
            }
        }
    }

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case DriveIndex:
                _driveDb = Math.Clamp(value, DriveMinDb, DriveMaxDb);
                _driveSmoother.SetTarget(DspUtils.DbToLinear(_driveDb));
                break;
            case BassIndex:
                _bass = Math.Clamp(value, -1f, 1f);
                UpdateBassFilters();
                break;
            case BassCompIndex:
                _bassCompRatio = Math.Clamp(value, 1f, 10f);
                break;
            case BassLcIndex:
                _bassLcEnabled = value >= 0.5f ? 1 : 0;
                UpdateBassFilters();
                break;
            case MidHiTuneIndex:
                _midHiTuneHz = Math.Clamp(value, 1100f, 22000f);
                UpdateMidFilters();
                break;
            case ProcessIndex:
                _process = Math.Clamp(value, 0f, 1f);
                _processSmoother.SetTarget(_process);
                break;
            case HighFreqIndex:
                _highFreqHz = Math.Clamp(value, 2000f, 20000f);
                UpdateHighFilters();
                break;
            case IntensityIndex:
                _intensity = Math.Clamp(value, 0f, 1f);
                _intensitySmoother.SetTarget(_intensity);
                break;
            case HighCompIndex:
                _highCompRatio = Math.Clamp(value, 1f, 10f);
                break;
            case HighLcIndex:
                _highLcEnabled = value >= 0.5f ? 1 : 0;
                UpdateHighFilters();
                break;
            case TubeIndex:
                int enabled = value >= 0.5f ? 1 : 0;
                if (enabled != _tubeEnabled)
                {
                    _tubeEnabled = enabled;
                    if (_tubeEnabled != 0)
                    {
                        _tubeUpsampler.Reset();
                        _tubeDownsampler.Reset();
                        _tubeLowpass.Reset();
                        _tubeMakeupSmoother.SetTarget(1f);
                    }
                }
                UpdateLatency();
                break;
            case OutputIndex:
                _outputDb = Math.Clamp(value, OutputMinDb, OutputMaxDb);
                _outputSmoother.SetTarget(DspUtils.DbToLinear(_outputDb));
                break;
            case LimitIndex:
                _limitEnabled = value >= 0.5f ? 1 : 0;
                break;
        }
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 13];
        Buffer.BlockCopy(BitConverter.GetBytes(_driveDb), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_bass), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_bassCompRatio), 0, bytes, 8, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_bassLcEnabled != 0 ? 1f : 0f), 0, bytes, 12, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_midHiTuneHz), 0, bytes, 16, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_process), 0, bytes, 20, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_highFreqHz), 0, bytes, 24, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_intensity), 0, bytes, 28, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_highCompRatio), 0, bytes, 32, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_highLcEnabled != 0 ? 1f : 0f), 0, bytes, 36, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_tubeEnabled != 0 ? 1f : 0f), 0, bytes, 40, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_outputDb), 0, bytes, 44, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_limitEnabled != 0 ? 1f : 0f), 0, bytes, 48, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float) * 6)
        {
            return;
        }

        _driveDb = Math.Clamp(BitConverter.ToSingle(state, 0), DriveMinDb, DriveMaxDb);
        _bass = Math.Clamp(BitConverter.ToSingle(state, 4), -1f, 1f);
        _bassCompRatio = Math.Clamp(BitConverter.ToSingle(state, 8), 1f, 10f);
        _bassLcEnabled = BitConverter.ToSingle(state, 12) >= 0.5f ? 1 : 0;
        _midHiTuneHz = Math.Clamp(BitConverter.ToSingle(state, 16), 1100f, 22000f);
        _process = Math.Clamp(BitConverter.ToSingle(state, 20), 0f, 1f);
        if (state.Length >= sizeof(float) * 7) _highFreqHz = Math.Clamp(BitConverter.ToSingle(state, 24), 2000f, 20000f);
        if (state.Length >= sizeof(float) * 8) _intensity = Math.Clamp(BitConverter.ToSingle(state, 28), 0f, 1f);
        if (state.Length >= sizeof(float) * 9) _highCompRatio = Math.Clamp(BitConverter.ToSingle(state, 32), 1f, 10f);
        if (state.Length >= sizeof(float) * 10) _highLcEnabled = BitConverter.ToSingle(state, 36) >= 0.5f ? 1 : 0;
        if (state.Length >= sizeof(float) * 11) _tubeEnabled = BitConverter.ToSingle(state, 40) >= 0.5f ? 1 : 0;
        if (state.Length >= sizeof(float) * 12) _outputDb = Math.Clamp(BitConverter.ToSingle(state, 44), OutputMinDb, OutputMaxDb);
        if (state.Length >= sizeof(float) * 13) _limitEnabled = BitConverter.ToSingle(state, 48) >= 0.5f ? 1 : 0;

        _driveSmoother.SetTarget(DspUtils.DbToLinear(_driveDb));
        _processSmoother.SetTarget(Math.Clamp(_process, 0f, 1f));
        _intensitySmoother.SetTarget(Math.Clamp(_intensity, 0f, 1f));
        _outputSmoother.SetTarget(DspUtils.DbToLinear(_outputDb));
        _tubeMakeupSmoother.SetTarget(1f);

        UpdateBassFilters();
        UpdateMidFilters();
        UpdateHighFilters();
        UpdateTubeFilters();
        UpdateLatency();
    }

    public void Dispose()
    {
    }

    private void UpdateBassFilters()
    {
        if (_sampleRate <= 0)
        {
            return;
        }

        float amount = MathF.Abs(_bass);
        float t = (_bass + 1f) * 0.5f; // 0=soft, 1=tight
        float freq = Lerp(60f, 130f, t);
        float q = Lerp(0.7f, 1.05f, t);
        float gainDb = amount * BassMaxDb;

        _bassShelf.SetLowShelf(_sampleRate, freq, gainDb, q);
        _bassLcPeak.SetPeaking(_sampleRate, freq * 0.9f, gainDb * 0.35f, q * 1.2f);
        _bassEnvLowPass.SetLowPass(_sampleRate, freq * 2f, 0.707f);
    }

    private void UpdateMidFilters()
    {
        if (_sampleRate <= 0)
        {
            return;
        }

        float tune = Math.Clamp(_midHiTuneHz, 1100f, 22000f);
        _midLowShelf.SetLowShelf(_sampleRate, tune, -MidTiltMaxDb, 0.8f);
        _midHighShelf.SetHighShelf(_sampleRate, tune, MidTiltMaxDb, 0.8f);

        // Mid-band envelope for dominant-mid detection.
        _midEnvBand.SetBandPass(_sampleRate, 1400f, 0.9f);

        // Two all-pass settings crossfaded for amplitude-correlated phase shifting.
        _midPhaseLowA.SetFrequency(_sampleRate, 650f);
        _midPhaseHighA.SetFrequency(_sampleRate, 2200f);
        _midPhaseLowB.SetFrequency(_sampleRate, 900f);
        _midPhaseHighB.SetFrequency(_sampleRate, 2800f);
    }

    private void UpdateHighFilters()
    {
        if (_sampleRate <= 0)
        {
            return;
        }

        float freq = Math.Clamp(_highFreqHz, 2000f, 20000f);
        _highShelfA.SetHighShelf(_sampleRate, freq, HighMaxDb, 0.75f);
        _highShelfB.SetHighShelf(_sampleRate, freq, HighMaxDb, 0.75f);
        _highLcPeak.SetPeaking(_sampleRate, freq * 0.9f, HighMaxDb * 0.25f, 1.15f);
        _highEnvBand.SetHighPass(_sampleRate, Math.Min(freq, _sampleRate * 0.45f), 0.707f);
    }

    private void UpdateTubeFilters()
    {
        if (_sampleRate <= 0)
        {
            return;
        }

        int oversampledRate = _sampleRate * 2;
        _tubeLowpass.SetLowPass(oversampledRate, TubeLowpassHz, 0.707f);
    }

    private void UpdateLatency()
    {
        int latency = 0;
        if (_tubeEnabled != 0)
        {
            latency = _tubeUpsampler.FilterDelaySamples;
        }

        Volatile.Write(ref _latencySamples, latency);
    }

    private void ProcessTubeStage(Span<float> buffer)
    {
        int requiredLength = buffer.Length * 2;
        if (_tubeBuffer.Length < requiredLength)
        {
            return;
        }

        _tubeUpsampler.ProcessUpsample(buffer, _tubeBuffer.AsSpan(0, requiredLength));

        float drive = TubeDriveBase + MathF.Max(0f, _driveDb) * 0.08f + _process * 0.6f + _intensity * 0.4f;
        float bias = 0.18f * drive;
        float k = 1.2f + drive * 1.6f;
        float biasTanh = MathF.Tanh(bias * k);
        float sech = 1f / MathF.Cosh(bias * k);
        float gainNorm = k * sech * sech;

        var oversampled = _tubeBuffer.AsSpan(0, requiredLength);
        for (int i = 0; i < oversampled.Length; i++)
        {
            float x = oversampled[i];
            float shaped = MathF.Tanh((x + bias) * k) - biasTanh;
            if (gainNorm > 0.001f)
            {
                shaped /= gainNorm;
            }
            oversampled[i] = _tubeLowpass.Process(shaped);
        }

        _tubeDownsampler.ProcessDownsample(oversampled, buffer);
    }

    private void ApplyTubeMakeup(Span<float> buffer, float preTubeEnergy)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        float postEnergy = 0f;
        for (int i = 0; i < buffer.Length; i++)
        {
            float sample = buffer[i];
            postEnergy += sample * sample;
        }

        float preRms = preTubeEnergy > 0f ? MathF.Sqrt(preTubeEnergy / buffer.Length) : 0f;
        float postRms = postEnergy > 0f ? MathF.Sqrt(postEnergy / buffer.Length) : 0f;
        float target = postRms > 1e-6f ? preRms / postRms : 1f;
        target = Math.Clamp(target, 0.6f, 2.4f);
        _tubeMakeupSmoother.SetTarget(target);

        Volatile.Write(ref _meterTubePreRms, preRms);
        Volatile.Write(ref _meterTubePostRms, postRms);
        Volatile.Write(ref _meterTubeMakeupTarget, target);

        if (!_tubeMakeupSmoother.IsSmoothing)
        {
            float gain = _tubeMakeupSmoother.Current;
            Volatile.Write(ref _meterTubeMakeupCurrent, gain);
            if (MathF.Abs(gain - 1f) <= 1e-6f)
            {
                return;
            }

            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] *= gain;
            }
            return;
        }

        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] *= _tubeMakeupSmoother.Next();
        }
        Volatile.Write(ref _meterTubeMakeupCurrent, _tubeMakeupSmoother.Current);
    }

    private static float ApplyBassLcSaturation(float input)
    {
        float bias = 0.08f;
        float k = 1.4f;
        float shaped = MathF.Tanh((input + bias) * k) - MathF.Tanh(bias * k);
        return shaped;
    }

    private static float ComputeCompGain(float envelope, float thresholdDb, float ratio)
    {
        if (ratio <= 1.01f)
        {
            return 1f;
        }

        float envDb = DspUtils.LinearToDb(envelope);
        float overDb = envDb - thresholdDb;
        if (overDb <= 0f)
        {
            return 1f;
        }

        float gainDb = -overDb + overDb / ratio;
        return DspUtils.DbToLinear(gainDb);
    }

    private static float MapProcessMix(float process)
    {
        float value = Math.Clamp(process, 0f, 1f);
        if (value <= ProcessGateThreshold)
        {
            return 0f;
        }

        float t = (value - ProcessGateThreshold) / (1f - ProcessGateThreshold);
        return t * t;
    }

    private static float MapCompNullifier(float ratio)
    {
        float t = Math.Clamp((ratio - 1f) / 9f, 0f, 1f);
        return t;
    }

    private static void ApplySoftLimit(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float x = buffer[i];
            float abs = MathF.Abs(x);
            if (abs <= LimitThresholdLinear)
            {
                continue;
            }

            float excess = abs - LimitThresholdLinear;
            float compressed = LimitThresholdLinear + excess / (1f + excess);
            buffer[i] = MathF.CopySign(compressed, x);
        }
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }

    private static string FormatToggle(float value)
    {
        return value >= 0.5f ? "On" : "Off";
    }
}
