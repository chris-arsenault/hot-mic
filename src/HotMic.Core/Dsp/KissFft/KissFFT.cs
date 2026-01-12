using System;

namespace HotMic.Core.Dsp.KissFft;

// Ported from KissFFT-CS (BSD-3-Clause). This keeps kissfft's mixed-radix support for 960-point FFTs.
internal sealed class KissFFT<kiss_fft_scalar>
{
    private const int MaxFactors = 32;
    private int _nfft;
    private bool _inverse;
    private readonly int[] _factors = new int[2 * MaxFactors];
    private kiss_fft_cpx<kiss_fft_scalar>[] _twiddles = System.Array.Empty<kiss_fft_cpx<kiss_fft_scalar>>();

    private readonly IArithmetic<kiss_fft_scalar> _a;

    public KissFFT(int nfft, bool inverse, IArithmetic<kiss_fft_scalar> arithmetic)
    {
        _a = arithmetic;
        kiss_fft_alloc(nfft, inverse);
    }

    private void kf_bfly2(Array<kiss_fft_cpx<kiss_fft_scalar>> fout, int fstride, int m)
    {
        Array<kiss_fft_cpx<kiss_fft_scalar>> fout2;
        Array<kiss_fft_cpx<kiss_fft_scalar>> tw1 = new(_twiddles);
        kiss_fft_cpx<kiss_fft_scalar> t;
        fout2 = fout + m;
        do
        {
            fout[0] = _a.FixDivide(fout[0], 2);
            fout2[0] = _a.FixDivide(fout2[0], 2);

            t = _a.Multiply(fout2[0], tw1[0]);
            tw1 += fstride;
            fout2[0] = _a.Subtract(fout[0], t);
            fout[0] = _a.Add(fout[0], t);
            ++fout2;
            ++fout;
        } while (--m != 0);
    }

    private void kf_bfly4(Array<kiss_fft_cpx<kiss_fft_scalar>> fout, int fstride, int m)
    {
        Array<kiss_fft_cpx<kiss_fft_scalar>> tw1;
        Array<kiss_fft_cpx<kiss_fft_scalar>> tw2;
        Array<kiss_fft_cpx<kiss_fft_scalar>> tw3;
        kiss_fft_cpx<kiss_fft_scalar>[] scratch = new kiss_fft_cpx<kiss_fft_scalar>[6];
        int k = m;
        int m2 = 2 * m;
        int m3 = 3 * m;

        tw1 = new Array<kiss_fft_cpx<kiss_fft_scalar>>(_twiddles);
        tw2 = new Array<kiss_fft_cpx<kiss_fft_scalar>>(_twiddles);
        tw3 = new Array<kiss_fft_cpx<kiss_fft_scalar>>(_twiddles);

        do
        {
            fout[0] = _a.FixDivide(fout[0], 4);
            fout[m] = _a.FixDivide(fout[m], 4);
            fout[m2] = _a.FixDivide(fout[m2], 4);
            fout[m3] = _a.FixDivide(fout[m3], 4);

            scratch[0] = _a.Multiply(fout[m], tw1[0]);
            scratch[1] = _a.Multiply(fout[m2], tw2[0]);
            scratch[2] = _a.Multiply(fout[m3], tw3[0]);

            scratch[5] = _a.Subtract(fout[0], scratch[1]);
            fout[0] = _a.Add(fout[0], scratch[1]);
            scratch[3] = _a.Add(scratch[0], scratch[2]);
            scratch[4] = _a.Subtract(scratch[0], scratch[2]);
            fout[m2] = _a.Subtract(fout[0], scratch[3]);
            tw1 += fstride;
            tw2 += fstride * 2;
            tw3 += fstride * 3;
            fout[0] = _a.Add(fout[0], scratch[3]);

            if (_inverse)
            {
                fout[m].r = _a.Subtract(scratch[5].r, scratch[4].i);
                fout[m].i = _a.Add(scratch[5].i, scratch[4].r);
                fout[m3].r = _a.Add(scratch[5].r, scratch[4].i);
                fout[m3].i = _a.Subtract(scratch[5].i, scratch[4].r);
            }
            else
            {
                fout[m].r = _a.Add(scratch[5].r, scratch[4].i);
                fout[m].i = _a.Subtract(scratch[5].i, scratch[4].r);
                fout[m3].r = _a.Subtract(scratch[5].r, scratch[4].i);
                fout[m3].i = _a.Add(scratch[5].i, scratch[4].r);
            }
            ++fout;
        } while (--k != 0);
    }

