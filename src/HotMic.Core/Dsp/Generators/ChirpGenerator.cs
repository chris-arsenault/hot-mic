namespace HotMic.Core.Dsp.Generators;

/// <summary>
/// Chirp generator for frequency response testing.
/// Generates logarithmic frequency sweeps with smooth envelope.
/// </summary>
public struct ChirpGenerator
{
    private int _sampleRate;
    private float _startHz;
    private float _endHz;
    private int _chirpDurationSamples;
    private int _intervalSamples;
    private int _sampleCounter;
    private int _chirpPosition;
    private double _phase;
    private bool _active;

    // Envelope ramp samples for click-free start/end
    private const int EnvelopeRampSamples = 64;

    public void Initialize(int sampleRate)
    {
        _sampleRate = sampleRate;
        _startHz = 80f;
        _endHz = 8000f;
        _chirpDurationSamples = (int)(200f * 0.001f * sampleRate); // 200ms default
        _intervalSamples = (int)(1000f * 0.001f * sampleRate); // 1 second default
        _sampleCounter = 0;
        _chirpPosition = 0;
        _phase = 0;
        _active = true;
    }

    public void SetDuration(float durationMs)
    {
        if (_sampleRate > 0)
        {
            _chirpDurationSamples = Math.Max(EnvelopeRampSamples * 2, (int)(durationMs * 0.001f * _sampleRate));
        }
    }

    public void SetInterval(float intervalMs)
    {
        if (_sampleRate > 0)
        {
            _intervalSamples = Math.Max(_chirpDurationSamples + 100, (int)(intervalMs * 0.001f * _sampleRate));
        }
    }

    public void SetFrequencyRange(float startHz, float endHz)
    {
        _startHz = Math.Clamp(startHz, 20f, 20000f);
        _endHz = Math.Clamp(endHz, 20f, 20000f);
    }

    public void Reset()
    {
        _sampleCounter = 0;
        _chirpPosition = 0;
        _phase = 0;
        _active = true;
    }

    public float Next()
    {
        float sample = 0f;

        if (_active && _chirpPosition < _chirpDurationSamples)
        {
            // Calculate normalized position (0 to 1)
            double t = (double)_chirpPosition / _chirpDurationSamples;

            // Logarithmic frequency sweep
            double logStart = Math.Log(_startHz);
            double logEnd = Math.Log(_endHz);
            double currentFreq = Math.Exp(logStart + t * (logEnd - logStart));

            // Generate sine at current frequency
            sample = MathF.Sin((float)(_phase * 2.0 * Math.PI));

            // Advance phase
            _phase += currentFreq / _sampleRate;
            if (_phase >= 1.0) _phase -= 1.0;

            // Apply envelope for smooth start/end
            float envelope = 1f;
            if (_chirpPosition < EnvelopeRampSamples)
            {
                envelope = (float)_chirpPosition / EnvelopeRampSamples;
            }
            else if (_chirpPosition >= _chirpDurationSamples - EnvelopeRampSamples)
            {
                envelope = (float)(_chirpDurationSamples - _chirpPosition) / EnvelopeRampSamples;
            }

            sample *= envelope;
            _chirpPosition++;

            if (_chirpPosition >= _chirpDurationSamples)
            {
                _active = false;
            }
        }

        // Count towards next chirp
        _sampleCounter++;
        if (_sampleCounter >= _intervalSamples)
        {
            _sampleCounter = 0;
            _chirpPosition = 0;
            _phase = 0;
            _active = true;
        }

        return sample;
    }
}
