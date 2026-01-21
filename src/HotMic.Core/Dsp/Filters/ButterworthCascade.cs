using System;

namespace HotMic.Core.Dsp.Filters;

/// <summary>
/// Cascaded biquad sections configured as an even-order Butterworth low/high-pass.
/// </summary>
internal sealed class ButterworthCascade
{
    private readonly BiquadFilter[] _sections;
    private readonly float[] _qValues;

    public ButterworthCascade(int order)
    {
        if ((order & 1) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(order), "Butterworth order must be even.");
        }

        int sectionCount = Math.Max(1, order / 2);
        _sections = new BiquadFilter[sectionCount];
        _qValues = new float[sectionCount];

        for (int i = 0; i < sectionCount; i++)
        {
            _sections[i] = new BiquadFilter();
        }

        ComputeButterworthQs(order, _qValues);
    }

    public void ConfigureLowPass(float sampleRate, float cutoffHz)
    {
        for (int i = 0; i < _sections.Length; i++)
        {
            _sections[i].SetLowPass(sampleRate, cutoffHz, _qValues[i]);
        }
    }

    public void ConfigureHighPass(float sampleRate, float cutoffHz)
    {
        for (int i = 0; i < _sections.Length; i++)
        {
            _sections[i].SetHighPass(sampleRate, cutoffHz, _qValues[i]);
        }
    }

    public float Process(float input)
    {
        float value = input;
        for (int i = 0; i < _sections.Length; i++)
        {
            value = _sections[i].Process(value);
        }

        return value;
    }

    public void Reset()
    {
        for (int i = 0; i < _sections.Length; i++)
        {
            _sections[i].Reset();
        }
    }

    private static void ComputeButterworthQs(int order, float[] destination)
    {
        int sectionCount = order / 2;
        for (int i = 0; i < sectionCount; i++)
        {
            int k = i + 1;
            double angle = Math.PI * (2.0 * k - 1.0) / (2.0 * order);
            double q = 1.0 / (2.0 * Math.Cos(angle));
            destination[i] = (float)q;
        }
    }
}
