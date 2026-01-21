using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Available spectrogram color palettes for vocal analysis.
/// </summary>
public enum SpectrogramColorMap
{
    Vocal = 0,
    VocalWarm = 1,
    Grayscale = 2,
    Inferno = 3,
    Viridis = 4,
    Magma = 5,
    Blue = 6
}

/// <summary>
/// Provides cached 256-color lookup tables for spectrogram rendering.
/// Uses OKLAB perceptual color space for smooth, uniform gradients.
/// </summary>
public static class SpectrogramColorMaps
{
    private static SKColor[]? _vocal;
    private static SKColor[]? _vocalWarm;
    private static SKColor[]? _grayscale;
    private static SKColor[]? _inferno;
    private static SKColor[]? _viridis;
    private static SKColor[]? _magma;
    private static SKColor[]? _blue;

    public static SKColor[] GetColors(SpectrogramColorMap map)
    {
        return map switch
        {
            SpectrogramColorMap.Vocal => _vocal ??= BuildVocal(),
            SpectrogramColorMap.VocalWarm => _vocalWarm ??= BuildVocalWarm(),
            SpectrogramColorMap.Grayscale => _grayscale ??= BuildGrayscale(),
            SpectrogramColorMap.Inferno => _inferno ??= BuildInferno(),
            SpectrogramColorMap.Viridis => _viridis ??= BuildViridis(),
            SpectrogramColorMap.Magma => _magma ??= BuildMagma(),
            SpectrogramColorMap.Blue => _blue ??= BuildBlue(),
            _ => _vocal ??= BuildVocal()
        };
    }

    private static SKColor[] BuildVocal()
    {
        // Vocal gradient per docs/technical/Spectrogram-Rendering.md##Color Maps: black -> purple -> blue -> cyan -> yellow -> white.
        SKColor[] stops =
        {
            new(0x00, 0x00, 0x00),
            new(0x1E, 0x00, 0x32),
            new(0x3C, 0x00, 0x78),
            new(0x00, 0x32, 0xB4),
            new(0x00, 0x96, 0xC8),
            new(0x32, 0xC8, 0x96),
            new(0xC8, 0xE6, 0x32),
            new(0xFF, 0xC8, 0x00),
            new(0xFF, 0xFF, 0xC8)
        };
        return BuildGradient(stops);
    }

    private static SKColor[] BuildVocalWarm()
    {
        SKColor[] stops =
        {
            new(0x00, 0x00, 0x00),
            new(0x2A, 0x12, 0x00),
            new(0x6A, 0x2A, 0x00),
            new(0xA8, 0x4A, 0x00),
            new(0xE8, 0x8A, 0x2A),
            new(0xFF, 0xC0, 0x5A),
            new(0xFF, 0xE6, 0xB0)
        };
        return BuildGradient(stops);
    }

    private static SKColor[] BuildGrayscale()
    {
        SKColor[] stops =
        {
            new(0x00, 0x00, 0x00),
            new(0xFF, 0xFF, 0xFF)
        };
        return BuildGradient(stops);
    }

    private static SKColor[] BuildInferno()
    {
        SKColor[] stops =
        {
            new(0x00, 0x00, 0x04),
            new(0x1B, 0x0C, 0x41),
            new(0x4A, 0x0C, 0x6B),
            new(0x78, 0x1C, 0x6D),
            new(0xA5, 0x2C, 0x60),
            new(0xD6, 0x4F, 0x4A),
            new(0xF1, 0x7C, 0x2F),
            new(0xFC, 0xAF, 0x28),
            new(0xFC, 0xE7, 0x5C)
        };
        return BuildGradient(stops);
    }

    private static SKColor[] BuildViridis()
    {
        SKColor[] stops =
        {
            new(0x0B, 0x1D, 0x3A),
            new(0x1C, 0x4E, 0x6D),
            new(0x2E, 0x7A, 0x74),
            new(0x4C, 0xA2, 0x6E),
            new(0x7C, 0xC8, 0x56),
            new(0xB9, 0xE0, 0x35),
            new(0xF4, 0xE8, 0x1C)
        };
        return BuildGradient(stops);
    }

    private static SKColor[] BuildMagma()
    {
        SKColor[] stops =
        {
            new(0x00, 0x00, 0x03),
            new(0x1A, 0x0A, 0x3A),
            new(0x4B, 0x0C, 0x6B),
            new(0x7C, 0x19, 0x6B),
            new(0xB2, 0x2F, 0x59),
            new(0xE5, 0x5B, 0x45),
            new(0xF7, 0x9F, 0x32),
            new(0xFC, 0xE8, 0x6C)
        };
        return BuildGradient(stops);
    }

