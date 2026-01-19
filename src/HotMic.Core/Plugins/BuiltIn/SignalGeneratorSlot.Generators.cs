using HotMic.Core.Dsp.Generators;

namespace HotMic.Core.Plugins.BuiltIn;

internal sealed class SignalGeneratorOscillator
{
    private OscillatorCore _oscillator;

    public void Initialize(int sampleRate)
    {
        _oscillator.Initialize(sampleRate);
    }

    public void SetFrequency(float frequency)
    {
        _oscillator.SetFrequency(frequency);
    }

    public void SetPulseWidth(float pulseWidth)
    {
        _oscillator.SetPulseWidth(pulseWidth);
    }

    public void ConfigureSweep(
        bool enabled,
        float startHz,
        float endHz,
        float durationMs,
        SweepDirection direction,
        SweepCurve curve)
    {
        _oscillator.ConfigureSweep(
            enabled,
            startHz,
            endHz,
            durationMs,
            (int)direction,
            (int)curve);
    }

    public float Next(GeneratorType type)
    {
        return type switch
        {
            GeneratorType.Sine => _oscillator.NextSine(),
            GeneratorType.Square => _oscillator.NextSquare(),
            GeneratorType.Saw => _oscillator.NextSaw(),
            GeneratorType.Triangle => _oscillator.NextTriangle(),
            _ => 0f
        };
    }
}

internal sealed class SignalGeneratorNoise
{
    private NoiseGenerator _noise;

    public void Initialize(uint seed)
    {
        _noise.Initialize(seed);
    }

    public float Next(GeneratorType type)
    {
        return type switch
        {
            GeneratorType.WhiteNoise => _noise.NextWhite(),
            GeneratorType.PinkNoise => _noise.NextPink(),
            GeneratorType.BrownNoise => _noise.NextBrown(),
            GeneratorType.BlueNoise => _noise.NextBlue(),
            _ => 0f
        };
    }
}

internal sealed class SignalGeneratorImpulse
{
    private ImpulseGenerator _impulse;

    public void Initialize(int sampleRate)
    {
        _impulse.Initialize(sampleRate);
    }

    public void SetInterval(float intervalMs)
    {
        _impulse.SetInterval(intervalMs);
    }

    public float Next()
    {
        return _impulse.Next();
    }
}

internal sealed class SignalGeneratorChirp
{
    private ChirpGenerator _chirp;

    public void Initialize(int sampleRate)
    {
        _chirp.Initialize(sampleRate);
    }

    public void SetDuration(float durationMs)
    {
        _chirp.SetDuration(durationMs);
    }

    public float Next()
    {
        return _chirp.Next();
    }
}

internal sealed class SignalGeneratorSample
{
    private SamplePlayer _player;

    public void Initialize(int sampleRate)
    {
        _player.Initialize(sampleRate);
    }

    public void SetLoopMode(SampleLoopMode loopMode)
    {
        _player.SetLoopMode((int)loopMode);
    }

    public void SetSpeed(float speed)
    {
        _player.SetSpeed(speed);
    }

    public void SetTrimStart(float trimStart)
    {
        _player.SetTrimStart(trimStart);
    }

    public void SetTrimEnd(float trimEnd)
    {
        _player.SetTrimEnd(trimEnd);
    }

    public void Reset()
    {
        _player.Reset();
    }

    public float Next(SampleBuffer buffer, out SamplePlaybackFlags flags)
    {
        return _player.Next(buffer, out flags);
    }
}
