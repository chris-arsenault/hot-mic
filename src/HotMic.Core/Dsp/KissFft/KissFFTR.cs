using System;

namespace HotMic.Core.Dsp.KissFft;

// Ported from kiss_fftr.c (KISS FFT, BSD-3-Clause) for real-input FFTs.
internal sealed class KissFFTR
{
    private readonly int _nfft;
    private readonly int _ncfft;
    private readonly KissFFT<float> _substate;
    private readonly kiss_fft_cpx<float>[] _tmpbuf;
    private readonly kiss_fft_cpx<float>[] _superTwiddles;
    private readonly kiss_fft_cpx<float>[] _packedTime;
    private readonly kiss_fft_cpx<float>[] _timeComplex;
    private readonly Array<kiss_fft_cpx<float>> _packedView;
    private readonly Array<kiss_fft_cpx<float>> _tmpView;
    private readonly Array<kiss_fft_cpx<float>> _timeView;
    private readonly bool _inverse;

    public KissFFTR(int nfft, bool inverse)
    {
        if ((nfft & 1) != 0)
        {
            throw new ArgumentException("Real FFT requires an even FFT size.", nameof(nfft));
        }

        _nfft = nfft;
        _ncfft = nfft / 2;
        _inverse = inverse;

        _substate = new KissFFT<float>(_ncfft, inverse, new FloatArithmetic());
        _tmpbuf = new kiss_fft_cpx<float>[_ncfft];
        _superTwiddles = new kiss_fft_cpx<float>[_ncfft / 2];
        _packedTime = new kiss_fft_cpx<float>[_ncfft];
        _timeComplex = new kiss_fft_cpx<float>[_ncfft];
        _packedView = new Array<kiss_fft_cpx<float>>(_packedTime);
        _tmpView = new Array<kiss_fft_cpx<float>>(_tmpbuf);
        _timeView = new Array<kiss_fft_cpx<float>>(_timeComplex);

        for (int i = 0; i < _superTwiddles.Length; i++)
        {
            double phase = -Math.PI * ((double)(i + 1) / _ncfft + 0.5);
            if (_inverse)
            {
                phase *= -1;
            }
            _superTwiddles[i] = new kiss_fft_cpx<float>((float)Math.Cos(phase), (float)Math.Sin(phase));
        }
    }

    public int FreqSize => _ncfft + 1;

    public void Forward(ReadOnlySpan<float> timedata, Span<kiss_fft_cpx<float>> freqdata)
    {
        if (_inverse)
        {
            throw new InvalidOperationException("Forward called on inverse FFT instance.");
        }
        if (timedata.Length != _nfft)
        {
            throw new ArgumentException("Input size mismatch.", nameof(timedata));
        }
        if (freqdata.Length < _ncfft + 1)
        {
            throw new ArgumentException("Output buffer too small.", nameof(freqdata));
        }

        for (int i = 0; i < _ncfft; i++)
        {
            int idx = i * 2;
            _packedTime[i].r = timedata[idx];
            _packedTime[i].i = timedata[idx + 1];
        }

        _substate.kiss_fft(_packedView, _tmpView);

        kiss_fft_cpx<float> tdc = _tmpbuf[0];
        freqdata[0].r = tdc.r + tdc.i;
        freqdata[0].i = 0f;
        freqdata[_ncfft].r = tdc.r - tdc.i;
        freqdata[_ncfft].i = 0f;

        for (int k = 1; k <= _ncfft / 2; k++)
        {
            kiss_fft_cpx<float> fpk = _tmpbuf[k];
            kiss_fft_cpx<float> fpnk = new(_tmpbuf[_ncfft - k].r, -_tmpbuf[_ncfft - k].i);

            kiss_fft_cpx<float> f1k = Add(fpk, fpnk);
            kiss_fft_cpx<float> f2k = Sub(fpk, fpnk);
            kiss_fft_cpx<float> tw = Mul(f2k, _superTwiddles[k - 1]);

            freqdata[k].r = 0.5f * (f1k.r + tw.r);
            freqdata[k].i = 0.5f * (f1k.i + tw.i);
            freqdata[_ncfft - k].r = 0.5f * (f1k.r - tw.r);
            freqdata[_ncfft - k].i = 0.5f * (tw.i - f1k.i);
        }
    }

    public void Inverse(ReadOnlySpan<kiss_fft_cpx<float>> freqdata, Span<float> timedata)
    {
        if (!_inverse)
        {
            throw new InvalidOperationException("Inverse called on forward FFT instance.");
        }
        if (timedata.Length != _nfft)
        {
            throw new ArgumentException("Output size mismatch.", nameof(timedata));
        }
        if (freqdata.Length < _ncfft + 1)
        {
            throw new ArgumentException("Input buffer too small.", nameof(freqdata));
        }

        _tmpbuf[0].r = freqdata[0].r + freqdata[_ncfft].r;
        _tmpbuf[0].i = freqdata[0].r - freqdata[_ncfft].r;

        for (int k = 1; k <= _ncfft / 2; k++)
        {
            kiss_fft_cpx<float> fk = freqdata[k];
            kiss_fft_cpx<float> fnkc = new(freqdata[_ncfft - k].r, -freqdata[_ncfft - k].i);

            kiss_fft_cpx<float> fek = Add(fk, fnkc);
            kiss_fft_cpx<float> tmp = Sub(fk, fnkc);
            kiss_fft_cpx<float> fok = Mul(tmp, _superTwiddles[k - 1]);

            _tmpbuf[k] = Add(fek, fok);
            _tmpbuf[_ncfft - k] = Sub(fek, fok);
            _tmpbuf[_ncfft - k].i *= -1f;
        }

        _substate.kiss_fft(_tmpView, _timeView);

        for (int i = 0; i < _ncfft; i++)
        {
            int idx = i * 2;
            timedata[idx] = _timeComplex[i].r;
            timedata[idx + 1] = _timeComplex[i].i;
        }
    }

    private static kiss_fft_cpx<float> Add(kiss_fft_cpx<float> a, kiss_fft_cpx<float> b)
    {
        return new kiss_fft_cpx<float>(a.r + b.r, a.i + b.i);
    }

    private static kiss_fft_cpx<float> Sub(kiss_fft_cpx<float> a, kiss_fft_cpx<float> b)
    {
        return new kiss_fft_cpx<float>(a.r - b.r, a.i - b.i);
    }

    private static kiss_fft_cpx<float> Mul(kiss_fft_cpx<float> a, kiss_fft_cpx<float> b)
    {
        return new kiss_fft_cpx<float>(
            a.r * b.r - a.i * b.i,
            a.r * b.i + a.i * b.r);
    }
}
