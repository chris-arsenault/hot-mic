using System;

namespace HotMic.Core.Dsp;

internal static class DeepFilterNetDsp
{
    public static int[] BuildErbBands(int sampleRate, int fftSize, int bandCount, int minBinsPerBand)
    {
        int nyquist = sampleRate / 2;
        float freqWidth = sampleRate / (float)fftSize;
        float erbLow = FreqToErb(0f);
        float erbHigh = FreqToErb(nyquist);
        var erb = new int[bandCount];
        float step = (erbHigh - erbLow) / bandCount;
        int prevFreq = 0;
        int freqOver = 0;
        for (int i = 1; i <= bandCount; i++)
        {
            float f = ErbToFreq(erbLow + i * step);
            int fb = (int)MathF.Round(f / freqWidth);
            int nbFreqs = fb - prevFreq - freqOver;
            if (nbFreqs < minBinsPerBand)
            {
                freqOver = minBinsPerBand - nbFreqs;
                nbFreqs = minBinsPerBand;
            }
            else
            {
                freqOver = 0;
            }

            erb[i - 1] = nbFreqs;
            prevFreq = fb;
        }

        erb[bandCount - 1] += 1;
        int tooLarge = 0;
        for (int i = 0; i < erb.Length; i++)
        {
            tooLarge += erb[i];
        }
        tooLarge -= fftSize / 2 + 1;
        if (tooLarge > 0)
        {
            erb[bandCount - 1] -= tooLarge;
        }

        return erb;
    }

    public static float CalcNormAlpha(int sampleRate, int hopSize, float tau)
    {
        float dt = hopSize / (float)sampleRate;
        float alpha = MathF.Exp(-dt / tau);
        float a = 1.0f;
        int precision = 3;
        while (a >= 1.0f)
        {
            float pow = MathF.Pow(10f, precision);
            a = MathF.Round(alpha * pow) / pow;
            precision++;
        }
        return a;
    }

    public static void ComputeBandCorr(float[] output, float[] specInterleaved, int[] erbBands)
    {
        Array.Clear(output, 0, output.Length);
        int bandOffset = 0;
        for (int band = 0; band < erbBands.Length; band++)
        {
            int bandSize = erbBands[band];
            float inv = 1f / bandSize;
            float sum = 0f;
            for (int j = 0; j < bandSize; j++)
            {
                int idx = bandOffset + j;
                float re = specInterleaved[idx * 2];
                float im = specInterleaved[idx * 2 + 1];
                sum += (re * re + im * im) * inv;
            }
            output[band] = sum;
            bandOffset += bandSize;
        }
    }

    public static void BandMeanNormErb(float[] features, float[] state, float alpha)
    {
        for (int i = 0; i < features.Length; i++)
        {
            float x = features[i];
            state[i] = x * (1f - alpha) + state[i] * alpha;
            x -= state[i];
            features[i] = x / 40f;
        }
    }

    public static void BandUnitNorm(float[] specInterleaved, int bins, float[] state, float alpha, float[] outputInterleaved)
    {
        // Match libDF band_unit_norm_t: output is [re0..reN-1, im0..imN-1].
        int imagOffset = bins;
        for (int i = 0; i < bins; i++)
        {
            int idx = i * 2;
            float re = specInterleaved[idx];
            float im = specInterleaved[idx + 1];
            float mag = MathF.Sqrt(re * re + im * im);
            state[i] = mag * (1f - alpha) + state[i] * alpha;
            float denom = MathF.Sqrt(state[i] + 1e-12f);
            outputInterleaved[i] = re / denom;
            outputInterleaved[imagOffset + i] = im / denom;
        }
    }

    public static void ApplyInterpBandGain(float[] specInterleaved, float[] gains, int[] erbBands)
    {
        int bandOffset = 0;
        for (int band = 0; band < erbBands.Length; band++)
        {
            int bandSize = erbBands[band];
            float gain = gains[band];
            for (int j = 0; j < bandSize; j++)
            {
                int idx = (bandOffset + j) * 2;
                specInterleaved[idx] *= gain;
                specInterleaved[idx + 1] *= gain;
            }
            bandOffset += bandSize;
        }
    }

    public static void PostFilter(float[] noisy, float[] enhanced, float beta)
    {
        float betaPlus = beta + 1f;
        float eps = 1e-12f;
        float pi = MathF.PI;

        int i = 0;
        for (; i + 7 < noisy.Length; i += 8)
        {
            float g0 = Ratio(enhanced[i], enhanced[i + 1], noisy[i], noisy[i + 1], eps);
            float g1 = Ratio(enhanced[i + 2], enhanced[i + 3], noisy[i + 2], noisy[i + 3], eps);
            float g2 = Ratio(enhanced[i + 4], enhanced[i + 5], noisy[i + 4], noisy[i + 5], eps);
            float g3 = Ratio(enhanced[i + 6], enhanced[i + 7], noisy[i + 6], noisy[i + 7], eps);

            float pf0 = PostFilterGain(g0, betaPlus, beta, pi);
            float pf1 = PostFilterGain(g1, betaPlus, beta, pi);
            float pf2 = PostFilterGain(g2, betaPlus, beta, pi);
            float pf3 = PostFilterGain(g3, betaPlus, beta, pi);

            enhanced[i] *= pf0;
            enhanced[i + 1] *= pf0;
            enhanced[i + 2] *= pf1;
            enhanced[i + 3] *= pf1;
            enhanced[i + 4] *= pf2;
            enhanced[i + 5] *= pf2;
            enhanced[i + 6] *= pf3;
            enhanced[i + 7] *= pf3;
        }

        for (; i + 1 < noisy.Length; i += 2)
        {
            float g = Ratio(enhanced[i], enhanced[i + 1], noisy[i], noisy[i + 1], eps);
            float pf = PostFilterGain(g, betaPlus, beta, pi);
            enhanced[i] *= pf;
            enhanced[i + 1] *= pf;
        }
    }

    private static float Ratio(float reEnh, float imEnh, float reNoisy, float imNoisy, float eps)
    {
        float magEnh = MathF.Sqrt(reEnh * reEnh + imEnh * imEnh);
        float magNoisy = MathF.Sqrt(reNoisy * reNoisy + imNoisy * imNoisy) + eps;
        float g = magEnh / magNoisy;
        if (g > 1f) g = 1f;
        if (g < eps) g = eps;
        return g;
    }

    private static float PostFilterGain(float g, float betaPlus, float beta, float pi)
    {
        float gSin = g * MathF.Sin(g * pi * 0.5f);
        float denom = 1f + beta * MathF.Pow(g / gSin, 2f);
        return (betaPlus * g / denom) / g;
    }

    private static float FreqToErb(float freqHz)
    {
        return 9.265f * MathF.Log(1f + freqHz / (24.7f * 9.265f));
    }

    private static float ErbToFreq(float erb)
    {
        return 24.7f * 9.265f * (MathF.Exp(erb / 9.265f) - 1f);
    }
}
