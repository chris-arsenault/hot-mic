using System;
using HotMic.Core.Dsp.KissFft;
using Xunit;

namespace HotMic.Core.Tests.Dsp;

public class KissFftTests
{
    [Fact]
    public void KissFFTR_Forward_MatchesNaiveDft_960()
    {
        const int nfft = 960;
        var input = new float[nfft];
        for (int i = 0; i < nfft; i++)
        {
            input[i] = (float)(0.6 * Math.Sin(2 * Math.PI * 7 * i / nfft)
                               + 0.3 * Math.Cos(2 * Math.PI * 13 * i / nfft)
                               + 0.1 * Math.Sin(2 * Math.PI * 121 * i / nfft));
        }

        var fft = new KissFFTR(nfft, inverse: false);
        var output = new kiss_fft_cpx<float>[nfft / 2 + 1];
        fft.Forward(input, output);

        double twoPiByN = 2.0 * Math.PI / nfft;
        for (int k = 0; k <= nfft / 2; k++)
        {
            double sumRe = 0.0;
            double sumIm = 0.0;
            double step = -twoPiByN * k;
            for (int t = 0; t < nfft; t++)
            {
                double angle = step * t;
                double sample = input[t];
                sumRe += sample * Math.Cos(angle);
                sumIm += sample * Math.Sin(angle);
            }

            AssertComplexClose(output[k], sumRe, sumIm, relTol: 2e-4, absTol: 1e-2);
        }
    }

    [Fact]
    public void KissFFTR_RoundTrip_ReconstructsSignal_960()
    {
        const int nfft = 960;
        var input = new float[nfft];
        for (int i = 0; i < nfft; i++)
        {
            input[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 31 * i / nfft)
                               + 0.25 * Math.Cos(2 * Math.PI * 97 * i / nfft));
        }

        var forward = new KissFFTR(nfft, inverse: false);
        var inverse = new KissFFTR(nfft, inverse: true);
        var freq = new kiss_fft_cpx<float>[nfft / 2 + 1];
        var output = new float[nfft];

        forward.Forward(input, freq);
        inverse.Inverse(freq, output);

        double invN = 1.0 / nfft;
        for (int i = 0; i < nfft; i++)
        {
            double reconstructed = output[i] * invN;
            Assert.InRange(reconstructed, input[i] - 1e-4, input[i] + 1e-4);
        }
    }

    [Fact]
    public void KissFFT_Forward_MatchesNaiveDft_480()
    {
        const int nfft = 480;
        var input = new kiss_fft_cpx<float>[nfft];
        for (int i = 0; i < nfft; i++)
        {
            input[i] = new kiss_fft_cpx<float>(
                (float)(0.4 * Math.Sin(2 * Math.PI * 11 * i / nfft)),
                (float)(0.2 * Math.Cos(2 * Math.PI * 29 * i / nfft)));
        }

        var fft = new KissFFT<float>(nfft, inverse: false, new FloatArithmetic());
        var output = new kiss_fft_cpx<float>[nfft];
        fft.kiss_fft(new Array<kiss_fft_cpx<float>>(input), new Array<kiss_fft_cpx<float>>(output));

        double twoPiByN = 2.0 * Math.PI / nfft;
        for (int k = 0; k < nfft; k++)
        {
            double sumRe = 0.0;
            double sumIm = 0.0;
            double step = -twoPiByN * k;
            for (int t = 0; t < nfft; t++)
            {
                double angle = step * t;
                double cos = Math.Cos(angle);
                double sin = Math.Sin(angle);
                double re = input[t].r;
                double im = input[t].i;
                sumRe += re * cos - im * sin;
                sumIm += re * sin + im * cos;
            }

            AssertComplexClose(output[k], sumRe, sumIm, relTol: 2e-4, absTol: 1e-2);
        }
    }

    private static void AssertComplexClose(kiss_fft_cpx<float> actual, double expectedRe, double expectedIm, double relTol, double absTol)
    {
        double errRe = actual.r - expectedRe;
        double errIm = actual.i - expectedIm;
        double err = Math.Sqrt(errRe * errRe + errIm * errIm);
        double mag = Math.Sqrt(expectedRe * expectedRe + expectedIm * expectedIm);
        double tol = absTol + relTol * mag;
        Assert.True(err <= tol, $"Expected ({expectedRe}, {expectedIm}) got ({actual.r}, {actual.i}) err {err} tol {tol}");
    }
}