    private void kf_bfly3(Array<kiss_fft_cpx<kiss_fft_scalar>> fout, int fstride, int m)
    {
        int k = m;
        int m2 = 2 * m;
        Array<kiss_fft_cpx<kiss_fft_scalar>> tw1;
        Array<kiss_fft_cpx<kiss_fft_scalar>> tw2;
        kiss_fft_cpx<kiss_fft_scalar>[] scratch = new kiss_fft_cpx<kiss_fft_scalar>[5];
        kiss_fft_cpx<kiss_fft_scalar> epi3 = _twiddles[fstride * m];

        tw1 = new Array<kiss_fft_cpx<kiss_fft_scalar>>(_twiddles);
        tw2 = new Array<kiss_fft_cpx<kiss_fft_scalar>>(_twiddles);

        do
        {
            fout[0] = _a.FixDivide(fout[0], 3);
            fout[m] = _a.FixDivide(fout[m], 3);
            fout[m2] = _a.FixDivide(fout[m2], 3);

            scratch[1] = _a.Multiply(fout[m], tw1[0]);
            scratch[2] = _a.Multiply(fout[m2], tw2[0]);

            scratch[3] = _a.Add(scratch[1], scratch[2]);
            scratch[0] = _a.Subtract(scratch[1], scratch[2]);
            tw1 += fstride;
            tw2 += fstride * 2;

            fout[m].r = _a.Subtract(fout[0].r, _a.Half(scratch[3].r));
            fout[m].i = _a.Subtract(fout[0].i, _a.Half(scratch[3].i));

            scratch[0] = _a.Multiply(scratch[0], epi3.i);

            fout[0] = _a.Add(fout[0], scratch[3]);

            fout[m2].r = _a.Add(fout[m].r, scratch[0].i);
            fout[m2].i = _a.Subtract(fout[m].i, scratch[0].r);

            fout[m].r = _a.Subtract(fout[m].r, scratch[0].i);
            fout[m].i = _a.Add(fout[m].i, scratch[0].r);

            ++fout;
        } while (--k != 0);
    }

