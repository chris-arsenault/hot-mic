namespace HotMic.Core.Dsp.KissFft;

internal interface IArithmetic<kiss_fft_scalar>
{
    kiss_fft_scalar Negate(kiss_fft_scalar a);
    kiss_fft_scalar Add(kiss_fft_scalar a, kiss_fft_scalar b);
    kiss_fft_scalar Subtract(kiss_fft_scalar a, kiss_fft_scalar b);
    kiss_fft_scalar Multiply(kiss_fft_scalar a, kiss_fft_scalar b);
    kiss_fft_scalar Divide(kiss_fft_scalar a, kiss_fft_scalar b);
    kiss_fft_scalar Half(kiss_fft_scalar a);

    kiss_fft_cpx<kiss_fft_scalar> Add(kiss_fft_cpx<kiss_fft_scalar> a, kiss_fft_cpx<kiss_fft_scalar> b);
    kiss_fft_cpx<kiss_fft_scalar> Subtract(kiss_fft_cpx<kiss_fft_scalar> a, kiss_fft_cpx<kiss_fft_scalar> b);
    kiss_fft_cpx<kiss_fft_scalar> Multiply(kiss_fft_cpx<kiss_fft_scalar> a, kiss_fft_cpx<kiss_fft_scalar> b);
    kiss_fft_cpx<kiss_fft_scalar> Multiply(kiss_fft_cpx<kiss_fft_scalar> a, kiss_fft_scalar b);
    kiss_fft_cpx<kiss_fft_scalar> FixDivide(kiss_fft_cpx<kiss_fft_scalar> a, int b);
    kiss_fft_cpx<kiss_fft_scalar> Exp(double phase);
}
