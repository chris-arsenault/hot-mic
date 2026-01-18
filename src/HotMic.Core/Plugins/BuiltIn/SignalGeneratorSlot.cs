using System;
using HotMic.Core.Dsp;
using HotMic.Core.Dsp.Filters;
using HotMic.Core.Dsp.Generators;

namespace HotMic.Core.Plugins.BuiltIn;

internal sealed class SignalGeneratorSlot
{
    private readonly SignalGeneratorOscillator _oscillator = new();
    private readonly SignalGeneratorNoise _noise = new();
    private readonly SignalGeneratorImpulse _impulse = new();
    private readonly SignalGeneratorChirp _chirp = new();
    private readonly SignalGeneratorSample _sample = new();
    private readonly SampleBuffer _sampleBuffer = new();
    private LinearSmoother _gainSmoother;

    internal GeneratorType Type;
    internal float Frequency;
    internal float GainDb;
    internal float GainLinear;
    internal bool Muted;
    internal bool Solo;
    internal bool SweepEnabled;
    internal float SweepStartHz;
    internal float SweepEndHz;
    internal float SweepDurationMs;
    internal SweepDirection SweepDirection;
    internal SweepCurve SweepCurve;
    internal float PulseWidth;
    internal float ImpulseIntervalMs;
    internal float ChirpDurationMs;
    internal SampleLoopMode SampleLoopMode;
    internal float SampleSpeed;
    internal float SampleTrimStart;
    internal float SampleTrimEnd;

    public void InitializeDefaults()
    {
        Type = GeneratorType.Sine;
        Frequency = 440f;
        GainDb = -12f;
        GainLinear = DspUtils.DbToLinear(-12f);
        Muted = false;
        Solo = false;
        SweepEnabled = false;
        SweepStartHz = 80f;
        SweepEndHz = 8000f;
        SweepDurationMs = 5000f;
        SweepDirection = SweepDirection.Up;
        SweepCurve = SweepCurve.Logarithmic;
        PulseWidth = 0.5f;
        ImpulseIntervalMs = 100f;
        ChirpDurationMs = 200f;
        SampleLoopMode = SampleLoopMode.Loop;
        SampleSpeed = 1f;
        SampleTrimStart = 0f;
        SampleTrimEnd = 1f;
    }

    public void Initialize(int sampleRate, uint noiseSeed)
    {
        _oscillator.Initialize(sampleRate);
        _noise.Initialize(noiseSeed);
        _impulse.Initialize(sampleRate);
        _chirp.Initialize(sampleRate);
        _sample.Initialize(sampleRate);
        _gainSmoother.Configure(sampleRate, 5f, GainLinear);

        ApplyParameters();
    }

    public bool IsActive(bool anySolo)
    {
        return !Muted && (!anySolo || Solo);
    }

    public float NextGain()
    {
        return _gainSmoother.IsSmoothing ? _gainSmoother.Next() : _gainSmoother.Current;
    }

    public float NextSample()
    {
        return Type switch
        {
            GeneratorType.Sine or GeneratorType.Square or GeneratorType.Saw or GeneratorType.Triangle => _oscillator.Next(Type),
            GeneratorType.WhiteNoise or GeneratorType.PinkNoise or GeneratorType.BrownNoise or GeneratorType.BlueNoise => _noise.Next(Type),
            GeneratorType.Impulse => _impulse.Next(),
            GeneratorType.Chirp => _chirp.Next(),
            GeneratorType.Sample => _sample.Next(_sampleBuffer),
            GeneratorType.DcTest => 0.5f,
            _ => 0f
        };
    }