    private void kf_bfly5(Array<kiss_fft_cpx<kiss_fft_scalar>> fout, int fstride, int m)
    {
        Array<kiss_fft_cpx<kiss_fft_scalar>> tw1;
        Array<kiss_fft_cpx<kiss_fft_scalar>> tw2;
        Array<kiss_fft_cpx<kiss_fft_scalar>> tw3;
        Array<kiss_fft_cpx<kiss_fft_scalar>> tw4;
        kiss_fft_cpx<kiss_fft_scalar>[] scratch = new kiss_fft_cpx<kiss_fft_scalar>[13];
        for (int j = 0; j < 13; j++) scratch[j] = new kiss_fft_cpx<kiss_fft_scalar>();
        kiss_fft_cpx<kiss_fft_scalar> ya;
        kiss_fft_cpx<kiss_fft_scalar> yb;

        int k = m;
        int m2 = 2 * m;
        int m3 = 3 * m;
        int m4 = 4 * m;

        tw1 = new Array<kiss_fft_cpx<kiss_fft_scalar>>(_twiddles);
        tw2 = new Array<kiss_fft_cpx<kiss_fft_scalar>>(_twiddles);
        tw3 = new Array<kiss_fft_cpx<kiss_fft_scalar>>(_twiddles);
        tw4 = new Array<kiss_fft_cpx<kiss_fft_scalar>>(_twiddles);

        ya = _twiddles[fstride * m];
        yb = _twiddles[fstride * 2 * m];

        do
        {
            fout[0] = _a.FixDivide(fout[0], 5);
            fout[m] = _a.FixDivide(fout[m], 5);
            fout[m2] = _a.FixDivide(fout[m2], 5);
            fout[m3] = _a.FixDivide(fout[m3], 5);
            fout[m4] = _a.FixDivide(fout[m4], 5);

            scratch[0] = _a.Multiply(fout[m], tw1[0]);
            scratch[1] = _a.Multiply(fout[m2], tw2[0]);
            scratch[2] = _a.Multiply(fout[m3], tw3[0]);
            scratch[3] = _a.Multiply(fout[m4], tw4[0]);

            scratch[5] = _a.Add(scratch[0], scratch[3]);
            scratch[6] = _a.Subtract(scratch[0], scratch[3]);
            scratch[7] = _a.Add(scratch[1], scratch[2]);
            scratch[8] = _a.Subtract(scratch[1], scratch[2]);

            scratch[9].r = _a.Add(fout[0].r, _a.Add(scratch[5].r, scratch[7].r));
            scratch[9].i = _a.Add(fout[0].i, _a.Add(scratch[5].i, scratch[7].i));

            scratch[10].r = _a.Add(fout[0].r, _a.Add(_a.Multiply(scratch[5].r, ya.r), _a.Multiply(scratch[7].r, yb.r)));
            scratch[10].i = _a.Add(fout[0].i, _a.Add(_a.Multiply(scratch[5].i, ya.r), _a.Multiply(scratch[7].i, yb.r)));

            scratch[11].r = _a.Add(fout[0].r, _a.Add(_a.Multiply(scratch[5].r, yb.r), _a.Multiply(scratch[7].r, ya.r)));
            scratch[11].i = _a.Add(fout[0].i, _a.Add(_a.Multiply(scratch[5].i, yb.r), _a.Multiply(scratch[7].i, ya.r)));

            scratch[12].r = _a.Add(_a.Multiply(scratch[6].r, ya.i), _a.Multiply(scratch[8].r, yb.i));
            scratch[12].i = _a.Add(_a.Multiply(scratch[6].i, ya.i), _a.Multiply(scratch[8].i, yb.i));

            scratch[4].r = _a.Subtract(_a.Multiply(scratch[6].r, yb.i), _a.Multiply(scratch[8].r, ya.i));
            scratch[4].i = _a.Subtract(_a.Multiply(scratch[6].i, yb.i), _a.Multiply(scratch[8].i, ya.i));

            fout[0] = scratch[9];
            fout[m].r = _a.Subtract(scratch[10].r, scratch[12].i);
            fout[m].i = _a.Add(scratch[10].i, scratch[12].r);
            fout[m2].r = _a.Subtract(scratch[11].r, scratch[4].i);
            fout[m2].i = _a.Add(scratch[11].i, scratch[4].r);
            fout[m3].r = _a.Add(scratch[11].r, scratch[4].i);
            fout[m3].i = _a.Subtract(scratch[11].i, scratch[4].r);
            fout[m4].r = _a.Add(scratch[10].r, scratch[12].i);
            fout[m4].i = _a.Subtract(scratch[10].i, scratch[12].r);

            tw1 += fstride;
            tw2 += fstride * 2;
            tw3 += fstride * 3;
            tw4 += fstride * 4;
            ++fout;
        } while (--k != 0);
    }