    private static SKColor[] BuildBlue()
    {
        // Blue-on-black theme requested for v1 readability on dark backgrounds.
        SKColor[] stops =
        {
            new(0x00, 0x00, 0x00),
            new(0x04, 0x0B, 0x1A),
            new(0x0A, 0x1E, 0x3A),
            new(0x0F, 0x3C, 0x72),
            new(0x1F, 0x6B, 0xB6),
            new(0x2F, 0xA4, 0xFF),
            new(0xC8, 0xF0, 0xFF)
        };
        return BuildGradient(stops);
    }

    /// <summary>
    /// Builds a 256-color gradient using OKLAB perceptual interpolation.
    /// OKLAB provides perceptually uniform transitions, avoiding the "muddy middle"
    /// problem of RGB interpolation and ensuring consistent perceived brightness changes.
    /// </summary>
    private static SKColor[] BuildGradient(ReadOnlySpan<SKColor> stops)
    {
        var colors = new SKColor[256];
        if (stops.Length == 0)
        {
            return colors;
        }

        if (stops.Length == 1)
        {
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = stops[0];
            }
            return colors;
        }

        // Convert all stops to OKLAB
        Span<(float L, float a, float b)> oklabStops = stackalloc (float, float, float)[stops.Length];
        for (int i = 0; i < stops.Length; i++)
        {
            oklabStops[i] = SrgbToOklab(stops[i]);
        }

        int segments = stops.Length - 1;
        for (int i = 0; i < colors.Length; i++)
        {
            float t = i / 255f;
            float scaled = t * segments;
            int index = Math.Min(segments - 1, (int)scaled);
            float local = scaled - index;

            // Interpolate in OKLAB space
            var (L1, a1, b1) = oklabStops[index];
            var (L2, a2, b2) = oklabStops[index + 1];

            float L = L1 + (L2 - L1) * local;
            float a = a1 + (a2 - a1) * local;
            float b = b1 + (b2 - b1) * local;

            colors[i] = OklabToSrgb(L, a, b);
        }

        return colors;
    }

    /// <summary>
    /// Converts sRGB color to OKLAB perceptual color space.
    /// </summary>
    private static (float L, float a, float b) SrgbToOklab(SKColor color)
    {
        // sRGB to linear RGB
        float r = SrgbToLinear(color.Red / 255f);
        float g = SrgbToLinear(color.Green / 255f);
        float b = SrgbToLinear(color.Blue / 255f);

        // Linear RGB to LMS (cone responses)
        float l = 0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b;
        float m = 0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b;
        float s = 0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b;

        // LMS to OKLAB (cube root for perceptual uniformity)
        float l_ = MathF.Cbrt(l);
        float m_ = MathF.Cbrt(m);
        float s_ = MathF.Cbrt(s);

        return (
            0.2104542553f * l_ + 0.7936177850f * m_ - 0.0040720468f * s_,
            1.9779984951f * l_ - 2.4285922050f * m_ + 0.4505937099f * s_,
            0.0259040371f * l_ + 0.7827717662f * m_ - 0.8086757660f * s_
        );
    }

    /// <summary>
    /// Converts OKLAB color back to sRGB.
    /// </summary>
    private static SKColor OklabToSrgb(float L, float a, float b)
    {
        // OKLAB to LMS
        float l_ = L + 0.3963377774f * a + 0.2158037573f * b;
        float m_ = L - 0.1055613458f * a - 0.0638541728f * b;
        float s_ = L - 0.0894841775f * a - 1.2914855480f * b;

        // Cube to undo the perceptual transformation
        float l = l_ * l_ * l_;
        float m = m_ * m_ * m_;
        float s = s_ * s_ * s_;

        // LMS to linear RGB
        float r = +4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s;
        float g = -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s;
        float bl = -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s;

        // Clamp and convert to sRGB
        return new SKColor(
            (byte)(Math.Clamp(LinearToSrgb(r), 0f, 1f) * 255f + 0.5f),
            (byte)(Math.Clamp(LinearToSrgb(g), 0f, 1f) * 255f + 0.5f),
            (byte)(Math.Clamp(LinearToSrgb(bl), 0f, 1f) * 255f + 0.5f)
        );
    }

    /// <summary>
    /// sRGB gamma expansion (sRGB to linear).
    /// </summary>
    private static float SrgbToLinear(float x)
    {
        return x <= 0.04045f
            ? x / 12.92f
            : MathF.Pow((x + 0.055f) / 1.055f, 2.4f);
    }

    /// <summary>
    /// sRGB gamma compression (linear to sRGB).
    /// </summary>
    private static float LinearToSrgb(float x)
    {
        return x <= 0.0031308f
            ? 12.92f * x
            : 1.055f * MathF.Pow(x, 1f / 2.4f) - 0.055f;
    }
}
