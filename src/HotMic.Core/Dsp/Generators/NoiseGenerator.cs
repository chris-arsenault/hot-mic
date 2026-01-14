namespace HotMic.Core.Dsp.Generators;

/// <summary>
/// Noise generator with white, pink, brown (red), and blue noise types.
/// Uses Voss-McCartney algorithm for pink noise and IIR filtering for colored noise.
/// </summary>
public struct NoiseGenerator
{
    // Random state (xorshift128+)
    private ulong _s0;
    private ulong _s1;

    // Pink noise state (Voss-McCartney with 16 octave bands)
    private const int PinkOctaves = 16;
    private float[] _pinkRows;
    private float _pinkRunningSum;
    private int _pinkIndex;
    private int _pinkIndexMask;

    // Brown noise state (integrated white noise with leaky integrator)
    private float _brownState;
    private const float BrownLeak = 0.999f;
    private const float BrownScale = 3.5f;

    // Blue noise state (differentiated white noise)
    private float _bluePrevious;
    private const float BlueScale = 0.7f;

    public NoiseGenerator()
    {
        _pinkRows = new float[PinkOctaves];
        _s0 = 0;
        _s1 = 0;
        _pinkRunningSum = 0;
        _pinkIndex = 0;
        _pinkIndexMask = (1 << PinkOctaves) - 1;
        _brownState = 0;
        _bluePrevious = 0;
    }

    public void Initialize(uint seed = 0)
    {
        // Initialize xorshift128+ state
        if (seed == 0)
        {
            seed = (uint)Environment.TickCount;
        }

        _s0 = seed;
        _s1 = seed ^ 0x5DEECE66DUL;

        // Pre-warm the RNG
        for (int i = 0; i < 20; i++)
        {
            NextRandom();
        }

        // Initialize pink noise rows (allocate if needed - structs may not call constructor)
        _pinkRows ??= new float[PinkOctaves];
        _pinkIndexMask = (1 << PinkOctaves) - 1;
        _pinkRunningSum = 0;
        for (int i = 0; i < PinkOctaves; i++)
        {
            _pinkRows[i] = NextWhite();
            _pinkRunningSum += _pinkRows[i];
        }

        _pinkIndex = 0;
        _brownState = 0;
        _bluePrevious = 0;
    }

    public void Reset()
    {
        _pinkRows ??= new float[PinkOctaves];
        _pinkRunningSum = 0;
        for (int i = 0; i < PinkOctaves; i++)
        {
            _pinkRows[i] = NextWhite();
            _pinkRunningSum += _pinkRows[i];
        }
        _pinkIndex = 0;
        _brownState = 0;
        _bluePrevious = 0;
    }

    /// <summary>
    /// Generate white noise sample (-1 to +1).
    /// </summary>
    public float NextWhite()
    {
        return NextRandom() * 2f - 1f;
    }

    /// <summary>
    /// Generate pink noise sample (-1 to +1) using Voss-McCartney algorithm.
    /// Pink noise has equal energy per octave (-3dB/octave slope).
    /// </summary>
    public float NextPink()
    {
        // Guard against use before Initialize
        if (_pinkRows == null)
        {
            return NextWhite();
        }

        // Voss-McCartney algorithm: update rows based on trailing zeros of index
        int lastIndex = _pinkIndex;
        _pinkIndex = (_pinkIndex + 1) & _pinkIndexMask;
        int diff = lastIndex ^ _pinkIndex;

        for (int i = 0; i < PinkOctaves; i++)
        {
            if ((diff & (1 << i)) != 0)
            {
                _pinkRunningSum -= _pinkRows[i];
                _pinkRows[i] = NextWhite();
                _pinkRunningSum += _pinkRows[i];
            }
        }

        // Add white noise component and normalize
        float white = NextWhite();
        float sample = (_pinkRunningSum + white) / (PinkOctaves + 1);

        return sample;
    }

    /// <summary>
    /// Generate brown (red) noise sample using leaky integrator on white noise.
    /// Brown noise has -6dB/octave slope (random walk / Brownian motion).
    /// </summary>
    public float NextBrown()
    {
        float white = NextWhite();
        _brownState = _brownState * BrownLeak + white * (1f - BrownLeak);

        // Scale to approximate unity amplitude
        return Math.Clamp(_brownState * BrownScale, -1f, 1f);
    }

    /// <summary>
    /// Generate blue noise sample using differentiation of white noise.
    /// Blue noise has +3dB/octave slope (high frequencies emphasized).
    /// </summary>
    public float NextBlue()
    {
        float white = NextWhite();
        float sample = (white - _bluePrevious) * BlueScale;
        _bluePrevious = white;

        return Math.Clamp(sample, -1f, 1f);
    }

    /// <summary>
    /// Generate random float in range [0, 1) using xorshift128+.
    /// </summary>
    private float NextRandom()
    {
        ulong s1 = _s0;
        ulong s0 = _s1;
        ulong result = s0 + s1;

        _s0 = s0;
        s1 ^= s1 << 23;
        _s1 = s1 ^ s0 ^ (s1 >> 18) ^ (s0 >> 5);

        // Convert to float [0, 1)
        return (result >> 40) * (1f / 16777216f);
    }
}