    private void kf_work(Array<kiss_fft_cpx<kiss_fft_scalar>> fout, Array<kiss_fft_cpx<kiss_fft_scalar>> f, int fstride, int in_stride, Array<int> factors)
    {
        kiss_fft_cpx<kiss_fft_scalar> t;
        int p = factors[0];
        int m = factors[1];
        int radix = p; // Save original radix before loop decrements p to 0
        Array<kiss_fft_cpx<kiss_fft_scalar>> fout1;
        Array<kiss_fft_cpx<kiss_fft_scalar>> foutBeg = new(fout); // Save start position before loop modifies fout

        if (m == 1)
        {
            do
            {
                fout[0] = f[0];
                f += fstride * in_stride;
                fout++;
            } while (--p != 0);
        }
        else
        {
            do
            {
                kf_work(fout, f, fstride * p, in_stride, factors + 2);
                f += fstride * in_stride;
                fout += m;
            } while (--p != 0);
        }

        switch (radix)
        {
            case 2:
                kf_bfly2(foutBeg, fstride, m);
                break;
            case 3:
                kf_bfly3(foutBeg, fstride, m);
                break;
            case 4:
                kf_bfly4(foutBeg, fstride, m);
                break;
            case 5:
                kf_bfly5(foutBeg, fstride, m);
                break;
            default:
                Array<kiss_fft_cpx<kiss_fft_scalar>> tw;
                for (int u = 0; u < m; ++u)
                {
                    fout1 = new Array<kiss_fft_cpx<kiss_fft_scalar>>(foutBeg);
                    for (int k = u; k < _nfft; k += m)
                    {
                        if (k != u)
                        {
                            tw = new Array<kiss_fft_cpx<kiss_fft_scalar>>(_twiddles);
                            tw += u * fstride;
                            t = _a.Multiply(fout1[0], tw[0]);
                            fout1[0] = t;
                        }
                        fout1 += m;
                    }
                }
                break;
        }
    }

    private void kf_factor(int n, Array<int> factors)
    {
        int p = 4;
        double floorSqrt = Math.Floor(Math.Sqrt(n));

        do
        {
            while (n % p != 0)
            {
                switch (p)
                {
                    case 4:
                        p = 2;
                        break;
                    case 2:
                        p = 3;
                        break;
                    default:
                        p += 2;
                        break;
                }
                if (p > floorSqrt)
                {
                    p = n;
                }
            }
            n /= p;
            factors[0] = p;
            factors[1] = n;
            factors += 2;
        } while (n > 1);
    }

    private void kiss_fft_alloc(int nfft, bool inverse)
    {
        _nfft = nfft;
        _inverse = inverse;
        _twiddles = new kiss_fft_cpx<kiss_fft_scalar>[nfft];
        for (int i = 0; i < nfft; ++i)
        {
            double phase = -2 * Math.PI * i / nfft;
            if (_inverse)
            {
                phase *= -1;
            }
            _twiddles[i] = _a.Exp(phase);
        }

        kf_factor(nfft, new Array<int>(_factors));
    }

    public void kiss_fft_stride(Array<kiss_fft_cpx<kiss_fft_scalar>> fin, Array<kiss_fft_cpx<kiss_fft_scalar>> fout, int in_stride)
    {
        if (fin == fout)
        {
            // Out-of-place scratch to avoid overwrite.
            kiss_fft_cpx<kiss_fft_scalar>[] tmpbuf = new kiss_fft_cpx<kiss_fft_scalar>[_nfft];
            kf_work(new Array<kiss_fft_cpx<kiss_fft_scalar>>(tmpbuf), fin, 1, in_stride, new Array<int>(_factors));
            for (int i = 0; i < _nfft; ++i)
            {
                fout[i] = new kiss_fft_cpx<kiss_fft_scalar>(tmpbuf[i]);
            }
        }
        else
        {
            kf_work(fout, fin, 1, in_stride, new Array<int>(_factors));
        }
    }

    public void kiss_fft(Array<kiss_fft_cpx<kiss_fft_scalar>> fin, Array<kiss_fft_cpx<kiss_fft_scalar>> fout)
    {
        kiss_fft_stride(fin, fout, 1);
    }

    public static int kiss_fft_next_fast_size(int n)
    {
        while (true)
        {
            int m = n;
            while ((m % 2) == 0)
            {
                m /= 2;
            }
            while ((m % 3) == 0)
            {
                m /= 3;
            }
            while ((m % 5) == 0)
            {
                m /= 5;
            }
            if (m <= 1)
            {
                break;
            }
            n++;
        }
        return n;
    }
}
