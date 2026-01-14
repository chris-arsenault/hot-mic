namespace HotMic.Core.Dsp.Generators;

/// <summary>
/// Core oscillator implementation with anti-aliased waveforms via PolyBLEP.
/// Supports sine, square, saw, and triangle with frequency sweep.
/// </summary>
public struct OscillatorCore
{
    private double _phase;
    private double _phaseIncrement;
    private float _frequency;
    private int _sampleRate;
    private float _pulseWidth;

    // Sweep state
    private bool _sweepEnabled;
    private float _sweepStartHz;
    private float _sweepEndHz;
    private float _sweepDurationMs;
    private int _sweepDirection; // 0=up, 1=down, 2=pingpong
    private int _sweepCurve; // 0=linear, 1=log
    private double _sweepPosition; // 0.0 to 1.0
    private double _sweepIncrement;
    private bool _sweepForward;

    public void Initialize(int sampleRate)
    {
        _sampleRate = sampleRate;
        _phase = 0.0;
        _frequency = 440f;
        _pulseWidth = 0.5f;
        _sweepEnabled = false;
        _sweepForward = true;
        UpdatePhaseIncrement();
    }

    public void SetFrequency(float hz)
    {
        _frequency = Math.Clamp(hz, 20f, 20000f);
        if (!_sweepEnabled)
        {
            UpdatePhaseIncrement();
        }
    }

    public void SetPulseWidth(float width)
    {
        _pulseWidth = Math.Clamp(width, 0.1f, 0.9f);
    }

    public void ConfigureSweep(bool enabled, float startHz, float endHz, float durationMs, int direction, int curve)
    {
        _sweepEnabled = enabled;
        _sweepStartHz = Math.Clamp(startHz, 20f, 20000f);
        _sweepEndHz = Math.Clamp(endHz, 20f, 20000f);
        _sweepDurationMs = Math.Max(100f, durationMs);
        _sweepDirection = direction;
        _sweepCurve = curve;

        if (enabled && _sampleRate > 0)
        {
            int sweepSamples = (int)(_sweepDurationMs * 0.001f * _sampleRate);
            _sweepIncrement = 1.0 / Math.Max(1, sweepSamples);
            _sweepPosition = 0.0;
            _sweepForward = true;
        }
    }

    public void Reset()
    {
        _phase = 0.0;
        _sweepPosition = 0.0;
        _sweepForward = true;
    }

    public float NextSine()
    {
        UpdateSweep();
        float sample = MathF.Sin((float)(_phase * 2.0 * Math.PI));
        AdvancePhase();
        return sample;
    }

    public float NextSquare()
    {
        UpdateSweep();
        double t = _phase;
        double pw = _pulseWidth;

        // Raw square
        float sample = t < pw ? 1f : -1f;

        // PolyBLEP correction at transition points
        sample += PolyBlep(t, _phaseIncrement);
        sample -= PolyBlep(Wrap(t - pw), _phaseIncrement);

        AdvancePhase();
        return sample;
    }

    public float NextSaw()
    {
        UpdateSweep();
        double t = _phase;

        // Raw saw (ramp down)
        float sample = (float)(1.0 - 2.0 * t);

        // PolyBLEP correction at discontinuity
        sample -= PolyBlep(t, _phaseIncrement);

        AdvancePhase();
        return sample;
    }

    public float NextTriangle()
    {
        UpdateSweep();

        // Integrate square wave for triangle (with PolyBLAMP for anti-aliasing)
        // Simplified: use naive triangle with slight aliasing at very high frequencies
        double t = _phase;
        float sample;

        if (t < 0.5)
        {
            sample = (float)(4.0 * t - 1.0);
        }
        else
        {
            sample = (float)(3.0 - 4.0 * t);
        }

        AdvancePhase();
        return sample;
    }

    private void AdvancePhase()
    {
        _phase += _phaseIncrement;
        if (_phase >= 1.0)
        {
            _phase -= 1.0;
        }
    }

    private void UpdateSweep()
    {
        if (!_sweepEnabled)
        {
            return;
        }

        // Update sweep position
        if (_sweepForward)
        {
            _sweepPosition += _sweepIncrement;
            if (_sweepPosition >= 1.0)
            {
                if (_sweepDirection == 2) // pingpong
                {
                    _sweepPosition = 1.0;
                    _sweepForward = false;
                }
                else if (_sweepDirection == 0) // up - restart
                {
                    _sweepPosition = 0.0;
                }
                else // down - restart
                {
                    _sweepPosition = 0.0;
                }
            }
        }
        else
        {
            _sweepPosition -= _sweepIncrement;
            if (_sweepPosition <= 0.0)
            {
                _sweepPosition = 0.0;
                _sweepForward = true;
            }
        }

        // Calculate current frequency
        double pos = _sweepPosition;
        if (_sweepDirection == 1) // down
        {
            pos = 1.0 - pos;
        }

        float currentFreq;
        if (_sweepCurve == 1) // logarithmic
        {
            double logStart = Math.Log(_sweepStartHz);
            double logEnd = Math.Log(_sweepEndHz);
            currentFreq = (float)Math.Exp(logStart + pos * (logEnd - logStart));
        }
        else // linear
        {
            currentFreq = (float)(_sweepStartHz + pos * (_sweepEndHz - _sweepStartHz));
        }

        _frequency = currentFreq;
        UpdatePhaseIncrement();
    }

    private void UpdatePhaseIncrement()
    {
        if (_sampleRate > 0)
        {
            _phaseIncrement = _frequency / _sampleRate;
        }
    }

    /// <summary>
    /// PolyBLEP (polynomial bandlimited step) correction for anti-aliased waveforms.
    /// </summary>
    private static float PolyBlep(double t, double dt)
    {
        if (t < dt)
        {
            t /= dt;
            return (float)(t + t - t * t - 1.0);
        }
        else if (t > 1.0 - dt)
        {
            t = (t - 1.0) / dt;
            return (float)(t * t + t + t + 1.0);
        }
        return 0f;
    }

    private static double Wrap(double t)
    {
        while (t < 0.0) t += 1.0;
        while (t >= 1.0) t -= 1.0;
        return t;
    }
}
