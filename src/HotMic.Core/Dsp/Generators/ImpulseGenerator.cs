namespace HotMic.Core.Dsp.Generators;

/// <summary>
/// Impulse generator for transient testing (clicks at configurable intervals).
/// Generates DC-free bipolar pulses.
/// </summary>
public struct ImpulseGenerator
{
    private int _sampleRate;
    private int _intervalSamples;
    private int _sampleCounter;
    private int _pulsePhase;

    // Bipolar pulse width in samples (short burst to be DC-free)
    private const int PulseWidth = 2;

    public void Initialize(int sampleRate)
    {
        _sampleRate = sampleRate;
        _intervalSamples = (int)(100f * 0.001f * sampleRate); // Default 100ms
        _sampleCounter = 0;
        _pulsePhase = PulseWidth; // Start ready for first impulse
    }

    public void SetInterval(float intervalMs)
    {
        if (_sampleRate > 0)
        {
            _intervalSamples = Math.Max(PulseWidth * 2, (int)(intervalMs * 0.001f * _sampleRate));
        }
    }

    public void Reset()
    {
        _sampleCounter = 0;
        _pulsePhase = PulseWidth;
    }

    public float Next()
    {
        float sample = 0f;

        // Generate bipolar pulse (positive then negative for DC-free)
        if (_pulsePhase < PulseWidth)
        {
            sample = _pulsePhase == 0 ? 1f : -1f;
            _pulsePhase++;
        }

        // Count towards next impulse
        _sampleCounter++;
        if (_sampleCounter >= _intervalSamples)
        {
            _sampleCounter = 0;
            _pulsePhase = 0;
        }

        return sample;
    }
}
