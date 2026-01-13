namespace HotMic.Core.Dsp;

/// <summary>
/// Supported window functions for FFT analysis.
/// </summary>
public enum WindowFunction
{
    Hann,
    Hamming,
    BlackmanHarris,
    Gaussian,
    Kaiser
}

/// <summary>
/// Window function generator with no runtime allocations.
/// </summary>
public static class WindowFunctions
{
    private const float TwoPi = MathF.PI * 2f;
    private const float DefaultGaussianSigma = 0.4f;
    private const float DefaultKaiserBeta = 9f;

    /// <summary>
    /// Fills the provided span with the selected window function.
    /// </summary>
    public static void Fill(Span<float> window, WindowFunction type)
    {
        switch (type)
        {
            case WindowFunction.Hamming:
                FillHamming(window);
                return;
            case WindowFunction.BlackmanHarris:
                FillBlackmanHarris(window);
                return;
            case WindowFunction.Gaussian:
                FillGaussian(window, DefaultGaussianSigma);
                return;
            case WindowFunction.Kaiser:
                FillKaiser(window, DefaultKaiserBeta);
                return;
            default:
                FillHann(window);
                return;
        }
    }

    /// <summary>
    /// Fills with a Hann window.
    /// </summary>
    public static void FillHann(Span<float> window)
    {
        int n = window.Length;
        if (n <= 1)
        {
            if (n == 1)
            {
                window[0] = 1f;
            }
            return;
        }

        float denom = n - 1;
        for (int i = 0; i < n; i++)
        {
            window[i] = 0.5f - 0.5f * MathF.Cos(TwoPi * i / denom);
        }
    }

    /// <summary>
    /// Fills with a Hamming window.
    /// </summary>
    public static void FillHamming(Span<float> window)
    {
        int n = window.Length;
        if (n <= 1)
        {
            if (n == 1)
            {
                window[0] = 1f;
            }
            return;
        }

        float denom = n - 1;
        for (int i = 0; i < n; i++)
        {
            window[i] = 0.54f - 0.46f * MathF.Cos(TwoPi * i / denom);
        }
    }

    /// <summary>
    /// Fills with a Blackman-Harris window (92 dB).
    /// </summary>
    public static void FillBlackmanHarris(Span<float> window)
    {
        int n = window.Length;
        if (n <= 1)
        {
            if (n == 1)
            {
                window[0] = 1f;
            }
            return;
        }

        const float a0 = 0.35875f;
        const float a1 = 0.48829f;
        const float a2 = 0.14128f;
        const float a3 = 0.01168f;
        float denom = n - 1;
        for (int i = 0; i < n; i++)
        {
            float phase = TwoPi * i / denom;
            window[i] = a0
                      - a1 * MathF.Cos(phase)
                      + a2 * MathF.Cos(2f * phase)
                      - a3 * MathF.Cos(3f * phase);
        }
    }

    /// <summary>
    /// Fills with a Gaussian window using the provided sigma (0.4 default).
    /// </summary>
    public static void FillGaussian(Span<float> window, float sigma)
    {
        int n = window.Length;
        if (n <= 1)
        {
            if (n == 1)
            {
                window[0] = 1f;
            }
            return;
        }

        float mean = 0.5f * (n - 1);
        float inv = 1f / (sigma * mean);
        for (int i = 0; i < n; i++)
        {
            float x = (i - mean) * inv;
            window[i] = MathF.Exp(-0.5f * x * x);
        }
    }

    /// <summary>
    /// Fills with a Kaiser window using the provided beta (9 default).
    /// </summary>
    public static void FillKaiser(Span<float> window, float beta)
    {
        int n = window.Length;
        if (n <= 1)
        {
            if (n == 1)
            {
                window[0] = 1f;
            }
            return;
        }

        float denom = n - 1;
        float invI0 = 1f / I0(beta);
        for (int i = 0; i < n; i++)
        {
            float ratio = 2f * i / denom - 1f;
            float val = I0(beta * MathF.Sqrt(1f - ratio * ratio)) * invI0;
            window[i] = val;
        }
    }

    // Modified Bessel function of the first kind (order 0).
    private static float I0(float x)
    {
        float ax = MathF.Abs(x);
        if (ax < 3.75f)
        {
            float y = x / 3.75f;
            y *= y;
            return 1f + y * (3.5156229f + y * (3.0899424f + y * (1.2067492f
                + y * (0.2659732f + y * (0.0360768f + y * 0.0045813f)))));
        }

        float z = 3.75f / ax;
        return (MathF.Exp(ax) / MathF.Sqrt(ax))
               * (0.39894228f + z * (0.01328592f + z * (0.00225319f
               + z * (-0.00157565f + z * (0.00916281f + z * (-0.02057706f
               + z * (0.02635537f + z * (-0.01647633f + z * 0.00392377f))))))));
    }
}
