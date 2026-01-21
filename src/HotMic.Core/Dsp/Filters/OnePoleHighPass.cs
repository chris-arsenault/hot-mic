namespace HotMic.Core.Dsp.Filters;

/// <summary>
/// First-order high-pass filter for sidechain rumble control.
/// </summary>
public struct OnePoleHighPass
{
    private float _a;
    private float _prevInput;
    private float _prevOutput;

    public void Configure(float cutoffHz, int sampleRate)
    {
        float clamped = Math.Clamp(cutoffHz, 10f, sampleRate * 0.45f);
        _a = MathF.Exp(-2f * MathF.PI * clamped / sampleRate);
    }

    public float Process(float input)
    {
        float output = _a * (_prevOutput + input - _prevInput);
        _prevInput = input;
        _prevOutput = DspUtils.FlushDenormal(output);
        return _prevOutput;
    }

    public void Reset()
    {
        _prevInput = 0f;
        _prevOutput = 0f;
    }
}
