using System;
using HotMic.Core.Dsp.KissFft;

namespace HotMic.Core.Dsp;

internal sealed class DeepFilterNetStft
{
    private readonly int _fftSize;
    private readonly int _hopSize;
    private readonly int _freqSize;
    private readonly float[] _window;
    private readonly float _wnorm;
    private readonly float[] _analysisMem;
    private readonly float[] _synthesisMem;
    private readonly float[] _timeBuffer;
    private readonly KissFFTR _fftForward;
    private readonly KissFFTR _fftInverse;
    private readonly kiss_fft_cpx<float>[] _freqBuffer;

    public DeepFilterNetStft(int fftSize, int hopSize)
    {
        _fftSize = fftSize;
        _hopSize = hopSize;
        _freqSize = fftSize / 2 + 1;
        _analysisMem = new float[fftSize - hopSize];
        _synthesisMem = new float[fftSize - hopSize];
        _timeBuffer = new float[fftSize];

        _window = BuildVorbisWindow(fftSize);
        // Match libDF normalization: apply wnorm in analysis only.
        _wnorm = 1f / (fftSize * fftSize / (2f * hopSize));

        _fftForward = new KissFFTR(fftSize, inverse: false);
        _fftInverse = new KissFFTR(fftSize, inverse: true);
        _freqBuffer = new kiss_fft_cpx<float>[_freqSize];
    }

    public int FftSize => _fftSize;
    public int HopSize => _hopSize;
    public int FreqSize => _freqSize;

    public void Reset()
    {
        System.Array.Clear(_analysisMem, 0, _analysisMem.Length);
        System.Array.Clear(_synthesisMem, 0, _synthesisMem.Length);
    }

    public void Analyze(ReadOnlySpan<float> inputHop, float[] specInterleaved)
    {
        if (inputHop.Length != _hopSize)
        {
            throw new ArgumentException("Input hop size mismatch.", nameof(inputHop));
        }
        if (specInterleaved.Length < _freqSize * 2)
        {
            throw new ArgumentException("Spec buffer too small.", nameof(specInterleaved));
        }

        int windowTail = _fftSize - _hopSize;
        for (int i = 0; i < windowTail; i++)
        {
            _timeBuffer[i] = _analysisMem[i] * _window[i];
        }
        for (int i = 0; i < _hopSize; i++)
        {
            _timeBuffer[windowTail + i] = inputHop[i] * _window[windowTail + i];
        }

        int analysisSplit = _analysisMem.Length - _hopSize;
        if (analysisSplit > 0)
        {
            System.Array.Copy(_analysisMem, _hopSize, _analysisMem, 0, analysisSplit);
        }
        for (int i = 0; i < _hopSize; i++)
        {
            _analysisMem[analysisSplit + i] = inputHop[i];
        }

        _fftForward.Forward(_timeBuffer, _freqBuffer);

        for (int i = 0; i < _freqSize; i++)
        {
            specInterleaved[i * 2] = _freqBuffer[i].r * _wnorm;
            specInterleaved[i * 2 + 1] = _freqBuffer[i].i * _wnorm;
        }
    }

    public void Synthesize(float[] specInterleaved, Span<float> outputHop)
    {
        if (outputHop.Length != _hopSize)
        {
            throw new ArgumentException("Output hop size mismatch.", nameof(outputHop));
        }
        if (specInterleaved.Length < _freqSize * 2)
        {
            throw new ArgumentException("Spec buffer too small.", nameof(specInterleaved));
        }

        for (int i = 0; i < _freqSize; i++)
        {
            float re = specInterleaved[i * 2];
            float im = specInterleaved[i * 2 + 1];
            if (!float.IsFinite(re) || !float.IsFinite(im))
            {
                re = 0f;
                im = 0f;
            }

            _freqBuffer[i].r = re;
            _freqBuffer[i].i = im;
        }

        _fftInverse.Inverse(_freqBuffer, _timeBuffer);

        for (int i = 0; i < _fftSize; i++)
        {
            _timeBuffer[i] *= _window[i];
        }

        for (int i = 0; i < _hopSize; i++)
        {
            outputHop[i] = _timeBuffer[i] + _synthesisMem[i];
        }

        int split = _synthesisMem.Length - _hopSize;
        if (split > 0)
        {
            System.Array.Copy(_synthesisMem, _hopSize, _synthesisMem, 0, split);
        }
        for (int i = 0; i < split; i++)
        {
            _synthesisMem[i] += _timeBuffer[_hopSize + i];
        }
        for (int i = split; i < _synthesisMem.Length; i++)
        {
            _synthesisMem[i] = _timeBuffer[_hopSize + i];
        }
    }

    private static float[] BuildVorbisWindow(int fftSize)
    {
        var window = new float[fftSize];
        double pi = Math.PI;
        int half = fftSize / 2;
        for (int i = 0; i < fftSize; i++)
        {
            double sin = Math.Sin(0.5 * pi * (i + 0.5) / half);
            window[i] = (float)Math.Sin(0.5 * pi * sin * sin);
        }
        return window;
    }
}