    public void SetParameter(int paramIndex, float value)
    {
        switch (paramIndex)
        {
            case SignalGeneratorPlugin.TypeIndex:
                Type = (GeneratorType)(int)value;
                break;
            case SignalGeneratorPlugin.FrequencyIndex:
                Frequency = Math.Clamp(value, 20f, 20000f);
                _oscillator.SetFrequency(Frequency);
                break;
            case SignalGeneratorPlugin.GainIndex:
                GainDb = Math.Clamp(value, -60f, 12f);
                GainLinear = DspUtils.DbToLinear(GainDb);
                _gainSmoother.SetTarget(GainLinear);
                break;
            case SignalGeneratorPlugin.MuteIndex:
                Muted = value >= 0.5f;
                break;
            case SignalGeneratorPlugin.SoloIndex:
                Solo = value >= 0.5f;
                break;
            case SignalGeneratorPlugin.SweepEnabledIndex:
                SweepEnabled = value >= 0.5f;
                ApplySweep();
                break;
            case SignalGeneratorPlugin.SweepStartHzIndex:
                SweepStartHz = Math.Clamp(value, 20f, 20000f);
                ApplySweep();
                break;
            case SignalGeneratorPlugin.SweepEndHzIndex:
                SweepEndHz = Math.Clamp(value, 20f, 20000f);
                ApplySweep();
                break;
            case SignalGeneratorPlugin.SweepDurationMsIndex:
                SweepDurationMs = Math.Clamp(value, 100f, 30000f);
                ApplySweep();
                break;
            case SignalGeneratorPlugin.SweepDirectionIndex:
                SweepDirection = (SweepDirection)(int)value;
                ApplySweep();
                break;
            case SignalGeneratorPlugin.SweepCurveIndex:
                SweepCurve = (SweepCurve)(int)value;
                ApplySweep();
                break;
            case SignalGeneratorPlugin.PulseWidthIndex:
                PulseWidth = Math.Clamp(value, 0.1f, 0.9f);
                _oscillator.SetPulseWidth(PulseWidth);
                break;
            case SignalGeneratorPlugin.ImpulseIntervalMsIndex:
                ImpulseIntervalMs = Math.Clamp(value, 10f, 5000f);
                _impulse.SetInterval(ImpulseIntervalMs);
                break;
            case SignalGeneratorPlugin.ChirpDurationMsIndex:
                ChirpDurationMs = Math.Clamp(value, 50f, 500f);
                _chirp.SetDuration(ChirpDurationMs);
                break;
            case SignalGeneratorPlugin.SampleLoopModeIndex:
                SampleLoopMode = (SampleLoopMode)(int)value;
                _sample.SetLoopMode(SampleLoopMode);
                break;
            case SignalGeneratorPlugin.SampleSpeedIndex:
                SampleSpeed = Math.Clamp(value, 0.5f, 2f);
                _sample.SetSpeed(SampleSpeed);
                break;
            case SignalGeneratorPlugin.SampleTrimStartIndex:
                SampleTrimStart = Math.Clamp(value, 0f, 0.99f);
                _sample.SetTrimStart(SampleTrimStart);
                break;
            case SignalGeneratorPlugin.SampleTrimEndIndex:
                SampleTrimEnd = Math.Clamp(value, 0.01f, 1f);
                _sample.SetTrimEnd(SampleTrimEnd);
                break;
        }
    }

    public void ApplyParameters()
    {
        _oscillator.SetFrequency(Frequency);
        _oscillator.SetPulseWidth(PulseWidth);
        ApplySweep();
        _impulse.SetInterval(ImpulseIntervalMs);
        _chirp.SetDuration(ChirpDurationMs);
        _sample.SetSpeed(SampleSpeed);
        _sample.SetLoopMode(SampleLoopMode);
        _sample.SetTrimStart(SampleTrimStart);
        _sample.SetTrimEnd(SampleTrimEnd);
    }

    public void ApplySweep()
    {
        _oscillator.ConfigureSweep(
            SweepEnabled,
            SweepStartHz,
            SweepEndHz,
            SweepDurationMs,
            SweepDirection,
            SweepCurve);
    }

    public void ResetSamplePlayback()
    {
        _sample.Reset();
    }

    public void LoadSample(float[] samples, int sampleRate)
    {
        _sampleBuffer.Load(samples, sampleRate);
        _sample.Reset();
    }

    public void LoadSample(ReadOnlySpan<float> samples, int sampleRate)
    {
        _sampleBuffer.Load(samples, sampleRate);
        _sample.Reset();
    }

    public bool HasSample()
    {
        return _sampleBuffer.IsLoaded;
    }

    public (float[] Samples, int SampleRate)? GetSampleData()
    {
        if (!_sampleBuffer.IsLoaded) return null;
        return (_sampleBuffer.ExportSamples(), _sampleBuffer.SampleRate);
    }

    public void SetGainSmootherTarget(float target)
    {
        _gainSmoother.SetTarget(target);
    }
}
