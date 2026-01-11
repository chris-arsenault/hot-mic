namespace HotMic.Core.Dsp;

/// <summary>
/// Linear ramp smoother for parameters that should change without zipper noise.
/// </summary>
public struct LinearSmoother
{
    private int _rampSamples;
    private int _samplesLeft;
    private float _current;
    private float _target;
    private float _step;

    public void Configure(int sampleRate, float timeMs, float initialValue)
    {
        _rampSamples = Math.Max(1, (int)(timeMs * 0.001f * sampleRate));
        _current = initialValue;
        _target = initialValue;
        _samplesLeft = 0;
        _step = 0f;
    }

    public float Current => _current;

    public bool IsSmoothing => _samplesLeft > 0;

    public void SetTarget(float target)
    {
        _target = target;
        float delta = _target - _current;
        if (MathF.Abs(delta) <= 1e-6f)
        {
            _current = _target;
            _samplesLeft = 0;
            _step = 0f;
            return;
        }

        if (_rampSamples <= 0)
        {
            _current = _target;
            _samplesLeft = 0;
            _step = 0f;
            return;
        }

        _samplesLeft = _rampSamples;
        _step = delta / _samplesLeft;
    }

    public float Next()
    {
        if (_samplesLeft > 0)
        {
            _current += _step;
            _samplesLeft--;
        }
        else
        {
            _current = _target;
        }

        return _current;
    }
}
