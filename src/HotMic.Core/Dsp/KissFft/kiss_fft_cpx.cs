namespace HotMic.Core.Dsp.KissFft;

internal struct kiss_fft_cpx<kiss_fft_scalar>
{
    public kiss_fft_cpx(kiss_fft_cpx<kiss_fft_scalar> other)
    {
        r = other.r;
        i = other.i;
    }

    public kiss_fft_cpx(kiss_fft_scalar real, kiss_fft_scalar imag)
    {
        r = real;
        i = imag;
    }

    public kiss_fft_scalar r;
    public kiss_fft_scalar i;
}
